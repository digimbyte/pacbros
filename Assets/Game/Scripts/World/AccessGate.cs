using UnityEngine;

/// <summary>
/// Access control for portals and doors.
/// If an entity lacks the required key, the gate is considered blocked for that entity.
/// For portals: lacking access means they just walk over (no teleport).
/// For doors: lacking access means collision should block them (door collider stays enabled).
/// </summary>
[RequireComponent(typeof(Collider))]
public class AccessGate : MonoBehaviour
{
    public enum GateType { Portal, Door }

[Header("Gate")]
public GateType gateType = GateType.Portal;
public KeycardColor requiredKey = KeycardColor.None;

[Header("Access Rules")]
[Tooltip("If true, enemies are always allowed to pass even when keys are required.")]
public bool allowEnemiesToBypass = true;

    /// <summary>
    /// Returns true if the given entity is allowed to use/pass this gate.
    /// </summary>
    public bool HasAccess(EntityIdentity entity)
    {
        if (!entity.IsValid) return false;

        // Ghosts always bypass all requirements
        if (entity.IsGhost) return true;

        if (allowEnemiesToBypass && entity.Kind == EntityKind.Enemy)
            return true;

        if (DoorOverrideRegistry.HasActive(entity, this))
            return true;

        if (requiredKey == KeycardColor.None) return true;
        return entity.HasKeycard(requiredKey, allowHigherKeys: false);
    }

    public bool HasAccess(PlayerEntity player) => HasAccess(new EntityIdentity(player));
    public bool HasAccess(EnemyEntity enemy) => HasAccess(new EntityIdentity(enemy));

    /// <summary>
    /// For doors: call this to decide if we should block the entity via collision.
    /// Door collider stays enabled; logic that checks access decides whether to treat it as blocking.
    /// For portals: lacking access just means "no teleport"; collider remains walkable.
    /// </summary>
    public bool ShouldBlock(EntityIdentity entity)
    {
        if (gateType == GateType.Door)
            return !HasAccess(entity);
        // Portals do not block; they just refuse teleport if no access.
        return false;
    }

    public bool ShouldBlock(PlayerEntity player) => ShouldBlock(new EntityIdentity(player));
    public bool ShouldBlock(EnemyEntity enemy) => ShouldBlock(new EntityIdentity(enemy));
}
