using UnityEngine;
using Pathfinding;

public enum EnemyBrainType
{
    Sniffer,
    Curious,
    Assault,
    Afraid
}

/// <summary>
/// Centralised AI controller that drives GridMotor movement via the PathFinding helper and A*.
/// Supports multiple brain personalities and automatically rotates player targets every N seconds.
/// </summary>
[RequireComponent(typeof(EnemyEntity))]
[RequireComponent(typeof(GridMotor))]
[RequireComponent(typeof(PathFinding))]
public class EnemyBrainController : MonoBehaviour
{
    [Header("Brain")]
    public EnemyBrainType brainType = EnemyBrainType.Sniffer;
    [Tooltip("Seconds between repath requests while pursuing a destination.")]
    public float repathInterval = 1.2f;
    [Tooltip("Force the controller to pick a different player target every N seconds (if available).")]
    public float targetShuffleInterval = 30f;
    [Tooltip("Minimum distance delta before forcing an immediate path refresh.")]
    public float destinationUpdateThreshold = 0.75f;

    [Header("Sniffer")]
    public float snifferChaseDistance = 3.5f;

    [Header("Curious")]
    public float curiousWanderDuration = 4f;
    public float curiousInterceptDuration = 3f;
    public float curiousWanderRadius = 6f;
    public float curiousLeadTime = 1.25f;

    [Header("Assault")]
    public float assaultLeadTime = 2f;
    public float assaultHoldSeconds = 1.75f;
    public float assaultTriggerDistance = 3f;
    public float assaultResetDistance = 1.25f;

    [Header("Afraid")]
    public float afraidDesiredRadius = 5f;
    public float afraidInnerRadius = 2.5f;
    public float afraidPanicChaseDistance = 1.5f;
    public float afraidFleeDistance = 4f;

    [Header("Door Overrides")]
    [Tooltip("Seconds without getting closer to the target before arming a door/portal override.")]
    public float doorOverrideTimeout = 6f;
    [Tooltip("Required distance improvement toward the target to count as progress (meters).")]
    public float doorOverrideProgressSlack = 0.75f;
    [Tooltip("Number of doors or portals the AI may bypass once overrides are armed (usually one).")]
    [Range(0, 2)]
    public int doorOverrideAllowance = 1;
    [Tooltip("Lifetime in seconds for a granted override ticket before it expires.")]
    public float doorOverrideTicketLifetime = 5f;

    EnemyEntity _enemy;
    GridMotor _motor;
    PathFinding _pathFollower;

    PlayerTracker _tracker;
    PlayerEntity _currentTarget;
    float _nextTargetShuffle;

    Vector3 _desiredDestination;
    bool _hasDestination;
    Vector3 _lastIssuedDestination;
    bool _hasIssuedDestination;
    float _nextPathTime;
    bool _pathPending;
    float _bestDistanceToTarget = float.MaxValue;
    float _lastProgressTime;
    bool _doorOverrideArmed;
    int _lastTargetId = -1;

    enum CuriousMode { Wander, Predict }
    CuriousMode _curiousMode = CuriousMode.Wander;
    float _curiousModeUntil;
    Vector3 _curiousGoal;

    enum AssaultState { Moving, Holding, Striking }
    AssaultState _assaultState = AssaultState.Moving;
    float _assaultHoldTimer;
    Vector3 _assaultAmbushPoint;

    void Awake()
    {
        _enemy = GetComponent<EnemyEntity>();
        _motor = GetComponent<GridMotor>();
        _pathFollower = GetComponent<PathFinding>();
        _lastIssuedDestination = Vector3.zero;
        ResetDoorOverrideTracker();
    }

    void Start()
    {
        _tracker = PlayerTracker.EnsureInstance();
    }

    void Update()
    {
        if (_enemy == null || _pathFollower == null)
            return;

        if (_tracker == null)
            _tracker = PlayerTracker.EnsureInstance();

        if (_tracker == null || !_tracker.HasPlayers)
        {
            _pathFollower.ClearPath();
            ResetDoorOverrideTracker();
            return;
        }

        UpdateDoorOverrideTracker();

        UpdateTargetSelection();
        if (_currentTarget == null)
        {
            _pathFollower.ClearPath();
            ResetDoorOverrideTracker();
            return;
        }

        float dt = Time.deltaTime;
        switch (brainType)
        {
            case EnemyBrainType.Sniffer:
                UpdateSniffer();
                break;
            case EnemyBrainType.Curious:
                UpdateCurious(dt);
                break;
            case EnemyBrainType.Assault:
                UpdateAssault(dt);
                break;
            case EnemyBrainType.Afraid:
                UpdateAfraid();
                break;
        }

        TickPathing();
    }

