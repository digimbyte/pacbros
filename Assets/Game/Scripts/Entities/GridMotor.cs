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
    static readonly System.Collections.Generic.List<GridMotor> _pendingMotors = new System.Collections.Generic.List<GridMotor>();
    [Header("Grid")]
    public float cellSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;

    [Tooltip("If true, pull grid origin / cell size / solidMask from LevelRuntime.Active on Awake.")]
    public bool bindFromLevelRuntime = true;

    [Range(-1f, 1f)] public float gridOffsetX = 0f;
    [Range(-1f, 1f)] public float gridOffsetZ = 0f;

    [Header("Body")]
    public CharacterController characterController;
    public float radiusOverride = 0f; // 0 = use CC radius, else explicit
    public float heightOverride = 0f; // 0 = use CC height, else explicit

    [Header("Movement")]
    public float moveSpeed = 6f;
    public float acceleration = 60f;
    [Tooltip("If true, velocity snaps directly to desired each frame (arcade slide). If false, use acceleration ramping.")]
    public bool instantAcceleration = true;
    [Tooltip("When forward is blocked: if true, clear moveDir/velocity immediately; if false, hold direction and wait for clearance/turn.")]
    public bool stopOnBlocked = true;

    [Tooltip("If true, a perpendicular queued turn is allowed when forward is blocked (when aligned and open).")]
    public bool allowBufferedTurnOnBlock = true;

    [Header("Buff/Debuff Multipliers")]
    [Tooltip("Runtime multiplier applied to moveSpeed.")]
    public float speedMultiplier = 1f;
    [Tooltip("Runtime multiplier applied to acceleration when instantAcceleration is false.")]
    public float accelerationMultiplier = 1f;
    [Tooltip("Runtime multiplier applied to lane snap positional correction speed.")]
    public float laneSnapMultiplier = 1f;
    public float directionDeadzone = 0.15f;
    [Tooltip("Allow immediate reversal (Pac-Man style). Off by default; you must stop to reverse.")]
    public bool allowInstantReverse = false;

    [Header("Turn / Corner Assist")]
    public float turnAlignEpsilon = 0.30f;

    [Tooltip("Spring strength pulling the perpendicular axis toward the nearest lane center.")]
    public float laneSnapSpring = 55f;

    [Tooltip("Damping for the snap spring. Higher = less oscillation.")]
    public float laneSnapDamping = 14f;

    [Tooltip("Max lateral correction speed contributed by lane snapping.")]
    public float laneSnapMaxSpeed = 8f;

    [Header("Collision")]
    public LayerMask solidMask = ~0;
    public float forwardProbeDistance = 0.10f;
    public float skin = 0.02f;
    [Tooltip("Scales collision probe radius to let the capsule slip past corners (0.5..1).")]
    [Range(0.5f, 1f)] public float cornerRadiusScale = 0.85f;
    [Tooltip("If true, ignore colliders that belong to PlayerEntity or EnemyEntity so entities phase through each other.")]
    public bool ignoreEntityColliders = true;
    [Tooltip("If true, snap to the grid lane immediately when `SetDesiredDirection` is called.")]
    public bool snapToGridOnSetDirection = true;

    [Header("Debug")]
    [Tooltip("If true, logs why movement is blocked (out-of-bounds vs collider hit). Throttled.")]
    public bool debugBlockers = false;
    public float debugBlockerLogInterval = 0.25f;

    Vector2 _desiredInput;
    float _nextBlockerLogTime;

    float _lastNudgeTime = 0f;

    // Current and queued directions in grid-space.
    Vector2Int _moveDir;
    Vector2Int _queuedDir;

    Vector3 _velocity;
    Vector3 _lastSafePos;
    Vector2Int _lastSafeCell;

    public void HardTeleport(Vector3 worldPosition)
    {
        // Reset state so snapping/turn buffering doesn't produce a big correction impulse.
        _velocity = Vector3.zero;
        _moveDir = Vector2Int.zero;
        _queuedDir = Vector2Int.zero;

        // If a CharacterController is present, compute the transform position such that
        // the capsule's bottom (feet) sits at worldPosition.y. This compensates for
        // CharacterController.center and height so entities land exactly on the grid.
        if (characterController != null)
        {
            float radius, height;
            GetCapsuleDims(out radius, out height);

            // center is in local-space; we only need its Y component for vertical math.
            float centerLocalY = characterController.center.y;

            // Desired bottom Y is the provided worldPosition.y. Solve for transform.position.y
            // bottomY = (transformY + centerLocalY) - height/2
            // => transformY = bottomY - centerLocalY + height/2
            Vector3 target = worldPosition;
            target.y = worldPosition.y - centerLocalY + (height * 0.5f);

            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.position = target;
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

        TryRegisterWithLevel();
    }

    void OnValidate()
    {
        cornerRadiusScale = Mathf.Clamp(cornerRadiusScale, 0.5f, 1f);
    }

    void OnEnable()
    {
        TryRegisterWithLevel();
    }

    void OnDisable()
    {
        if (LevelRuntime.Active != null)
            LevelRuntime.Active.UnregisterMotor(this);
        _pendingMotors.Remove(this);
    }

    void TryRegisterWithLevel()
    {
        if (!bindFromLevelRuntime)
            return;

        if (LevelRuntime.Active != null)
        {
            LevelRuntime.Active.RegisterMotor(this);
        }
        else if (!_pendingMotors.Contains(this))
        {
            _pendingMotors.Add(this);
        }
    }

    internal void OnLevelReady(LevelRuntime level)
    {
        if (level == null) return;

        cellSize   = level.cellSize;
        gridOrigin = level.gridOrigin;
        solidMask  = level.solidMask;
    }

    public static void FlushPendingMotorsTo(LevelRuntime level)
    {
        if (level == null) return;

        for (int i = 0; i < _pendingMotors.Count; i++)
        {
            var m = _pendingMotors[i];
            if (m != null)
                level.RegisterMotor(m);
        }
        _pendingMotors.Clear();
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
        // Optionally snap to grid axis immediately when direction is set (helps AI align to lanes)
        // Always attempt to snap when the caller requests it; snapping only when move==queued
        // could leave AI slightly off-lane (e.g. half-cell offsets) and prevent turns.
        if (snapToGridOnSetDirection)
        {
            SnapToIntersectionForTurn(_queuedDir);
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Track last safe at the start of the frame; will be overwritten after a successful move.
        _lastSafePos = transform.position;
        _lastSafeCell = CurrentCell();

        // 1) Derive desired cardinal direction from input (if any)
        Vector2Int inputDir = GetCardinalFromInput(_desiredInput, directionDeadzone);
        if (stopOnBlocked && inputDir != Vector2Int.zero && IsBlocked(inputDir))
        {
            inputDir = Vector2Int.zero; // ignore pushing into walls/out-of-bounds
            _queuedDir = Vector2Int.zero;
        }
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
                // Instant reverse (e.g., Pac-Man can turn back immediately)
                if (allowInstantReverse && _queuedDir == -_moveDir && !IsBlocked(_queuedDir))
                {
                    _moveDir = _queuedDir;
                }
                else
                {
                    bool perpendicular = (_queuedDir.x != 0 && _moveDir.y != 0) || (_queuedDir.y != 0 && _moveDir.x != 0);
                    if (perpendicular && IsTurnAligned(_queuedDir) && !IsBlocked(_queuedDir))
                        _moveDir = _queuedDir;
                }
            }
        }

        // 3) If forward is blocked, don't keep pushing into it
        if (_moveDir != Vector2Int.zero && IsBlocked(_moveDir))
        {
            // Pac-Man style: if we have a perpendicular queued turn and that lane is open,
            // take it only when we're aligned; otherwise stop to avoid hooking into blocked corners.
            bool turned = false;
            if (allowBufferedTurnOnBlock && _queuedDir != Vector2Int.zero && _queuedDir != _moveDir)
            {
                bool perpendicular = (_queuedDir.x != 0 && _moveDir.y != 0) || (_queuedDir.y != 0 && _moveDir.x != 0);
                if (perpendicular && !IsBlocked(_queuedDir) && IsTurnAligned(_queuedDir))
                {
                    _moveDir = _queuedDir;
                    turned = true;
                }
            }
            if (!turned && stopOnBlocked)
            if (!turned)
            {
                // Stop moving but keep queuedDir so we can turn or reverse next frame.
                _moveDir = Vector2Int.zero;
                _velocity = Vector3.zero;
                _queuedDir = Vector2Int.zero;
            }
        }

        // 3b) Re-validate current forward cell every frame; stop if newly blocked.
        if (_moveDir != Vector2Int.zero && IsBlocked(_moveDir))
        {
            _moveDir = Vector2Int.zero;
            _velocity = Vector3.zero;
        }

        // 4) Build desired velocity: cardinal-only motion (no diagonals)
        Vector3 desiredVel = Vector3.zero;
        if (_moveDir != Vector2Int.zero)
        {
            Vector3 dirWorld = new Vector3(_moveDir.x, 0f, _moveDir.y);
            desiredVel = dirWorld * moveSpeed * speedMultiplier;
        }

        // Apply lane snap by positional correction (does not create diagonal velocity).
        ApplyLaneSnap(dt);

        // 5) Accelerate toward desired velocity (or snap instantly for block-slide feel)
        float accel = acceleration * Mathf.Max(0f, accelerationMultiplier);
        if (instantAcceleration)
        {
            _velocity = desiredVel;
        }
        else
        {
            _velocity = Vector3.MoveTowards(_velocity, desiredVel, accel * dt);
        }
        // 6) Clamp displacement against walls as a fallback to avoid sticking/penetration.
        Vector3 displacement = _velocity * dt;
        displacement = ClampDisplacement(displacement);

        // 7) Move via CharacterController (preferred) or fallback to transform
        if (characterController != null && characterController.enabled)
        {
            characterController.Move(displacement);
        }
        else
        {
            transform.position += displacement;
        }

        // 8) If we ended up inside a wall, attempt a gentle penetration-resolve nudge
        // before resorting to a hard teleport back to the last safe position.
        if (IsInsideWall())
        {
            bool resolved = TryResolvePenetration();
            if (!resolved)
            {
                HardTeleport(_lastSafePos);
            }
            _velocity = Vector3.zero;
            _moveDir = Vector2Int.zero;
            _queuedDir = Vector2Int.zero;
        }
        else
        {
            _lastSafePos = transform.position;
            _lastSafeCell = CurrentCell();
        }
    }

    void ApplyLaneSnap(float dt)
    {
        Vector3 pos = transform.position;
        Vector3 origin = EffectiveOrigin();

        bool snapX = _moveDir == Vector2Int.zero || _moveDir.y != 0;
        bool snapZ = _moveDir == Vector2Int.zero || _moveDir.x != 0;

        if (snapX)
        {
            float targetX = SnapToLane(pos.x, origin.x, cellSize);
            pos.x = Mathf.MoveTowards(pos.x, targetX, laneSnapMaxSpeed * laneSnapMultiplier * dt);
        }
        if (snapZ)
        {
            float targetZ = SnapToLane(pos.z, origin.z, cellSize);
            pos.z = Mathf.MoveTowards(pos.z, targetZ, laneSnapMaxSpeed * laneSnapMultiplier * dt);
        }

        if (characterController != null && characterController.enabled)
        {
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.position = pos;
            characterController.enabled = wasEnabled;
        }
        else
        {
            transform.position = pos;
        }
    }

    bool IsEntityCollider(Collider col)
    {
        if (!ignoreEntityColliders || col == null) return false;
        if (col.GetComponentInParent<PlayerEntity>() != null) return true;
        if (col.GetComponentInParent<EnemyEntity>() != null) return true;
        return false;
    }

    /// <summary>
    /// Expose current velocity for external systems (teleporters, effects).
    /// </summary>
    public Vector3 GetVelocity()
    {
        return _velocity;
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

        if (IsOutOfBounds(dirWorld))
        {
            if (debugBlockers && Time.unscaledTime >= _nextBlockerLogTime)
            {
                _nextBlockerLogTime = Time.unscaledTime + Mathf.Max(0.05f, debugBlockerLogInterval);
                if (LevelRuntime.Active != null)
                {
                    var lr = LevelRuntime.Active;
                    var b = lr.levelBoundsXZ;
                    float step = Mathf.Max(cellSize, forwardProbeDistance);
                    Vector3 target = transform.position + dirWorld.normalized * step;
                    string atlasName = (lr.levelAtlas != null) ? lr.levelAtlas.name : "<null>";
                    int atlasW = (lr.levelAtlas != null) ? lr.levelAtlas.width : -1;
                    int atlasH = (lr.levelAtlas != null) ? lr.levelAtlas.height : -1;
                    Debug.Log($"GridMotor: BLOCKED (OOB) pos={transform.position} target={target} boundsMin={b.min} boundsMax={b.max} atlas={atlasName}({atlasW}x{atlasH}) floorMask={lr.floorLayers.value} wallMask={lr.wallLayers.value}", this);
                }
                else
                {
                    Debug.Log($"GridMotor: BLOCKED (OOB) pos={transform.position} (no LevelRuntime.Active)", this);
                }
            }
            return true;
        }

        float radius, height;
        GetCapsuleDims(out radius, out height);

        Vector3 center = GetCapsuleCenterWorld();
        float half = Mathf.Max(0f, (height * 0.5f) - radius);

        Vector3 p1 = center + Vector3.up * half;
        Vector3 p2 = center - Vector3.up * half;

        float dist = Mathf.Max(0f, forwardProbeDistance);
        float probeRadius = Mathf.Max(0.001f, radius * cornerRadiusScale - skin);

        RaycastHit[] hits = Physics.CapsuleCastAll(p1, p2, probeRadius, dirWorld.normalized, dist, solidMask, QueryTriggerInteraction.Ignore);
        RaycastHit hitInfo = default;
        bool hit = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsEntityCollider(hits[i].collider))
                continue;
            hitInfo = hits[i];
            hit = true;
            break;
        }
        if (hit && debugBlockers && Time.unscaledTime >= _nextBlockerLogTime)
        {
            _nextBlockerLogTime = Time.unscaledTime + Mathf.Max(0.05f, debugBlockerLogInterval);
            var go = hitInfo.collider != null ? hitInfo.collider.gameObject : null;
            string hitName = go != null ? go.name : "<null>";
            int hitLayer = go != null ? go.layer : -1;
            Debug.Log($"GridMotor: BLOCKED (HIT) dir={dir} hit={hitName} layer={hitLayer} dist={hitInfo.distance} mask={solidMask.value}", this);
        }
        return hit;
    }

    Vector3 GetCapsuleCenterWorld()
    {
        // IMPORTANT: CharacterController's collision capsule is centered at transform.position + center.
        // Using transform.position directly can cause false overlaps (e.g. with floor/walls) and freeze motion.
        if (characterController != null)
        {
            // center is in local-space; TransformPoint accounts for local transforms.
            return transform.TransformPoint(characterController.center);
        }
        return transform.position;
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

    bool IsOutOfBounds(Vector3 dirWorld)
    {
        if (LevelRuntime.Active == null || LevelRuntime.Active.levelBoundsXZ.size == Vector3.zero)
            return false;

        float step = Mathf.Max(cellSize, forwardProbeDistance);
        Vector3 target = transform.position + dirWorld.normalized * step;
        Bounds b = LevelRuntime.Active.levelBoundsXZ;
        return target.x < b.min.x || target.x > b.max.x || target.z < b.min.z || target.z > b.max.z;
    }

    Vector3 ClampDisplacement(Vector3 displacement)
    {
        if (displacement.sqrMagnitude < 0.000001f) return displacement;

        float radius, height;
        GetCapsuleDims(out radius, out height);

        Vector3 center = GetCapsuleCenterWorld();
        float half = Mathf.Max(0f, (height * 0.5f) - radius);
        Vector3 p1 = center + Vector3.up * half;
        Vector3 p2 = center - Vector3.up * half;

        float dist = displacement.magnitude;
        Vector3 dir = displacement / dist;

        float probeRadius = Mathf.Max(0.001f, radius * cornerRadiusScale - skin);
        RaycastHit[] hits = Physics.CapsuleCastAll(p1, p2, probeRadius, dir, dist + skin, solidMask, QueryTriggerInteraction.Ignore);
        RaycastHit hit = default;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsEntityCollider(hits[i].collider))
                continue;
            hit = hits[i];
            found = true;
            break;
        }
        if (found)
        {
            // Clamp to just before contact and zero velocity along blocked axis.
            float allowed = Mathf.Max(0f, hit.distance - skin);
            // If basically touching, cancel motion and clear velocity into the wall to avoid jitter.
            if (allowed <= 0.0001f)
            {
                _velocity -= dir * Vector3.Dot(_velocity, dir);
                return Vector3.zero;
            }

            _velocity -= dir * Vector3.Dot(_velocity, dir);
            return dir * allowed;
        }

        return displacement;
    }

    bool IsInsideWall()
    {
        float radius, height;
        GetCapsuleDims(out radius, out height);
        Vector3 center = GetCapsuleCenterWorld();
        float half = Mathf.Max(0f, (height * 0.5f) - radius);
        Vector3 p1 = center + Vector3.up * half;
        Vector3 p2 = center - Vector3.up * half;
        float probeRadius = Mathf.Max(0.001f, radius * cornerRadiusScale - skin * 0.5f);
        // Use OverlapCapsule so we can ignore entity colliders when desired.
        var hits = Physics.OverlapCapsule(p1, p2, probeRadius, solidMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsEntityCollider(hits[i])) continue;
            return true;
        }
        return false;
    }

    bool TryResolvePenetration()
    {
        float radius, height;
        GetCapsuleDims(out radius, out height);
        Vector3 center = GetCapsuleCenterWorld();
        float probeRadius = Mathf.Max(0.001f, radius * cornerRadiusScale - skin * 0.5f);

        var overlaps = Physics.OverlapSphere(center, probeRadius, solidMask, QueryTriggerInteraction.Ignore);
        if (overlaps == null || overlaps.Length == 0)
            return false;
        Vector3 totalPush = Vector3.zero;
        foreach (var col in overlaps)
        {
            if (col == null) continue;
            if (IsEntityCollider(col))
                continue;
            Vector3 closest = col.ClosestPoint(center);
            Vector3 diff = center - closest;
            float dist = diff.magnitude;
            float penetration = probeRadius - dist;
            if (penetration > 0.0001f)
            {
                if (dist > 0.0001f)
                    totalPush += diff.normalized * penetration;
                else
                    totalPush += Vector3.up * penetration; // fallback direction
            }
        }

        if (totalPush.sqrMagnitude < 1e-8f)
            return false;

        // Apply a conservative push to escape geometry.
        Vector3 push = totalPush.normalized * Mathf.Min(totalPush.magnitude, 0.5f);
        if (characterController != null && characterController.enabled)
        {
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.position += push;
            characterController.enabled = wasEnabled;
        }
        else
        {
            transform.position += push;
        }

        return true;
    }

    Vector2Int CurrentCell()
    {
        Vector3 origin = EffectiveOrigin();
        float tX = (transform.position.x - origin.x) / Mathf.Max(0.0001f, cellSize);
        float tZ = (transform.position.z - origin.z) / Mathf.Max(0.0001f, cellSize);
        return new Vector2Int(Mathf.RoundToInt(tX), Mathf.RoundToInt(tZ));
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

    void SnapToIntersectionForTurn(Vector2Int turningTo)
    {
        Vector3 pos = transform.position;
        Vector3 origin = EffectiveOrigin();

        float targetX = SnapToLane(pos.x, origin.x, cellSize);
        float targetZ = SnapToLane(pos.z, origin.z, cellSize);

        // If turning to X, align Z; if turning to Z, align X; otherwise align both.
        if (turningTo.x != 0)
            pos.z = targetZ;
        else if (turningTo.y != 0)
            pos.x = targetX;
        else
        {
            pos.x = targetX;
            pos.z = targetZ;
        }

        if (characterController != null && characterController.enabled)
        {
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            // Also snap Y to grid origin to ensure entity sits on the ground level.
            float radius, height;
            GetCapsuleDims(out radius, out height);
            float centerLocalY = characterController.center.y;
            pos.y = origin.y - centerLocalY + (height * 0.5f);
            transform.position = pos;
            characterController.enabled = wasEnabled;
        }
        else
        {
            pos.y = origin.y;
            transform.position = pos;
        }
    }

    static Vector2Int ClampDir(Vector2Int dir)
    {
        // Clamp to cardinal
        if (dir.x != 0) return new Vector2Int(dir.x > 0 ? 1 : -1, 0);
        if (dir.y != 0) return new Vector2Int(0, dir.y > 0 ? 1 : -1); // allow callers passing Vector2Int(x,y)
        return Vector2Int.zero;
    }
}
