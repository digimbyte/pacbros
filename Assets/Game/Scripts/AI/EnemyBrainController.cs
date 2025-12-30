using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding;

/// <summary>
/// Modern Pac-Man-style enemy brain:
/// - Primary navigation: A* (via DoorDelegate) for long-distance.
/// - Secondary navigation: local grid steering at intersections (corner-aware), to prevent wall-hug loops and explore.
/// - Per-entity heat: remembers last N tiles; if a tile is touched > threshold, its heat ramps hard (avoid loops).
/// - Panic meter: ramps in tight spaces / stuck / looping; when full can force ghost + door/portal override.
/// - IMPORTANT: Grid math matches GridMotor.cs:
///     * World->Cell uses FLOOR on (world - gridOrigin) / cellSize
///     * Cell center is gridOrigin + (cell + 0.5) * cellSize
///
/// NOTE (your request):
/// - GhostRole enum + field are PURGED.
/// - Classic 4-corner scatter behavior is preserved by *mapping BrainType -> classic corner slot* (inspiration),
///   while BrainType remains the real behavior driver.
/// </summary>
public enum EnemyBrainType
{
    Sniffer,   // breadcrumb tracker
    Curious,   // wander/predict hybrid
    Assault,   // aggressive interceptor
    Afraid     // keep-away / flee
}

[RequireComponent(typeof(EnemyEntity))]
[RequireComponent(typeof(GridMotor))]
[RequireComponent(typeof(PathFinding))]
public class EnemyBrainController : MonoBehaviour
{
    public enum GhostMode
    {
        Scatter,
        Chase
    }

    [Header("Mode Timing")]
    public GhostMode mode = GhostMode.Scatter;
    public float scatterSeconds = 7f;
    public float chaseSeconds = 20f;
    float _modeUntil;

    [Header("Vision")]
    public float sightRadius = 7.5f;
    public LayerMask wallMask; // walls that block LOS
    float _lastSeenPlayerTime = -999f;

    // ----------------------------
    // Brain / Pathing
    // ----------------------------
    [Header("Brain")]
    public EnemyBrainType brainType = EnemyBrainType.Sniffer;

    [Tooltip("Seconds between repath requests while pursuing a destination.")]
    public float repathInterval = 0.30f;

    [Tooltip(
        "Repath is also triggered after an axis change, but only if we've moved at least this many cells since the last repath."
    )]
    [Range(0, 6)]
    public int minCellsBetweenAxisRepath = 2;

    [Tooltip(
        "Force the controller to pick a different player target every N seconds (if available)."
    )]
    public float targetShuffleInterval = 30f;

    [Tooltip("Minimum distance delta before forcing an immediate path refresh.")]
    public float destinationUpdateThreshold = 0.75f;

    [Tooltip(
        "How often we run a cheap A* reachability health-check to the player (0.5–1s is fine for 4 enemies)."
    )]
    [Range(0.2f, 2.0f)]
    public float aStarHealthCheckInterval = 0.75f;

    [Header("Intersection / Cornering")]
    [Tooltip(
        "How close (meters) to the center of a grid cell before we consider turning decisions."
    )]
    public float centerSnapEpsilon = 0.12f;

    [Tooltip(
        "When motor is stopped, allow steering even if not centered (prevents permanent stall)."
    )]
    public float stoppedSteerEpsilon = 0.60f;

    [Tooltip("Pre-turn window for buffering desired turns before hitting cell center.")]
    public float preTurnWindow = 0.35f;

    [Tooltip("Continuously buffer A* advice so the motor can turn as soon as it's legal.")]
    public bool alwaysBufferAStar = true;

    Vector2Int _bufferedDir = Vector2Int.zero;

    [Header("Decision Blend")]
    [Range(0f, 5f)]
    public float astarVote = 2.5f; // how strongly A* "suggests" a step

    [Range(0f, 5f)]
    public float marchVote = 1.5f; // how strongly marching suggests a step

    [Range(0f, 2f)]
    public float randomness = 0.25f; // tie breaker + variety

    [Header("Intersection Policy")]
    [Tooltip("Bonus score for making a turn at a junction (reduces corridor hovering).")]
    public float turnBonus = 0.75f;

    [Tooltip("Bonus score for going straight (usually smaller than turnBonus).")]
    public float straightBonus = 0.10f;

    // ----------------------------
    // Direction basis adapter (fixes X/Z swap or inversion between brain and motor)
    // ----------------------------
    [Header("Coordinate Adapter (ONLY if motor basis differs)")]
    [Tooltip(
        "If motor interprets Vector2Int.up as +X (or your world basis is rotated), enable swap."
    )]
    public bool swapXZForMotor = false;

    [Tooltip("Invert X direction when talking to the motor.")]
    public bool invertXForMotor = false;

    [Tooltip("Invert Z direction when talking to the motor.")]
    public bool invertZForMotor = false;

    // ----------------------------
    // Heat (Shared + Per-Entity)
    // ----------------------------
    [Header("Shared Heat (crowd avoidance)")]
    public bool useSharedHeatMap = true;
    public float sharedHeatRadius = 2.5f;

    [Range(0f, 1f)]
    public float sharedHeatAvoidanceWeight = 0.15f;

    static readonly Dictionary<Vector2Int, float> _sharedHeatMap = new Dictionary<Vector2Int, float>(
        1024
    );
    static float _sharedHeatMapNextUpdate;

    [Header("Per-Entity Heat (loop avoidance)")]
    [Tooltip("Number of recent tiles remembered.")]
    public int perEntityHistorySize = 1000;

    [Tooltip("If a tile is touched more than this, it becomes heavily avoided.")]
    public int perEntityTouchThreshold = 10;

    [Tooltip("Heat added per touch above threshold (scaled hard).")]
    public float perEntityHeatPerExtraTouch = 10f;

    [Tooltip("Passive heat on spawn tiles (encourages leaving spawn area).")]
    public float spawnTilePassiveHeat = 50f;

    [Tooltip("How much per-entity heat influences direction choice.")]
    [Range(0f, 2f)]
    public float perEntityHeatWeight = 0.65f;

    [Header("Enemy Spawn Tile Detection (optional)")]
    [Tooltip(
        "If you have spawn tiles as colliders on a layer, set it here. Otherwise leave empty."
    )]
    public LayerMask enemySpawnMask;

    [Tooltip("If you tag spawn tile objects, set the tag here (optional).")]
    public string enemySpawnTag = "EnemySpawn";

    [Header("Portal Avoidance")]
    public LayerMask portalMask;
    public string portalTag = "Portal";
    public float portalAvoidHeat = 250f;
    public float portalAllowAfterSeenSeconds = 3.0f;
    public float portalDesperationAfterNoSightSeconds = 6.0f;
    float _portalAllowedUntil = -999f;

    // ----------------------------
    // Debug / Diagnostics
    // ----------------------------
    [Header("Debug")]
    [Tooltip("Enable verbose debug logging for this enemy.")]
    public bool verboseDebug = false;

    [Tooltip("Maximum number of debug log entries to keep.")]
    public int maxDebugEntries = 50;

    [Tooltip("Enable path visualization debugging.")]
    public bool pathDebug = false;

    [Tooltip("Duration to show debug path lines.")]
    public float pathDebugDuration = 1.0f;

    // Runtime debug state
    private Queue<string> _debugLog = new();
    private bool _hasAStarPathToPlayer = false;
    private float _lastAStarCheckTime = 0f;
    private List<Vector3> _currentPathPoints = new();
    private List<Vector3> _playerPathPoints = new();

    // ----------------------------
    // Stability / Stuck / Panic
    // ----------------------------
    [Header("AI Stability")]
    public float decisionCooldown = 0.35f;
    public float stuckTimeout = 2.2f;

    [Header("Panic")]
    [Tooltip("Panic meter ranges 0..1000")]
    public float panicMax = 1000f;

    [Tooltip("Base panic gain per second.")]
    public float panicBaseGainPerSecond = 6f;

    [Tooltip("Extra panic gain per second when in small floor space.")]
    public float panicTightSpaceGainPerSecond = 40f;

    [Tooltip("How often we re-scan local floor space to scale panic gain.")]
    public float floorSpaceScanInterval = 0.60f;

    [Tooltip("Max BFS nodes for floor space scan (keep this small).")]
    public int floorSpaceScanMaxNodes = 80;

