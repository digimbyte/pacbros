using UnityEngine;

/// <summary>
/// Continuous movement that stays grid-respecting (90-degree turns) without feeling staggered.
///
/// Core idea:
/// - Movement is cardinal (X or Z) based on input or path-following.
/// - A spring pulls the perpendicular axis toward the nearest lane/centerline.
/// - Turns are buffered and applied when we're "close enough" to the intersection.
/// - Collision checks prevent stepping into blocked space.
/// </summary>
[DisallowMultipleComponent]
public class GridMotor : MonoBehaviour
{
    [Header("Grid")]
    public float cellSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;

    [Range(-1f, 1f)] public float gridOffsetX = 0f;
    [Range(-1f, 1f)] public float gridOffsetZ = 0f;

    [Header("Body")]
    public CharacterController characterController;
    public float radiusOverride = 0f; // 0 = use CC radius, else explicit
    public float heightOverride = 0f; // 0 = use CC height, else explicit

    [Header("Movement")]
    public float moveSpeed = 6f;
    public float acceleration = 60f;
    public float directionDeadzone = 0.15f;

    [Header("Turn / Corner Assist")]
    public float turnAlignEpsilon = 0.20f;

    [Tooltip("Spring strength pulling the perpendicular axis toward the nearest lane center.")]
    public float laneSnapSpring = 55f;

    [Tooltip("Damping for the snap spring. Higher = less oscillation.")]
    public float laneSnapDamping = 14f;

    [Tooltip("Max lateral correction speed contributed by lane snapping.")]
    public float laneSnapMaxSpeed = 8f;

    [Header("Collision")]
    public LayerMask solidMask = ~0;
    public float forwardProbeDistance = 0.20f;
    public float skin = 0.02f;

    Vector2 _desiredInput;

    // Current and queued directions in grid-space.
    Vector2Int _moveDir;
    Vector2Int _queuedDir;

    Vector3 _velocity;

    public void HardTeleport(Vector3 worldPosition)
    {
        // Reset state so snapping/turn buffering doesn't produce a big correction impulse.
        _velocity = Vector3.zero;
        _moveDir = Vector2Int.zero;
        _queuedDir = Vector2Int.zero;

        if (characterController != null && characterController.enabled)
        {
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.position = worldPosition;
            characterController.enabled = wasEnabled;
        }
        else
        {
            transform.position = worldPosition;
        }
    }

    void Reset()
    {
        characterController = GetComponent<CharacterController>();
    }

