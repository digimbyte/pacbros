using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(AccessGate))]
public class PairedTunnel : MonoBehaviour
{
    public PairedTunnel paired;

    [Header("Debug")]
    public bool verboseDebug = false;

    [Header("Activation")]
    [Tooltip("Optional: assign a separate trigger collider (child) to use for activation. If null, the collider on this GameObject is used.")]
    public Collider activationCollider;

    [Header("Teleport")]
    public Vector3 exitWorldOffset = Vector3.zero;
    public float cooldownSeconds = 0.20f;
    [Tooltip("Activation radius on XZ (meters). Tunnels use a larger radius so walking onto the edge triggers teleport.")]
    public float activationRadius = 0.45f;

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

        if (activationCollider != null && activationCollider != c)
        {
            activationCollider.isTrigger = true;

            var fwd = activationCollider.GetComponent<TunnelTriggerForwarder>();
            if (fwd == null)
                fwd = activationCollider.gameObject.AddComponent<TunnelTriggerForwarder>();

            fwd.parentTunnel = this;
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
        EntityIdentity id = EntityIdentityUtility.From(other);
        Transform root = id.IsValid && id.Transform != null ? id.Transform : other.transform.root;
        var tl = root.GetComponent<TeleportLethargy>();
        if (tl != null)
            tl.ClearIgnoredPortal(this.GetInstanceID());
    }

    void ProcessTrigger(Collider other)
    {
        if (paired == null) return;

        int key = GetCooldownKey(other);
        if (IsCoolingDown(key)) return;

        var gate = GetComponent<AccessGate>();
        if (gate == null) return;

        EntityIdentity identity = EntityIdentityUtility.From(other);
        if (identity.IsValid && !gate.HasAccess(identity)) return;

        Transform root =
            identity.IsValid && identity.Transform != null
                ? identity.Transform
                : other.transform.root;

        TeleportLethargy tl = root.GetComponent<TeleportLethargy>();
        if (tl == null) tl = root.gameObject.AddComponent<TeleportLethargy>();
        // Respect per-portal ignore as well as the generic recent-teleport timer.
        if (tl.IsIgnoredByPortal(this.GetInstanceID()) || tl.IsRecentlyTeleported())
        {
            if (verboseDebug) Debug.Log($"{name}: Ignored — entity recently teleported into this tunnel.", this);
            return;
        }

        Vector2 rootXZ = new(root.position.x, root.position.z);
        Vector2 tunnelXZ = new(transform.position.x, transform.position.z);
        if (Vector2.Distance(rootXZ, tunnelXZ) > activationRadius) return;

        Vector3 lastMove = Vector3.zero;
        GridMotor motor = root.GetComponent<GridMotor>();
        if (motor != null) lastMove = motor.GetVelocity();
        else if (root.TryGetComponent(out Rigidbody rb0)) lastMove = rb0.linearVelocity;

        // compute target after exitDir is known so we can optionally offset out of collider

        Vector3 inbound = root.position - transform.position;
        inbound.y = 0f;
        Vector2 inboundXZ = new(inbound.x, inbound.z);
        bool hasInbound = inboundXZ.sqrMagnitude > 0.0001f;

        float srcYaw = transform.eulerAngles.y;
        float dstYaw = paired.transform.eulerAngles.y;

        Vector2Int exitDir = Vector2Int.zero;

        if (hasInbound)
        {
            float inboundAngle = Mathf.Atan2(inboundXZ.y, inboundXZ.x) * Mathf.Rad2Deg;
            float relative = Mathf.DeltaAngle(srcYaw, inboundAngle);
            float exitAngle = dstYaw + relative;
            float snapped = Mathf.Round(exitAngle / 90f) * 90f;
            float ang = Mathf.DeltaAngle(0f, snapped);

            if (Mathf.Abs(ang) < 1f) exitDir = new Vector2Int(1, 0);
            else if (Mathf.Abs(ang - 90f) < 1f) exitDir = new Vector2Int(0, 1);
            else if (Mathf.Abs(Mathf.Abs(ang) - 180f) < 1f) exitDir = new Vector2Int(-1, 0);
            else exitDir = new Vector2Int(0, -1);
        }

        Vector3 target = paired.transform.position + paired.exitWorldOffset;
        // Ensure teleport places entity at ground Y = 0
        target.y = 0f;

        // small outward nudge to avoid overlapping the destination trigger
        Vector3 outward = Vector3.zero;
        if (exitDir != Vector2Int.zero) outward = new Vector3(exitDir.x, 0f, exitDir.y).normalized;
        if (outward.sqrMagnitude < 0.0001f) outward = paired.transform.forward;
        const float exitOffset = 0.20f;
        target += outward * exitOffset;

        if (motor != null)
        {
            motor.HardTeleport(target);

            if (exitDir != Vector2Int.zero)
            {
                motor.SetDesiredDirection(exitDir);
            }
            else if (lastMove.sqrMagnitude > 0.0001f)
            {
                Vector3 inv = -lastMove;
                Vector2Int dir =
                    Mathf.Abs(inv.x) >= Mathf.Abs(inv.z)
                        ? new Vector2Int(inv.x > 0 ? 1 : -1, 0)
                        : new Vector2Int(0, inv.z > 0 ? 1 : -1);

                motor.SetDesiredDirection(dir);
            }
        }
        else if (root.TryGetComponent(out CharacterController cc))
        {
            bool wasEnabled = cc.enabled;
            cc.enabled = false;
            root.position = target;
            cc.enabled = wasEnabled;
        }
        else if (root.TryGetComponent(out Rigidbody rb))
        {
            rb.position = target;

            if (exitDir != Vector2Int.zero && lastMove.sqrMagnitude > 0.0001f)
            {
                float speed = lastMove.magnitude;
                Vector3 outDir = new(exitDir.x, 0f, exitDir.y);
                rb.linearVelocity = outDir.normalized * speed;
            }
            else
            {
                rb.linearVelocity = -lastMove;
            }
        }
        else
        {
            root.position = target;
        }

        tl.MarkTeleportedNow();

        SetCooldown(key);
        if (paired != null)
            paired.SetCooldown(key);
    }

    int GetCooldownKey(Collider other)
    {
        EntityIdentity identity = EntityIdentityUtility.From(other);
        return identity.IsValid ? identity.InstanceId : other.transform.root.GetInstanceID();
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
}