    [Tooltip("If reachable floor-space is <= this, treat it as cramped.")]
    public int floorSpaceCrampedThreshold = 26;

    [Tooltip("Additional panic gained when we detect repeated tile hits (looping).")]
    public float panicLoopBonus = 30f;

    [Tooltip(
        "When panic is full, enable ghost mode and force door/portal overrides to reach player."
    )]
    public bool enableGhostOnPanic = true;

    [Tooltip(
        "Once we pass the first door/portal during panic, disable ghost mode and re-evaluate (prevents beeline)."
    )]
    public bool disableGhostAfterFirstDoorOrPortal = true;

    [Tooltip("Cooldown after a panic break (door/portal pass) before panic can re-trigger.")]
    public float postPanicCooldown = 2.0f;

    // ----------------------------
    // Brain-specific tunables
    // ----------------------------
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

    // ----------------------------
    // Door/Portal Override tickets (A*)
    // ----------------------------
    [Header("Door/Portal Overrides")]
    [Tooltip("Seconds without getting closer to the target before arming a door/portal override.")]
    public float doorOverrideTimeout = 6f;

    [Tooltip("Required distance improvement toward the target to count as progress (meters).")]
    public float doorOverrideProgressSlack = 0.75f;

    [Tooltip("Number of doors/portals the AI may bypass once overrides are armed.")]
    [Range(0, 2)]
    public int doorOverrideAllowance = 1;

    [Tooltip("Lifetime in seconds for a granted override ticket before it expires.")]
    public float doorOverrideTicketLifetime = 5f;

    // ----------------------------
    // Components / State
    // ----------------------------
    EnemyEntity _enemy;
    GridMotor _motor;
    PathFinding _pathFollower;

    PlayerTracker _tracker;
    PlayerEntity _currentTarget;
    float _nextTargetShuffle;

    Vector3 _desiredDestination;
    bool _hasDestination;

    float _nextRepathTime;
    bool _pathPending;
    float _pathPendingSince;

    Vector3 _lastIssuedGoal;
    bool _hasIssuedGoal;

    float _bestDistanceToTarget = float.MaxValue;
    float _lastProgressTime;
    bool _doorOverrideArmed;
    int _lastTargetId = -1;

    // Marching state (brain-basis)
    Vector2Int _marchFacing = Vector2Int.right;

    // Curious
    enum CuriousMode
    {
        Wander,
        Predict
    }

    CuriousMode _curiousMode = CuriousMode.Wander;
    float _curiousModeUntil;
    Vector3 _curiousGoal;

    // Assault
    enum AssaultState
    {
        Moving,
        Holding,
        Striking
    }

    AssaultState _assaultState = AssaultState.Moving;
    float _assaultHoldTimer;
    Vector3 _assaultAmbushPoint;
    bool _isCamping;

    // Afraid
    Vector3 _afraidFleeDirection;
    Vector3 _afraidLastPlayerPos;
    float _lastAfraidDecision;

    // Stuck detection
    Vector3 _lastPos;
    float _lastMovedAt;
    bool _isStuck;

    // Per-entity heat tracking (tile repeats)
    readonly Dictionary<Vector2Int, int> _tileTouchCounts = new Dictionary<Vector2Int, int>(2048);
    readonly Dictionary<Vector2Int, float> _tileHeat = new Dictionary<Vector2Int, float>(2048);
    readonly Queue<Vector2Int> _recentTiles = new Queue<Vector2Int>(1024);
    Vector2Int _lastRecordedTile = new Vector2Int(int.MinValue, int.MinValue);

    // Panic
    float _panic;
    float _nextFloorScanAt;
    int _lastFloorSpaceCount = 999;
    float _panicSuppressedUntil;
    bool _inPanic;
    bool _pendingGhostDisableAfterDoor;
    bool _needsPostDoorRecheck;

    // Direction memory (reduces 180 jitter)
    Vector2Int _lastChosenDir; // brain-basis
    float _lastDirChangeAt;
    float _lastDecisionAt;
    Vector2Int _lastDecisionCell;

    // Repath policy (axis change + min cells)
    bool _wantsAxisRepath;
    int _cellsSinceLastRepath;
    Vector2Int _lastRepathCell;

    void Awake()
    {
        _enemy = GetComponent<EnemyEntity>();
        _motor = GetComponent<GridMotor>();
        _pathFollower = GetComponent<PathFinding>();

        _lastPos = transform.position;
        _lastMovedAt = Time.time;
        _lastChosenDir = Vector2Int.zero;
        _lastDirChangeAt = 0f;
        _lastDecisionAt = 0f;

        _wantsAxisRepath = false;
        _cellsSinceLastRepath = 999;
        _lastRepathCell = new Vector2Int(int.MinValue, int.MinValue);

        if (_motor != null)
            _motor.OnTeleport += OnTeleported;

        if (_enemy != null)
            _enemy.onRespawn.AddListener(OnEnemyRespawn);

        ResetDoorOverrideTracker();
    }

    void Start()
    {
        _tracker = PlayerTracker.EnsureInstance();
        _nextFloorScanAt = Time.time + Random.Range(0f, floorSpaceScanInterval);

        _modeUntil = Time.time + scatterSeconds;
        mode = GhostMode.Scatter;

        // Prevent "immediately desperate" portal allowance on spawn.
        _lastSeenPlayerTime = Time.time;
    }

    void OnDestroy()
    {
        if (_motor != null)
            _motor.OnTeleport -= OnTeleported;

        if (_enemy != null)
            _enemy.onRespawn.RemoveListener(OnEnemyRespawn);
    }

    void Update()
    {
        if (_enemy == null || _motor == null || _pathFollower == null)
        {
            LogDebug("Update halted: missing required components");
            return;
        }

        if (_tracker == null)
            _tracker = PlayerTracker.EnsureInstance();

        if (_tracker == null || !_tracker.HasPlayers)
        {
            _pathFollower.ClearPath();
            _hasDestination = false;
            _pathPending = false;
            _panic = 0f;
            ResetDoorOverrideTracker();
            return;
        }

        UpdateTargetSelection();

        CheckAStarReachabilityToPlayer();

        UpdateStuckDetection();
        UpdateSharedHeatMapIfEnabled();
        UpdatePerEntityHeatTracking();
        UpdatePanicMeter();
        UpdateDoorOverrideTracker();
        UpdatePortalAllowanceFromBreadcrumbs();

        UpdateVisionAndMode();

        if (_currentTarget == null)
        {
            _pathFollower.ClearPath();
            _hasDestination = false;
            ResetDoorOverrideTracker();
            return;
        }

        // After a panic door/portal pass we immediately recheck environment and drop any beeline.
        if (_needsPostDoorRecheck)
        {
            _needsPostDoorRecheck = false;
            _inPanic = false;
            _panic = 0f;
            _panicSuppressedUntil = Time.time + postPanicCooldown;

            _hasDestination = false;
            _pathFollower.ClearPath();
            _pathPending = false;
            _doorOverrideArmed = false;

            ForceExploreGoalNow();
        }

        float dt = Time.deltaTime;

        if (ShouldEnterPanicNow())
            EnterPanic();

        if (_inPanic)
        {
            ScheduleDestination(_currentTarget.transform.position, forceImmediate: true);
        }
        else
        {
            // Scatter overrides destination (corner), but NOT the steering/heat/policies.
            if (mode == GhostMode.Scatter)
            {
                ScheduleDestination(GetScatterCorner(), forceImmediate: false);
            }
            else
            {
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
            }
        }

        TickAStarRequests();
        TickBufferedDesiredDirection();
        TickMoveDecision();

        if (pathDebug)
        {
            DrawCurrentPath();
            DrawPlayerPath();
            DrawDestinationLine();
            DrawStatusIndicator();
        }
    }

    void TickBufferedDesiredDirection()
    {
        if (!alwaysBufferAStar)
            return;

        Vector2Int astar = GetAStarAdviceDir();
        if (astar == Vector2Int.zero)
            return;
        if (MotorIsBlockedBrain(astar))
            return;

        // Buffer it.
        _bufferedDir = astar;

        // If we're approaching an intersection where this turn becomes legal, push it now.
        Vector2Int curDir = MotorCurDirBrain();
        Vector2Int cell = WorldToGrid(transform.position);

        // If we are moving and A* wants a perpendicular turn, start feeding it shortly before center.
        if (curDir != Vector2Int.zero && IsPerpendicular(curDir, astar))
        {
            float d = DistanceToCellCenterAlongAxis(cell, curDir);
            if (d <= preTurnWindow)
                MotorSetDesiredBrain(astar);
        }
        else if (curDir == Vector2Int.zero)
        {
            // If stopped, just apply buffered immediately.
            MotorSetDesiredBrain(astar);
        }
    }

