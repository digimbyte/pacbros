using UnityEngine;

/// <summary>
/// Simple dolly camera: snaps at start, smooth-follow via SmoothDamp, optional look-ahead,
/// and a small viewport clamp so the player stays on screen.
/// Designed to be simple and stable for fast lateral action.
/// </summary>
[DisallowMultipleComponent]
public class CameraControl : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Offset")]
    [Tooltip("World-space offset from target (applied in camera rotation).")]
    public Vector3 offset = new Vector3(0f, 6f, -10f);

    [Header("Follow")]
    [Tooltip("Smooth time for camera follow. Lower = snappier.")]
    public float smoothTime = 0.12f;

    [Tooltip("Maximum speed for SmoothDamp (0 = unlimited).")]
    public float maxSpeed = 40f;

    [Header("Look Ahead")]
    [Tooltip("How far ahead along velocity to bias the camera (world units).")]
    public float lookAhead = 2f;
    [Tooltip("Player speed that yields full lookAhead.")]
    public float lookAheadAtSpeed = 8f;

    [Header("Viewport")]
    [Range(0f, 0.5f)] public float deadzoneRadius = 0.16f;

    private Camera _cam;
    private Vector3 _velocity;
    private Vector3 _lastTargetPos;
    private bool _hasLast;
    private Vector3 _virtualFocus;
    private Vector3 _virtualFocusVel;
    private Vector3 _smoothedTargetVel;
    private Vector3 _smoothedTargetVelVel;
    [Header("Velocity Smoothing")]
    [Tooltip("Smooth time for target velocity smoothing. Lower = snappier.")]
    public float velocitySmoothTime = 0.12f;
    [Tooltip("If the target speed is below this value, smooth toward zero faster.")]
    public float velocityZeroSpeed = 0.5f;
    [Tooltip("Multiplier applied to smooth time when zeroing (0..1). Lower = faster zeroing.")]
    [Range(0.01f,1f)] public float velocityZeroSmoothFactor = 0.3f;
    [Header("Pivot")]
    [Tooltip("Optional pivot Transform. If set, the script will move the pivot (XZ) toward the virtual focus and leave the camera as a child to keep its local offset.")]
    public Transform pivot;
    private Vector3 _pivotVel;
    private bool _warnedNoPivot = false;
    [Tooltip("Unused: smoothing removed; offset follows smoothed velocity proportionally.")]
    private const float _unused_offsetSmooth = 0f;
    [Tooltip("Invert the look-ahead direction (subtract instead of add).")]
    public bool invertLookAhead = false;
    [Tooltip("How quickly the virtual focal point follows the player (seconds). Lower = snappier)")]
    public float focusSmoothTime = 0.12f;
    [Tooltip("Maximum allowed world magnitude for camera/virtual focus to avoid runaway values")]
    public float maxWorldRadius = 100000f;

    void Awake()
    {
        _cam = GetComponentInChildren<Camera>();
        if (_cam == null) _cam = Camera.main;
    }

    void Start()
    {
        if (target == null || _cam == null) return;
        // Snap camera to desired start position
        Vector3 targetFlat = new Vector3(target.position.x, 0f, target.position.z);
        _virtualFocus = targetFlat;
        _virtualFocusVel = Vector3.zero;
        // Require a pivot: we will never move the camera transform directly.
        if (pivot != null)
        {
            pivot.position = new Vector3(_virtualFocus.x, pivot.position.y, _virtualFocus.z);
        }
        else
        {
            if (!_warnedNoPivot)
            {
                Debug.LogWarning("CameraControl: No `pivot` assigned — script will not move the camera transform. Assign a pivot (parent of the camera) to enable movement.");
                _warnedNoPivot = true;
            }
        }
        _velocity = Vector3.zero;
        _lastTargetPos = target.position;
        _hasLast = true;
    }

    void LateUpdate()
    {
        if (target == null || _cam == null) return;

        Vector3 rawTargetPos = target.position;
        Vector3 targetVel = GetTargetVelocity(rawTargetPos);

        // compute desired virtual focal on ground (player pos + movement vector scaled)
        Vector3 playerFlat = new Vector3(rawTargetPos.x, 0f, rawTargetPos.z);
        float speed = targetVel.magnitude;
        float lookScale = lookAheadAtSpeed > 0f ? Mathf.Clamp01(speed / lookAheadAtSpeed) : 1f;
        Vector3 look = (targetVel.sqrMagnitude > 0.0001f) ? targetVel * (lookAhead * lookScale) : Vector3.zero;

        Vector3 desiredVirtual = playerFlat + new Vector3(look.x, 0f, look.z);
        // clamp runaway desiredVirtual
        if (desiredVirtual.sqrMagnitude > maxWorldRadius * maxWorldRadius) desiredVirtual = desiredVirtual.normalized * maxWorldRadius;

        // SmoothDamp the virtual focus on the ground
        _virtualFocus = Vector3.SmoothDamp(_virtualFocus, desiredVirtual, ref _virtualFocusVel, focusSmoothTime, maxSpeed, Time.deltaTime);

        // Compute desired camera pos from virtual focus and rotated horizontal offset; lock camera Y to offset.y above ground
        Vector3 desired = ComputeDesiredPosition(rawTargetPos, look, _virtualFocus);

        // If a pivot is provided, move the pivot toward the virtual focus (XZ) and leave the camera child offset alone.
        if (pivot != null)
        {
            Vector3 pivotTarget = new Vector3(_virtualFocus.x, pivot.position.y, _virtualFocus.z);
            pivot.position = Vector3.SmoothDamp(pivot.position, pivotTarget, ref _pivotVel, focusSmoothTime, maxSpeed, Time.deltaTime);
            // also gently damp camera velocity so local spring doesn't fight
            _velocity *= 0.8f;
        }
        else
        {
            if (!_warnedNoPivot)
            {
                Debug.LogWarning("CameraControl: No `pivot` assigned — script will not move the camera transform. Assign a pivot (parent of the camera) to enable movement.");
                _warnedNoPivot = true;
            }
        }

        // Simple XZ-only pivot target using last-known smoothed velocity.
        if (pivot != null)
        {
            Vector3 targetXZ = new Vector3(rawTargetPos.x, pivot.position.y, rawTargetPos.z);
            Vector3 velXZ = new Vector3(_smoothedTargetVel.x, 0f, _smoothedTargetVel.z);
            float speedVel = velXZ.magnitude;
            // Compute offset proportional to velocity so it naturally goes to zero when the target stops.
            float lookK = (lookAheadAtSpeed > 0f) ? (lookAhead / Mathf.Max(lookAheadAtSpeed, 0.0001f)) : 0f;
            Vector3 offsetXZ = velXZ * lookK;
            // Clamp magnitude to avoid exceeding configured lookAhead
            if (offsetXZ.magnitude > lookAhead) offsetXZ = offsetXZ.normalized * lookAhead;
            if (invertLookAhead) offsetXZ = -offsetXZ;
            Vector3 desiredPivot = targetXZ + offsetXZ;
            // clamp runaway
            if (desiredPivot.sqrMagnitude > maxWorldRadius * maxWorldRadius) desiredPivot = desiredPivot.normalized * maxWorldRadius;
            // Apply deadzone (slop): only move pivot when desiredPivot is outside `deadzoneRadius`.
            Vector3 delta = desiredPivot - pivot.position;
            float dz = Mathf.Max(0f, deadzoneRadius);
            if (delta.sqrMagnitude <= dz * dz)
            {
                // inside deadzone: don't move pivot (keeps the character "locked" with slop)
                // lightly damp camera velocity so local spring doesn't fight
                _velocity *= 0.9f;
            }
            else
            {
                // Move pivot toward desiredPivot but keep it at the edge of the deadzone to provide slop
                Vector3 pivotTarget = desiredPivot - delta.normalized * dz;
                pivot.position = Vector3.SmoothDamp(pivot.position, pivotTarget, ref _pivotVel, focusSmoothTime, maxSpeed, Time.deltaTime);
                // damp camera velocity lightly to avoid fight
                _velocity *= 0.8f;
            }
        }
        else
        {
            if (!_warnedNoPivot)
            {
                Debug.LogWarning("CameraControl: No `pivot` assigned — script will not move the camera transform. Assign a pivot (parent of the camera) to enable movement.");
                _warnedNoPivot = true;
            }
        }
    }

    Vector3 ComputeDesiredPosition(Vector3 rawTargetPosition, Vector3 lookAheadWorld, Vector3 virtualFocus)
    {
        // We treat the virtual focus as the ground focal point (y = 0)
        Vector3 focus = virtualFocus;
        // Use camera yaw to rotate horizontal offset so camera stays oriented
        Quaternion camYaw = (_cam != null) ? Quaternion.Euler(0f, _cam.transform.eulerAngles.y, 0f) : Quaternion.identity;
        Vector3 horizontalOffset = camYaw * new Vector3(offset.x, 0f, offset.z);
        // Camera y is focus.y + offset.y (locking camera height)
        return focus + horizontalOffset + Vector3.up * offset.y;
    }

    Vector3 GetTargetVelocity(Vector3 targetPos)
    {
        if (_hasLast)
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 vel = (targetPos - _lastTargetPos) / dt;
            _lastTargetPos = targetPos;
            // If target is nearly stopped, allow faster smoothing toward zero to avoid whiplash
            float smoothTime = velocitySmoothTime;
            if (vel.magnitude < velocityZeroSpeed) smoothTime *= velocityZeroSmoothFactor;
            _smoothedTargetVel = Vector3.SmoothDamp(_smoothedTargetVel, vel, ref _smoothedTargetVelVel, smoothTime, maxSpeed, dt);
            return _smoothedTargetVel;
        }
        _lastTargetPos = targetPos;
        _hasLast = true;
        _smoothedTargetVel = Vector3.zero;
        return Vector3.zero;
    }
}