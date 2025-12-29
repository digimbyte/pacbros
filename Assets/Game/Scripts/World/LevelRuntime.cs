using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding; // A* Project
using UnityEngine.Events;
using Core.Registry; // for Registry (Entities.asset)

/// <summary>
/// Runtime data + hooks for the currently loaded level instance.
/// Lives on the level root under WORLD/LevelRoot/<name>.
/// Grid agents (GridMotor, pathfinding, etc.) can bind to this and navmesh can be built per-level.
/// </summary>
public class LevelRuntime : MonoBehaviour
{
    public static LevelRuntime Active { get; private set; }
    [Header("Level List")]
    [Tooltip("Optional list of level atlases usable by this runtime. Use LoadLevel(index) to switch.")]
    public TileAdjacencyAtlas[] levels = Array.Empty<TileAdjacencyAtlas>();
    [Tooltip("Index of the currently loaded atlas in `levels` (-1 = none).")]
    [HideInInspector] public int activeLevelIndex = -1;
    [Header("Level Asset")]
    [Tooltip("TileAdjacencyAtlas to instantiate as level geometry under this runtime root.")]
    public TileAdjacencyAtlas levelAtlas;
    [Tooltip("Optional explicit container for spawned level geometry. Will be created if null.")]
    public Transform levelContainer;
    [Tooltip("Root that will hold runtime entities (players, coins, items, etc.). If null, a sibling/child named 'Entities' will be created or reused.")]
    public Transform entitiesRoot;
    [Header("Registries")]
    [Tooltip("Primary registry (Assets/Game/Entities.asset created via Core/Data/Registry). Must be a Core.Registry.Registry instance.")]
    public Registry entityRegistry;

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
    [Tooltip("If true, use A* pathfinding for enemies. If false, use simple marching algorithm.")]
    public bool useAStar = false;

    [Header("State")] 
    [Tooltip("True once the level has finished its Awake/initialisation and all registered motors have been notified.")]
    public bool isReady;

    [Header("Local Player")]
    [Tooltip("Camera used for the local player. If assigned, LevelRuntime will attach a CameraFollow to it and set the spawned player as the follow target.")]
    public Camera playerCamera;
    [Tooltip("If true, LevelRuntime will spawn the local Player prefab at the atlas 'player' spawn and assign the camera to follow it.")]
    public bool spawnLocalPlayer = true;
    [Tooltip("Registry key used when spawning the local player prefab from entityRegistry (e.g. 'Player').")]
    public string localPlayerRegistryKey = "Player";
    [Tooltip("Number of lives the local player starts with (each spawn/respawn consumes one).")]
    public int startingLives = 3;
    [Tooltip("If true, LevelRuntime will respawn the local player at the last chosen spawn point until lives are exhausted.")]
    public bool enableRespawn = true;
    [Header("Ghost Enemy Auto-Spawn")]
    [Tooltip("If true, LevelRuntime will spawn each ghost registry key once at random enemy spawn points when the level boots.")]
    public bool spawnGhostEnemiesOnAwake = true;
    [Tooltip("Registry keys (from entityRegistry) used for ghost auto-spawn.")]
    public string[] ghostEnemyRegistryKeys = new string[]
    {
        "Ghost_Green",
        "Ghost_Yellow",
        "Ghost_Red",
        "Ghost_Purple"
    };
    [Tooltip("If true, prefer EnemySpawnPoints flagged as ghostEnemySpawn; falls back to any enemy spawn if none exist.")]
    public bool restrictGhostAutoSpawnsToGhostMarkers = true;
    [Tooltip("If true, attempt to use unique spawn tiles per ghost until the pool is exhausted.")]
    public bool enforceUniqueGhostSpawnTiles = true;

    [Header("Multiplayer Role")]
    [Tooltip("If true, force this runtime to behave as a CLIENT even when no ClientSessionMarker is present in the scene.")]
    public bool forceClientMode = false;
    [Tooltip("Computed on Awake: true if this runtime is acting as a client (joined/guest session). If false, treated as host.")]
    [HideInInspector] public bool isClient = false;

    // Runtime instance for the local player (if spawned by this runtime).
    [HideInInspector] public GameObject localPlayerInstance;
    [HideInInspector] public int currentLives;
    [HideInInspector] public PlayerSpawnPoint lastPlayerSpawnPoint;

