using UnityEngine;
using System;
using System.Collections.Generic;
using Pathfinding; // A* Project

/// <summary>
/// Runtime data + hooks for the currently loaded level instance.
/// Lives on the level root under WORLD/LevelRoot/<name>.
/// Grid agents (GridMotor, pathfinding, etc.) can bind to this and navmesh can be built per-level.
/// </summary>
public class LevelRuntime : MonoBehaviour
{
    public static LevelRuntime Active { get; private set; }
    [Header("Level Asset")]
    [Tooltip("TileAdjacencyAtlas to instantiate as level geometry under this runtime root.")]
    public TileAdjacencyAtlas levelAtlas;
    [Tooltip("Optional explicit container for spawned level geometry. Will be created if null.")]
    public Transform levelContainer;
    [Tooltip("Root that will hold runtime entities (players, coins, items, etc.). If null, a sibling/child named 'Entities' will be created or reused.")]
    public Transform entitiesRoot;
    [Header("Registries")]
    [Tooltip("Primary registry ScriptableObject (e.g. Assets/Game/Entities.asset). Registry is authoritative; no local fallbacks are used.")]
    public ScriptableObject registryAsset;

    [Header("Grid (auto-computed from children)")]
    [HideInInspector] public float cellSize = 1f;
    [HideInInspector] public Vector3 gridOrigin = Vector3.zero;
    [HideInInspector] public Bounds levelBoundsXZ;

    [Header("Collision (auto-computed from children)")]
    [HideInInspector] public LayerMask solidMask = 0;

    [Header("Layers")]
    [Tooltip("Layer(s) used for walls. Auto-filled from name 'wall' on Awake, but can be overridden.")]
    public LayerMask wallLayers;
    [Tooltip("Layer(s) used for floors. Auto-filled from name 'floor' on Awake, but can be overridden.")]
    public LayerMask floorLayers;
    [Tooltip("Layer(s) used for portals (walkable like floor).")]
    public LayerMask portalLayers;
    [Tooltip("Layer(s) used for doors (walkable like floor).")]
    public LayerMask doorLayers;

    [Header("Navigation")] 
    [Tooltip("If true, build/scan navmesh on Awake for this level instance.")]
    public bool buildNavmeshOnAwake = true;
    [Tooltip("Optional explicit AstarPath reference; if null, will FindObjectOfType.")]
    public AstarPath astar;

    [Header("State")] 
    [Tooltip("True once the level has finished its Awake/initialisation and all registered motors have been notified.")]
    public bool isReady;

    readonly List<GridMotor> _motors = new();

    public void RegisterMotor(GridMotor motor)
    {
        if (motor == null) return;
        if (!_motors.Contains(motor))
            _motors.Add(motor);

        if (isReady)
            motor.OnLevelReady(this);
    }

    public void UnregisterMotor(GridMotor motor)
    {
        if (motor == null) return;
        _motors.Remove(motor);
    }

    void Awake()
    {
        Active = this;

        // Instantiate the level geometry/placeables from atlas before scanning grid/collision.
        if (levelAtlas != null)
        {
            SpawnLevelFromAtlas(levelAtlas);
        }

        // Auto-detect named layers for wall/floor.
        if (wallLayers == 0)
            wallLayers = LayerMask.GetMask("wall");
        if (floorLayers == 0)
            floorLayers = LayerMask.GetMask("floor");
        if (portalLayers == 0)
            portalLayers = LayerMask.GetMask("portal");
        if (doorLayers == 0)
            doorLayers = LayerMask.GetMask("door");
        // Pair portals/tunnels now that the level is spawned (before graph build).
        var setup = GetComponentInChildren<LevelSetup>();
        if (setup != null)
        {
            setup.PairPortals();
            setup.PairTunnels();
        }

        // Auto-prime grid + collision from child data (tiles, walls, etc.).
        AutoConfigureFromChildren();

        // Enforce 1m grid cells.
        cellSize = 1f;

        // Navmesh: grab or find AstarPath and ensure a 1m GridGraph centered on this level.
        if (astar == null)
            astar = FindObjectOfType<AstarPath>();

        if (astar != null)
        {
            ConfigureGridGraph(astar);

            if (buildNavmeshOnAwake)
            {
                astar.Scan();
                // After scan, register graph edges for portals/tunnels.
                if (setup != null)
                    setup.RegisterAstarPortalEdges();
            }
        }

        // Drain any motors that awoke before this level (race-safe).
        GridMotor.FlushPendingMotorsTo(this);


        isReady = true;

        // Notify everything that's registered.
        for (int i = 0; i < _motors.Count; i++)
        {
            var m = _motors[i];
            if (m != null)
                m.OnLevelReady(this);
        }
    }

