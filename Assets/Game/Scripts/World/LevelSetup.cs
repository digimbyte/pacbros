using System.Collections.Generic;
using UnityEngine;
using Pathfinding;

/// <summary>
/// Runtime helper that pairs `PairedPortal` and `PairedTunnel` instances and optionally
/// registers teleport edges with the A* `Graph` so pathfinding can consider portal jumps.
/// </summary>
[DisallowMultipleComponent]
public class LevelSetup : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Main player entity in this level.")]
    public PlayerEntity player;
    [Tooltip("Ordered list of enemies to position from EnemySpawnPoints (e.g. 4 Pac ghosts).")]
    public EnemyEntity[] enemies;
    [Tooltip("Camera control to retarget; pivot is read from this component.")]
    public CameraControl cameraControl;

    [Header("Pairing")]
    public bool randomizePortals = true;
    public bool randomizeTunnels = true;

    [Header("A* Integration")]
    [Tooltip("If true, add graph connections between nearest A* nodes at portal endpoints after scan.")]
    public bool registerAstarPortalEdges = true;

    /// <summary>
    /// Initialize pairings and (optionally) register graph edges. Call after A* scan.
    /// </summary>
    public void Initialize()
    {
        PairPortals();
        PairTunnels();

        if (registerAstarPortalEdges)
        {
            RegisterAstarPortalEdges();
        }

        // After level geometry + entities are spawned and portals linked,
        // snap the player (and camera) to a SpawnPlayer anchor.
        PositionPlayersAndCamera();
    }

    /// <summary>
    /// Choose a PlayerSpawnPoint for a player.
    /// MVP rule: ANY spawn point is valid; we do not reserve specific spawns per player.
    /// When multiple spawns exist, pick one at random using the spawn's weight.
    /// </summary>
    PlayerSpawnPoint ChoosePlayerSpawn(int playerIndex)
    {
        // NOTE: playerIndex is currently ignored by design (no per-player reservation).

        // Prefer using an explicit registry if present so we avoid repeated FindObjects calls.
        var registry = Object.FindObjectOfType<SpawnPointsRegistry>(includeInactive: true);
        List<PlayerSpawnPoint> allSpawns = null;

        if (registry != null && registry.PlayerSpawns != null && registry.PlayerSpawns.Count > 0)
        {
            allSpawns = new List<PlayerSpawnPoint>(registry.PlayerSpawns);
        }
        else
        {
            var spawnsArr = GameObject.FindObjectsOfType<PlayerSpawnPoint>(includeInactive: true);
            if (spawnsArr != null && spawnsArr.Length > 0)
                allSpawns = new List<PlayerSpawnPoint>(spawnsArr);
        }

        if (allSpawns == null || allSpawns.Count == 0)
            return null;

        // MVP: just choose a random spawn (weighted) from the full set.
        return ChooseWeightedRandom(allSpawns);
    }

    /// <summary>
    /// Choose a random spawn from the list using its weight; if all weights are non-positive,
    /// select uniformly.
    /// </summary>
    PlayerSpawnPoint ChooseWeightedRandom(List<PlayerSpawnPoint> spawns)
    {
        if (spawns == null || spawns.Count == 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < spawns.Count; i++)
        {
            var s = spawns[i];
            if (s == null) continue;
            float w = Mathf.Max(0f, s.weight);
            totalWeight += w;
        }

        if (totalWeight <= 0f)
        {
            // All weights are zero or negative; choose uniformly.
            int idx = Random.Range(0, spawns.Count);
            return spawns[idx];
        }

        float r = Random.Range(0f, totalWeight);
        float accum = 0f;
        for (int i = 0; i < spawns.Count; i++)
        {
            var s = spawns[i];
            if (s == null) continue;
            float w = Mathf.Max(0f, s.weight);
            if (w <= 0f) continue;
            accum += w;
            if (r <= accum)
                return s;
        }

        // Numerical edge case; just return the last valid.
        for (int i = spawns.Count - 1; i >= 0; i--)
        {
            if (spawns[i] != null)
                return spawns[i];
        }
        return null;
    }

    /// <summary>
    /// Find a SpawnPlayer anchor (PlayerSpawnPoint), spawn the local Player prefab there via LevelRuntime,
    /// and wire the camera to follow it. Falls back to repositioning an existing PlayerEntity if no runtime is present.
    /// Also positions enemies from EnemySpawnPoints.
    /// </summary>
    public void PositionPlayersAndCamera(int playerIndex = 0)
    {
        var chosen = ChoosePlayerSpawn(playerIndex);
        if (chosen == null)
            return;

        // Prefer letting the LevelRuntime own instantiation, lives and camera wiring.
        var runtime = LevelRuntime.Active != null ? LevelRuntime.Active : GetComponentInParent<LevelRuntime>();
        PlayerEntity pEntity = null;

        if (runtime != null && runtime.spawnLocalPlayer)
        {
            pEntity = runtime.SpawnLocalPlayerAt(chosen, playerIndex, isRespawn: false);
            if (pEntity != null)
            {
                // Keep the reference field in sync for consumers of LevelSetup.player.
                player = pEntity;
            }
        }

        // Fallback: move an already-placed PlayerEntity and manually retarget the camera.
        if (pEntity == null)
        {
            pEntity = player != null ? player : Object.FindObjectOfType<PlayerEntity>(includeInactive: true);
            if (pEntity != null)
            {
                var t = pEntity.transform;
                var pos = chosen.transform.position;
                t.position = new Vector3(pos.x, t.position.y, pos.z);
            }

            var cam = cameraControl != null ? cameraControl : Object.FindObjectOfType<CameraControl>(includeInactive: true);
            if (cam != null && pEntity != null)
            {
                cam.target = pEntity.transform;
                if (cam.pivot != null)
                {
                    var pos = pEntity.transform.position;
                    cam.pivot.position = new Vector3(pos.x, cam.pivot.position.y, pos.z);
                }
            }
        }

        // Position enemies from EnemySpawnPoints.
        PositionEnemies();
    }

    public void PositionEnemies()
    {
        if (enemies == null || enemies.Length == 0)
            return;

        var eSpawns = GameObject.FindObjectsOfType<EnemySpawnPoint>(includeInactive: true);
        if (eSpawns == null || eSpawns.Length == 0)
            return;

        // Simple strategy: for each enemy in order, find the first compatible spawn.
        for (int ei = 0; ei < enemies.Length; ei++)
        {
            var enemy = enemies[ei];
            if (enemy == null) continue;

            EnemySpawnPoint chosen = null;
            int typeId = enemy.enemyTypeId;

            // Prefer exact enemyTypeId match.
            for (int si = 0; si < eSpawns.Length; si++)
            {
                var s = eSpawns[si];
                if (s == null) continue;
                if (typeId >= 0 && s.enemyTypeId == typeId)
                {
                    chosen = s;
                    break;
                }
            }

            // Fallback: any spawn with enemyTypeId < 0.
            if (chosen == null)
            {
                for (int si = 0; si < eSpawns.Length; si++)
                {
                    var s = eSpawns[si];
                    if (s == null) continue;
                    if (s.enemyTypeId < 0)
                    {
                        chosen = s;
                        break;
                    }
                }
            }

            if (chosen == null) continue;

            var t = enemy.transform;
            var pos = chosen.transform.position;
            t.position = new Vector3(pos.x, t.position.y, pos.z);
        }
    }

    public void PairPortals()
    {
        var portals = GameObject.FindObjectsOfType<PairedPortal>(includeInactive: true);
        if (portals == null || portals.Length == 0) return;

        // Group by color level (use portal.requiredKey if requiresKey; otherwise KeycardColor.None)
        var groups = new Dictionary<KeycardColor, List<PairedPortal>>();
        foreach (var p in portals)
        {
            KeycardColor k = KeycardColor.None;
            if (p != null)
            {
                var gate = p.GetComponent<AccessGate>();
                if (gate != null) k = gate.requiredKey;
            }
            if (!groups.TryGetValue(k, out var list)) { list = new List<PairedPortal>(); groups[k] = list; }
            list.Add(p);
        }

        foreach (var kv in groups)
        {
            var list = kv.Value;
            if (list.Count <= 1) continue;
            if (randomizePortals) Shuffle(list);

            // Pair sequentially. If odd, leave last unpaired and log.
            for (int i = 0; i + 1 < list.Count; i += 2)
            {
                var a = list[i];
                var b = list[i + 1];
                if (a != null && b != null && a != b)
                {
                    a.paired = b;
                    b.paired = a;
                }
            }

            if (list.Count % 2 == 1)
            {
                Debug.LogWarning($"LevelSetup: odd number of portals for key {kv.Key}; one portal will be left unpaired.");
            }
        }
    }

    public void PairTunnels()
    {
        var tunnels = GameObject.FindObjectsOfType<PairedTunnel>(includeInactive: true);
        if (tunnels == null || tunnels.Length == 0) return;

        var list = new List<PairedTunnel>(tunnels);
        if (list.Count <= 1) return;
        if (randomizeTunnels) Shuffle(list);

        for (int i = 0; i + 1 < list.Count; i += 2)
        {
            var a = list[i];
            var b = list[i + 1];
            if (a != null && b != null && a != b)
            {
                a.paired = b;
                b.paired = a;
            }
        }

        if (list.Count % 2 == 1)
        {
            Debug.LogWarning("LevelSetup: odd number of tunnels; one tunnel will be left unpaired.");
        }
    }

    public void RegisterAstarPortalEdges()
    {
        if (AstarPath.active == null) return;

        var portals = GameObject.FindObjectsOfType<PairedPortal>(includeInactive: true);
        if (portals == null) return;

        foreach (var p in portals)
        {
            if (p == null || p.paired == null) continue;
            // Only register once per pair (skip if this instance id is greater than paired id)
            if (p.GetInstanceID() > p.paired.GetInstanceID()) continue;

            var na = AstarPath.active.GetNearest(p.transform.position, NNConstraint.None).node;
            var nb = AstarPath.active.GetNearest(p.paired.transform.position, NNConstraint.None).node;
            if (na == null || nb == null) continue;

            // Add bidirectional connection if not present.
            AddConnectionIfMissing(na, nb, 1);
            AddConnectionIfMissing(nb, na, 1);
        }
    }

    static void AddConnectionIfMissing(Pathfinding.GraphNode a, Pathfinding.GraphNode b, uint cost)
    {
        if (a == null || b == null) return;
        // Many versions of the A* API do not expose the connections array publicly.
        // To remain compatible, attempt to add the connection and ignore failures (duplicates).
        try
        {
            a.AddConnection(b, cost);
        }
        catch
        {
            // Ignore - connection may already exist or API may behave differently.
        }
    }

    static void Shuffle<T>(List<T> list)
    {
        int n = list.Count;
        for (int i = 0; i < n; i++)
        {
            int j = Random.Range(i, n);
            var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }
}