    // --------------------------------------------------------------------
    // Direction adapter helpers (brain <-> motor)
    // --------------------------------------------------------------------
    Vector2Int BrainToMotorDir(Vector2Int d)
    {
        // brain basis: (x, z) where Vector2Int.y represents Z
        int x = d.x;
        int z = d.y;

        if (swapXZForMotor)
        {
            int t = x;
            x = z;
            z = t;
        }
        if (invertXForMotor)
            x = -x;
        if (invertZForMotor)
            z = -z;

        return new Vector2Int(x, z);
    }

    static bool IsPerpendicular(Vector2Int a, Vector2Int b)
    {
        if (a == Vector2Int.zero || b == Vector2Int.zero)
            return false;
        return (a.x == 0 && b.x != 0) || (a.x != 0 && b.x == 0);
    }

    float DistanceToCellCenterAlongAxis(Vector2Int cell, Vector2Int moveDirBrain)
    {
        Vector3 c = GridToWorldCenter(cell);
        if (moveDirBrain.x != 0)
            return Mathf.Abs(transform.position.x - c.x);
        return Mathf.Abs(transform.position.z - c.z);
    }

    Vector2Int MotorToBrainDir(Vector2Int d)
    {
        // inverse mapping (swap is its own inverse; invert is its own inverse)
        int x = d.x;
        int z = d.y;

        if (invertXForMotor)
            x = -x;
        if (invertZForMotor)
            z = -z;

        if (swapXZForMotor)
        {
            int t = x;
            x = z;
            z = t;
        }

        return new Vector2Int(x, z);
    }

    Vector2Int MotorCurDirBrain()
    {
        return _motor != null ? MotorToBrainDir(_motor.GetCurrentDirection()) : Vector2Int.zero;
    }

    bool MotorIsBlockedBrain(Vector2Int brainDir)
    {
        if (_motor == null)
            return false;
        return _motor.IsDirectionBlocked(BrainToMotorDir(brainDir));
    }

    void MotorSetDesiredBrain(Vector2Int brainDir)
    {
        if (_motor == null)
            return;
        _motor.SetDesiredDirection(BrainToMotorDir(brainDir));
    }

    // --------------------------------------------------------------------
    // Target selection
    // --------------------------------------------------------------------
    void UpdateTargetSelection()
    {
        if (_currentTarget != null && (!_currentTarget.isActiveAndEnabled || _currentTarget.isDead))
            _currentTarget = null;

        if (_currentTarget == null)
        {
            _currentTarget = _tracker.GetRandomPlayer();
            _nextTargetShuffle = Time.time + targetShuffleInterval;
            _lastDecisionAt = Time.time;
            _lastTargetId = _currentTarget != null ? _currentTarget.GetInstanceID() : -1;
            return;
        }

        if (Time.time < _nextTargetShuffle)
            return;
        if (Time.time - _lastDecisionAt < decisionCooldown)
            return;

        var players = _tracker.Players;
        if (players == null || players.Count <= 1)
        {
            _nextTargetShuffle = Time.time + targetShuffleInterval;
            return;
        }

        float currentDist = Vector3.Distance(transform.position, _currentTarget.transform.position);
        bool shouldSwitch = false;

        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null || p == _currentTarget)
                continue;