    void UpdateTargetSelection()
    {
        if (_currentTarget != null && (!_currentTarget.isActiveAndEnabled || _currentTarget.isDead))
            _currentTarget = null;

        if (_currentTarget == null)
        {
            _currentTarget = _tracker.GetRandomPlayer();
            _nextTargetShuffle = Time.time + targetShuffleInterval;
            return;
        }

        if (Time.time >= _nextTargetShuffle)
        {
            var next = _tracker.GetRandomPlayer(_currentTarget);
            if (next != null)
                _currentTarget = next;
            _nextTargetShuffle = Time.time + targetShuffleInterval;
        }
    }

    void UpdateSniffer()
    {
        Vector3 targetPos = _currentTarget.transform.position;
        float dist = Vector3.Distance(transform.position, targetPos);

        Vector3 goal = targetPos;
        if (dist > snifferChaseDistance)
        {
            var crumbs = _tracker.GetBreadcrumbs(_currentTarget);
            if (crumbs.Count > 0)
                goal = crumbs[0];
        }

        ScheduleDestination(goal, dist > snifferChaseDistance);
    }

    void UpdateCurious(float dt)
    {
        if (Time.time >= _curiousModeUntil || _curiousGoal == Vector3.zero)
            SwitchCuriousMode();

        if (Vector3.Distance(transform.position, _curiousGoal) <= Mathf.Max(0.6f, destinationUpdateThreshold))
            SwitchCuriousMode();

        ScheduleDestination(_curiousGoal);
    }

    void SwitchCuriousMode()
    {
        _curiousMode = _curiousMode == CuriousMode.Wander ? CuriousMode.Predict : CuriousMode.Wander;
        float duration = _curiousMode == CuriousMode.Wander ? curiousWanderDuration : curiousInterceptDuration;
        _curiousModeUntil = Time.time + duration;

        if (_curiousMode == CuriousMode.Wander)
        {
            _curiousGoal = SampleReachablePoint(_currentTarget.transform.position, curiousWanderRadius);
        }
        else
        {
            _curiousGoal = PredictPlayerPosition(_currentTarget, curiousLeadTime);
        }
    }

