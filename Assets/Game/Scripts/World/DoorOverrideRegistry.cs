using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks temporary door/portal override tickets granted to specific entities.
/// Tickets expire after a lifetime and can have limited uses (typically one).
/// AccessGate consults this registry to decide whether an entity may bypass
/// a gate even if it lacks the proper keycard.
/// </summary>
public static class DoorOverrideRegistry
{
    class Ticket
    {
        public float expiresAt;
        public int usesRemaining;
    }

    static readonly Dictionary<int, Ticket> _tickets = new();

    /// <summary>
    /// Grant (or refresh) an override ticket for the given entity+gate pair.
    /// </summary>
    public static void Grant(EntityIdentity entity, AccessGate gate, float lifetimeSeconds, int uses = 1)
    {
        if (!entity.IsValid || gate == null || lifetimeSeconds <= 0f || uses <= 0)
            return;

        int key = ComposeKey(entity, gate);
        PurgeIfExpired(key);

        if (!_tickets.TryGetValue(key, out var ticket))
        {
            ticket = new Ticket();
            _tickets[key] = ticket;
        }

        ticket.expiresAt = Time.time + lifetimeSeconds;
        ticket.usesRemaining = uses;
    }

    /// <summary>
    /// Returns true if there is an active, non-expired ticket for the entity+gate pair.
    /// </summary>
    public static bool HasActive(EntityIdentity entity, AccessGate gate)
    {
        if (!entity.IsValid || gate == null)

            return false;

        int key = ComposeKey(entity, gate);
        if (!_tickets.TryGetValue(key, out var ticket))
            return false;

        if (Time.time >= ticket.expiresAt)
        {
            _tickets.Remove(key);
            return false;
        }

        return ticket.usesRemaining > 0;
    }

    /// <summary>
    /// Consume a single use from an active ticket (if any).
    /// Returns true if a ticket was consumed.
    /// </summary>
    public static bool Consume(EntityIdentity entity, AccessGate gate)
    {
        if (!entity.IsValid || gate == null)
            return false;

        int key = ComposeKey(entity, gate);
        if (!_tickets.TryGetValue(key, out var ticket))
            return false;

        if (Time.time >= ticket.expiresAt)
        {
            _tickets.Remove(key);
            return false;
        }

        if (ticket.usesRemaining <= 0)
            return false;

        ticket.usesRemaining--;
        if (ticket.usesRemaining <= 0)
            _tickets.Remove(key);

        return true;
    }

    static void PurgeIfExpired(int key)
    {
        if (_tickets.TryGetValue(key, out var ticket) && Time.time >= ticket.expiresAt)
        {
            _tickets.Remove(key);
        }
    }

    static int ComposeKey(EntityIdentity entity, AccessGate gate)
    {
        unchecked
        {
            int e = entity.IsValid ? entity.InstanceId : 0;
            int g = gate != null ? gate.GetInstanceID() : 0;
            return (e * 397) ^ g;
        }
    }

    public static void Grant(Component component, AccessGate gate, float lifetimeSeconds, int uses = 1) =>
        Grant(EntityIdentityUtility.From(component), gate, lifetimeSeconds, uses);

    public static bool HasActive(Component component, AccessGate gate) =>
        HasActive(EntityIdentityUtility.From(component), gate);

    public static bool Consume(Component component, AccessGate gate) =>
        Consume(EntityIdentityUtility.From(component), gate);
}