    void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }

    Transform ResolveEntitiesRoot()
    {
        if (entitiesRoot != null) return entitiesRoot;

        Transform candidate = null;

        if (transform.parent != null)
            candidate = transform.parent.Find("Entities");

        if (candidate == null)
            candidate = transform.Find("Entities");

        if (candidate == null)
        {
            var go = new GameObject("Entities");
            if (transform.parent != null)
                go.transform.SetParent(transform.parent, false);
            else
                go.transform.SetParent(transform, false);
            candidate = go.transform;
        }

        entitiesRoot = candidate;
        return entitiesRoot;
    }

    Transform ResolveLevelContainer(string nameHint = null)
    {
        if (levelContainer != null) return levelContainer;

        // Try to find an existing child that matches the atlas name.
        if (!string.IsNullOrEmpty(nameHint))
        {
            var existing = transform.Find(nameHint);
            if (existing != null)
            {
                levelContainer = existing;
                return levelContainer;
            }
        }

        var go = new GameObject(string.IsNullOrEmpty(nameHint) ? "Level" : nameHint);
        go.transform.SetParent(transform, false);
        levelContainer = go.transform;
        return levelContainer;
    }

    void SpawnLevelFromAtlas(TileAdjacencyAtlas atlas)
    {
        if (atlas == null) return;

        var levelRoot = ResolveLevelContainer(atlas.name);
        var entities = ResolveEntitiesRoot();

        // Clear previous children to avoid duplicates on reload.
        for (int i = levelRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(levelRoot.GetChild(i).gameObject);
        }

        // Tiles (world geometry)
        if (atlas.cells != null)
        {
            for (int i = 0; i < atlas.cells.Count; i++)
            {
                var c = atlas.cells[i];
                if (c.tile == null) continue;

                var prefab = c.tile.gameObject;
                if (prefab == null) continue;

                var inst = Instantiate(prefab, levelRoot);
                Transform prefabT = c.tile.transform;
                // Preserve prefab local position additively (grid + prefab local offset).
                inst.transform.localPosition = new Vector3(c.x, 0f, c.y) + prefabT.localPosition;
                if (Mathf.Abs(prefabT.localPosition.x) > 0.001f || Mathf.Abs(prefabT.localPosition.z) > 0.001f)
                    Debug.Log($"Spawn Tile: cell=({c.x},{c.y}) prefab='{prefab.name}' localOffset={prefabT.localPosition} => worldLocal={inst.transform.localPosition}", prefab);
                inst.transform.localRotation = Quaternion.Euler(0f, TileAdjacencyAtlas.NormalizeRot(c.rotationIndex) * 90f, 0f) * prefabT.localRotation;
                inst.transform.localScale = prefabT.localScale;
            }
        }

        // Placeables
        if (atlas.placeables != null && atlas.placeables.Count > 0)
        {
            for (int i = 0; i < atlas.placeables.Count; i++)
            {
                var p = atlas.placeables[i];
                GameObject prefab = p.prefab;
                if (prefab == null)
                {
                    // Registry decides default; we do not add local fallbacks.
                    if (!string.IsNullOrWhiteSpace(p.marker))
                        prefab = GetRegistryPrefab(p.marker);
                    if (prefab == null && p.kind != TileAdjacencyAtlas.PlaceableKind.None)
                        prefab = GetRegistryPrefab(p.kind.ToString());
                }
                if (prefab == null) continue; // respect registry authority

                var target = IsWorldPlaceable(p.kind) ? levelRoot : entities;

                var inst = Instantiate(prefab, target);
                Transform prefabT = prefab.transform;
                // Preserve prefab local position additively (grid + prefab local offset).
                inst.transform.localPosition = new Vector3(p.x, 0f, p.y) + prefabT.localPosition;
                if (Mathf.Abs(prefabT.localPosition.x) > 0.001f || Mathf.Abs(prefabT.localPosition.z) > 0.001f)
                    Debug.Log($"Spawn Placeable: cell=({p.x},{p.y}) prefab='{prefab.name}' localOffset={prefabT.localPosition} => worldLocal={inst.transform.localPosition}", prefab);
                inst.transform.localRotation = Quaternion.Euler(0f, TileAdjacencyAtlas.NormalizeRot(p.rotationIndex) * 90f, 0f) * prefabT.localRotation;
                inst.transform.localScale = prefabT.localScale;
            }
        }
    }

    static bool IsWorldPlaceable(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return false;
    }
    // ---------- Registry API (singleton-friendly via LevelRuntime.Active) ----------

    public GameObject GetRegistryPrefab(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        // 1) Try registry asset API methods directly (preferred).
        var fromApi = InvokeRegistryGetter(registryAsset, key);
        if (fromApi != null) return fromApi;
        return null;
    }

    public GameObject InstantiateRegistryPrefab(string key, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        var prefab = GetRegistryPrefab(key);
        if (prefab == null) return null;

        var targetParent = parent ?? ResolveEntitiesRoot();
        return Instantiate(prefab, position, rotation, targetParent);
    }

    public T InstantiateRegistryPrefab<T>(string key, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
    {
        var go = InstantiateRegistryPrefab(key, position, rotation, parent);
        return go != null ? go.GetComponent<T>() : null;
    }


    // Try to call public/protected API methods on the registry ScriptableObject before reflection digging.
    static GameObject InvokeRegistryGetter(ScriptableObject registry, string key)
    {
        if (registry == null || string.IsNullOrWhiteSpace(key)) return null;
        var t = registry.GetType();
        string[] methodNames =
        {
            "GetAsset", "Get", "GetItem", "GetByUid", "Find", "FindAsset", "Lookup"
        };

        foreach (var name in methodNames)
        {
            var m = t.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (m == null) continue;
            try
            {
                var obj = m.Invoke(registry, new object[] { key }) as UnityEngine.Object;
                var go = obj as GameObject;
                if (go == null && obj is Component comp) go = comp.gameObject;
                if (go != null) return go;
            }
            catch { /* ignore and continue */ }
        }
        return null;
    }
    void AutoConfigureFromChildren()
    {
        // Use child colliders; rely on explicit wall/floor layers you set in the scene.
        var cols = GetComponentsInChildren<Collider>(includeInactive: true);
        if (cols == null || cols.Length == 0)
            return;

        bool anyFloor = false;
        float minX = float.PositiveInfinity;
        float minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxZ = float.NegativeInfinity;

        // solidMask comes directly from configured wall layers.
        solidMask = wallLayers;

        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (c == null) continue;

            int layerMaskBit = 1 << c.gameObject.layer;

            // Floor tiles drive the grid origin extents.
            if ((floorLayers.value & layerMaskBit) != 0 ||
                (portalLayers.value & layerMaskBit) != 0 ||
                (doorLayers.value & layerMaskBit) != 0)
            {
                anyFloor = true;
                Bounds b = c.bounds;
                Vector3 p = b.center;
                if (p.x < minX) minX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.x > maxX) maxX = p.x;
                if (p.z > maxZ) maxZ = p.z;
                // include extents so we don't clip edges
                if (b.max.x > maxX) maxX = b.max.x;
                if (b.max.z > maxZ) maxZ = b.max.z;
                if (b.min.x < minX) minX = b.min.x;
                if (b.min.z < minZ) minZ = b.min.z;
            }
        }

        if (!anyFloor)
            return;

        // 1m grid, floor cubes are 1m and centered at integer+0.5.
        cellSize = 1f;

        float originX = Mathf.Floor(minX) + 0.5f;
        float originZ = Mathf.Floor(minZ) + 0.5f;
        gridOrigin = new Vector3(originX, transform.position.y, originZ);

        // Store bounds (XZ only, keep Y unchanged).
        levelBoundsXZ = new Bounds();
        float sizeX = (maxX - minX);
        float sizeZ = (maxZ - minZ);
        Vector3 center = new Vector3(minX + sizeX * 0.5f, transform.position.y, minZ + sizeZ * 0.5f);
        Vector3 size = new Vector3(sizeX, 0f, sizeZ);
        levelBoundsXZ.center = center;
        levelBoundsXZ.size = size;
    }

    void ConfigureGridGraph(AstarPath path)
    {
        if (path == null) return;

        // Ensure we have at least one GridGraph.
        var data = path.data;
        GridGraph graph = null;

        // Try to reuse an existing GridGraph.
        for (int i = 0; i < data.graphs.Length; i++)
        {
            graph = data.graphs[i] as GridGraph;
            if (graph != null)
                break;
        }

        if (graph == null)
        {
            graph = data.AddGraph(typeof(GridGraph)) as GridGraph;
        }

        if (graph == null)
            return;

        // All grid cells are exactly 1m apart.
        graph.isometricAngle = 0f;
        graph.uniformEdgeCosts = true;
        graph.nodeSize = 1f;

        // Use walls as unwalkable obstacles, with a reduced sampling footprint so 1-wide corridors stay open.
        graph.collision.use2D = false;
        graph.collision.collisionCheck = true;
        graph.collision.mask = wallLayers;    // only walls block
        graph.collision.thickRaycast = false;
        graph.collision.diameter = 0.5f;      // default is 1; smaller keeps nodes off wall corners
        graph.collision.collisionOffset = -0.45f; // inset more aggressively

        // Use floors only for height/grounding, not for blocking.
        graph.collision.heightCheck = true;
        // Floors + portals + doors are considered ground; they do NOT block.
        graph.collision.heightMask = floorLayers | portalLayers | doorLayers;
        graph.collision.fromHeight = 5f;
        graph.collision.height = 10f;

        // No extra erosion by default; corridors are already tight.
        graph.erodeIterations = 0;

        // Compute bounds in XZ from level children.
        var cols = GetComponentsInChildren<Collider>(includeInactive: true);
        if (cols == null || cols.Length == 0)
        {
            // Fallback: small graph around level root.
            graph.center = transform.position;
            graph.SetDimensions(32, 32, 1f);
            return;
        }

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minZ = float.PositiveInfinity;
        float maxZ = float.NegativeInfinity;

        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (c == null) continue;
            var b = c.bounds;
            if (b.min.x < minX) minX = b.min.x;
            if (b.max.x > maxX) maxX = b.max.x;
            if (b.min.z < minZ) minZ = b.min.z;
            if (b.max.z > maxZ) maxZ = b.max.z;
        }

        // Snap bounds to 1m grid.
        minX = Mathf.Floor(minX);
        minZ = Mathf.Floor(minZ);
        maxX = Mathf.Ceil(maxX);
        maxZ = Mathf.Ceil(maxZ);

        float sizeX = Mathf.Max(1f, maxX - minX);
        float sizeZ = Mathf.Max(1f, maxZ - minZ);

        int width  = Mathf.CeilToInt(sizeX / 1f);
        int depth  = Mathf.CeilToInt(sizeZ / 1f);

        // Center of the graph in world space.
        float centerX = minX + sizeX * 0.5f;
        float centerZ = minZ + sizeZ * 0.5f;

        // Offset so nodes are at floor centers, not at integer corners:
        //  - floor tiles are 1m cubes centered at x/z + 0.5
        //  - floor Y is -1, so put nodes around -0.5 in Y.
        centerX += 0.5f;
        centerZ += 0.5f;

        graph.center = new Vector3(centerX, -0.5f, centerZ);
        graph.SetDimensions(width, depth, 1f);

        // Optionally, you could set collision/height testing here to match your tiles.
    }
}
