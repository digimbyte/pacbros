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

        if (motor != null)
        {
            motor.HardTeleport(target);

            if (lastMove.sqrMagnitude > 0.0001f)
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
            rb.linearVelocity = lastMove;
        }
        else
        {
            root.position = target;
        }

        if (identity.IsValid)
            DoorOverrideRegistry.Consume(identity, gate);

        SetCooldown(key);
    }

    bool IsCoolingDown(int key)
    {
        return cooldownSeconds > 0f
            && _cooldowns.TryGetValue(key, out float until)
            && Time.time < until;
    }

    void SetCooldown(int key)
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
