using UnityEngine;

/// <summary>
/// Place this on a child trigger collider and assign `parentTunnel` (or let it auto-find).
/// It forwards OnTriggerEnter to the parent `PairedTunnel` so you can use a separate
/// activation collider without moving the `PairedTunnel` component.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TunnelTriggerForwarder : MonoBehaviour
{
    [Tooltip("PairedTunnel to forward to. If null, will try to find one on parent objects.")]
    public PairedTunnel parentTunnel;

    void Awake()
    {
        if (parentTunnel == null)
            parentTunnel = GetComponentInParent<PairedTunnel>();
        if (parentTunnel == null)
            Debug.LogWarning($"TunnelTriggerForwarder on '{name}' has no PairedTunnel parent.", this);
    }

    void OnTriggerEnter(Collider other)
    {
        if (parentTunnel == null) return;
        parentTunnel.ExternalTriggerEnter(other);
    }

    void OnTriggerExit(Collider other)
    {
        if (parentTunnel == null) return;
        parentTunnel.ExternalTriggerExit(other);
    }
}
