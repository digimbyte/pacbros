
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(AccessGate))]
public class PairedPortal : MonoBehaviour
{
    public PairedPortal paired;

    [Header("Debug")]
    public bool verboseDebug = false;

    [Header("Activation")]
    [Tooltip("Optional: assign a separate trigger collider (child) to use for activation. If null, the collider on this GameObject is used.")]
    public Collider activationCollider;

    [Header("Teleport")]
    public Vector3 exitWorldOffset = Vector3.zero;
    public float cooldownSeconds = 0.20f;
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
        if (c != null)
        {
            c.isTrigger = true;
            if (activationCollider == null)
                activationCollider = c;
        }

        // If a separate activation collider exists, ensure it forwards triggers
        if (activationCollider != null && activationCollider != c)
        {
            activationCollider.isTrigger = true;

            var fwd = activationCollider.GetComponent<PortalTriggerForwarder>();
            if (fwd == null)
                fwd = activationCollider.gameObject.AddComponent<PortalTriggerForwarder>();

            fwd.parentPortal = this;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        ProcessTrigger(other);
    }

    public void ExternalTriggerEnter(Collider other)
    {
        ProcessTrigger(other);
    }

    public void ExternalTriggerExit(Collider other)
    {
        // When an entity leaves this portal's activation collider, clear any per-entity ignore flag
        EntityIdentity id = EntityIdentityUtility.From(other);
        Transform root = id.IsValid && id.Transform != null ? id.Transform : other.transform.root;
        var tl = root.GetComponent<TeleportLethargy>();
        if (tl != null)
            tl.ClearIgnoredPortal(this.GetInstanceID());
    }

