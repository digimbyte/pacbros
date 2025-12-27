using UnityEngine;

/// <summary>
/// World-space camera follow with elastic (spring) motion and speed-based FOV.
/// Keeps the target within a 20% deadzone of the viewport before applying acceleration.
/// </summary>
[DisallowMultipleComponent]
public class CameraControl : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Primary target (e.g., player).")]
    public Transform target;

    [Header("Offset")]
    [Tooltip("World-space offset from target (in camera space forward/right/up).")]
    public Vector3 offset = new Vector3(0f, 12f, -10f);

    [Tooltip("How far ahead along the target move direction to bias the camera (world units).")]
    public float lookAhead = 2f;

    [Tooltip("Target speed that yields full lookAhead. Below this, look-ahead scales down.")]
    public float lookAheadAtSpeed = 8f;

    [Header("Elastic Motion")]
    [Tooltip("Spring strength pulling camera toward desired position.")]
    public float spring = 12f;

    [Tooltip("Damping factor (critically damped ~2*sqrt(spring)).")]
    public float damping = 18f;

    [Tooltip("Max translation speed of the camera (units/sec). 0 = unlimited.")]
    public float maxSpeed = 40f;

    [Tooltip("Half-size of the center box in viewport fraction. 0.166 = player stays within center third.")]
    [Range(0f, 0.5f)] public float deadzoneBox = 1f / 6f;
    [Tooltip("Fraction of spring/damping applied even when inside deadzone (prevents drift when standing still).")]
    [Range(0f, 1f)] public float recenterFactor = 0.35f;
    [Header("Velocity Smoothing")]
    [Tooltip("How quickly target velocity estimate follows position deltas. Higher = snappier, lower = smoother.")]
    public float velocityLerp = 10f;

    [Header("FOV")]
    [Tooltip("Camera Field of View at zero speed.")]
    public float fovMin = 55f;

    [Tooltip("Camera Field of View at maxSpeedForFov or higher.")]
    public float fovMax = 70f;

    [Tooltip("Target speed that maps to fovMax.")]
    public float maxSpeedForFov = 12f;

    [Tooltip("How quickly FOV interpolates to its target.")]
    public float fovLerpSpeed = 6f;

    private Camera _cam;
    private Vector3 _vel; // camera velocity (for our spring)
    private Vector3 _lastTargetPos;
    private bool _hasLast;
    private float _fovVel;
    private Vector3 _smoothedTargetVel;

    void Awake()
    {
        _cam = GetComponentInChildren<Camera>();
        if (_cam == null)
            _cam = Camera.main;
    }

    void Start()
    {
        // On start, immediately snap camera to desired position so the player starts centered
        if (target == null || _cam == null) return;
        Vector3 targetPos = target.position;
        // compute desired using the actual camera rotation (safer if camera is child)
        Vector3 desired = targetPos + _cam.transform.rotation * offset;
        transform.position = desired;
        _vel = Vector3.zero;
        // initialize velocity tracking
        _lastTargetPos = targetPos;
        _hasLast = true;
        _smoothedTargetVel = Vector3.zero;
    }

    void LateUpdate()
    {
        if (target == null || _cam == null) return;

        Vector3 rawTargetPos = target.position;
        Vector3 targetVel = GetTargetVelocity(rawTargetPos);
        // For camera placement/clamping assume player Y = 0 (flat ground)
        Vector3 targetPos = new Vector3(rawTargetPos.x, 0f, rawTargetPos.z);

        // Desired point: target + look-ahead in move dir + offset in camera space
        float speed = targetVel.magnitude;
        float lookAheadDist = lookAheadAtSpeed > 0.0001f
            ? Mathf.Lerp(0f, lookAhead, Mathf.InverseLerp(0f, lookAheadAtSpeed, speed))
            : lookAhead;

        // Use camera-plane velocity (right/forward, ignore vertical) so look-ahead matches view angle.
        Vector3 camRight = _cam.transform.right;
        Vector3 camForward = Vector3.ProjectOnPlane(_cam.transform.forward, Vector3.up).normalized;
        Vector3 velOnPlane = camRight * Vector3.Dot(targetVel, camRight) + camForward * Vector3.Dot(targetVel, camForward);

        Vector3 ahead = velOnPlane.sqrMagnitude > 0.01f ? velOnPlane.normalized * lookAheadDist : Vector3.zero;
        Vector3 desired = targetPos + ahead + _cam.transform.rotation * offset;

        // Apply deadzone in viewport: if target is within deadzone, freeze acceleration.
        Vector3 viewport = _cam.WorldToViewportPoint(targetPos);
        float cx = viewport.x - 0.5f;
        float cy = viewport.y - 0.5f;
        bool insideDeadzone = Mathf.Abs(cx) <= deadzoneBox && Mathf.Abs(cy) <= deadzoneBox;
        // If nearly stopped, always recenter (ignore deadzone).
        if (speed < 0.1f) insideDeadzone = false;

        Vector3 toDesired = desired - transform.position;
        Vector3 accel = Vector3.zero;
        float springScale = insideDeadzone ? recenterFactor : 1f;
        // Boost spring with horizontal speed for snappier lateral camera at higher player speeds
        float speedFactor = lookAheadAtSpeed > 0f ? Mathf.Clamp01(speed / lookAheadAtSpeed) : 0f;
        float effectiveSpring = Mathf.Lerp(spring, spring * 1.8f, speedFactor) * springScale;
        float effectiveDamping = Mathf.Lerp(damping, damping * 1.4f, speedFactor) * springScale;
        accel = toDesired * effectiveSpring - _vel * effectiveDamping;

        _vel += accel * Time.deltaTime;
        if (maxSpeed > 0f)
            _vel = Vector3.ClampMagnitude(_vel, maxSpeed);

        transform.position += _vel * Time.deltaTime;

        // Ensure the target remains within the viewport deadzone: if it's outside, correct
        // the camera by using a ray -> ground (y=0) intersection as the camera's focal point
        // and moving the camera in XZ so that the focal point moves toward the player.
        Vector3 vp = _cam.WorldToViewportPoint(rawTargetPos);
        float cx2 = vp.x - 0.5f;
        float cy2 = vp.y - 0.5f;
        bool outsideDeadzone = Mathf.Abs(cx2) > deadzoneBox || Mathf.Abs(cy2) > deadzoneBox;
        if (outsideDeadzone)
        {
            // Ray from camera along forward to intersect Y=0 plane
            Vector3 camPos = _cam.transform.position;
            Vector3 camFwd = _cam.transform.forward;
            Vector3 focal;
            if (Mathf.Abs(camFwd.y) > 1e-5f)
            {
                float tau = (0f - camPos.y) / camFwd.y;
                focal = camPos + camFwd * tau;
            }
            else
            {
                // fallback: project camera position down to ground
                focal = new Vector3(camPos.x, 0f, camPos.z);
            }

            Vector3 playerFlat = new Vector3(rawTargetPos.x, 0f, rawTargetPos.z);
            Vector3 correction = playerFlat - focal; // in world XZ
            Vector3 correctionXZ = new Vector3(correction.x, 0f, correction.z);

            // Safety: avoid massive teleports from bad math
            float maxCorrection = 50f;
            if (correctionXZ.magnitude > maxCorrection)
                correctionXZ = correctionXZ.normalized * maxCorrection;

            transform.position += correctionXZ;
            _vel = Vector3.zero;
        }

        // Always face same yaw/pitch as current rotation; do not spin.
        // If you want to keep a fixed roll of 0, ensure rotation is managed in editor.

        // FOV based on speed
        float t = Mathf.InverseLerp(0f, maxSpeedForFov, speed);
        float targetFov = Mathf.Lerp(fovMin, fovMax, t);
        _cam.fieldOfView = Mathf.SmoothDamp(_cam.fieldOfView, targetFov, ref _fovVel, 1f / Mathf.Max(0.0001f, fovLerpSpeed));
    }

    Vector3 GetTargetVelocity(Vector3 targetPos)
    {
        if (_hasLast)
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 vel = (targetPos - _lastTargetPos) / dt;
            _lastTargetPos = targetPos;
            float lerp = Mathf.Clamp01(velocityLerp * dt);
            _smoothedTargetVel = Vector3.Lerp(_smoothedTargetVel, vel, lerp);
            return _smoothedTargetVel;
        }
        _lastTargetPos = targetPos;
        _hasLast = true;
        _smoothedTargetVel = Vector3.zero;
        return Vector3.zero;
    }
}
