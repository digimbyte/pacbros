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

    // (legacy KeycardInventory support removed) - use EntityIdentity-based checks only.

    // Hashable token for entity identity.
    readonly struct AuthorizedToken
    {
        readonly int _instanceId;

        AuthorizedToken(int instanceId) => _instanceId = instanceId;
        public static AuthorizedToken For(EntityIdentity e) => new AuthorizedToken(e.InstanceId);

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

    int TotalAuthorizedCount => _authorizedTokens.Count;

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
        EntityIdentity identity = EntityIdentityUtility.From(other);
        if (identity.IsValid)
        {
            bool isEnemy = identity.Kind == EntityKind.Enemy;
            if (!isEnemy && identity.IsGhost)
            {
                // Player ghosts still can't open doors.
                return;
            }

            bool hasAccess = isEnemy || identity.HasKeycard(door.requiredKey, door.allowHigherKeys);
            if (!hasAccess)
            {
                Debug.LogWarning($"{name}: '{identity.Name}' is missing key {door.requiredKey} (allowHigherKeys={door.allowHigherKeys}); door stays closed.");
                return;
            }
            AuthorizedToken token = AuthorizedToken.For(identity);
            _authorizedTokens.Add(token);
            if (TotalAuthorizedCount == 1)
                door.Open();

            return;
        }

        // No legacy KeycardInventory support; rely on EntityIdentity only.
        return;
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

        EntityIdentity identity = EntityIdentityUtility.From(other);
        if (identity.IsValid)
        {
            AuthorizedToken token = AuthorizedToken.For(identity);
            if (_authorizedTokens.Remove(token) && TotalAuthorizedCount == 0)
                door.Close();

            // Notify AI systems that an enemy has passed through this door
            if (identity.Kind == EntityKind.Enemy)
            {
                var ai = identity.Transform.GetComponent<EnemyBrainController>();
                if (ai != null)
                {
                    ai.OnPassedThroughDoorOrPortal();
                }
            }

            return;
        }

        // No legacy KeycardInventory support; nothing to remove for non-entity objects.
        return;
    }

    public void PushOccupantsAlongWorldZ(float doorWorldZ, float strength)
    {
        // "Greater bias" = push them toward whichever side (Z+ or Z-) they're already on.
        foreach (Collider c in _occupants)
        {
            if (c == null) continue;

            // Ghosts should not be pushed by doors.
            EntityIdentity identity = EntityIdentityUtility.From(c);
            if (identity.IsValid && identity.IsGhost)
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
