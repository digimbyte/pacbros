using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding;

/// <summary>
/// DoorDelegate: complements AccessGate by providing helper methods to
/// modify A* graph walkability for the door bounds and to resolve
/// paths for a specific entity taking AccessGate rules into account.
///
/// Usage:
/// - Attach to door GameObjects alongside an AccessGate and Collider.
/// - Use DoorDelegate.FindPathForEntity(...) to request a path that
///   temporarily treats inaccessible doors as blocked for the duration
///   of the path computation.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(AccessGate))]
public class DoorDelegate : MonoBehaviour
{
    Collider _col;
    AccessGate _gate;

    void Awake()
    {
        _col = GetComponent<Collider>();
        _gate = GetComponent<AccessGate>();
    }

    /// <summary>
    /// Returns the world-space bounds of the door (collider bounds).
    /// </summary>
    public Bounds Bounds => _col != null ? _col.bounds : new Bounds(transform.position, Vector3.one);

    /// <summary>
    /// Returns true if the given entity is allowed to pass this door according to AccessGate.
    /// </summary>
    public bool IsAccessibleFor(EntityBase entity)
    {
        if (_gate == null) return true;
        return _gate.HasAccess(entity);
    }

    /// <summary>
    /// Create a GraphUpdateObject that sets walkability for this door's bounds.
    /// </summary>
    GraphUpdateObject CreateGUO(bool walkable)
    {
        var guo = new GraphUpdateObject(Bounds)
        {
            modifyWalkability = true,
            setWalkability = walkable,
            updatePhysics = false
        };
        return guo;
    }

    /// <summary>
    /// Helper: find an A* path for `entity` from `start` to `end` while temporarily
    /// blocking doors that `entity` cannot access. The graphs are restored after
    /// the path completes (callback invoked on completion).
    /// </summary>
    public static void FindPathForEntity(Vector3 start, Vector3 end, EntityBase entity, Action<Path> onComplete)
    {
        if (AstarPath.active == null)
        {
            Debug.LogError("AstarPath.active is null; cannot compute path.");
            onComplete?.Invoke(null);
            return;
        }

        // Collect doors that the entity cannot access and build GUOs to mark them unwalkable.
        var doors = GameObject.FindObjectsOfType<DoorDelegate>(includeInactive: true);
        var blockGuos = new List<GraphUpdateObject>();
        var restoreGuos = new List<GraphUpdateObject>();

        foreach (var d in doors)
        {
            if (d == null) continue;
            bool accessible = d.IsAccessibleFor(entity);
            if (!accessible)
            {
                // Block this door for the path computation (make walkability = false)
                blockGuos.Add(d.CreateGUO(false));
                // We'll restore to walkable=true afterwards
                restoreGuos.Add(d.CreateGUO(true));
            }
        }

        // Apply blocking GUOs synchronously (apply one-by-one to match A* API)
        if (blockGuos.Count > 0)
        {
            foreach (var guo in blockGuos)
                AstarPath.active.UpdateGraphs(guo);
        }

        // Start the path (ABPath recommended for grid).
        // Attach the completion callback to the path via Construct, then start it.
        var path = ABPath.Construct(start, end, p =>
        {
            try
            {
                onComplete?.Invoke(p);
            }
            finally
            {
                // Restore graphs one-by-one
                if (restoreGuos.Count > 0)
                {
                    foreach (var guo in restoreGuos)
                        AstarPath.active.UpdateGraphs(guo);
                }
            }
        });

        AstarPath.StartPath(path);
    }
}