    void UpdateAssault(float dt)
    {
        switch (_assaultState)
        {
            case AssaultState.Moving:
                if (_assaultAmbushPoint == Vector3.zero || Vector3.Distance(_assaultAmbushPoint, _currentTarget.transform.position) < 0.5f)
                    PickAmbushPoint();

                ScheduleDestination(_assaultAmbushPoint);

                if (Vector3.Distance(transform.position, _assaultAmbushPoint) <= Mathf.Max(0.5f, destinationUpdateThreshold))
                {
                    _assaultState = AssaultState.Holding;
                    _assaultHoldTimer = assaultHoldSeconds;
                }
                break;

            case AssaultState.Holding:
                _assaultHoldTimer -= dt;
                float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);
                if (dist <= assaultTriggerDistance || _assaultHoldTimer <= 0f)
                {
                    _assaultState = AssaultState.Striking;
                }
                break;

            case AssaultState.Striking:
                Vector3 strikeGoal = _currentTarget.transform.position;
                ScheduleDestination(strikeGoal, true);

                if (Vector3.Distance(transform.position, strikeGoal) <= assaultResetDistance)
                {
                    _assaultState = AssaultState.Moving;
                    PickAmbushPoint();
                }
                break;
        }
    }

    void UpdateAfraid()
    {
        Vector3 playerPos = _currentTarget.transform.position;
        float dist = Vector3.Distance(transform.position, playerPos);

        if (dist <= afraidPanicChaseDistance)
        {
            ScheduleDestination(playerPos, true);
            return;
        }

        Vector3 away = (transform.position - playerPos);
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
            away = Random.insideUnitSphere;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
            away = Vector3.forward;
        away.Normalize();

        float desired = dist < afraidInnerRadius ? afraidFleeDistance : afraidDesiredRadius;
        Vector3 goal = playerPos + away * desired;
        ScheduleDestination(goal);
    }

    void PickAmbushPoint()
    {
        Vector3 predicted = PredictPlayerPosition(_currentTarget, assaultLeadTime);
        Vector3 velocity = _tracker.GetVelocity(_currentTarget);
        Vector3 forward = velocity.sqrMagnitude > 0.01f ? velocity.normalized : _currentTarget.transform.forward;
        Vector3 lateral = new Vector3(-forward.z, 0f, forward.x) * Random.Range(-2f, 2f);
        _assaultAmbushPoint = SampleReachablePoint(predicted + lateral, 2.5f);
    }

    Vector3 PredictPlayerPosition(PlayerEntity player, float leadTime)
    {
        Vector3 pos = player.transform.position;
        Vector3 vel = _tracker.GetVelocity(player);
        if (vel.sqrMagnitude < 0.01f)
            return pos;
        return ClampToLevel(pos + vel * Mathf.Max(0.1f, leadTime));
    }

    Vector3 SampleReachablePoint(Vector3 near, float radius)
    {
        var astar = AstarPath.active;
        Vector3 candidate = ClampToLevel(near);
        if (astar == null)
            return candidate;

        const int attempts = 6;
        for (int i = 0; i < attempts; i++)
        {
            Vector2 circle = Random.insideUnitCircle * radius;
            Vector3 probe = ClampToLevel(new Vector3(near.x + circle.x, near.y, near.z + circle.y));
            var nn = astar.GetNearest(probe, NNConstraint.None);
            if (nn.node != null && nn.node.Walkable)
                return (Vector3)nn.position;
        }

        var fallback = astar.GetNearest(candidate, NNConstraint.None);
        if (fallback.node != null && fallback.node.Walkable)
            return (Vector3)fallback.position;

        return candidate;
    }

    Vector3 ClampToLevel(Vector3 pos)
    {
        var runtime = LevelRuntime.Active;
        if (runtime == null)
            return pos;

        var bounds = runtime.levelBoundsXZ;
        pos.x = Mathf.Clamp(pos.x, bounds.min.x, bounds.max.x);
        pos.z = Mathf.Clamp(pos.z, bounds.min.z, bounds.max.z);
        pos.y = bounds.center.y;
        return pos;
    }

    void ScheduleDestination(Vector3 worldPos, bool forceImmediate = false)
    {
        _desiredDestination = worldPos;
        _hasDestination = true;

        if (forceImmediate || !_hasIssuedDestination || Vector3.Distance(_lastIssuedDestination, worldPos) >= destinationUpdateThreshold)
        {
            _nextPathTime = Time.time;
            _lastIssuedDestination = worldPos;
            _hasIssuedDestination = true;
        }
    }

    void TickPathing()
    {
        if (!_hasDestination || _pathPending)
            return;
        if (Time.time < _nextPathTime)
            return;

        _pathPending = true;
        Vector3 start = transform.position;
        Vector3 goal = _desiredDestination;
        int overrideAllowance = _doorOverrideArmed ? Mathf.Max(0, doorOverrideAllowance) : 0;
        float ticketLifetime = Mathf.Max(0.1f, doorOverrideTicketLifetime);
        DoorDelegate.FindPathForEntity(start, goal, _enemy, OnPathReady, overrideAllowance, ticketLifetime);
        _nextPathTime = Time.time + repathInterval;
    }
    void UpdateDoorOverrideTracker()
    {
        if (_currentTarget == null)
        {
            ResetDoorOverrideTracker();
            return;
        }

        int targetId = _currentTarget.GetInstanceID();
        if (targetId != _lastTargetId)
        {
            _lastTargetId = targetId;
            _bestDistanceToTarget = float.MaxValue;
            _lastProgressTime = Time.time;
            _doorOverrideArmed = false;
        }

        float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);
        float slack = Mathf.Max(0.01f, doorOverrideProgressSlack);

        if (dist + slack < _bestDistanceToTarget)
        {
            _bestDistanceToTarget = dist;
            _lastProgressTime = Time.time;
            _doorOverrideArmed = false;
        }
        else if (_bestDistanceToTarget.Equals(float.MaxValue))
        {
            _bestDistanceToTarget = dist;
        }

        if (!_doorOverrideArmed && doorOverrideAllowance > 0 && doorOverrideTimeout > 0f)
        {
            if (Time.time - _lastProgressTime >= doorOverrideTimeout)
                _doorOverrideArmed = true;
        }
    }

    void ResetDoorOverrideTracker()
    {
        _bestDistanceToTarget = float.MaxValue;
        _lastProgressTime = Time.time;
        _doorOverrideArmed = false;
        _lastTargetId = _currentTarget != null ? _currentTarget.GetInstanceID() : -1;
    }

    void OnPathReady(Path path, bool overridesUsed)
    {
        _pathPending = false;
        if (path == null || path.error || path.vectorPath == null || path.vectorPath.Count == 0)
        {
            Debug.LogWarning($"{name}: Path request failed (overrideUsed={overridesUsed}).");
            _pathFollower.ClearPath();
            return;
        }

        if (overridesUsed)
            _doorOverrideArmed = false;
        _pathFollower.SetPathFromWorldPoints(path.vectorPath);
        if (_pathFollower != null && _pathFollower.verboseDebug)
        {
            Debug.Log($"{name}: OnPathReady -> path received ({path.vectorPath.Count} points).", this);
        }
        ResetDoorOverrideTracker();
    }
}
