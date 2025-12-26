using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class PairedPortal : MonoBehaviour
{
    public PairedPortal paired;

    [Header("Access")]
    public bool requiresKey;
    public KeycardColor requiredKey = KeycardColor.Green;
    public bool allowHigherKeys = true;

    [Header("Ghosts")]
    public bool allowGhosts = true;
    public bool ghostsBypassKeyRequirement;

    [Header("Teleport")]
    public Vector3 exitWorldOffset = Vector3.zero;
    public float cooldownSeconds = 0.20f;

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

        EntityBase entity = other.GetComponentInParent<EntityBase>();
        if (entity != null)
        {
            if (entity.isGhost && !allowGhosts) return;

            if (requiresKey && !(entity.isGhost && ghostsBypassKeyRequirement))
            {
                if (!entity.HasKeycard(requiredKey, allowHigherKeys))
                    return;
            }
        }

        // Teleport
        Vector3 target = paired.transform.position + paired.exitWorldOffset;
        TeleportOther(other, target);
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
        // Prefer entity root.
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