            float otherDist = Vector3.Distance(transform.position, p.transform.position);
            if (otherDist < currentDist * 0.70f)
            {
                shouldSwitch = true;
                break;
            }
        }

        if (shouldSwitch)
        {
            var next = _tracker.GetRandomPlayer(_currentTarget);
            if (next != null)
            {
                _currentTarget = next;
                _lastDecisionAt = Time.time;
            }
        }

        _nextTargetShuffle = Time.time + targetShuffleInterval;
    }

    // --------------------------------------------------------------------
    // Brain goals
    // --------------------------------------------------------------------
    void UpdateSniffer()
    {
        Vector3 targetPos = _currentTarget.transform.position;
        float dist = Vector3.Distance(transform.position, targetPos);

        Vector3 goal = targetPos;
        var crumbs = _tracker.GetBreadcrumbs(_currentTarget);

        if (dist > snifferChaseDistance)
        {
            if (crumbs != null && crumbs.Count > 0)
                goal = crumbs[0];
        }

        ScheduleDestination(goal, forceImmediate: false);
    }

    void UpdateCurious(float dt)
    {
        if (Time.time >= _curiousModeUntil && Time.time - _lastDecisionAt >= decisionCooldown)
            SwitchCuriousMode();

        if (
            Vector3.Distance(transform.position, _curiousGoal)
            <= Mathf.Max(0.6f, destinationUpdateThreshold)
        )
        {
            if (Time.time - _lastDecisionAt >= decisionCooldown)
                SwitchCuriousMode();
        }

        ScheduleDestination(_curiousGoal, forceImmediate: false);
    }

    void SwitchCuriousMode()
    {
        _curiousMode =
            (_curiousMode == CuriousMode.Wander) ? CuriousMode.Predict : CuriousMode.Wander;
        float duration =
            (_curiousMode == CuriousMode.Wander) ? curiousWanderDuration : curiousInterceptDuration;
        _curiousModeUntil = Time.time + duration;
        _lastDecisionAt = Time.time;

        if (_curiousMode == CuriousMode.Wander)
            _curiousGoal = SampleReachablePoint(
                _currentTarget.transform.position,
                curiousWanderRadius
            );
        else
            _curiousGoal = PredictPlayerPosition(_currentTarget, curiousLeadTime);
    }

    void UpdateAssault(float dt)
    {
        switch (_assaultState)
        {
            case AssaultState.Moving:
            {
                _isCamping = false;

                if (
                    _assaultAmbushPoint == Vector3.zero
                    || Vector3.Distance(_assaultAmbushPoint, _currentTarget.transform.position) > 2f
                )
                {
                    if (Time.time - _lastDecisionAt >= decisionCooldown)
                    {
                        PickAmbushPoint();
                        _lastDecisionAt = Time.time;
                    }
                }

                if (_assaultAmbushPoint != Vector3.zero)
                    ScheduleDestination(_assaultAmbushPoint, forceImmediate: false);

                if (
                    _assaultAmbushPoint != Vector3.zero
                    && Vector3.Distance(transform.position, _assaultAmbushPoint)
                        <= Mathf.Max(0.5f, destinationUpdateThreshold)
                )
                {
                    if (Time.time - _lastDecisionAt >= decisionCooldown)
                    {
                        _assaultState = AssaultState.Holding;
                        _assaultHoldTimer = assaultHoldSeconds;
                        _lastDecisionAt = Time.time;
                    }
                }
                break;
            }

            case AssaultState.Holding:
            {
                _isCamping = true;
                _assaultHoldTimer -= dt;

                float dist = Vector3.Distance(
                    transform.position,
                    _currentTarget.transform.position
                );
                if (
                    (dist <= assaultTriggerDistance || _assaultHoldTimer <= 0f)
                    && Time.time - _lastDecisionAt >= decisionCooldown
                )
                {
                    _assaultState = AssaultState.Striking;
                    _lastDecisionAt = Time.time;
                }
                break;
            }

            case AssaultState.Striking:
            {
                _isCamping = false;
                Vector3 strikeGoal = _currentTarget.transform.position;
                ScheduleDestination(strikeGoal, forceImmediate: true);

                if (
                    Vector3.Distance(transform.position, strikeGoal) <= assaultResetDistance
                    && Time.time - _lastDecisionAt >= decisionCooldown
                )
                {
                    _assaultState = AssaultState.Moving;
                    _assaultAmbushPoint = Vector3.zero;
                    _lastDecisionAt = Time.time;
                }
                break;
            }
        }
    }

    void UpdateAfraid()
    {
        Vector3 playerPos = _currentTarget.transform.position;
        float dist = Vector3.Distance(transform.position, playerPos);

        if (dist <= afraidPanicChaseDistance)
        {
            ScheduleDestination(playerPos, forceImmediate: true);
            return;
        }

        if (
            _afraidFleeDirection == Vector3.zero
            || Time.time - _lastAfraidDecision > decisionCooldown
            || Vector3.Distance(_afraidLastPlayerPos, playerPos) > 1f
        )
        {
            Vector3 away = (transform.position - playerPos);
            away.y = 0f;

            if (away.sqrMagnitude < 0.001f)
                away = Random.insideUnitSphere;

            away.y = 0f;
            if (away.sqrMagnitude < 0.001f)
                away = Vector3.forward;

            away.Normalize();
            _afraidFleeDirection = away;
            _afraidLastPlayerPos = playerPos;
            _lastAfraidDecision = Time.time;
        }

        float desired = dist < afraidInnerRadius ? afraidFleeDistance : afraidDesiredRadius;
        Vector3 goal = playerPos + _afraidFleeDirection * desired;
        ScheduleDestination(goal, forceImmediate: false);
    }

    void PickAmbushPoint()
    {
        Vector3 predicted = PredictPlayerPosition(_currentTarget, assaultLeadTime);
        Vector3 velocity = _tracker.GetVelocity(_currentTarget);
        Vector3 forward =
            velocity.sqrMagnitude > 0.01f ? velocity.normalized : _currentTarget.transform.forward;
        Vector3 lateral = new Vector3(-forward.z, 0f, forward.x) * Random.Range(-2f, 2f);
        _assaultAmbushPoint = SampleReachablePoint(predicted + lateral, 2.5f);
    }

    // --------------------------------------------------------------------
    // Scheduling / Hysteresis
    // --------------------------------------------------------------------
    void UpdateVisionAndMode()
    {
        if (_currentTarget == null)
            return;

        // crude LOS: if close and line is not blocked, we "see" the player
        Vector3 a = transform.position + Vector3.up * 0.25f;
        Vector3 b = _currentTarget.transform.position + Vector3.up * 0.25f;
        float dist = Vector3.Distance(a, b);

        bool sees =
            dist <= sightRadius
            && !Physics.Linecast(a, b, wallMask, QueryTriggerInteraction.Ignore);
        if (sees)
            _lastSeenPlayerTime = Time.time;

        // mode switch timer
        if (Time.time >= _modeUntil)
        {
            if (mode == GhostMode.Scatter)
            {
                mode = GhostMode.Chase;
                _modeUntil = Time.time + chaseSeconds;
            }
            else
            {
                mode = GhostMode.Scatter;
                _modeUntil = Time.time + scatterSeconds;
            }
        }

        // if we saw player recently, force chase regardless of timer
        if (Time.time - _lastSeenPlayerTime <= 1.0f)
        {
            mode = GhostMode.Chase;
            _modeUntil = Mathf.Max(_modeUntil, Time.time + 2.0f);
        }
    }

    void ScheduleDestination(Vector3 worldPos, bool forceImmediate)
    {
        _desiredDestination = ClampToLevel(worldPos);
        _hasDestination = true;

        bool shouldIssue =
            forceImmediate
            || !_hasIssuedGoal
            || Vector3.Distance(_lastIssuedGoal, _desiredDestination) >= destinationUpdateThreshold
            || Time.time >= _nextRepathTime;

        if (shouldIssue)
        {
            _lastIssuedGoal = _desiredDestination;
            _hasIssuedGoal = true;
            _nextRepathTime = Time.time; // allow immediate request
        }
    }

    // --------------------------------------------------------------------
    // A* request tick (DoorDelegate integration)
    // --------------------------------------------------------------------
    void TickAStarRequests()
    {
        if (!_hasDestination)
            return;

        // Fail-fast if pending too long (keeps AI alive even if callback breaks).
        if (_pathPending && Time.time - _pathPendingSince > 2.0f)
        {
            _pathPending = false;
            _pathFollower.ClearPath();
        }

        if (_pathPending)
            return;

        // If no A* system is present, skip (local steering still works).
        if (AstarPath.active == null)
            return;

        bool axisRepathAllowed =
            _wantsAxisRepath && (_cellsSinceLastRepath >= Mathf.Max(0, minCellsBetweenAxisRepath));
        bool shouldRepath =
            Time.time >= _nextRepathTime || _isStuck || _inPanic || axisRepathAllowed;

        if (!shouldRepath)
            return;

        _pathPending = true;
        _pathPendingSince = Time.time;

        Vector3 start = transform.position;
        Vector3 goal = _desiredDestination;

        int overrideAllowance;
        float ticketLifetime = Mathf.Max(0.1f, doorOverrideTicketLifetime);

        if (_inPanic)
            overrideAllowance = Mathf.Max(1, doorOverrideAllowance);
        else
            overrideAllowance = _doorOverrideArmed ? Mathf.Max(0, doorOverrideAllowance) : 0;

        // consume axis-repath intent
        _wantsAxisRepath = false;
        _cellsSinceLastRepath = 0;
        _lastRepathCell = WorldToGrid(transform.position);

        DoorDelegate.FindPathForEntity(
            start,
            goal,
            _enemy,
            OnPathReady,
            overrideAllowance,
            ticketLifetime
        );
        _nextRepathTime = Time.time + repathInterval;
    }

    void OnPathReady(Path path, bool overridesUsed)
    {
        _pathPending = false;

        if (path == null || path.error || path.path == null || path.path.Count == 0)
        {
            _pathFollower.ClearPath();
            _currentPathPoints.Clear();
            return;
        }

        if (overridesUsed)
            _doorOverrideArmed = false;

        // Use node path for strict 4-way grid (PathFinding should step cell-by-cell)
        var gridCells = ConvertNodePathToGridCells(path);
        _pathFollower.SetPath(gridCells);

        // Store vectorPath for advice + debug
        _currentPathPoints.Clear();
        if (path.vectorPath != null)
            _currentPathPoints.AddRange(path.vectorPath);

        ResetDoorOverrideTracker();
    }

    List<Vector2Int> ConvertNodePathToGridCells(Path path)
    {
        if (path == null || path.path == null || path.path.Count == 0)
            return new List<Vector2Int>();

        var cells = new List<Vector2Int>(path.path.Count);
        Vector3 origin = _motor != null ? _motor.EffectiveOrigin() : Vector3.zero;
        float cellSize = _motor != null ? _motor.cellSize : 1f;

        for (int i = 0; i < path.path.Count; i++)
        {
            var node = path.path[i] as GridNodeBase;
            if (node == null)
                continue;

            Vector3 nodePos = (Vector3)node.position;
            int x = Mathf.FloorToInt((nodePos.x - origin.x) / cellSize);
            int z = Mathf.FloorToInt((nodePos.z - origin.z) / cellSize);
            var cell = new Vector2Int(x, z);

            if (cells.Count == 0 || cells[cells.Count - 1] != cell)
                cells.Add(cell);
        }
        // IMPORTANT: don't include our current cell as the first step,
        // otherwise PathFinding can "target" our current tile forever.
        Vector2Int cur = WorldToGrid(transform.position);
        if (cells.Count > 0 && cells[0] == cur)
            cells.RemoveAt(0);
        return cells;
    }

    void TickMoveDecision()
    {
        var curDirBrain = MotorCurDirBrain();
        bool blockedForward = (curDirBrain != Vector2Int.zero && MotorIsBlockedBrain(curDirBrain));

        float eps =
            (curDirBrain == Vector2Int.zero || blockedForward)
                ? stoppedSteerEpsilon
                : centerSnapEpsilon;

        // CORNER-AWARE DECISION GATE:
        // - When moving, we allow decisions inside a "preTurnWindow" along travel axis,
        //   but require snapping on the perpendicular axis (prevents ignoring side gaps).
        if (!IsNearDecisionPoint(curDirBrain, eps) && !blockedForward)
            return;

        Vector2Int currentGrid = WorldToGrid(transform.position);

        // Hard rule: if there's only one way out (or one way excluding reverse), take it.
        List<Vector2Int> openDirs = new List<Vector2Int>(4);
        Vector2Int[] dirs4 = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int i = 0; i < dirs4.Length; i++)
            if (!MotorIsBlockedBrain(dirs4[i]))
                openDirs.Add(dirs4[i]);

        if (openDirs.Count <= 1)
        {
            if (openDirs.Count == 1)
                CommitDirection(currentGrid, curDirBrain, openDirs[0]);
            return;
        }

        // If corridor (2 exits) prefer the one that's not reverse unless A* says otherwise.
        if (openDirs.Count == 2 && curDirBrain != Vector2Int.zero)
        {
            Vector2Int rev = -curDirBrain;
            Vector2Int astar = GetAStarAdviceDir();
            if (astar != Vector2Int.zero && !MotorIsBlockedBrain(astar))
            {
                CommitDirection(currentGrid, curDirBrain, astar);
                return;
            }
            // pick non-reverse if present
            for (int i = 0; i < openDirs.Count; i++)
                if (openDirs[i] != rev)
                {
                    CommitDirection(currentGrid, curDirBrain, openDirs[i]);
                    return;
                }
        }

        // Decide only once per cell while centered-ish (prevents multi-frame rerolls at junctions)
        if (currentGrid == _lastDecisionCell && !_isStuck)
            return;

        Vector2Int astarDir = GetAStarAdviceDir();
        Vector2Int marchDir = GetMarchAdviceDir();

        // Dynamic blending: chase leans A*, scatter leans marching/heat exploration.
        float astarBias = astarVote * (mode == GhostMode.Chase ? 1.0f : 0.35f);
        float marchBias = marchVote * (mode == GhostMode.Scatter ? 1.0f : 0.65f);

        var options = new List<Vector2Int>(4)
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        float minS = float.PositiveInfinity;
        var candidates = new List<(Vector2Int d, float s)>(4);
        Vector2Int rev2 = -curDirBrain;
        int openCount = 0;
        for (int i = 0; i < options.Count; i++)
            if (!MotorIsBlockedBrain(options[i]))
                openCount++;

        bool isDeadEnd = (openCount <= 1); // only one way out

        foreach (var d in options)
        {
            if (MotorIsBlockedBrain(d))
                continue;

            // HARD RULE: cannot reverse unless dead end (or stuck/panic)
            if (curDirBrain != Vector2Int.zero && d == rev2 && !isDeadEnd && !_isStuck && !_inPanic)
                continue;

            float s = 1f;

            // Prefer turning at junctions (when >2 options exist)
            if (!isDeadEnd && openCount >= 3 && curDirBrain != Vector2Int.zero)
            {
                if (d == curDirBrain)
                    s += straightBonus;
                else if (d != rev2)
                    s += turnBonus;
            }

            if (d == astarDir)
                s += astarBias;
            if (d == marchDir)
                s += marchBias;

            // Buffered A* gets a small extra nudge (helps early commitment at corners)
            if (_bufferedDir != Vector2Int.zero && d == _bufferedDir)
                s += astarBias * 0.25f;

            if (astarDir == Vector2Int.zero && d == marchDir)
                s += marchBias; // extra recovery

            Vector2Int stepCell = currentGrid + d;
            s -= GetPerEntityHeat(stepCell) * perEntityHeatWeight;

            // Portal avoidance unless allowed / panic
            if (!_inPanic && Time.time > _portalAllowedUntil)
            {
                Vector3 stepWorld = GridToWorldCenter(stepCell);
                if (IsPortalWorld(stepWorld))
                    s -= portalAvoidHeat; // big shove away
            }

            if (useSharedHeatMap && sharedHeatAvoidanceWeight > 0f)
                s -= GetSharedHeat(stepCell) * sharedHeatAvoidanceWeight;

            // If we *are* allowed to reverse (dead end), still slightly discourage it
            if (curDirBrain != Vector2Int.zero && d == rev2 && isDeadEnd && !_isStuck && !_inPanic)
                s -= 0.25f;

            s += Random.Range(-randomness, randomness);

            candidates.Add((d, s));
            if (s < minS)
                minS = s;
        }

        if (candidates.Count == 0)
            return;

        float sum = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            float w = Mathf.Max(0.001f, candidates[i].s - minS + 0.001f);
            candidates[i] = (candidates[i].d, w);
            sum += w;
        }

        float r = Random.value * sum;
        for (int i = 0; i < candidates.Count; i++)
        {
            r -= candidates[i].s;
            if (r <= 0f)
            {
                CommitDirection(currentGrid, curDirBrain, candidates[i].d);
                return;
            }
        }

        CommitDirection(currentGrid, curDirBrain, candidates[candidates.Count - 1].d);
    }

    bool IsNearDecisionPoint(Vector2Int curDirBrain, float eps)
    {
        if (_motor == null)
            return true;

        Vector2Int cell = WorldToGrid(transform.position);
        Vector3 c = GridToWorldCenter(cell);

        float dx = Mathf.Abs(transform.position.x - c.x);
        float dz = Mathf.Abs(transform.position.z - c.z);

        // Stopped: require both axes within eps
        if (curDirBrain == Vector2Int.zero)
            return (dx <= eps && dz <= eps);

        // Moving: lock perpendicular axis tightly, allow a window on travel axis.
        float w = Mathf.Max(0.05f, preTurnWindow);

        if (curDirBrain.x != 0) // moving along X
            return (dz <= eps && dx <= w);
        // moving along Z
        return (dx <= eps && dz <= w);
    }

    void CommitDirection(Vector2Int currentGrid, Vector2Int curDirBrain, Vector2Int chosenBrainDir)
    {
        _lastChosenDir = chosenBrainDir;
        _lastDirChangeAt = Time.time;
        _lastDecisionCell = currentGrid;

        // Your requested rule: repath on axis change, throttled by minCellsBetweenAxisRepath
        if (chosenBrainDir != Vector2Int.zero && chosenBrainDir != curDirBrain)
            _wantsAxisRepath = true;

        MotorSetDesiredBrain(chosenBrainDir);
    }

    Vector2Int GetAStarAdviceDir()
    {
        // Use vectorPath segment direction in world-space to avoid "diagonal delta => X-first bias".
        if (_currentPathPoints != null && _currentPathPoints.Count >= 2)
        {
            Vector3 here = transform.position;
            int idx = 0;
            while (
                idx < _currentPathPoints.Count
                && (here - _currentPathPoints[idx]).sqrMagnitude < 0.01f
            )
                idx++;

            int nextIdx = Mathf.Min(_currentPathPoints.Count - 1, idx + 1);
            Vector3 next = _currentPathPoints[nextIdx];

            Vector3 d = next - here;
            d.y = 0f;

            if (d.sqrMagnitude > 0.0001f)
            {
                // If A* direction has any meaningful +Z component and up is open,
                // prefer UP when it is available. This stops horizontal dithering.
                if (d.z > 0.05f && !MotorIsBlockedBrain(Vector2Int.up))
                    return Vector2Int.up;

                Vector2Int brainDir;
                if (Mathf.Abs(d.x) >= Mathf.Abs(d.z))
                    brainDir = d.x >= 0f ? Vector2Int.right : Vector2Int.left;
                else
                    brainDir = d.z >= 0f ? Vector2Int.up : Vector2Int.down;

                if (brainDir != Vector2Int.zero && MotorIsBlockedBrain(brainDir))
                    return Vector2Int.zero;

                // Don't let A* "vote" for reversals unless dead end
                var cur = MotorCurDirBrain();
                if (cur != Vector2Int.zero && brainDir == -cur)
                {
                    int open = 0;
                    if (!MotorIsBlockedBrain(Vector2Int.up))
                        open++;
                    if (!MotorIsBlockedBrain(Vector2Int.down))
                        open++;
                    if (!MotorIsBlockedBrain(Vector2Int.left))
                        open++;
                    if (!MotorIsBlockedBrain(Vector2Int.right))
                        open++;
                    if (open > 1)
                        return Vector2Int.zero;
                }
                return brainDir;
            }
        }

        // Fallback: use grid cells if vectorPath isn't available.
        if (_pathFollower == null || !_pathFollower.HasActivePath)
            return Vector2Int.zero;

        Vector2Int currentGrid = WorldToGrid(transform.position);
        Vector2Int nextCell = _pathFollower.CurrentTargetCell;
        Vector2Int delta = nextCell - currentGrid;

        Vector2Int d2 = Vector2Int.zero;
        if (delta.x > 0)
            d2 = Vector2Int.right;
        else if (delta.x < 0)
            d2 = Vector2Int.left;
        else if (delta.y > 0)
            d2 = Vector2Int.up;
        else if (delta.y < 0)
            d2 = Vector2Int.down;

        if (d2 != Vector2Int.zero && MotorIsBlockedBrain(d2))
            return Vector2Int.zero;

        return d2;
    }

    Vector2Int GetMarchAdviceDir()
    {
        var cur = MotorCurDirBrain();
        if (cur != Vector2Int.zero)
            _marchFacing = cur;

        Vector2Int left = TurnLeft(_marchFacing);
        Vector2Int right = TurnRight(_marchFacing);

        if (!MotorIsBlockedBrain(left))
        {
            _marchFacing = left;
            return left;
        }
        if (!MotorIsBlockedBrain(_marchFacing))
            return _marchFacing;
        if (!MotorIsBlockedBrain(right))
        {
            _marchFacing = right;
            return right;
        }

        Vector2Int back = -_marchFacing;
        if (!MotorIsBlockedBrain(back))
        {
            _marchFacing = back;
            return back;
        }

        return Vector2Int.zero;
    }

    static Vector2Int TurnLeft(Vector2Int d) => new Vector2Int(-d.y, d.x);
    static Vector2Int TurnRight(Vector2Int d) => new Vector2Int(d.y, -d.x);

    // --------------------------------------------------------------------
    // Scatter corner (BrainType -> classic corner slot mapping)
    // --------------------------------------------------------------------
    Vector3 GetScatterCorner()
    {
        var rt = LevelRuntime.Active;
        if (rt == null)
            return transform.position;

        var b = rt.levelBoundsXZ;

        // Mapping (inspiration only):
        // Assault  -> corner slot 0 (maxX,maxZ)
        // Sniffer  -> corner slot 1 (minX,maxZ)
        // Curious  -> corner slot 2 (maxX,minZ)
        // Afraid   -> corner slot 3 (minX,minZ)
        return brainType switch
        {
            EnemyBrainType.Assault => new Vector3(b.max.x, b.center.y, b.max.z),
            EnemyBrainType.Sniffer => new Vector3(b.min.x, b.center.y, b.max.z),
            EnemyBrainType.Curious => new Vector3(b.max.x, b.center.y, b.min.z),
            _ => new Vector3(b.min.x, b.center.y, b.min.z),
        };
    }

    // --------------------------------------------------------------------
    // Per-entity heat tracking
    // --------------------------------------------------------------------
    void UpdatePerEntityHeatTracking()
    {
        var rt = LevelRuntime.Active;
        if (rt == null)
            return;

        Vector2Int tile = WorldToGrid(transform.position);

        if (tile == _lastRecordedTile)
            return;

        // track movement for axis-repath throttling
        _cellsSinceLastRepath++;
        _lastRecordedTile = tile;

        _recentTiles.Enqueue(tile);

        // Evict oldest history entry and correctly recompute its heat (fix: stale heat accumulation)
        if (_recentTiles.Count > perEntityHistorySize)
        {
            Vector2Int old = _recentTiles.Dequeue();
            if (_tileTouchCounts.TryGetValue(old, out int oldCount))
            {
                oldCount = Mathf.Max(0, oldCount - 1);
                if (oldCount == 0)
                {
                    _tileTouchCounts.Remove(old);
                    _tileHeat.Remove(old);
                }
                else
                {
                    _tileTouchCounts[old] = oldCount;
                    _tileHeat[old] = ComputeTileHeat(old, oldCount);
                }
            }
        }

        // Add touch for current tile
        if (_tileTouchCounts.TryGetValue(tile, out int count))
            count++;
        else
            count = 1;

        _tileTouchCounts[tile] = count;
        _tileHeat[tile] = ComputeTileHeat(tile, count);

        // Loop pressure -> panic bump (NOT dt-scaled; this is an event, not a per-frame rate)
        if (count > perEntityTouchThreshold)
            _panic = Mathf.Min(panicMax, _panic + panicLoopBonus);
    }

    float ComputeTileHeat(Vector2Int tile, int count)
    {
        float heat = 0f;

        if (IsSpawnTile(tile))
            heat += spawnTilePassiveHeat;

        if (count > perEntityTouchThreshold)
        {
            int extra = count - perEntityTouchThreshold;
            heat += extra * perEntityHeatPerExtraTouch * 10f; // your "10x shove"
        }

        // baseline "recentness" penalty
        heat += count * 0.75f;

        return heat;
    }

    float GetPerEntityHeat(Vector2Int tile)
    {
        if (_tileHeat.TryGetValue(tile, out float h))
            return h;
        return 0f;
    }

    bool IsSpawnTile(Vector2Int tile)
    {
        var rt = LevelRuntime.Active;
        if (rt == null)
            return false;

        Vector3 wp = GridToWorldCenter(tile);
        wp.y = transform.position.y;

        // Tag check
        if (!string.IsNullOrEmpty(enemySpawnTag))
        {
            Collider[] cols = Physics.OverlapSphere(
                wp,
                rt.cellSize * 0.35f,
                ~0,
                QueryTriggerInteraction.Collide
            );
            for (int i = 0; i < cols.Length; i++)
                if (cols[i] != null && cols[i].CompareTag(enemySpawnTag))
                    return true;
        }

        // Layer check
        if (enemySpawnMask.value != 0)
        {
            Collider[] cols = Physics.OverlapSphere(
                wp,
                rt.cellSize * 0.35f,
                enemySpawnMask,
                QueryTriggerInteraction.Collide
            );
            if (cols != null && cols.Length > 0)
                return true;
        }

        return false;
    }

    // --------------------------------------------------------------------
    // Shared heat map
    // --------------------------------------------------------------------
    void UpdateSharedHeatMapIfEnabled()
    {
        if (!useSharedHeatMap)
            return;
        if (Time.time < _sharedHeatMapNextUpdate)
            return;

        _sharedHeatMapNextUpdate = Time.time + 0.50f;
        _sharedHeatMap.Clear();

        var enemies = FindObjectsOfType<EnemyBrainController>();
        for (int i = 0; i < enemies.Length; i++)
        {
            var e = enemies[i];
            if (e == null || e == this)
                continue;

            Vector2Int gp = WorldToGrid(e.transform.position);
            AddSharedHeat(gp, sharedHeatRadius, 1f);
        }

        if (!(brainType == EnemyBrainType.Assault && _assaultState == AssaultState.Holding))
        {
            Vector2Int my = WorldToGrid(transform.position);
            _sharedHeatMap[my] = float.MaxValue;
        }
    }

    void AddSharedHeat(Vector2Int center, float radius, float value)
    {
        int r = Mathf.CeilToInt(radius);
        for (int x = center.x - r; x <= center.x + r; x++)
        {
            for (int y = center.y - r; y <= center.y + r; y++)
            {
                Vector2Int p = new Vector2Int(x, y);
                float d = Vector2Int.Distance(center, p);
                if (d > radius)
                    continue;

                float falloff = 1f - (d / radius);
                float add = value * falloff;

                if (_sharedHeatMap.TryGetValue(p, out float existing))
                    _sharedHeatMap[p] = existing + add;
                else
                    _sharedHeatMap[p] = add;
            }
        }
    }

    float GetSharedHeat(Vector2Int tile)
    {
        return _sharedHeatMap.TryGetValue(tile, out float h) ? h : 0f;
    }

    // --------------------------------------------------------------------
    // Panic
    // --------------------------------------------------------------------
    void UpdatePanicMeter()
    {
        if (Time.time < _panicSuppressedUntil)
        {
            _panic = Mathf.Max(0f, _panic - Time.deltaTime * 60f);
            return;
        }

        if (Time.time >= _nextFloorScanAt)
        {
            _nextFloorScanAt = Time.time + floorSpaceScanInterval;
            _lastFloorSpaceCount = EstimateLocalFloorSpace(
                WorldToGrid(transform.position),
                floorSpaceScanMaxNodes
            );
        }

        float gain = panicBaseGainPerSecond;

        if (_lastFloorSpaceCount <= floorSpaceCrampedThreshold)
        {
            float t =
                1f
                - Mathf.Clamp01(
                    _lastFloorSpaceCount / Mathf.Max(1f, (float)floorSpaceCrampedThreshold)
                );
            gain += panicTightSpaceGainPerSecond * t;
        }

        if (_isStuck)
            gain += 35f;

        if (brainType == EnemyBrainType.Assault && _assaultState == AssaultState.Holding)
            gain *= 0.25f;

        // If A* says "no path to player", push panic up faster to encourage overrides/exploration.
        if (!_hasAStarPathToPlayer && !_inPanic)
            gain += 12f;

        _panic = Mathf.Clamp(_panic + gain * Time.deltaTime, 0f, panicMax);
    }

    bool ShouldEnterPanicNow()
    {
        if (_inPanic)
            return false;
        if (Time.time < _panicSuppressedUntil)
            return false;
        return _panic >= panicMax;
    }

    void EnterPanic()
    {
        _inPanic = true;

        if (enableGhostOnPanic && _enemy != null)
            _enemy.isGhost = true;

        _pendingGhostDisableAfterDoor = disableGhostAfterFirstDoorOrPortal;

        _doorOverrideArmed = true;
        _bestDistanceToTarget = float.MaxValue;
        _lastProgressTime = Time.time;

        _nextRepathTime = Time.time;
        _pathFollower.ClearPath();
        _pathPending = false;

        // Panic means portals are fair game.
        _portalAllowedUntil = Time.time + 1.0f;
    }

    void ExitPanic()
    {
        _inPanic = false;
        _panic = 0f;
        _pendingGhostDisableAfterDoor = false;
    }

    void ForceExploreGoalNow()
    {
        Vector3 basePos = transform.position;

        Vector3 best = basePos;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < 10; i++)
        {
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(3f, 8f);
            Vector3 probe = basePos + new Vector3(r.x, 0f, r.y);
            Vector3 candidate = SampleReachablePoint(probe, 2.5f);

            Vector2Int g = WorldToGrid(candidate);
            float score = 0f;

            score -= GetPerEntityHeat(g) * 2.5f;
            if (useSharedHeatMap)
                score -= GetSharedHeat(g) * 5f;
            score += CountOpenNeighbors(g) * 2f;
            score += Random.Range(-0.5f, 0.5f);

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        ScheduleDestination(best, forceImmediate: true);
    }

    // --------------------------------------------------------------------
    // Door/portal override arming
    // --------------------------------------------------------------------
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

        // If A* says "no path", arm overrides sooner.
        if (!_doorOverrideArmed && doorOverrideAllowance > 0 && !_hasAStarPathToPlayer)
        {
            if (Time.time - _lastProgressTime >= Mathf.Max(1.5f, doorOverrideTimeout * 0.50f))
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

    // --------------------------------------------------------------------
    // Teleport / Door / Portal events
    // --------------------------------------------------------------------
    void OnTeleported(Vector3 fromPos, Vector3 toPos)
    {
        if (Vector3.Distance(fromPos, toPos) <= 1f)
            return;

        OnPassedThroughDoorOrPortal();
    }

    public void OnPassedThroughDoorOrPortal()
    {
        if (_inPanic && _pendingGhostDisableAfterDoor)
        {
            _pendingGhostDisableAfterDoor = false;

            if (_enemy != null && enableGhostOnPanic)
                _enemy.isGhost = false;

            _needsPostDoorRecheck = true;
        }
    }

    void OnEnemyRespawn()
    {
        ExitPanic();

        if (_enemy != null)
            _enemy.isGhost = false;

        _pathFollower.ClearPath();
        _pathPending = false;
        _hasDestination = false;

        _tileTouchCounts.Clear();
        _tileHeat.Clear();
        _recentTiles.Clear();
        _lastRecordedTile = new Vector2Int(int.MinValue, int.MinValue);

        _wantsAxisRepath = false;
        _cellsSinceLastRepath = 999;
        _lastRepathCell = new Vector2Int(int.MinValue, int.MinValue);

        _panicSuppressedUntil = Time.time + 0.25f;
        ResetDoorOverrideTracker();
    }

    // --------------------------------------------------------------------
    // Stuck detection
    // --------------------------------------------------------------------
    void UpdateStuckDetection()
    {
        float moved = Vector3.Distance(transform.position, _lastPos);
        if (moved > 0.05f)
        {
            _lastPos = transform.position;
            _lastMovedAt = Time.time;
            _isStuck = false;
            return;
        }

        _isStuck = (Time.time - _lastMovedAt) >= stuckTimeout;

        if (_isStuck)
        {
            _nextRepathTime = Time.time;
            _pathFollower.ClearPath();
            _pathPending = false;

            _panic = Mathf.Min(panicMax, _panic + Time.deltaTime * 80f);
        }
    }

    // --------------------------------------------------------------------
    // Utility: floor space scan (BFS)
    // --------------------------------------------------------------------
    int EstimateLocalFloorSpace(Vector2Int start, int maxNodes)
    {
        if (AstarPath.active == null)
            return maxNodes;

        GraphNode startNode = AstarPath.active
            .GetNearest(GridToWorldCenter(start), NNConstraint.None)
            .node;
        if (startNode == null || !startNode.Walkable)
            return 0;

        Queue<GraphNode> q = new Queue<GraphNode>(maxNodes);
        HashSet<uint> visited = new HashSet<uint>();

        q.Enqueue(startNode);
        visited.Add(startNode.NodeIndex);

        int count = 0;

        while (q.Count > 0 && count < maxNodes)
        {
            GraphNode n = q.Dequeue();
            if (n == null || !n.Walkable)
                continue;

            count++;

            n.GetConnections(conn =>
            {
                if (conn == null || !conn.Walkable)
                    return;
                if (visited.Count >= maxNodes)
                    return;

                if (visited.Add(conn.NodeIndex))
                    q.Enqueue(conn);
            });
        }

        return count;
    }

    // --------------------------------------------------------------------
    // Portal allowance / avoidance helpers
    // --------------------------------------------------------------------
    void UpdatePortalAllowanceFromBreadcrumbs()
    {
        if (_inPanic)
        {
            _portalAllowedUntil = Time.time + 1.0f;
            return;
        }

        // If we saw the player recently, allow portals briefly (chase pressure).
        if (Time.time - _lastSeenPlayerTime <= portalAllowAfterSeenSeconds)
        {
            _portalAllowedUntil = Mathf.Max(_portalAllowedUntil, Time.time + portalAllowAfterSeenSeconds);
            return;
        }

        // If we've been blind too long, allow portals as a desperation tool.
        if (Time.time - _lastSeenPlayerTime >= portalDesperationAfterNoSightSeconds)
        {
            _portalAllowedUntil = Mathf.Max(_portalAllowedUntil, Time.time + 1.25f);
        }
    }

    bool IsPortalWorld(Vector3 worldPos)
    {
        float r = (_motor != null ? _motor.cellSize : 1f) * 0.25f;

        // Layer check first (fast)
        if (portalMask.value != 0)
        {
            Collider[] cols = Physics.OverlapSphere(
                worldPos,
                r,
                portalMask,
                QueryTriggerInteraction.Collide
            );
            if (cols != null && cols.Length > 0)
                return true;
        }

        // Tag fallback
        if (!string.IsNullOrEmpty(portalTag))
        {
            Collider[] cols = Physics.OverlapSphere(
                worldPos,
                r,
                ~0,
                QueryTriggerInteraction.Collide
            );
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null && cols[i].CompareTag(portalTag))
                    return true;
            }
        }

        return false;
    }

    // --------------------------------------------------------------------
    // World/Grid helpers (MATCH GridMotor.cs)
    // --------------------------------------------------------------------
    Vector2Int WorldToGrid(Vector3 worldPos)
    {
        Vector3 origin = _motor != null ? _motor.EffectiveOrigin() : Vector3.zero;
        float cs = _motor != null ? _motor.cellSize : 1f;

        Vector3 local = worldPos - origin;
        int cx = Mathf.FloorToInt(local.x / cs);
        int cz = Mathf.FloorToInt(local.z / cs);
        return new Vector2Int(cx, cz);
    }

    Vector3 GridToWorldCenter(Vector2Int cell)
    {
        Vector3 origin = _motor != null ? _motor.EffectiveOrigin() : Vector3.zero;
        float cs = _motor != null ? _motor.cellSize : 1f;

        return origin
            + new Vector3((cell.x + 0.5f) * cs, transform.position.y, (cell.y + 0.5f) * cs);
    }

    Vector3 ClampToLevel(Vector3 pos)
    {
        var rt = LevelRuntime.Active;
        if (rt == null)
            return pos;

        var b = rt.levelBoundsXZ;
        pos.x = Mathf.Clamp(pos.x, b.min.x, b.max.x);
        pos.z = Mathf.Clamp(pos.z, b.min.z, b.max.z);
        pos.y = b.center.y;
        return pos;
    }

    // --------------------------------------------------------------------
    // Path sampling / prediction helpers
    // --------------------------------------------------------------------
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
        Vector3 candidate = ClampToLevel(near);

        if (AstarPath.active == null)
            return candidate;

        const int attempts = 10;

        for (int i = 0; i < attempts; i++)
        {
            Vector2 circle = Random.insideUnitCircle * radius;
            Vector3 probe = ClampToLevel(new Vector3(near.x + circle.x, near.y, near.z + circle.y));
            var nn = AstarPath.active.GetNearest(probe, NNConstraint.None);

            if (nn.node != null && nn.node.Walkable)
                return (Vector3)nn.position;
        }

        var fallback = AstarPath.active.GetNearest(candidate, NNConstraint.None);
        if (fallback.node != null && fallback.node.Walkable)
            return (Vector3)fallback.position;

        return candidate;
    }

    // --------------------------------------------------------------------
    // Local graph options
    // --------------------------------------------------------------------
    int CountOpenNeighbors(Vector2Int cell)
    {
        int count = 0;
        if (IsNeighborOpen(cell, Vector2Int.up))
            count++;
        if (IsNeighborOpen(cell, Vector2Int.down))
            count++;
        if (IsNeighborOpen(cell, Vector2Int.left))
            count++;
        if (IsNeighborOpen(cell, Vector2Int.right))
            count++;
        return count;
    }

    bool IsNeighborOpen(Vector2Int from, Vector2Int dir)
    {
        if (AstarPath.active == null || LevelRuntime.Active == null)
            return true;

        Vector2Int to = from + dir;

        GraphNode a = AstarPath.active.GetNearest(GridToWorldCenter(from), NNConstraint.None).node;
        GraphNode b = AstarPath.active.GetNearest(GridToWorldCenter(to), NNConstraint.None).node;

        if (a == null || b == null)
            return false;
        if (!a.Walkable || !b.Walkable)
            return false;

        bool connected = false;
        a.GetConnections(conn =>
        {
            if (connected)
                return;
            if (conn == null)
                return;
            if (conn == b)
                connected = true;
        });

        return connected;
    }

    // --------------------------------------------------------------------
    // Debug drawing
    // --------------------------------------------------------------------
    void DrawPath(IReadOnlyList<Vector3> points, Color color, float duration = 0f)
    {
        if (!pathDebug || points == null || points.Count < 2)
            return;
        if (duration <= 0f)
            duration = pathDebugDuration;

        for (int i = 0; i < points.Count - 1; i++)
            Debug.DrawLine(points[i], points[i + 1], color, duration);
    }

    void DrawCurrentPath()
    {
        if (_currentPathPoints.Count > 0)
            DrawPath(_currentPathPoints, Color.green, 0.5f);
    }

    void DrawPlayerPath()
    {
        if (_playerPathPoints.Count > 0)
            DrawPath(_playerPathPoints, Color.yellow, aStarHealthCheckInterval);

        if (_tracker != null && _currentTarget != null)
        {
            var crumbs = _tracker.GetBreadcrumbs(_currentTarget);
            if (crumbs != null && crumbs.Count > 0)
                DrawPath(crumbs, new Color(1f, 0.5f, 0f), 0.5f);
        }
    }

    void DrawDestinationLine()
    {
        if (!pathDebug || !_hasDestination)
            return;
        Debug.DrawLine(transform.position, _desiredDestination, Color.cyan, 0.1f);
    }

    void DrawStatusIndicator()
    {
        if (!pathDebug)
            return;

        Color statusColor = Color.white;
        if (_inPanic)
            statusColor = Color.red;
        else if (_isStuck)
            statusColor = Color.black;
        else if (_hasAStarPathToPlayer)
            statusColor = Color.green;
        else
            statusColor = Color.gray;

        Debug.DrawLine(
            transform.position + Vector3.up * 2f,
            transform.position + Vector3.up * 2.5f,
            statusColor,
            0.1f
        );
    }

    void LogDebug(string message)
    {
        if (!verboseDebug)
            return;

        string timestamped = $"[{Time.time:F2}] {message}";
        _debugLog.Enqueue(timestamped);

        while (_debugLog.Count > maxDebugEntries)
            _debugLog.Dequeue();

        Debug.Log($"EnemyBrain ({name}): {message}", this);
    }

    void CheckAStarReachabilityToPlayer()
    {
        if (Time.time - _lastAStarCheckTime < aStarHealthCheckInterval)
            return;
        _lastAStarCheckTime = Time.time;

        if (_currentTarget == null)
        {
            _hasAStarPathToPlayer = false;
            return;
        }

        if (AstarPath.active == null)
        {
            _hasAStarPathToPlayer = false;
            return;
        }

        Vector3 start = transform.position;
        Vector3 goal = _currentTarget.transform.position;

        DoorDelegate.FindPathForEntity(
            start,
            goal,
            _enemy,
            (path, overridesUsed) =>
            {
                _hasAStarPathToPlayer =
                    path != null
                    && !path.error
                    && path.vectorPath != null
                    && path.vectorPath.Count > 0;

                _playerPathPoints.Clear();
                if (_hasAStarPathToPlayer && path.vectorPath != null)
                    _playerPathPoints.AddRange(path.vectorPath);
            },
            0,
            0f
        );
    }

    // --------------------------------------------------------------------
    // Context menu debug
    // --------------------------------------------------------------------
    [ContextMenu("Dump Debug State")]
    void DumpDebugState()
    {
        var rt = LevelRuntime.Active;
        Vector2Int cell = (rt != null) ? WorldToGrid(transform.position) : Vector2Int.zero;
        Vector3 center = (rt != null) ? GridToWorldCenter(cell) : transform.position;

        Debug.Log($"=== EnemyBrain Debug Dump for {name} ===", this);
        Debug.Log($"Position: {transform.position}", this);
        Debug.Log($"Current Target: {_currentTarget?.name ?? "null"}", this);
        Debug.Log($"Has Destination: {_hasDestination}", this);
        Debug.Log($"Desired Destination: {_desiredDestination}", this);
        Debug.Log($"Path Pending: {_pathPending}", this);
        Debug.Log($"In Panic: {_inPanic} (meter: {_panic})", this);
        Debug.Log($"Is Stuck: {_isStuck}", this);
        Debug.Log($"A* Path to Player: {_hasAStarPathToPlayer}", this);
        Debug.Log($"Brain Type: {brainType}", this);
        Debug.Log($"Mode: {mode}", this);
        Debug.Log($"Grid Cell: {cell}", this);

        Debug.Log(
            $"Adapter swapXZForMotor={swapXZForMotor}, invertX={invertXForMotor}, invertZ={invertZForMotor}",
            this
        );
        Debug.Log(
            $"MotorCurDir (raw): {_motor.GetCurrentDirection()}  BrainCurDir: {MotorCurDirBrain()}",
            this
        );

        if (rt != null)
        {
            Debug.Log($"Grid Origin: {rt.gridOrigin}  CellSize: {rt.cellSize}", this);
            Debug.Log(
                $"Cell Center: {center}  CenterDelta: ({transform.position.x - center.x:F3}, {transform.position.z - center.z:F3})",
                this
            );
        }

        Debug.Log($"Current Path Points: {_currentPathPoints.Count}", this);
        Debug.Log($"Player Path Points: {_playerPathPoints.Count}", this);

        if (_motor != null)
        {
            Vector2Int[] dirs =
            {
                Vector2Int.up,
                Vector2Int.down,
                Vector2Int.left,
                Vector2Int.right
            };
            foreach (var dir in dirs)
            {
                bool blocked = MotorIsBlockedBrain(dir);
                Debug.Log($"Motor Direction (brain) {dir}: {(blocked ? "BLOCKED" : "open")}", this);
            }
        }

        if (_pathFollower != null)
        {
            Debug.Log($"Path Follower Has Path: {_pathFollower.HasActivePath}", this);
            Debug.Log($"Path Follower Idle: {_pathFollower.IsIdle}", this);
            Debug.Log($"Path Follower Target Cell: {_pathFollower.CurrentTargetCell}", this);
        }

        Debug.Log("=== End Debug Dump ===", this);
    }

    [ContextMenu("Force Repath")]
    void ForceRepath()
    {
        if (_currentTarget != null)
            ScheduleDestination(_currentTarget.transform.position, forceImmediate: true);
    }

    [ContextMenu("Toggle Path Debug")]
    void TogglePathDebug()
    {
        pathDebug = !pathDebug;
        Debug.Log($"Path debug {(pathDebug ? "enabled" : "disabled")} for {name}", this);
    }
}
