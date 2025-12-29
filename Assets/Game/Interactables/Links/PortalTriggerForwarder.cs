using UnityEngine;

/// <summary>
/// Place this on a child trigger collider and assign `parentPortal` (or let it auto-find).
/// It forwards OnTriggerEnter to the parent `PairedPortal` so you can use a separate
/// activation collider without moving the `PairedPortal` component.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PortalTriggerForwarder : MonoBehaviour
{
    [Tooltip("PairedPortal to forward to. If null, will try to find one on parent objects.")]
    public PairedPortal parentPortal;

    void Awake()
    {
        if (parentPortal == null)
            parentPortal = GetComponentInParent<PairedPortal>();
        if (parentPortal == null)
            Debug.LogWarning($"PortalTriggerForwarder on '{name}' has no PairedPortal parent.", this);
    }

    void OnTriggerEnter(Collider other)
    {
        if (parentPortal == null) return;
        parentPortal.ExternalTriggerEnter(other);
    }

    // Optional: forward other trigger events if you need them in future.
}