    void Awake()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();
    }

    public void SetDesiredInput(Vector2 inputXZ)
    {
        _desiredInput = inputXZ;
    }

    /// <summary>
    /// For AI/path-following: force the desired cardinal direction.
    /// </summary>
    public void SetDesiredDirection(Vector2Int dir)
    {
        _desiredInput = Vector2.zero;
        _queuedDir = ClampDir(dir);
        if (_moveDir == Vector2Int.zero)
            _moveDir = _queuedDir;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // 1) Derive desired cardinal direction from input (if any)
        Vector2Int inputDir = GetCardinalFromInput(_desiredInput, directionDeadzone);
        if (inputDir != Vector2Int.zero)
            _queuedDir = inputDir;

        // 2) Apply queued turn when aligned enough and the next cell isn't blocked
        if (_queuedDir != Vector2Int.zero)
        {
            if (_moveDir == Vector2Int.zero)
            {
                if (!IsBlocked(_queuedDir))
                    _moveDir = _queuedDir;
            }
            else if (_queuedDir != _moveDir)
            {
                bool perpendicular = (_queuedDir.x != 0 && _moveDir.y != 0) || (_queuedDir.y != 0 && _moveDir.x != 0);
                if (perpendicular && IsTurnAligned(_queuedDir) && !IsBlocked(_queuedDir))
                    _moveDir = _queuedDir;
            }
        }

        // 3) If forward is blocked, don't keep pushing into it
        if (_moveDir != Vector2Int.zero && IsBlocked(_moveDir))
        {
            _moveDir = Vector2Int.zero;
        }

        // 4) Build desired velocity: cardinal motion + perpendicular lane snap
        Vector3 desiredVel = Vector3.zero;
        if (_moveDir != Vector2Int.zero)
        {
            Vector3 dirWorld = new Vector3(_moveDir.x, 0f, _moveDir.y);
            desiredVel += dirWorld * moveSpeed;
        }

        desiredVel += ComputeLaneSnapVelocity(dt);

        // 5) Accelerate toward desired velocity
        _velocity = Vector3.MoveTowards(_velocity, desiredVel, acceleration * dt);

        // 6) Move via CharacterController (preferred) or fallback to transform
        if (characterController != null && characterController.enabled)
        {
            characterController.Move(_velocity * dt);
        }
        else
        {
            transform.position += _velocity * dt;
        }
    }

    Vector3 ComputeLaneSnapVelocity(float dt)
    {
        // Pull the perpendicular coordinate toward the nearest cell centerline.
        // If we're moving along X, we snap Z. If moving along Z, we snap X.
        // If not moving, snap both (lightly) so entities settle nicely.

        Vector3 pos = transform.position;

        bool snapX = _moveDir == Vector2Int.zero || _moveDir.y != 0;
        bool snapZ = _moveDir == Vector2Int.zero || _moveDir.x != 0;

        Vector3 origin = EffectiveOrigin();
        float targetX = snapX ? SnapToLane(pos.x, origin.x, cellSize) : pos.x;
        float targetZ = snapZ ? SnapToLane(pos.z, origin.z, cellSize) : pos.z;

        float errX = targetX - pos.x;
        float errZ = targetZ - pos.z;

        // Critically-damped-ish spring in velocity form
        Vector3 snapVel = Vector3.zero;
        if (snapX)
        {
            float v = laneSnapSpring * errX - laneSnapDamping * _velocity.x;
            snapVel.x = Mathf.Clamp(v, -laneSnapMaxSpeed, laneSnapMaxSpeed);
        }
        if (snapZ)
        {
            float v = laneSnapSpring * errZ - laneSnapDamping * _velocity.z;
            snapVel.z = Mathf.Clamp(v, -laneSnapMaxSpeed, laneSnapMaxSpeed);
        }

        return snapVel;
    }

    bool IsTurnAligned(Vector2Int turningTo)
    {
        // To turn into X motion, Z must be near its lane center. To turn into Z motion, X must be near its lane center.
        Vector3 pos = transform.position;
        Vector3 origin = EffectiveOrigin();
        if (turningTo.x != 0)
        {
            float targetZ = SnapToLane(pos.z, origin.z, cellSize);
            return Mathf.Abs(targetZ - pos.z) <= turnAlignEpsilon;
        }
        if (turningTo.y != 0)
        {
            float targetX = SnapToLane(pos.x, origin.x, cellSize);
            return Mathf.Abs(targetX - pos.x) <= turnAlignEpsilon;
        }
        return false;
    }

    bool IsBlocked(Vector2Int dir)
    {
        // Probe ahead with capsule cast to see if we'd hit a wall.
        Vector3 dirWorld = new Vector3(dir.x, 0f, dir.y);
        if (dirWorld.sqrMagnitude < 0.5f) return false;

        float radius, height;
        GetCapsuleDims(out radius, out height);

        Vector3 center = transform.position;
        float half = Mathf.Max(0f, (height * 0.5f) - radius);

        Vector3 p1 = center + Vector3.up * half;
        Vector3 p2 = center - Vector3.up * half;

        float dist = Mathf.Max(0f, forwardProbeDistance);
        return Physics.CapsuleCast(p1, p2, Mathf.Max(0.001f, radius - skin), dirWorld.normalized, dist, solidMask, QueryTriggerInteraction.Ignore);
    }

    void GetCapsuleDims(out float radius, out float height)
    {
        if (characterController != null)
        {
            radius = radiusOverride > 0f ? radiusOverride : characterController.radius;
            height = heightOverride > 0f ? heightOverride : characterController.height;
            return;
        }

        // fallback
        radius = radiusOverride > 0f ? radiusOverride : 0.45f;
        height = heightOverride > 0f ? heightOverride : 1.8f;
    }

    static Vector2Int GetCardinalFromInput(Vector2 input, float deadzone)
    {
        if (input.sqrMagnitude < deadzone * deadzone)
            return Vector2Int.zero;

        // Choose dominant axis (prevents diagonal corner cutting).
        if (Mathf.Abs(input.x) >= Mathf.Abs(input.y))
            return new Vector2Int(input.x >= 0f ? 1 : -1, 0);
        return new Vector2Int(0, input.y >= 0f ? 1 : -1);
    }

    public Vector3 EffectiveOrigin()
    {
        // Offsets are expressed as a fraction of a cell size.
        // Example: for a half-cell offset, set gridOffsetX/Z = 0.5.
        return gridOrigin + new Vector3(gridOffsetX * cellSize, 0f, gridOffsetZ * cellSize);
    }

    static float SnapToLane(float v, float origin, float size)
    {
        if (size <= 0f) return v;
        float t = (v - origin) / size;
        float snapped = Mathf.Round(t) * size + origin;
        return snapped;
    }

    static Vector2Int ClampDir(Vector2Int dir)
    {
        // Clamp to cardinal
        if (dir.x != 0) return new Vector2Int(dir.x > 0 ? 1 : -1, 0);
        if (dir.y != 0) return new Vector2Int(0, dir.y > 0 ? 1 : -1); // allow callers passing Vector2Int(x,y)
        return Vector2Int.zero;
    }
}
