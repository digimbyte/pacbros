using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Put this on a separate GameObject that has a BoxCollider marked as Trigger.
/// It detects entities entering/exiting and opens the referenced KeycardDoorController
/// if at least one entity inside has the required keycard.
/// </summary>
[RequireComponent(typeof(Collider))]
public class KeycardDoorTrigger : MonoBehaviour
{
    [Header("References")]
    public KeycardDoorController door;

    [Header("Entity Filtering")]
    [Tooltip("Optional: only colliders on these layers will be considered.")]
    public LayerMask entityLayers = ~0;

    [Tooltip("Colliders on these layers will be ignored (won't open the door and won't be pushed while closing).")]
    public LayerMask ignoreLayers = 0;

    // Occupants (colliders) currently inside the trigger. Used for closing-push.
    readonly HashSet<Collider> _occupants = new();

    // Authorized entities currently inside.
    readonly HashSet<AuthorizedToken> _authorizedTokens = new();

    // Legacy authorized inventories (older component).
    readonly HashSet<KeycardInventory> _authorizedLegacy = new();

    // Hashable token for EntityBase reference identity.
    readonly struct AuthorizedToken
    {
        readonly int _instanceId;

        AuthorizedToken(int instanceId) => _instanceId = instanceId;

        public static AuthorizedToken For(EntityBase e) => new AuthorizedToken(e.GetInstanceID());

        public override int GetHashCode() => _instanceId;
        public override bool Equals(object obj) => obj is AuthorizedToken t && t._instanceId == _instanceId;
    }

    void Reset()
    {
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void Awake()
    {
        Collider c = GetComponent<Collider>();
        if (c != null && !c.isTrigger)
            Debug.LogWarning($"{name}: KeycardDoorTrigger collider should be marked as Trigger.");

        if (door != null)
            door.trigger = this;
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsOnLayerMask(other.gameObject, ignoreLayers))
            return;

        if (!IsOnLayerMask(other.gameObject, entityLayers))
            return;

        _occupants.Add(other);

        if (door == null)
            return;

        // New entity system (preferred)
        EntityBase entity = other.GetComponentInParent<EntityBase>();
        if (entity != null)
        {
            if (entity.isGhost)
                return;

            if (!entity.HasKeycard(door.requiredKey, door.allowHigherKeys))
                return;

            // Track authorization by entity via a lightweight shim (see AuthorizedToken).
            AuthorizedToken token = AuthorizedToken.For(entity);
            _authorizedTokens.Add(token);
            if (_authorizedTokens.Count == 1)
                door.Open();

            return;
        }

        // Legacy fallback (older component)
        KeycardInventory inv = other.GetComponentInParent<KeycardInventory>();
        if (inv == null)
            return;

        if (!inv.HasAccess(door.requiredKey, door.allowHigherKeys))
            return;

        _authorizedLegacy.Add(inv);
        if (_authorizedLegacy.Count == 1)
            door.Open();
    }

    void OnTriggerExit(Collider other)
    {
        if (IsOnLayerMask(other.gameObject, ignoreLayers))
            return;

        if (!IsOnLayerMask(other.gameObject, entityLayers))
            return;

        _occupants.Remove(other);

        if (door == null)
            return;

        EntityBase entity = other.GetComponentInParent<EntityBase>();
        if (entity != null)
        {
            if (entity.isGhost)
                return;

            AuthorizedToken token = AuthorizedToken.For(entity);
            if (_authorizedTokens.Remove(token) && _authorizedTokens.Count == 0 && _authorizedLegacy.Count == 0)
                door.Close();

            return;
        }

        KeycardInventory inv = other.GetComponentInParent<KeycardInventory>();
        if (inv == null)
            return;

        if (_authorizedLegacy.Remove(inv) && _authorizedLegacy.Count == 0 && _authorizedTokens.Count == 0)
            door.Close();
    }

    public void PushOccupantsAlongWorldZ(float doorWorldZ, float strength)
    {
        // "Greater bias" = push them toward whichever side (Z+ or Z-) they're already on.
        foreach (Collider c in _occupants)
        {
            if (c == null) continue;

            // Ghosts should not be pushed by doors.
            EntityBase entity = c.GetComponentInParent<EntityBase>();
            if (entity != null && entity.isGhost)
                continue;

            Transform t = c.transform;
            float dz = t.position.z - doorWorldZ;
            float sign = dz >= 0f ? 1f : -1f;
            Vector3 pushDir = Vector3.forward * sign;

            Rigidbody rb = c.attachedRigidbody;
            if (rb != null && !rb.isKinematic)
            {
                // VelocityChange ignores mass, feels like a shove.
                rb.AddForce(pushDir * strength, ForceMode.VelocityChange);
                continue;
            }

            CharacterController cc = c.GetComponentInParent<CharacterController>();
            if (cc != null && cc.enabled)
            {
                cc.Move(pushDir * (strength * Time.deltaTime));
                continue;
            }

            // Last resort for non-rigidbody objects.
            t.position += pushDir * (strength * Time.deltaTime);
        }
    }

    static bool IsOnLayerMask(GameObject go, LayerMask mask)
    {
        int layer = go.layer;
        return (mask.value & (1 << layer)) != 0;
    }
}
