using UnityEngine;
using DG.Tweening;

public class KeycardDoorController : MonoBehaviour
{
    [Header("Key Requirements")]
    public KeycardColor requiredKey = KeycardColor.Green;
    [Tooltip("If true, higher keys (e.g. Purple) can open lower doors (e.g. Red).")]
    public bool allowHigherKeys = true;

    [Header("Layer Collision (optional)")]
    [Tooltip("Entities on these layers will always be able to pass through the door blocker (even when closed) via Physics.IgnoreLayerCollision.")]
    public LayerMask ghostPassThroughLayers = 0;

    [Tooltip("If true, configures Physics.IgnoreLayerCollision between the door's layer and ghostPassThroughLayers at runtime.")]
    public bool configureGhostLayerIgnores = true;

    [Header("References")]
    [Tooltip("Cosmetic door transform that will be animated (NOT the trigger collider object).")]
    public Transform doorVisual;

    [Tooltip("Optional physical collider that blocks passage. Will be disabled when open.")]
    public Collider doorBlocker;

    [Header("Tween - Local Offsets")]
    [Tooltip("Door visual's local position offset when open, relative to its closed pose.")]
    public Vector3 openLocalPositionOffset = new Vector3(0f, 2f, 0f);

    [Tooltip("Door visual's local euler offset when open, relative to its closed pose.")]
    public Vector3 openLocalEulerOffset = Vector3.zero;

    [Header("Tween - Timing")]
    public float openDuration = 0.25f;
    public float closeDuration = 0.25f;
    public Ease openEase = Ease.OutCubic;
    public Ease closeEase = Ease.InCubic;
    public float autoCloseDelay = 0.15f;

    [Header("Closing Push")]
    [Tooltip("If set, will push any occupants while closing.")]
    public KeycardDoorTrigger trigger;

    [Tooltip("Strength of the closing push.")]
    public float closingPushStrength = 6f;

    [Tooltip("Push duration equals the close tween duration. Set 0 to disable pushing.")]
    public bool pushWhileClosing = true;

    Vector3 _closedLocalPos;
    Quaternion _closedLocalRot;
    Tween _tween;

    bool _isOpen;

    void Awake()
    {
        if (doorVisual == null)
            doorVisual = transform;

        _closedLocalPos = doorVisual.localPosition;
        _closedLocalRot = doorVisual.localRotation;

        if (configureGhostLayerIgnores && ghostPassThroughLayers.value != 0)
            ConfigureGhostLayerIgnores();

        // Ensure we start closed.
        SetOpenStateImmediate(false);
    }

    void ConfigureGhostLayerIgnores()
    {
        int doorLayer = (doorBlocker != null) ? doorBlocker.gameObject.layer : gameObject.layer;

        // Apply IgnoreLayerCollision for every layer bit set in the mask.
        for (int layer = 0; layer < 32; layer++)
        {
            if ((ghostPassThroughLayers.value & (1 << layer)) == 0)
                continue;

            Physics.IgnoreLayerCollision(doorLayer, layer, true);
        }
    }

    public void SetOpenStateImmediate(bool open)
    {
        _tween?.Kill();
        _tween = null;

        _isOpen = open;

        if (doorVisual != null)
        {
            if (open)
            {
                doorVisual.localPosition = _closedLocalPos + openLocalPositionOffset;
                doorVisual.localRotation = _closedLocalRot * Quaternion.Euler(openLocalEulerOffset);
            }
            else
            {
                doorVisual.localPosition = _closedLocalPos;
                doorVisual.localRotation = _closedLocalRot;
            }
        }

        if (doorBlocker != null)
            doorBlocker.enabled = !open;
    }

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        _tween?.Kill();

        if (doorBlocker != null)
            doorBlocker.enabled = false;

        if (doorVisual == null)
            return;

        Vector3 targetPos = _closedLocalPos + openLocalPositionOffset;
        Quaternion targetRot = _closedLocalRot * Quaternion.Euler(openLocalEulerOffset);

        _tween = DOTween.Sequence()
            .Join(doorVisual.DOLocalMove(targetPos, openDuration).SetEase(openEase))
            .Join(doorVisual.DOLocalRotateQuaternion(targetRot, openDuration).SetEase(openEase));
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        _tween?.Kill();

        if (doorVisual == null)
        {
            if (doorBlocker != null)
                doorBlocker.enabled = true;
            return;
        }

        // Delay close slightly (helps avoid jitter if multiple entities are rapidly entering/exiting).
        Sequence closeMotion = DOTween.Sequence()
            .Join(doorVisual.DOLocalMove(_closedLocalPos, closeDuration).SetEase(closeEase))
            .Join(doorVisual.DOLocalRotateQuaternion(_closedLocalRot, closeDuration).SetEase(closeEase));

        if (pushWhileClosing && closingPushStrength > 0f && trigger != null)
        {
            closeMotion.OnUpdate(() =>
            {
                trigger.PushOccupantsAlongWorldZ(transform.position.z, closingPushStrength);
            });
        }

        _tween = DOTween.Sequence()
            .AppendInterval(Mathf.Max(0f, autoCloseDelay))
            .Append(closeMotion)
            .OnComplete(() =>
            {
                if (doorBlocker != null)
                    doorBlocker.enabled = true;
            });
    }
}