    void ProcessTrigger(Collider other)
    {
        if (paired == null)
        {
            if (verboseDebug)
                Debug.LogWarning($"{name}: Ignored — portal not paired.", this);
            return;
        }

        int key = GetCooldownKey(other);
        if (IsCoolingDown(key))
        {
            if (verboseDebug)
                Debug.Log($"{name}: Ignored — cooldown active.", this);
            return;
        }

        var gate = GetComponent<AccessGate>();
        EntityIdentity identity = EntityIdentityUtility.From(other);

        if (identity.IsValid && !gate.HasAccess(identity))
        {
            if (verboseDebug)
                Debug.Log($"{name}: Access denied for {identity.Name}.", this);
            return;
        }

        Transform root =
            identity.IsValid && identity.Transform != null
            ? identity.Transform
            : other.transform.root;

        // Ensure entity has a TeleportLethargy component so we can prevent immediate re-triggers
        TeleportLethargy tl = root.GetComponent<TeleportLethargy>();
        if (tl == null) tl = root.gameObject.AddComponent<TeleportLethargy>();
        // Respect per-portal ignore as well as the generic recent-teleport timer.
        if (tl.IsIgnoredByPortal(this.GetInstanceID()) || tl.IsRecentlyTeleported())
        {
            if (verboseDebug) Debug.Log($"{name}: Ignored — entity recently teleported into this portal.", this);
            return;
        }

        Vector2 rootXZ = new(root.position.x, root.position.z);
        Vector2 portalXZ = new(transform.position.x, transform.position.z);

        if (Vector2.Distance(rootXZ, portalXZ) > activationRadius)
            return;

        Vector3 lastMove = Vector3.zero;
        GridMotor motor = root.GetComponent<GridMotor>();
        if (motor != null) lastMove = motor.GetVelocity();
        else if (root.TryGetComponent(out Rigidbody rb)) lastMove = rb.linearVelocity;

        Vector3 target = paired.transform.position + paired.exitWorldOffset;
        // Force global ground Y to 0 so teleported objects don't end up underground.
        target.y = 0f;

        // Compute mirrored cardinal exit direction based on inbound angle vs this portal's yaw.
        Vector3 inbound = (root.position - transform.position);
        inbound.y = 0f;
        Vector2 inboundXZ = new Vector2(inbound.x, inbound.z);
        bool hasInbound = inboundXZ.sqrMagnitude > 0.0001f;

        float srcYaw = transform.eulerAngles.y;
        float dstYaw = paired.transform.eulerAngles.y;
        Vector2Int exitDirCardinal = Vector2Int.zero;
        if (hasInbound)
        {
            float inboundAngle = Mathf.Atan2(inboundXZ.y, inboundXZ.x) * Mathf.Rad2Deg;
            float relative = Mathf.DeltaAngle(srcYaw, inboundAngle);
            float exitAngle = dstYaw + relative;
            float snapped = Mathf.Round(exitAngle / 90f) * 90f;
            float ang = Mathf.DeltaAngle(0f, snapped);
            if (Mathf.Abs(Mathf.DeltaAngle(ang, 0f)) < 1f)
                exitDirCardinal = new Vector2Int(1, 0);
            else if (Mathf.Abs(Mathf.DeltaAngle(ang, 90f)) < 1f)
                exitDirCardinal = new Vector2Int(0, 1);
            else if (Mathf.Abs(Mathf.DeltaAngle(ang, 180f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(ang, -180f)) < 1f)
                exitDirCardinal = new Vector2Int(-1, 0);
            else
                exitDirCardinal = new Vector2Int(0, -1);
        }

        if (motor != null)
        {
            motor.HardTeleport(target);

            if (exitDirCardinal != Vector2Int.zero)
            {
                motor.SetDesiredDirection(exitDirCardinal);
            }
            else if (lastMove.sqrMagnitude > 0.0001f)
            {
                Vector2Int dir =
                    Mathf.Abs(lastMove.x) >= Mathf.Abs(lastMove.z)
                        ? new Vector2Int(lastMove.x > 0 ? 1 : -1, 0)
                        : new Vector2Int(0, lastMove.z > 0 ? 1 : -1);

                motor.SetDesiredDirection(dir);
            }
        }
        else if (root.TryGetComponent(out CharacterController cc))
        {
            bool enabled = cc.enabled;
            cc.enabled = false;
            root.position = target;
            cc.enabled = enabled;
        }
        else if (root.TryGetComponent(out Rigidbody rb))
        {
            rb.position = target;
            if (exitDirCardinal != Vector2Int.zero && lastMove.sqrMagnitude > 0.0001f)
            {
                float speed = lastMove.magnitude;
                Vector3 outDir = new Vector3(exitDirCardinal.x, 0f, exitDirCardinal.y);
                rb.linearVelocity = outDir.normalized * speed;
            }
            else
            {
                rb.linearVelocity = lastMove;
            }
        }
        else
        {
            root.position = target;
        }

        if (identity.IsValid)
            DoorOverrideRegistry.Consume(identity, gate);

        // Mark entity as recently teleported and mark the destination portal instance to be ignored
        // until the entity exits that portal's activation collider.
        tl?.MarkTeleportedByPortal(paired.GetInstanceID());

        SetCooldown(key);
        // Prevent immediate refire on the target portal by setting the same cooldown there.
        if (paired != null)
        {
            try { paired.SetCooldown(key); } catch { /* defensive: ignore if paired lacks method */ }
            if (verboseDebug) Debug.Log($"{name}: applied cooldown on paired portal for key={key}", this);
        }
    }

    bool IsCoolingDown(int key)
    {
        return cooldownSeconds > 0f
            && _cooldowns.TryGetValue(key, out float until)
            && Time.time < until;
    }

    public void SetCooldown(int key)
    {
        if (cooldownSeconds > 0f)
            _cooldowns[key] = Time.time + cooldownSeconds;
    }

    int GetCooldownKey(Collider other)
    {
        EntityIdentity id = EntityIdentityUtility.From(other);
        return id.IsValid ? id.InstanceId : other.GetInstanceID();
    }
}

