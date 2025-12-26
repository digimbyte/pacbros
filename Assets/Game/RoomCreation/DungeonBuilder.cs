using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DungeonBuilder : MonoBehaviour
{
    [Header("Generator Reference")]
    [SerializeField] private WaveCollapseGeneration generator;
    [Header("Placement / Origin")]
    [SerializeField] private Transform originTransform;

    [Header("Tiles")]
    [SerializeField] private TilesDatabase tilesDatabase;
    [SerializeField] private TilesDatabase borderTilesDatabase;
    [SerializeField] private TilesDatabase borderTunnelDatabase;
    [SerializeField] private Tile fallbackTile;
    

    [Header("Map Settings")]
    [SerializeField, Min(2)] private int width = 21;
    [SerializeField, Min(2)] private int height = 21;
    [SerializeField] private bool allowBorderTunnels = true;
    [Header("Type Limits")]
    [Tooltip("Number of border-tunnel pairs to place (1 pair == 2 tunnels)")]
    [SerializeField, Min(0)] private int borderTunnelPairs = 0;
    [Tooltip("Maximum number of doors allowed in the map. 0 = no limit.")]
    [SerializeField, Min(0)] private int maxDoors = 0;
    [Tooltip("Maximum number of portal pairs allowed in the map (1 pair == 2 portals). 0 = no limit.")]
    [SerializeField, Min(0)] private int maxPortalPairs = 0;

    [Header("Anchors")]
    [SerializeField] private bool autoPlaceAnchors = true;
    [SerializeField] private Vector2Int startCoords = new Vector2Int(1, 1);
    [SerializeField] private Vector2Int prisonCoords = new Vector2Int(2, 2);
    [SerializeField] private Tile startTile;
    [SerializeField] private Tile prisonTile;
    [SerializeField] private GameObject prisonPrefab;
    [SerializeField] private Vector3 prisonPrefabOffset = Vector3.zero;

    [Header("Seeding")]
    [SerializeField] private bool randomizeSeedEachBuild = false;
    [SerializeField] private int seed = 12345;

    [Header("Generation Flow")]
    [SerializeField] private bool buildOnAwake = true;
    [Tooltip("If enabled, generation can be stepped incrementally from the inspector.")]
    [SerializeField] private bool iterative = false;

    private const string GeneratedTilesRootName = "__GeneratedTiles";
    private const string GeneratedPropsRootName = "__GeneratedProps";

    [NonSerialized] private WaveCollapseEngine.IncrementalRun iterativeRun;
    [NonSerialized] private SpawnedCell[] spawned;
    [NonSerialized] private GameObject prisonInstance;
    [NonSerialized] private GameObject prisonPrefabLast;

    private struct SpawnedCell
    {
        public GameObject instance;
        public Tile tile;
        public int rotation;
        public bool skipSpawn;
    }

    public bool IterativeEnabled => iterative;
    public bool IterativeIsRunning => iterativeRun != null;
    public bool IterativeIsComplete => iterativeRun != null && iterativeRun.IsComplete;
    public int IterativeStepIndex => iterativeRun != null ? iterativeRun.StepIndex : 0;

    private void Awake()
    {
        if (buildOnAwake)
        {
            Build();
        }
    }

    public void Build()
    {
        // Build a data-only config for the Wave Collapse Engine. The engine
        // will not touch the scene; it only returns a Result we can instantiate.
        WaveCollapseEngine.Config cfg = BuildConfig();

        // Full run
        iterativeRun = null;
        spawned = null;
        var result = WaveCollapseEngine.Generate(cfg);

        ApplyResultToScene(result);
        EnsurePrisonInstance();
    }

    public void BeginIterative()
    {
        WaveCollapseEngine.Config cfg = BuildConfig();
        iterativeRun = new WaveCollapseEngine.IncrementalRun(cfg);
        iterativeRun.Initialize();
        spawned = null;
        ApplyResultToScene(iterativeRun.BuildResult());
        EnsurePrisonInstance();
    }

    public void StepIterative()
    {
        if (iterativeRun == null)
        {
            BeginIterative();
            return;
        }

        if (iterativeRun.Step())
        {
            ApplyResultToScene(iterativeRun.BuildResult());
            EnsurePrisonInstance();
        }
    }

    public void RunIterativeToEnd(int maxSteps = 10000)
    {
        if (iterativeRun == null)
        {
            BeginIterative();
        }

        int guard = Mathf.Max(0, maxSteps);
        while (guard-- > 0 && iterativeRun.Step())
        {
            // keep stepping
        }

        ApplyResultToScene(iterativeRun.BuildResult());
        EnsurePrisonInstance();
    }

    public void ResetIterative()
    {
        iterativeRun = null;
        spawned = null;
    }

    private Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform existing = parent.Find(childName);
        if (existing != null) return existing;
        var go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private Transform GetTilesRoot()
    {
        return GetOrCreateChild(GetParentTransform(), GeneratedTilesRootName);
    }

    private Transform GetPropsRoot()
    {
        return GetOrCreateChild(GetParentTransform(), GeneratedPropsRootName);
    }

    private void EnsurePrisonInstance()
    {
        if (prisonPrefab == null)
        {
            if (prisonInstance != null)
            {
                GameObject.DestroyImmediate(prisonInstance);
                prisonInstance = null;
            }
            prisonPrefabLast = null;
            return;
        }

        if (prisonPrefabLast != prisonPrefab)
        {
            if (prisonInstance != null)
            {
                GameObject.DestroyImmediate(prisonInstance);
                prisonInstance = null;
            }
            prisonPrefabLast = prisonPrefab;
        }

        if (prisonInstance == null)
        {
            prisonInstance = GameObject.Instantiate(prisonPrefab, GetPropsRoot());
        }

        int w = Mathf.Max(1, width);
        int h = Mathf.Max(1, height);
        Vector2Int prison = autoPlaceAnchors
            ? new Vector2Int(Mathf.Max(1, w - 2), Mathf.Max(1, h - 2))
            : ClampToInterior(prisonCoords, w, h);

        prisonInstance.transform.localPosition = new Vector3(prison.x, 0f, prison.y) + prisonPrefabOffset;
        prisonInstance.transform.localRotation = Quaternion.identity;
    }

    public void RebuildWithNewSeed()
    {
        seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        Build();
    }

    private Transform GetParentTransform()
    {
        return originTransform != null ? originTransform : this.transform;
    }

    private WaveCollapseEngine.Config BuildConfig()
    {
        int cfgWidth = Mathf.Max(1, width);
        int cfgHeight = Mathf.Max(1, height);
        int cfgSeed = randomizeSeedEachBuild ? UnityEngine.Random.Range(int.MinValue, int.MaxValue) : seed;

        var allTiles = ExtractTiles(tilesDatabase);
        var interiorTiles = allTiles.Where(t => t != null && t.tileType != Tile.TileType.Border && t.tileType != Tile.TileType.BorderTunnel).ToArray();
        var borderFromMain = allTiles.Where(t => t != null && t.tileType == Tile.TileType.Border).ToArray();
        var borderTunnelFromMain = allTiles.Where(t => t != null && t.tileType == Tile.TileType.BorderTunnel).ToArray();

        Tile[] borderTiles = ConcatArrays(ExtractTiles(borderTilesDatabase), borderFromMain);
        Tile[] borderTunnelTiles = ConcatArrays(ExtractTiles(borderTunnelDatabase), borderTunnelFromMain);

        return new WaveCollapseEngine.Config
        {
            width = cfgWidth,
            height = cfgHeight,
            seed = cfgSeed,
            tileSet = interiorTiles,
            borderTiles = borderTiles,
            borderTunnelTiles = borderTunnelTiles,
            fallbackTile = fallbackTile != null ? fallbackTile : (tilesDatabase != null ? tilesDatabase.defaultTile : null),
            forcedCells = BuildForcedCells(cfgWidth, cfgHeight, cfgSeed, interiorTiles, borderTunnelTiles),
            allowBorderTunnels = allowBorderTunnels,
            // When >0, treat these as REQUIRED counts by pre-placing them as forced cells.
            // The max values still act as a hard cap so WFC cannot add extras.
            maxDoorCount = maxDoors > 0 ? maxDoors : -1,
            maxBorderTunnelCount = borderTunnelPairs > 0 ? borderTunnelPairs * 2 : -1,
            maxPortalCount = maxPortalPairs > 0 ? maxPortalPairs * 2 : -1
        };
    }

    private void ApplyResultToScene(WaveCollapseEngine.Result result)
    {
        Transform tilesRoot = GetTilesRoot();

        // reset spawn cache if dimensions changed
        int expected = Mathf.Max(1, result.width) * Mathf.Max(1, result.height);
        if (spawned == null || spawned.Length != expected)
        {
            // Clear only generated tiles (avoid destroying unrelated children / props).
            for (int i = tilesRoot.childCount - 1; i >= 0; i--)
            {
                var c = tilesRoot.GetChild(i);
                GameObject.DestroyImmediate(c.gameObject);
            }
            spawned = new SpawnedCell[expected];
        }

        for (int i = 0; i < result.cells.Length; i++)
        {
            var cell = result.cells[i];
            ref SpawnedCell s = ref spawned[i];

            bool shouldExist = cell.tile != null && !cell.skipSpawn;
            if (!shouldExist)
            {
                if (s.instance != null)
                {
                    GameObject.DestroyImmediate(s.instance);
                    s.instance = null;
                    s.tile = null;
                    s.rotation = 0;
                    s.skipSpawn = false;
                }
                continue;
            }

            bool needsRebuild = s.instance == null || s.tile != cell.tile || s.rotation != cell.rotation;
            if (needsRebuild)
            {
                if (s.instance != null)
                {
                    GameObject.DestroyImmediate(s.instance);
                }

                // Preserve the prefab's authored local transform, and apply the grid placement as an offset.
                Transform prefabT = cell.tile.transform;
                Vector3 prefabLocalPos = prefabT.localPosition;
                Quaternion prefabLocalRot = prefabT.localRotation;
                Vector3 prefabLocalScale = prefabT.localScale;

                GameObject instance = GameObject.Instantiate(cell.tile.gameObject, tilesRoot);
                Transform t = instance.transform;
                t.localPosition = new Vector3(cell.coords.x, 0f, cell.coords.y) + prefabLocalPos;
                t.localRotation = Quaternion.Euler(0f, cell.rotation * 90f, 0f) * prefabLocalRot;
                t.localScale = prefabLocalScale;

                s.instance = instance;
                s.tile = cell.tile;
                s.rotation = cell.rotation;
                s.skipSpawn = cell.skipSpawn;
            }
            else
            {
                // still keep transform in sync (coords could be same, but cheap)
                Transform prefabT = cell.tile.transform;
                Vector3 prefabLocalPos = prefabT.localPosition;
                Quaternion prefabLocalRot = prefabT.localRotation;
                Vector3 prefabLocalScale = prefabT.localScale;

                Transform t = s.instance.transform;
                t.localPosition = new Vector3(cell.coords.x, 0f, cell.coords.y) + prefabLocalPos;
                t.localRotation = Quaternion.Euler(0f, cell.rotation * 90f, 0f) * prefabLocalRot;
                t.localScale = prefabLocalScale;
            }
        }

        // prune any extra children not tracked (e.g. if someone manually adds children)
        // (optional: skip; we'd rather not destroy user objects)
    }

    private List<WaveCollapseEngine.ForcedCell> BuildForcedCells(int w, int h, int cfgSeed, Tile[] interiorTiles, Tile[] borderTunnelTiles)
    {
        var list = new List<WaveCollapseEngine.ForcedCell>();

        Vector2Int start = autoPlaceAnchors ? new Vector2Int(1, 1) : startCoords;
        Vector2Int prison = autoPlaceAnchors ? new Vector2Int(Mathf.Max(1, w - 2), Mathf.Max(1, h - 2)) : prisonCoords;

        start = ClampToInterior(start, w, h);
        prison = ClampToInterior(prison, w, h);

        // Track occupied special cells so doors/portals/tunnels don't stack.
        // "At least 1 block apart" => not adjacent => Manhattan distance >= 2.
        const int minSpecialManhattan = 2;
        var occupied = new List<Vector2Int>();

        void AddForced(Vector2Int c, Tile t, bool skip, bool lockRotation)
        {
            if (t == null) return;
            list.Add(new WaveCollapseEngine.ForcedCell
            {
                coords = c,
                tile = t,
                skipSpawn = skip,
                rotation = 0,
                lockRotation = lockRotation
            });
            occupied.Add(c);
        }

        if (startTile != null)
        {
            // Anchor tiles should not randomly rotate.
            AddForced(start, startTile, skip: false, lockRotation: true);
        }

        if (prisonTile != null)
        {
            // Anchor tiles should not randomly rotate.
            AddForced(prison, prisonTile, skip: prisonPrefab != null, lockRotation: true);
        }

        var rng = new System.Random(cfgSeed);

        // 1) Border tunnels: pick the slots first (border infill happens inside WaveCollapseEngine).
        int tunnelCount = (allowBorderTunnels && borderTunnelPairs > 0) ? borderTunnelPairs * 2 : 0;
        if (tunnelCount > 0)
        {
            var tunnelTiles = (borderTunnelTiles ?? System.Array.Empty<Tile>())
                .Where(t => t != null && t.tileType == Tile.TileType.BorderTunnel)
                .ToArray();

            if (tunnelTiles.Length > 0)
            {
                int pairs = tunnelCount / 2;

                // Prefer classic Pac-style left/right pairs (same Y) when possible.
                if (w >= 3 && h >= 3)
                {
                    var yCandidates = new List<int>();
                    for (int y = 1; y <= h - 2; y++) yCandidates.Add(y);

                    // Shuffle so ties don't bias.
                    ShuffleInPlace(yCandidates, rng);

                    int placedPairs = 0;
                    foreach (int y in yCandidates)
                    {
                        if (placedPairs >= pairs) break;

                        Vector2Int left = new Vector2Int(0, y);
                        Vector2Int right = new Vector2Int(w - 1, y);

                        if (!IsFarEnough(left, occupied, minSpecialManhattan) || !IsFarEnough(right, occupied, minSpecialManhattan))
                            continue;

                        // Score: noise + distance-from-corners (keeps tunnels away from corners)
                        float score = Hash01(cfgSeed, 0, y, 991) + 0.35f * BorderCornerDistance01(w, h, left);
                        if (score < 0.35f) continue; // light gating so we don't pack them all in one band

                        Tile tunnelTile = tunnelTiles[rng.Next(tunnelTiles.Length)];
                        // Border tunnels: tile is fixed here, rotation resolved by WFC against neighbors.
                        AddForced(left, tunnelTile, skip: false, lockRotation: false);
                        AddForced(right, tunnelTile, skip: false, lockRotation: false);
                        placedPairs++;
                    }

                    // Fallback: if we couldn't place enough pairs, place remaining on any border slots.
                    int remaining = pairs - placedPairs;
                    if (remaining > 0)
                    {
                        var borderCandidates = BuildBorderCandidates(w, h);
                        PlaceBorderTunnelsFallback(borderCandidates, remaining, tunnelTiles, occupied, minSpecialManhattan, w, h, cfgSeed, rng,
                            (c, t, skip) => AddForced(c, t, skip, lockRotation: false));
                    }
                }
            }
        }

        // Build interior candidates once (exclude border).
        var interiorCandidates = new List<Vector2Int>();
        for (int y = 1; y <= h - 2; y++)
        {
            for (int x = 1; x <= w - 2; x++)
            {
                var c = new Vector2Int(x, y);
                // keep anchors clear
                if (c == start || c == prison) continue;
                interiorCandidates.Add(c);
            }
        }

        // 2) Portals then doors (spaced across all specials).
        int portalCount = maxPortalPairs > 0 ? maxPortalPairs * 2 : 0;
        if (portalCount > 0)
        {
            var portalTiles = (interiorTiles ?? System.Array.Empty<Tile>())
                .Where(t => t != null && t.tileType == Tile.TileType.Portal)
                .ToArray();

            if (portalTiles.Length > 0)
            {
                var coords = SelectSpacedByScore(interiorCandidates, portalCount, occupied, minSpecialManhattan,
                    c => Hash01(cfgSeed, c.x, c.y, 101) + 0.6f * Manhattan01(w, h, c, start),
                    rng);

                foreach (var c in coords)
                {
                    Tile t = portalTiles[rng.Next(portalTiles.Length)];
                    AddForced(c, t, skip: false, lockRotation: false);
                }
            }
        }

        int doorCount = maxDoors > 0 ? maxDoors : 0;
        if (doorCount > 0)
        {
            var doorTiles = (interiorTiles ?? System.Array.Empty<Tile>())
                .Where(t => t != null && t.tileType == Tile.TileType.Door)
                .ToArray();

            if (doorTiles.Length > 0)
            {
                var coords = SelectSpacedByScore(interiorCandidates, doorCount, occupied, minSpecialManhattan,
                    c => Hash01(cfgSeed, c.x, c.y, 202) + 0.35f * Manhattan01(w, h, c, prison),
                    rng);

                foreach (var c in coords)
                {
                    Tile t = doorTiles[rng.Next(doorTiles.Length)];
                    AddForced(c, t, skip: false, lockRotation: false);
                }
            }
        }

        return list;
    }

    private static bool IsFarEnough(Vector2Int p, List<Vector2Int> placed, int minManhattan)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            if (Mathf.Abs(p.x - placed[i].x) + Mathf.Abs(p.y - placed[i].y) < minManhattan)
                return false;
        }
        return true;
    }

    private static List<Vector2Int> SelectSpacedByScore(
        List<Vector2Int> candidates,
        int count,
        List<Vector2Int> occupied,
        int minManhattan,
        Func<Vector2Int, float> score,
        System.Random rng)
    {
        if (count <= 0) return new List<Vector2Int>();

        var scored = new List<(Vector2Int c, float s)>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (!IsFarEnough(c, occupied, minManhattan)) continue;
            scored.Add((c, score(c)));
        }

        // Shuffle before sort so equal scores don't bias by scan order.
        for (int i = scored.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (scored[i], scored[j]) = (scored[j], scored[i]);
        }

        scored.Sort((a, b) => b.s.CompareTo(a.s));

        var result = new List<Vector2Int>(count);
        for (int i = 0; i < scored.Count && result.Count < count; i++)
        {
            var c = scored[i].c;
            if (!IsFarEnough(c, occupied, minManhattan)) continue;
            if (!IsFarEnough(c, result, minManhattan)) continue;
            result.Add(c);
            occupied.Add(c);
        }

        // Remove the temporary occupancy we added for selection so caller can add forced cells explicitly.
        for (int i = 0; i < result.Count; i++)
        {
            occupied.Remove(result[i]);
        }

        return result;
    }

    private static void ShuffleInPlace<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static float Hash01(int seed, int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)(x * 374761393);
            h = (h << 13) | (h >> 19);
            h ^= (uint)(y * 668265263);
            h = (h << 13) | (h >> 19);
            h ^= (uint)(salt * 1274126177);
            h *= 2246822519u;
            h ^= h >> 15;
            // 24-bit mantissa for stable float-ish distribution
            return (h & 0x00FFFFFFu) / 16777215f;
        }
    }

    private static float Manhattan01(int w, int h, Vector2Int p, Vector2Int anchor)
    {
        float d = Mathf.Abs(p.x - anchor.x) + Mathf.Abs(p.y - anchor.y);
        float max = Mathf.Max(1f, (w - 1) + (h - 1));
        return d / max;
    }

    private static float BorderCornerDistance01(int w, int h, Vector2Int p)
    {
        // Distance from nearest corner, normalized.
        int d00 = Mathf.Abs(p.x - 0) + Mathf.Abs(p.y - 0);
        int d10 = Mathf.Abs(p.x - (w - 1)) + Mathf.Abs(p.y - 0);
        int d01 = Mathf.Abs(p.x - 0) + Mathf.Abs(p.y - (h - 1));
        int d11 = Mathf.Abs(p.x - (w - 1)) + Mathf.Abs(p.y - (h - 1));
        int d = Mathf.Min(Mathf.Min(d00, d10), Mathf.Min(d01, d11));
        float max = Mathf.Max(1f, (w - 1) + (h - 1));
        return d / max;
    }

    private static List<Vector2Int> BuildBorderCandidates(int w, int h)
    {
        var list = new List<Vector2Int>();
        // top/bottom edges (excluding corners)
        for (int x = 1; x <= w - 2; x++)
        {
            list.Add(new Vector2Int(x, 0));
            list.Add(new Vector2Int(x, h - 1));
        }
        // left/right edges (excluding corners)
        for (int y = 1; y <= h - 2; y++)
        {
            list.Add(new Vector2Int(0, y));
            list.Add(new Vector2Int(w - 1, y));
        }
        return list;
    }

    private static void PlaceBorderTunnelsFallback(
        List<Vector2Int> borderCandidates,
        int pairsRemaining,
        Tile[] tunnelTiles,
        List<Vector2Int> occupied,
        int minManhattan,
        int w,
        int h,
        int cfgSeed,
        System.Random rng,
        Action<Vector2Int, Tile, bool> addForced)
    {
        if (pairsRemaining <= 0 || tunnelTiles == null || tunnelTiles.Length == 0) return;

        // Score and greedily place 2*pairsRemaining tunnels anywhere on border.
        int needed = pairsRemaining * 2;
        var coords = SelectSpacedByScore(borderCandidates, needed, occupied, minManhattan,
            c => Hash01(cfgSeed, c.x, c.y, 303) + 0.5f * BorderCornerDistance01(w, h, c),
            rng);

        for (int i = 0; i < coords.Count; i++)
        {
            addForced(coords[i], tunnelTiles[rng.Next(tunnelTiles.Length)], false);
        }
    }

    private Tile[] ExtractTiles(TilesDatabase db)
    {
        if (db == null || db.tiles == null || db.tiles.Count == 0) return Array.Empty<Tile>();
        return db.tiles.ToArray();
    }

    private Tile[] ConcatArrays(Tile[] a, Tile[] b)
    {
        if ((a == null || a.Length == 0) && (b == null || b.Length == 0)) return Array.Empty<Tile>();
        if (a == null || a.Length == 0) return b ?? Array.Empty<Tile>();
        if (b == null || b.Length == 0) return a ?? Array.Empty<Tile>();
        var list = new List<Tile>(a);
        list.AddRange(b);
        return list.ToArray();
    }

    private Vector2Int ClampToInterior(Vector2Int coords, int w, int h)
    {
        int x = Mathf.Clamp(coords.x, 1, Mathf.Max(1, w - 2));
        int y = Mathf.Clamp(coords.y, 1, Mathf.Max(1, h - 2));
        return new Vector2Int(x, y);
    }

    private void OnGenerationCompleted()
    {
        if (prisonPrefab == null)
        {
            return;
        }

        int w = Mathf.Max(2, width);
        int h = Mathf.Max(2, height);

        Vector2Int prison = autoPlaceAnchors ? new Vector2Int(Mathf.Max(1, w - 2), Mathf.Max(1, h - 2)) : ClampToInterior(prisonCoords, w, h);
        Vector3 worldPos = new Vector3(prison.x, 0f, prison.y) + prisonPrefabOffset;
        Instantiate(prisonPrefab, worldPos, Quaternion.identity);
    }
}
