using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(AccessGate))]
public class PairedPortal : MonoBehaviour
{
    public PairedPortal paired;

    // Access/security is handled by a required `AccessGate` component on this GameObject.

    [Header("Teleport")]
    public Vector3 exitWorldOffset = Vector3.zero;
    public float cooldownSeconds = 0.20f;
    [Tooltip("Activation radius on XZ (meters). Must be within this distance from portal center to trigger teleport.")]
    public float activationRadius = 0.25f;

    static readonly Dictionary<int, float> _cooldowns = new();

    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void Awake()
    {
        var c = GetComponent<Collider>();
        if (c != null && !c.isTrigger)
            c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (paired == null) return;

        int key = GetCooldownKey(other);
        if (IsCoolingDown(key)) return;

        var gate = GetComponent<AccessGate>();
        if (gate == null)
        {
            Debug.LogError("PairedPortal requires an AccessGate component to decide access.");
            return;
        }

        EntityIdentity identity = EntityIdentityUtility.From(other);
        if (identity.IsValid && !gate.HasAccess(identity))
            return;

        // Only teleport when the entity root is close enough to the portal center on XZ.
        Transform root = identity.IsValid && identity.Transform != null ? identity.Transform : other.transform.root;
        Vector3 rootPos = root.position;
        Vector2 rootXZ = new Vector2(rootPos.x, rootPos.z);
        Vector2 portalXZ = new Vector2(transform.position.x, transform.position.z);
        if (Vector2.Distance(rootXZ, portalXZ) > activationRadius) return;

        // Capture last movement direction from GridMotor or Rigidbody so we can reapply after teleport.
        Vector3 lastMove = Vector3.zero;
        GridMotor motor = root.GetComponent<GridMotor>();
        if (motor != null) lastMove = motor.GetVelocity();
        else
        {
            var rb = root.GetComponent<Rigidbody>();
            if (rb != null) lastMove = rb.linearVelocity;
        }

        // Teleport to center
        Vector3 target = paired.transform.position + paired.exitWorldOffset;

        // Apply teleportation (prefer GridMotor to preserve grid state)
        if (motor != null)
        {
            motor.HardTeleport(target);
            // Restore movement direction as a desired cardinal direction if available
            if (lastMove.sqrMagnitude > 0.0001f)
            {
                Vector2Int dir = (Mathf.Abs(lastMove.x) >= Mathf.Abs(lastMove.z)) ? new Vector2Int(lastMove.x > 0f ? 1 : -1, 0) : new Vector2Int(0, lastMove.z > 0f ? 1 : -1);
                motor.SetDesiredDirection(dir);
            }
        }
        else
        {
            // Use CharacterController / Rigidbody fallback
            CharacterController cc = root.GetComponent<CharacterController>();
            if (cc != null)
            {
                bool wasEnabled = cc.enabled;
                cc.enabled = false;
                root.position = target;
                cc.enabled = wasEnabled;
            }
            else
            {
                Rigidbody rb = root.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = target;
                    rb.linearVelocity = lastMove; // preserve motion for rigidbodies
                }
                else
                {
                    root.position = target;
                }
            }
        }

        if (identity.IsValid)
        {
            DoorOverrideRegistry.Consume(identity, gate);
        }
        SetCooldown(key);
    }

    int GetCooldownKey(Collider other)
    {
        EntityIdentity identity = EntityIdentityUtility.From(other);
        if (identity.IsValid) return identity.InstanceId;
        return other.transform.root.GetInstanceID();
    }

    bool IsCoolingDown(int key)
    {
        if (cooldownSeconds <= 0f) return false;
        if (!_cooldowns.TryGetValue(key, out float until)) return false;
        return Time.time < until;
    }

    void SetCooldown(int key)
    {
        if (cooldownSeconds <= 0f) return;
        _cooldowns[key] = Time.time + cooldownSeconds;
    }

    static void TeleportOther(Collider other, Vector3 target)
    {
        // Prefer entity root.
        Transform root = other.transform.root;
        EntityIdentity identity = EntityIdentityUtility.From(other);
        if (identity.IsValid && identity.Transform != null) root = identity.Transform;

        GridMotor motor = root.GetComponent<GridMotor>();
        if (motor != null)
        {
            motor.HardTeleport(target);
            return;
        }

        CharacterController cc = root.GetComponent<CharacterController>();
        if (cc != null)
        {
            bool wasEnabled = cc.enabled;
            cc.enabled = false;
            root.position = target;
            cc.enabled = wasEnabled;
            return;
        }

        Rigidbody rb = root.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.position = target;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        root.position = target;
    }
}
