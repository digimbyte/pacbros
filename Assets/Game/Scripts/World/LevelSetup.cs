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
