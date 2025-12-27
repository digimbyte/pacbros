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

    [Tooltip("Optional Rigidbody to read velocity for FOV scaling. If null, uses target delta per frame.")]
    public Rigidbody targetBody;

    [Header("Offset")]
    [Tooltip("World-space offset from target (in camera space forward/right/up).")]
    public Vector3 offset = new Vector3(0f, 12f, -10f);

    [Tooltip("How far ahead along the target move direction to bias the camera (world units).")]
    public float lookAhead = 2f;

    [Header("Elastic Motion")]
    [Tooltip("Spring strength pulling camera toward desired position.")]
    public float spring = 12f;

    [Tooltip("Damping factor (critically damped ~2*sqrt(spring)).")]
    public float damping = 18f;

    [Tooltip("Max translation speed of the camera (units/sec). 0 = unlimited.")]
    public float maxSpeed = 40f;

    [Tooltip("Viewport deadzone radius (fraction of min(width,height)); inside it, no acceleration. Default 0.2 = 20%.")]
    [Range(0f, 0.45f)] public float deadzone = 0.2f;

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

    void Awake()
    {
        _cam = GetComponentInChildren<Camera>();
        if (_cam == null)
            _cam = Camera.main;
    }

    void LateUpdate()
    {
        if (target == null || _cam == null) return;

        Vector3 targetPos = target.position;
        Vector3 targetVel = GetTargetVelocity(targetPos);

        // Desired point: target + look-ahead in move dir + offset in camera space
        Vector3 ahead = targetVel.sqrMagnitude > 0.01f ? targetVel.normalized * lookAhead : Vector3.zero;
        Vector3 desired = targetPos + ahead + transform.rotation * offset;

        // Apply deadzone in viewport: if target is within deadzone, freeze acceleration.
        Vector3 viewport = _cam.WorldToViewportPoint(targetPos);
        float cx = viewport.x - 0.5f;
        float cy = viewport.y - 0.5f;
        float radius = Mathf.Sqrt(cx * cx + cy * cy);
        bool insideDeadzone = radius <= deadzone;

        Vector3 toDesired = desired - transform.position;
        Vector3 accel = Vector3.zero;
        if (!insideDeadzone)
        {
            accel = toDesired * spring - _vel * damping;
        }

        _vel += accel * Time.deltaTime;
        if (maxSpeed > 0f)
            _vel = Vector3.ClampMagnitude(_vel, maxSpeed);

        transform.position += _vel * Time.deltaTime;

        // Always face same yaw/pitch as current rotation; do not spin.
        // If you want to keep a fixed roll of 0, ensure rotation is managed in editor.

        // FOV based on speed
        float speed = targetVel.magnitude;
        float t = Mathf.InverseLerp(0f, maxSpeedForFov, speed);
        float targetFov = Mathf.Lerp(fovMin, fovMax, t);
        _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, targetFov, fovLerpSpeed * Time.deltaTime);
    }

    Vector3 GetTargetVelocity(Vector3 targetPos)
    {
        if (targetBody != null)
        {
            return targetBody.linearVelocity;
        }
        if (_hasLast)
        {
            Vector3 vel = (targetPos - _lastTargetPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            _lastTargetPos = targetPos;
            return vel;
        }
        _lastTargetPos = targetPos;
        _hasLast = true;
        return Vector3.zero;
    }
}
