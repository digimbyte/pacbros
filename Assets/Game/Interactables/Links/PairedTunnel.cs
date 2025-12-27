using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(AccessGate))]
public class PairedTunnel : MonoBehaviour
{
    public PairedTunnel paired;

    // Access/security is handled by a required `AccessGate` component on this GameObject.

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
            Debug.LogError("PairedTunnel requires an AccessGate component to decide access.");
            return;
        }

        EntityBase entity = other.GetComponentInParent<EntityBase>();
        if (entity != null && !gate.HasAccess(entity))
            return;

        Transform root = other.transform.root;
        Vector3 rootPos = root.position;
        Vector2 rootXZ = new Vector2(rootPos.x, rootPos.z);
        Vector2 tunnelXZ = new Vector2(transform.position.x, transform.position.z);
        if (Vector2.Distance(rootXZ, tunnelXZ) > activationRadius) return;

        // Capture last movement direction and invert it for tunnels (opposite-way teleport)
        Vector3 lastMove = Vector3.zero;
        GridMotor motor = root.GetComponent<GridMotor>();
        if (motor != null) lastMove = motor.GetVelocity();
        else
        {
            var rb = root.GetComponent<Rigidbody>();
            if (rb != null) lastMove = rb.linearVelocity;
        }

        Vector3 target = paired.transform.position + paired.exitWorldOffset;

        // Determine inbound direction relative to this tunnel's rotation and compute corresponding exit direction
        Vector3 inbound = (root.position - transform.position);
        inbound.y = 0f;
        Vector2 inboundXZ = new Vector2(inbound.x, inbound.z);

        // If we couldn't determine a meaningful inbound vector, fall back to simple inversion
        bool hasInbound = inboundXZ.sqrMagnitude > 0.0001f;

        // Source and destination yaw (degrees)
        float srcYaw = transform.eulerAngles.y;
        float dstYaw = paired.transform.eulerAngles.y;

        Vector2Int exitDirCardinal = Vector2Int.zero;

        if (hasInbound)
        {
            float inboundAngle = Mathf.Atan2(inboundXZ.y, inboundXZ.x) * Mathf.Rad2Deg; // 0 = +X (east), 90 = +Z (north)
            float relative = Mathf.DeltaAngle(srcYaw, inboundAngle); // angle from source forward to inbound
            float exitAngle = dstYaw + relative;
            // Snap to nearest cardinal (90-degree) direction
            float snapped = Mathf.Round(exitAngle / 90f) * 90f;
            float ang = Mathf.DeltaAngle(0f, snapped); // normalize
            if (Mathf.Abs(Mathf.DeltaAngle(ang, 0f)) < 1f)
                exitDirCardinal = new Vector2Int(1, 0); // east
            else if (Mathf.Abs(Mathf.DeltaAngle(ang, 90f)) < 1f)
                exitDirCardinal = new Vector2Int(0, 1); // north
            else if (Mathf.Abs(Mathf.DeltaAngle(ang, 180f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(ang, -180f)) < 1f)
                exitDirCardinal = new Vector2Int(-1, 0); // west
            else
                exitDirCardinal = new Vector2Int(0, -1); // south
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
                // fallback: invert
                Vector3 inv = -lastMove;
                Vector2Int dir = (Mathf.Abs(inv.x) >= Mathf.Abs(inv.z)) ? new Vector2Int(inv.x > 0f ? 1 : -1, 0) : new Vector2Int(0, inv.z > 0f ? 1 : -1);
                motor.SetDesiredDirection(dir);
            }
        }
        else
        {
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
                    if (exitDirCardinal != Vector2Int.zero && lastMove.sqrMagnitude > 0.0001f)
                    {
                        // preserve speed, assign direction according to exitDirCardinal
                        float speed = lastMove.magnitude;
                        Vector3 outDir = new Vector3(exitDirCardinal.x, 0f, exitDirCardinal.y);
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
            }
        }

        SetCooldown(key);
    }

    int GetCooldownKey(Collider other)
    {
        EntityBase entity = other.GetComponentInParent<EntityBase>();
        if (entity != null) return entity.GetInstanceID();
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
        Transform root = other.transform.root;
        EntityBase entity = other.GetComponentInParent<EntityBase>();
        if (entity != null) root = entity.transform;

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
