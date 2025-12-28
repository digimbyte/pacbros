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
///   of the path computation, optionally allowing limited overrides.
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
    /// AccessGate accessor for diagnostics/override issuance.
    /// </summary>
    public AccessGate Gate => _gate;

    /// <summary>
    /// Returns the world-space bounds of the door (collider bounds).
    /// </summary>
    public Bounds Bounds => _col != null ? _col.bounds : new Bounds(transform.position, Vector3.one);

    /// <summary>
    /// Returns true if the given entity is allowed to pass this door according to AccessGate.
    /// </summary>
    public bool IsAccessibleFor(EntityIdentity entity)
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
    /// blocking doors that `entity` cannot access. Optionally allow bypassing a limited
    /// number of doors; any bypassed doors grant override tickets so AccessGate permits
    /// traversal after the path is generated.
    /// </summary>
    public static void FindPathForEntity(
        Vector3 start,
        Vector3 end,
        EnemyEntity entity,
        Action<Path, bool> onComplete,
        int maxDoorOverrides = 0,
        float overrideTicketLifetime = 5f)
    {
        var entityIdentity = entity != null ? new EntityIdentity(entity) : default;
        if (AstarPath.active == null)
        {
            Debug.LogError("AstarPath.active is null; cannot compute path.");
            onComplete?.Invoke(null, false);
            return;
        }

        if (!entityIdentity.IsValid)
        {
            Debug.LogError($"DoorDelegate: entity reference missing while requesting path from {start} to {end}.");
            onComplete?.Invoke(null, false);
            return;
        }

        var doors = GameObject.FindObjectsOfType<DoorDelegate>(includeInactive: true);
        var blockedDoors = new List<DoorDelegate>();

        foreach (var door in doors)
        {
            if (door == null) continue;

            bool accessible = door.IsAccessibleFor(entityIdentity);
            if (!accessible && entityIdentity.IsValid && door.Gate != null && door.Gate.requiredKey != KeycardColor.None)
            {
                bool entityHasKey = entityIdentity.HasKeycard(door.Gate.requiredKey, allowHigherKeys: true);
                if (entityHasKey)
                {
                    Debug.LogWarning(
                        $"DoorDelegate: entity '{entityIdentity.Name}' owns key '{door.Gate.requiredKey}' but door '{door.name}' reported inaccessible. Allowing passage and flagging a possible gate misconfiguration.");
                    accessible = true;
                }
            }

            if (!accessible)
                blockedDoors.Add(door);
        }

        var attempts = BuildOverrideAttempts(blockedDoors, Mathf.Clamp(maxDoorOverrides, 0, 1));

        void StartAttempt(int attemptIndex)
        {
            if (attemptIndex >= attempts.Count)
            {
                Debug.LogWarning($"DoorDelegate: Unable to find path for '{entityIdentity.Name}' to {end} after {attempts.Count} attempts (blocked doors: {blockedDoors.Count}).");
                onComplete?.Invoke(null, false);
                return;
            }

            var overrides = attempts[attemptIndex];
            var blockGuos = new List<GraphUpdateObject>();
            var restoreGuos = new List<GraphUpdateObject>();

            foreach (var door in blockedDoors)
            {
                if (door == null) continue;
                if (overrides.Contains(door)) continue;

                blockGuos.Add(door.CreateGUO(false));
                restoreGuos.Add(door.CreateGUO(true));
            }

            if (blockGuos.Count > 0)
            {
                foreach (var guo in blockGuos)
                    AstarPath.active.UpdateGraphs(guo);
            }

            var path = ABPath.Construct(start, end, p =>
            {
                bool scheduleRetry = false;
                int nextAttempt = attemptIndex + 1;
                bool overridesUsed = false;

                try
                {
                    bool success = p != null && !p.error && p.vectorPath != null && p.vectorPath.Count > 0;
                    overridesUsed = overrides.Count > 0 && success;

                    if (overridesUsed)
                    {
                        float lifetime = Mathf.Max(0.01f, overrideTicketLifetime);
                        foreach (var door in overrides)
                        {
                            if (door?.Gate == null) continue;
                            DoorOverrideRegistry.Grant(entityIdentity, door.Gate, lifetime, uses: 1);
                        }
                    }

                    if (success || attemptIndex >= attempts.Count - 1)
                    {
                        if (success && overridesUsed)
                        {
                            Debug.LogWarning($"DoorDelegate: '{entityIdentity.Name}' bypassed {overrides.Count} door(s) to reach {end}.");
                        }
                        onComplete?.Invoke(p, overridesUsed);
                    }
                    else
                    {
                        scheduleRetry = true;
                    }
                }
                finally
                {
                    if (restoreGuos.Count > 0)
                    {
                        foreach (var guo in restoreGuos)
                            AstarPath.active.UpdateGraphs(guo);
                    }
                }

                if (scheduleRetry)
                    StartAttempt(nextAttempt);
            });

            AstarPath.StartPath(path);
        }

        StartAttempt(0);
    }

    static List<List<DoorDelegate>> BuildOverrideAttempts(List<DoorDelegate> blockedDoors, int maxDoorOverrides)
    {
        var attempts = new List<List<DoorDelegate>>
        {
            new List<DoorDelegate>(0) // baseline attempt with zero overrides
        };

        if (maxDoorOverrides <= 0 || blockedDoors.Count == 0)
            return attempts;

        foreach (var door in blockedDoors)
        {
            if (door == null) continue;
            attempts.Add(new List<DoorDelegate> { door });
        }

        return attempts;
    }
}