    [Header("UI/HUD")]
    [Tooltip("HUD GameObject to enable when the game is over.")]
    public GameObject gameOverHUD;
    [Tooltip("HUD GameObject to enable when the level is won (all coins collected).")]
    public GameObject winHUD;

    [Header("Game State")]
    [Tooltip("How often (seconds) to poll win/lose conditions. Lower = more responsive but slightly more CPU.")]
    public float gameStateCheckInterval = 0.5f;
    float _nextGameStateCheck = 0f;

    readonly List<GridMotor> _motors = new();
    bool _ghostEnemiesSpawned;

    [Header("Events")]
    [Tooltip("Invoked when the local player dies. Parameter: the local player GameObject.")]
    public UnityEvent<GameObject> onLocalPlayerDeath;

    /// <summary>
    /// Notify the runtime that the local player has died. Invokes `onLocalPlayerDeath`.
    /// </summary>
    public void NotifyLocalPlayerDeath(GameObject localPlayer)
    {
        if (localPlayer == null) return;
        try
        {
            onLocalPlayerDeath?.Invoke(localPlayer);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    [Tooltip("Invoked when the local player has no lives remaining. Parameter: the local player GameObject.")]
    public UnityEvent<GameObject> onLocalPlayerOutOfLives;

    [Tooltip("Invoked when the level is won (all coins collected). No parameters.")]
    public UnityEvent onLevelWon;

    /// <summary>
    /// Notify the runtime that the local player has run out of lives. Invokes `onLocalPlayerOutOfLives`.
    /// </summary>
    public void NotifyLocalPlayerOutOfLives(GameObject localPlayer)
    {
        if (localPlayer == null) return;
        try
        {
            onLocalPlayerOutOfLives?.Invoke(localPlayer);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

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
        PlayerTracker.EnsureInstance();

        // Determine host vs client role.
        // If a ClientSessionMarker singleton exists, consult its SessionMode.
        // If not, assume a purely local hosted game unless forceClientMode overrides.
        var session = ClientSessionMarker.Instance;
        if (session != null)
        {
            isClient = session.IsClient;
        }
        else
        {
            // No session object in play: treat as local host by default.
            isClient = false;
        }

        if (forceClientMode)
        {
            isClient = true;
        }

        if (isClient)
        {
            // In client mode we do not spawn the authoritative local player here;
            // that should be controlled by the netcode / host.
            spawnLocalPlayer = false;
            enableRespawn = false;
        }

        // Initialize lives for the local player (host-side only, but harmless if disabled above).
        if (startingLives < 0)
            startingLives = 0;
        currentLives = startingLives;

        // Instantiate the level geometry/placeables from atlas before scanning grid/collision.
        if (levelAtlas != null)
        {
            SpawnLevelFromAtlas(levelAtlas);
        }

        // If configured with a list of levels and no explicit atlas assigned, optionally load index 0.
        if ((levelAtlas == null || levelAtlas == default) && levels != null && levels.Length > 0)
        {
            LoadLevel(0);
        }

        if (spawnGhostEnemiesOnAwake)
        {
            SpawnInitialGhostEnemies();
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

        // Navmesh: grab or find AstarPath and configure after one frame (colliders ready).
        if (astar == null)
            astar = FindObjectOfType<AstarPath>();
        if (astar != null && buildNavmeshOnAwake)
            StartCoroutine(ConfigureAndScanNextFrame(astar, setup));

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

    /// <summary>
    /// Load a level by index from the configured `levels` array. This assigns `levelAtlas` and
    /// spawns the level geometry and placeables under the runtime roots.
    /// </summary>
    public void LoadLevel(int index)
    {
        if (levels == null || index < 0 || index >= levels.Length) return;
        activeLevelIndex = index;
        levelAtlas = levels[index];

        // Spawn the atlas content into the level container / entities root.
        SpawnLevelFromAtlas(levelAtlas);

        // Recompute grid/collision and navmesh as needed.
        AutoConfigureFromChildren();
        if (astar != null && buildNavmeshOnAwake)
        {
            StartCoroutine(ConfigureAndScanNextFrame(astar, GetComponentInChildren<LevelSetup>()));
        }
    }

    Transform ResolveEntitiesRoot()
    {
        if (entitiesRoot == null)
        {
            Debug.LogError("LevelRuntime: entitiesRoot is not assigned. Please assign the root that holds all entities.", this);
        }
        return entitiesRoot;
    }

    Transform ResolveLevelContainer(string nameHint = null)
    {
        if (levelContainer == null)
        {
            Debug.LogError("LevelRuntime: levelContainer is not assigned. Please assign the root that will hold spawned level geometry.", this);
        }
        return levelContainer;
    }

    void SpawnLevelFromAtlas(TileAdjacencyAtlas atlas)
    {
        if (atlas == null) return;

        var levelRoot = ResolveLevelContainer();
        // Create/ensure a child named after the atlas to hold all spawned tiles/placeables
        Transform atlasRoot = levelRoot != null ? levelRoot.Find(atlas.name) : null;
        if (atlasRoot == null && levelRoot != null)
        {
            var go = new GameObject(atlas.name);
            go.transform.SetParent(levelRoot, false);
            atlasRoot = go.transform;
        }

        var entities = ResolveEntitiesRoot();

        // Clear previous children to avoid duplicates on reload.
        if (atlasRoot != null)
        {
            for (int i = atlasRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(atlasRoot.GetChild(i).gameObject);
        }
        }

        // Tiles (world geometry)
        if (atlas.cells != null && atlasRoot != null)
        {
            for (int i = 0; i < atlas.cells.Count; i++)
            {
                var c = atlas.cells[i];
                if (c.tile == null) continue;

                var prefab = c.tile.gameObject;
                if (prefab == null) continue;

                var inst = Instantiate(prefab, atlasRoot);
                Transform prefabT = c.tile.transform;
                // Preserve prefab local position additively (grid + prefab local offset).
                inst.transform.localPosition = new Vector3(c.x, 0f, c.y) + prefabT.localPosition;
                // preserve prefab local position additively
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
                if (string.IsNullOrEmpty(p.kind))
                    continue;

                GameObject prefab = GetRegistryPrefab(p.kind);
                if (prefab == null)
                    continue;

                var inst = Instantiate(prefab, entities);
                if (inst != null)
                    inst.name = $"{prefab.name}<{p.kind}>";
                Transform prefabT = prefab.transform;
                inst.transform.localPosition = new Vector3(p.x, 0f, p.y) + prefabT.localPosition;
                inst.transform.localRotation = Quaternion.Euler(0f, TileAdjacencyAtlas.NormalizeRot(p.rotationIndex) * 90f, 0f) * prefabT.localRotation;
                inst.transform.localScale = prefabT.localScale;

                // If this is a player spawn marker kind, ensure it carries a PlayerSpawnPoint component
                // so LevelSetup can discover it and choose a spawn for the local player.
                if (string.Equals(p.kind, TileAdjacencyAtlas.PlaceableKind.SpawnPlayer, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.kind, "SpawnPoint", StringComparison.OrdinalIgnoreCase))
                {
                    var marker = inst.GetComponent<PlayerSpawnPoint>();
                    if (marker == null)
                    {
                        marker = inst.AddComponent<PlayerSpawnPoint>();
                        marker.playerIndex = -1;   // default: any player
                        marker.weight = 1f;
                    }
                }
                else if (string.Equals(p.kind, TileAdjacencyAtlas.PlaceableKind.Enemy, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(p.kind, "EnemySpawn", StringComparison.OrdinalIgnoreCase))
                {
                    var marker = inst.GetComponent<EnemySpawnPoint>();
                    if (marker == null)
                    {
                        marker = inst.AddComponent<EnemySpawnPoint>();
                        marker.enemyTypeId = -1;
                        marker.weight = 1f;
                    }

                    if (restrictGhostAutoSpawnsToGhostMarkers && !marker.ghostEnemySpawn)
                    {
                        marker.ghostEnemySpawn = true;
                    }
                }
            }
        }
    }


    // ---------- Registry API (singleton-friendly via LevelRuntime.Active) ----------

    public GameObject GetRegistryPrefab(string uid)
    {
        if (entityRegistry == null)
        {
            Debug.LogError($"LevelRuntime '{name}' has no entityRegistry assigned; cannot resolve uid '{uid}'.", this);
            return null; // explicit config error; avoids immediate null-ref crash
        }

        // Core.Registry.Registry already handles missing UIDs by returning its default prefab.
        return entityRegistry.GetPrefabByUID(uid);
    }

    public GameObject InstantiateRegistryPrefab(string key, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        var prefab = GetRegistryPrefab(key);
        if (prefab == null) return null;

        var targetParent = parent ?? ResolveEntitiesRoot();
        var go = Instantiate(prefab, position, rotation, targetParent);
        if (go != null)
            go.name = $"{prefab.name}<{key}>";
        return go;
    }

    public T InstantiateRegistryPrefab<T>(string key, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
    {
        var go = InstantiateRegistryPrefab(key, position, rotation, parent);
        return go != null ? go.GetComponent<T>() : null;
    }

    /// <summary>
    /// Spawn (or respawn) the local player at the given spawn point, consuming one life.
    /// Stores the spawn as the current respawn point and wires the camera rig to the new instance.
    /// </summary>
    public PlayerEntity SpawnLocalPlayerAt(PlayerSpawnPoint spawnPoint, int playerIndex = 0, bool isRespawn = false)
    {
        if (!spawnLocalPlayer || spawnPoint == null)
            return null;

        if (!enableRespawn && isRespawn)
            return null;

        // Initialise lives on first use if something else has not already.
        if (currentLives <= 0 && startingLives > 0 && !isRespawn && localPlayerInstance == null)
        {
            currentLives = startingLives;
        }

        if (currentLives <= 0)
        {
            Debug.Log("LevelRuntime: Cannot spawn local player; no lives remaining.", this);
            return null;
        }

        // Consume a life only when this is an explicit respawn. Initial level
        // spawns should not decrement the player's lives so the HUD shows the
        // configured startingLives value.
        if (isRespawn)
            currentLives--;

        // Clean up any previous instance.
        if (localPlayerInstance != null)
        {
            Destroy(localPlayerInstance);
            localPlayerInstance = null;
        }

        Vector3 spawnPos = spawnPoint.transform.position;
        Quaternion spawnRot = Quaternion.identity;

        var playerEntity = InstantiateRegistryPrefab<PlayerEntity>(localPlayerRegistryKey, spawnPos, spawnRot);
        if (playerEntity == null)
            return null;

        localPlayerInstance = playerEntity.gameObject;
        playerEntity.playerIndex = playerIndex;
        playerEntity.isLocal = true;
        var lifeController = localPlayerInstance.GetComponent<PlayerLifeController>();
        if (lifeController != null)
            lifeController.RegisterSpawnPoint(spawnPoint);

        // Ensure PlayerController (if present) is wired to this motor.
        var motor = localPlayerInstance.GetComponent<GridMotor>();
        var controller = localPlayerInstance.GetComponent<PlayerController>();
        if (controller != null && controller.motor == null)
        {
            controller.motor = motor;
        }

        // Align the grid motor precisely with the spawn to avoid half-cell offsets.
        if (motor != null)
        {
            motor.HardTeleport(spawnPos);
        }
        else
        {
            localPlayerInstance.transform.position = spawnPos;
        }

        lastPlayerSpawnPoint = spawnPoint;

        AssignCameraToPlayer(localPlayerInstance.transform);

        return playerEntity;
    }

    /// <summary>
    /// Attempt to respawn the local player at the last stored respawn point, if any lives remain.
    /// </summary>
    public bool TryRespawnLocalPlayer(int playerIndex = 0)
    {
        if (!enableRespawn)
            return false;
        if (lastPlayerSpawnPoint == null)
            return false;
        if (currentLives <= 0)
            return false;

        var player = SpawnLocalPlayerAt(lastPlayerSpawnPoint, playerIndex, isRespawn: true);
        return player != null;
    }

    void AssignCameraToPlayer(Transform playerTransform)
    {
        if (playerTransform == null)
            return;

        // Prefer the project's CameraControl rig when present.
        var cc = FindObjectOfType<CameraControl>();
        if (cc != null)
        {
            cc.target = playerTransform;
            // If the rig exposes a pivot, snap it to the player's position so the camera starts aligned.
            if (cc.pivot != null)
            {
                cc.pivot.position = playerTransform.position;
            }
            return;
        }

        // No CameraControl found — if a `playerCamera` is assigned, log guidance.
        if (playerCamera != null)
        {
            Debug.LogWarning("LevelRuntime: No CameraControl found in scene; assign the camera rig to set follow target. `playerCamera` is present but LevelRuntime will not auto-wire it.", this);
        }
        else
        {
            Debug.LogWarning("LevelRuntime: No CameraControl found in scene and no `playerCamera` is assigned. Camera will not follow the spawned player.", this);
        }
    }


    // No reflection into fields: registry must implement a direct API.
    void AutoConfigureFromChildren()
    {
        // Use colliders from the levelContainer only (entities are dynamic/non-world and excluded).
        var cols = levelContainer != null
            ? levelContainer.GetComponentsInChildren<Collider>(includeInactive: true)
            : Array.Empty<Collider>();

        if (cols == null || cols.Length == 0)
        {
            // Fallback: derive grid origin/bounds from atlas dimensions.
            if (levelAtlas != null)
            {
                cellSize = 1f;
                float fOriginX = 0.5f;
                float fOriginZ = 0.5f;
                gridOrigin = new Vector3(fOriginX, transform.position.y, fOriginZ);

                float fSizeX = Mathf.Max(1f, levelAtlas.width);
                float fSizeZ = Mathf.Max(1f, levelAtlas.height);
                levelBoundsXZ = new Bounds(
                    new Vector3((fSizeX) * 0.5f, transform.position.y, (fSizeZ) * 0.5f),
                    new Vector3(fSizeX, 0f, fSizeZ));
            }
            return;
        }

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
        {
            // No floor/portal/door colliders detected on the configured layers.
            // Treat the atlas as the source of truth for bounds so GridMotor's OOB logic matches the level data.
            EnsureBoundsCoverAtlas();
            return;
        }

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

        // Always ensure runtime bounds cover the atlas extents (atlas is the authoritative layout).
        // This prevents tiny bounds (e.g. 1x1) when layers are misconfigured or colliders are missing.
        EnsureBoundsCoverAtlas();
    }

    void EnsureBoundsCoverAtlas()
    {
        if (levelAtlas == null)
            return;

        // Atlas cell coordinates map directly to world XZ (1 unit per cell).
        // Bounds should include centers at x/z = 0..width-1 and 0..height-1.
        // That yields min=-0.5, max=width-0.5 (and same for Z).
        float w = Mathf.Max(1f, levelAtlas.width);
        float h = Mathf.Max(1f, levelAtlas.height);

        float y = transform.position.y;
        Vector3 atlasMin = new Vector3(-0.5f, y, -0.5f);
        Vector3 atlasMax = new Vector3(w - 0.5f, y, h - 0.5f);

        // If bounds are unset, set them directly.
        if (levelBoundsXZ.size == Vector3.zero)
        {
            levelBoundsXZ = new Bounds(
                new Vector3((atlasMin.x + atlasMax.x) * 0.5f, y, (atlasMin.z + atlasMax.z) * 0.5f),
                new Vector3(w, 0f, h));
            return;
        }

        // Expand existing bounds to include atlas bounds (never shrink).
        Vector3 min = levelBoundsXZ.min;
        Vector3 max = levelBoundsXZ.max;
        if (atlasMin.x < min.x) min.x = atlasMin.x;
        if (atlasMin.z < min.z) min.z = atlasMin.z;
        if (atlasMax.x > max.x) max.x = atlasMax.x;
        if (atlasMax.z > max.z) max.z = atlasMax.z;

        levelBoundsXZ.SetMinMax(min, max);
    }

    void ConfigureGridGraph(AstarPath path)
    {
        if (path == null) return;
    }

    System.Collections.IEnumerator ConfigureAndScanNextFrame(AstarPath path, LevelSetup setup)
    {
        // wait one frame so Unity creates colliders on spawned tiles
        yield return null;

        ConfigureGridGraphFromAtlas(path);

        if (path != null)
        {
            path.Scan();
            if (setup != null)
            {
                setup.RegisterAstarPortalEdges();
                // Let LevelSetup choose a spawn and ask this runtime to spawn the local player there.
                setup.PositionPlayersAndCamera();
            }
        }
    }

    void ConfigureGridGraphFromAtlas(AstarPath path)
    {
        if (path == null || levelAtlas == null) return;

        // All grid cells are exactly 1m apart.
        GridGraph graph = EnsureGridGraph(path);
        if (graph == null) return;

        // Use walls as unwalkable obstacles, with a reduced sampling footprint so 1-wide corridors stay open.
        graph.collision.use2D = false;
        graph.collision.collisionCheck = true;
        graph.collision.mask = wallLayers;    // only walls block
        graph.collision.thickRaycast = false;
        graph.collision.diameter = 0.5f;      // default is 1; smaller keeps nodes off wall corners
        graph.collision.collisionOffset = -0.45f; // inset more aggressively

        graph.collision.heightCheck = true;
        graph.collision.heightMask = floorLayers | portalLayers | doorLayers;
        graph.collision.fromHeight = 5f;
        graph.collision.height = 10f;
        graph.erodeIterations = 0;

        // Size from atlas first.
        float fSizeX = Mathf.Max(1f, levelAtlas.width);
        float fSizeZ = Mathf.Max(1f, levelAtlas.height);
        int gWidth = Mathf.CeilToInt(fSizeX / 1f);
        int gDepth = Mathf.CeilToInt(fSizeZ / 1f);
        float gCenterX = fSizeX * 0.5f + 0.5f;
        float gCenterZ = fSizeZ * 0.5f + 0.5f;

        // If colliders exist, expand (never shrink) to cover them.
        var cols = levelContainer != null
            ? levelContainer.GetComponentsInChildren<Collider>(includeInactive: true)
            : Array.Empty<Collider>();
        if (cols != null && cols.Length > 0)
        {
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
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
            float cSizeX = Mathf.Max(fSizeX, maxX - minX);
            float cSizeZ = Mathf.Max(fSizeZ, maxZ - minZ);
            gWidth = Mathf.CeilToInt(cSizeX / 1f);
            gDepth = Mathf.CeilToInt(cSizeZ / 1f);
            gCenterX = Mathf.Max(gCenterX, minX + cSizeX * 0.5f + 0.5f);
            gCenterZ = Mathf.Max(gCenterZ, minZ + cSizeZ * 0.5f + 0.5f);
        }

        graph.center = new Vector3(gCenterX, -0.5f, gCenterZ);
        graph.SetDimensions(gWidth, gDepth, 1f);
    }

    void LateUpdate()
    {
        // Poll win/lose conditions periodically.
        if (Time.time >= _nextGameStateCheck)
        {
            _nextGameStateCheck = Time.time + Mathf.Max(0.05f, gameStateCheckInterval);
            EvaluateGameState();
        }
    }

    bool _gameOverTriggered = false;
    bool _winTriggered = false;

    void EvaluateGameState()
    {
        if (_gameOverTriggered || _winTriggered) return;

        // Win condition: no remaining ItemPickup objects (coins) in the entities root.
        int remainingCoins = CountRemainingCoins();
        if (remainingCoins <= 0)
        {
            _winTriggered = true;
            if (winHUD != null) winHUD.SetActive(true);
            try
            {
                onLevelWon?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            return;
        }

        // Game over condition: all players dead AND no lives remaining.
        bool allPlayersDead = AreAllPlayersDead();
        if (allPlayersDead && currentLives <= 0)
        {
            _gameOverTriggered = true;
            if (gameOverHUD != null) gameOverHUD.SetActive(true);
        }
    }

    public int CountRemainingCoins()
    {
        // Consider ItemPickup instances that are active in the scene and not consumed.
        var pickups = FindObjectsOfType<ItemPickup>();
        int count = 0;
        for (int i = 0; i < pickups.Length; i++)
        {
            var p = pickups[i];
            if (p == null) continue;
            if (!p.gameObject.activeInHierarchy) continue;
            count++;
        }
        return count;
    }

    bool AreAllPlayersDead()
    {
        var players = FindObjectsOfType<PlayerEntity>();
        if (players == null || players.Length == 0)
            return false; // no players means not a local-game over decision here

        for (int i = 0; i < players.Length; i++)
        {
            var p = players[i];
            if (p == null) continue;
            // If any player is not marked dead, we're not game over.
            if (!p.isDead) return false;
        }
        return true;
    }

    GridGraph EnsureGridGraph(AstarPath path)
    {
        if (path == null) return null;
        var data = path.data;
        GridGraph graph = null;
        for (int i = 0; i < data.graphs.Length; i++)
        {
            graph = data.graphs[i] as GridGraph;
            if (graph != null)
                break;
        }
        if (graph == null)
            graph = data.AddGraph(typeof(GridGraph)) as GridGraph;
        if (graph == null) return null;

        graph.isometricAngle = 0f;
        graph.uniformEdgeCosts = true;
        graph.nodeSize = 1f;
        return graph;
    }

    void SpawnInitialGhostEnemies()
    {
        if (_ghostEnemiesSpawned)
            return;

        _ghostEnemiesSpawned = true;

        if (ghostEnemyRegistryKeys == null || ghostEnemyRegistryKeys.Length == 0)
            return;

        var spawnPool = AcquireEnemySpawnPoints(restrictGhostAutoSpawnsToGhostMarkers);
        if (spawnPool == null || spawnPool.Count == 0)
        {
            Debug.LogWarning("LevelRuntime: No EnemySpawnPoint instances available for ghost auto-spawn.", this);
            return;
        }

        var uniquePool = enforceUniqueGhostSpawnTiles ? new List<EnemySpawnPoint>(spawnPool) : spawnPool;

        for (int i = 0; i < ghostEnemyRegistryKeys.Length; i++)
        {
            string rawKey = ghostEnemyRegistryKeys[i];
            if (string.IsNullOrWhiteSpace(rawKey))
                continue;

            string key = rawKey.Trim();
            EnemySpawnPoint spawn = null;

            if (enforceUniqueGhostSpawnTiles && uniquePool.Count > 0)
            {
                spawn = ChooseRandomEnemySpawn(uniquePool, consume: true);
            }

            if (spawn == null)
            {
                spawn = ChooseRandomEnemySpawn(spawnPool, consume: false);
            }

            if (spawn == null)
                break;

            Vector3 position = spawn.transform.position;
            Quaternion rotation = spawn.transform.rotation;
            GameObject ghost = InstantiateRegistryPrefab(key, position, rotation);
            if (ghost == null)
            {
                Debug.LogError($"LevelRuntime: Failed to auto-spawn ghost '{key}'. Verify the registry entry exists.", this);
            }
        }
    }

    List<EnemySpawnPoint> AcquireEnemySpawnPoints(bool preferGhostMarkers)
    {
        var primary = GatherEnemySpawnPoints(preferGhostMarkers);
        if (primary.Count == 0 && preferGhostMarkers)
        {
            return GatherEnemySpawnPoints(false);
        }
        return primary;
    }

    List<EnemySpawnPoint> GatherEnemySpawnPoints(bool ghostOnly)
    {
        var result = new List<EnemySpawnPoint>();

        var registry = FindObjectOfType<SpawnPointsRegistry>(includeInactive: true);
        if (registry != null)
        {
            var registered = registry.GetEnemySpawns(-1, ghostOnly ? true : (bool?)null);
            if (registered != null)
            {
                for (int i = 0; i < registered.Count; i++)
                {
                    var spawn = registered[i];
                    if (spawn == null)
                        continue;
                    if (ghostOnly && !spawn.ghostEnemySpawn)
                        continue;
                    if (!result.Contains(spawn))
                        result.Add(spawn);
                }
            }
        }

        if (result.Count == 0)
        {
            var found = GameObject.FindObjectsOfType<EnemySpawnPoint>(includeInactive: true);
            if (found != null)
            {
                for (int i = 0; i < found.Length; i++)
                {
                    var spawn = found[i];
                    if (spawn == null)
                        continue;
                    if (ghostOnly && !spawn.ghostEnemySpawn)
                        continue;
                    if (!result.Contains(spawn))
                        result.Add(spawn);
                }
            }
        }

        if (result.Count == 0 && ghostOnly)
        {
            return GatherEnemySpawnPoints(false);
        }

        return result;
    }

    EnemySpawnPoint ChooseRandomEnemySpawn(List<EnemySpawnPoint> pool, bool consume)
    {
        if (pool == null || pool.Count == 0)
            return null;

        EnemySpawnPoint chosen = null;
        float totalWeight = 0f;
        bool anyPositive = false;
        for (int i = pool.Count - 1; i >= 0; i--)
        {
            var spawn = pool[i];
            if (spawn == null)
            {
                pool.RemoveAt(i);
                continue;
            }
            float w = Mathf.Max(0f, spawn.weight);
            if (w > 0f)
            {
                anyPositive = true;
                totalWeight += w;
            }
        }

        if (pool.Count == 0)
            return null;

        if (anyPositive && totalWeight > 0f)
        {
            float r = UnityEngine.Random.Range(0f, totalWeight);
            float accum = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                var spawn = pool[i];
                float w = Mathf.Max(0f, spawn.weight);
                if (w <= 0f) continue;
                accum += w;
                if (r <= accum)
                {
                    chosen = spawn;
                    break;
                }
            }
        }

        if (chosen == null)
        {
            int idx = UnityEngine.Random.Range(0, pool.Count);
            chosen = pool[idx];
            if (consume)
                pool.RemoveAt(idx);
            return chosen;
        }

        if (consume)
        {
            pool.Remove(chosen);
        }

        return chosen;
    }
}
