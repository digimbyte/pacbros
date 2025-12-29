using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding;

/// <summary>
/// Modern Pac-Man-style enemy brain:
/// - Primary navigation: A* (with door/portal override tickets when armed or in panic).
/// - Secondary navigation: local grid steering at intersections (corner-aware), used to prevent “wall-hug 180s” and to explore side paths.
/// - Per-entity heat: remembers last 1000 tiles; if a tile is touched > 10 times, its heat ramps hard (avoid loops).
/// - “Current tile is infinitely bad” (strongly discourages lingering / re-entering same tile).
/// - “Player within 3 tiles is infinitely good” (forces aggressive interception locally, even if A* path is noisy).
/// - Panic meter: 0..1000, rises faster in small floor spaces + with loop behavior; when full:
///     -> enable ghost, path with A* through doors/portals to find player,
///     -> once passing the first door/portal, disable ghost and immediately re-evaluate environment (don’t keep beelining).
/// Doors and portals are treated the same; portals trigger teleport events.
/// </summary>
public enum EnemyBrainType
{
    Sniffer,
    Curious,
    Assault,
    Afraid
}

[RequireComponent(typeof(EnemyEntity))]
[RequireComponent(typeof(GridMotor))]
[RequireComponent(typeof(PathFinding))]
public class EnemyBrainController : MonoBehaviour
{
    // ----------------------------
    // Brain / Pathing
    // ----------------------------
    [Header("Brain")]
    public EnemyBrainType brainType = EnemyBrainType.Sniffer;

    [Tooltip("Seconds between repath requests while pursuing a destination.")]
    public float repathInterval = 0.30f;

    [Tooltip("Force the controller to pick a different player target every N seconds (if available).")]
    public float targetShuffleInterval = 30f;

    [Tooltip("Minimum distance delta before forcing an immediate path refresh.")]
    public float destinationUpdateThreshold = 0.75f;

    [Header("Intersection / Cornering")]
    [Tooltip("How close (meters) to the center of a grid cell before we consider turning decisions.")]
    public float centerSnapEpsilon = 0.12f;

    [Tooltip("Hard penalty for immediate 180 reversals unless forced.")]
    public float reversePenalty = 250f;

    [Tooltip("Extra bias to keep moving forward when reasonable (prevents jitter).")]
    public float forwardBias = 4f;

    [Tooltip("Small randomness added to break ties and remove East/West bias.")]
    [Range(0f, 2f)] public float tieBreakJitter = 0.35f;

    [Header("Goal Bias")]
    [Tooltip("Weight for preferring directions that lead toward the goal (0 = random, 1 = strongly prefer goal direction).")]
    [Range(0f, 1f)]
    public float goalDirectionBias = 0.65f;

    // ----------------------------
    // Heat (Shared + Per-Entity)
    // ----------------------------
    [Header("Shared Heat (crowd avoidance)")]
    public bool useSharedHeatMap = true;
    public float sharedHeatRadius = 2.5f;
    [Range(0f, 1f)] public float sharedHeatAvoidanceWeight = 0.15f;
    static readonly Dictionary<Vector2Int, float> _sharedHeatMap = new Dictionary<Vector2Int, float>(1024);
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
    [Range(0f, 2f)] public float perEntityHeatWeight = 0.65f;

    [Header("Enemy Spawn Tile Detection (optional)")]
    [Tooltip("If you have spawn tiles as colliders on a layer, set it here. Otherwise leave empty.")]
    public LayerMask enemySpawnMask;

    [Tooltip("If you tag spawn tile objects, set the tag here (optional).")]
    public string enemySpawnTag = "EnemySpawn";

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

    [Tooltip("When panic is full, enable ghost mode and force door/portal overrides to reach player.")]
    public bool enableGhostOnPanic = true;

    [Tooltip("Once we pass the first door/portal during panic, disable ghost mode and re-evaluate (prevents beeline).")]
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

    // Curious
    enum CuriousMode { Wander, Predict }
    CuriousMode _curiousMode = CuriousMode.Wander;
    float _curiousModeUntil;
    Vector3 _curiousGoal;

    // Assault
    enum AssaultState { Moving, Holding, Striking }
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

    // Panic
    float _panic;
    float _nextFloorScanAt;
    int _lastFloorSpaceCount = 999;
    float _panicSuppressedUntil;
    bool _inPanic;
    bool _pendingGhostDisableAfterDoor;
    bool _needsPostDoorRecheck;

    // Direction memory (reduces 180 jitter)
    Vector2Int _lastChosenDir;
    float _lastDirChangeAt;
    float _lastDecisionAt;

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
            return;

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

        UpdateStuckDetection();
        UpdateSharedHeatMapIfEnabled();
        UpdatePerEntityHeatTracking();      // updates tile touch + heat
        UpdatePanicMeter();                 // uses floor space + loops + stuck
        UpdateDoorOverrideTracker();        // arms override if no progress

        UpdateTargetSelection();
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

            // “Modern” behavior: re-evaluate a fresh local exploration goal instead of snapping to player
            // (prevents instant re-beeline from panic logic).
            ForceExploreGoalNow();
        }

        // Choose a high-level destination goal by brain type (or panic override).
        float dt = Time.deltaTime;

        if (ShouldEnterPanicNow())
            EnterPanic();

        if (_inPanic)
        {
            // Panic mode: always set goal to player, and force A* overrides + ghost (as requested).
            Vector3 playerPos = _currentTarget.transform.position;
            ScheduleDestination(playerPos, forceImmediate: true);
        }
        else
        {
            // Normal brain behavior.
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

        // Primary: A* tick (requests path). Secondary: local steering (cornering / exploration / player reaction).
        TickAStarRequests();
        TickLocalSteering(); // ALWAYS runs; it fixes the “hug wall + 180” and ignores-West/East bias issues.
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
            if (p == null || p == _currentTarget) continue;

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

        if (dist > snifferChaseDistance)
        {
            var crumbs = _tracker.GetBreadcrumbs(_currentTarget);
            if (crumbs != null && crumbs.Count > 0)
                goal = crumbs[0];
        }

        ScheduleDestination(goal, forceImmediate: false);
    }

    void UpdateCurious(float dt)
    {
        if (Time.time >= _curiousModeUntil && Time.time - _lastDecisionAt >= decisionCooldown)
            SwitchCuriousMode();

        // If we reached the curious goal, pick another quickly (prevents wall-hug idle).
        if (Vector3.Distance(transform.position, _curiousGoal) <= Mathf.Max(0.6f, destinationUpdateThreshold))
        {
            if (Time.time - _lastDecisionAt >= decisionCooldown)
                SwitchCuriousMode();
        }

        ScheduleDestination(_curiousGoal, forceImmediate: false);
    }

    void SwitchCuriousMode()
    {
        _curiousMode = (_curiousMode == CuriousMode.Wander) ? CuriousMode.Predict : CuriousMode.Wander;
        float duration = (_curiousMode == CuriousMode.Wander) ? curiousWanderDuration : curiousInterceptDuration;
        _curiousModeUntil = Time.time + duration;
        _lastDecisionAt = Time.time;

        if (_curiousMode == CuriousMode.Wander)
            _curiousGoal = SampleReachablePoint(_currentTarget.transform.position, curiousWanderRadius);
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

                if (_assaultAmbushPoint == Vector3.zero ||
                    Vector3.Distance(_assaultAmbushPoint, _currentTarget.transform.position) > 2f)
                {
                    if (Time.time - _lastDecisionAt >= decisionCooldown)
                    {
                        PickAmbushPoint();
                        _lastDecisionAt = Time.time;
                    }
                }

                if (_assaultAmbushPoint != Vector3.zero)
                    ScheduleDestination(_assaultAmbushPoint, forceImmediate: false);

                if (_assaultAmbushPoint != Vector3.zero &&
                    Vector3.Distance(transform.position, _assaultAmbushPoint) <= Mathf.Max(0.5f, destinationUpdateThreshold))
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

                float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);
                if ((dist <= assaultTriggerDistance || _assaultHoldTimer <= 0f) &&
                    Time.time - _lastDecisionAt >= decisionCooldown)
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

                if (Vector3.Distance(transform.position, strikeGoal) <= assaultResetDistance &&
                    Time.time - _lastDecisionAt >= decisionCooldown)
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

        if (_afraidFleeDirection == Vector3.zero ||
            Time.time - _lastAfraidDecision > decisionCooldown ||
            Vector3.Distance(_afraidLastPlayerPos, playerPos) > 1f)
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
        Vector3 forward = velocity.sqrMagnitude > 0.01f ? velocity.normalized : _currentTarget.transform.forward;
        Vector3 lateral = new Vector3(-forward.z, 0f, forward.x) * Random.Range(-2f, 2f);
        _assaultAmbushPoint = SampleReachablePoint(predicted + lateral, 2.5f);
    }

    // --------------------------------------------------------------------
    // Scheduling / Hysteresis (kept tight; do NOT over-reject or you stall)
    // --------------------------------------------------------------------
    void ScheduleDestination(Vector3 worldPos, bool forceImmediate)
    {
        _desiredDestination = ClampToLevel(worldPos);
        _hasDestination = true;

        bool shouldIssue =
            forceImmediate ||
            !_hasIssuedGoal ||
            Vector3.Distance(_lastIssuedGoal, _desiredDestination) >= destinationUpdateThreshold ||
            Time.time >= _nextRepathTime;

        if (shouldIssue)
        {
            _lastIssuedGoal = _desiredDestination;
            _hasIssuedGoal = true;
            _nextRepathTime = Time.time; // allows immediate request
        }
    }

    // --------------------------------------------------------------------
    // A* request tick (DoorDelegate integration)
    // --------------------------------------------------------------------
    void TickAStarRequests()
    {
        var runtime = LevelRuntime.Active;
        if (runtime == null || !runtime.useAStar)
            return;

        if (!_hasDestination)
            return;

        // If a path request is pending too long, fail fast and fall back to local steering.
        if (_pathPending && Time.time - _pathPendingSince > 2.0f)
        {
            _pathPending = false;
            _pathFollower.ClearPath();
        }

        if (_pathPending)
            return;

        // Rate limit repaths unless stuck/panic.
        bool shouldRepath =
            Time.time >= _nextRepathTime ||
            _isStuck ||
            _inPanic;

        if (!shouldRepath)
            return;

        _pathPending = true;
        _pathPendingSince = Time.time;

        Vector3 start = transform.position;
        Vector3 goal = _desiredDestination;

        int overrideAllowance = 0;
        float ticketLifetime = Mathf.Max(0.1f, doorOverrideTicketLifetime);

        // Panic: force overrides aggressively.
        if (_inPanic)
        {
            overrideAllowance = Mathf.Max(1, doorOverrideAllowance);
        }
        else
        {
            overrideAllowance = _doorOverrideArmed ? Mathf.Max(0, doorOverrideAllowance) : 0;
        }

        DoorDelegate.FindPathForEntity(start, goal, _enemy, OnPathReady, overrideAllowance, ticketLifetime);
        _nextRepathTime = Time.time + repathInterval;
    }

    void OnPathReady(Path path, bool overridesUsed)
    {
        _pathPending = false;

        if (path == null || path.error || path.vectorPath == null || path.vectorPath.Count == 0)
        {
            _pathFollower.ClearPath();
            return;
        }

        if (overridesUsed)
            _doorOverrideArmed = false;

        _pathFollower.SetPathFromWorldPoints(path.vectorPath);

        // Progress tracker reset: we made a fresh plan.
        ResetDoorOverrideTracker();
    }

    // --------------------------------------------------------------------
    // Local steering (the part that fixes your screenshot issues)
    // --------------------------------------------------------------------
    void TickLocalSteering()
    {
        // If we’re not near a cell center, don’t spam turns; GridMotor will carry us cleanly.
        if (!IsNearCellCenter(centerSnapEpsilon))
            return;

        Vector2Int currentDir = _motor.GetCurrentDirection();
        Vector2Int currentGrid = WorldToGrid(transform.position);

        // “Current tile is infinitely bad”: discourage staying / re-entering same cell.
        // We apply this by making the current cell absurdly hot for evaluation.
        // (Doesn’t affect A* graph; only affects local steering choices.)
        float currentTileInfiniteHeat = 1_000_000f;

        // If player is within 3 tiles: infinitely good (hard override).
        Vector2Int playerDirOverride = GetPlayerWithin3TilesDirection(currentGrid);
        if (playerDirOverride != Vector2Int.zero && !_motor.IsDirectionBlocked(playerDirOverride))
        {
            SetDesiredDirStable(playerDirOverride);
            return;
        }

        // Build candidates at intersection.
        List<Vector2Int> dirs = new List<Vector2Int>(4) { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        // If moving, avoid immediate 180 unless forced/stuck/panic.
        Vector2Int reverseDir = new Vector2Int(-currentDir.x, -currentDir.y);

        float bestScore = float.NegativeInfinity;
        Vector2Int bestDir = Vector2Int.zero;

        for (int i = 0; i < dirs.Count; i++)
        {
            Vector2Int d = dirs[i];
            if (_motor.IsDirectionBlocked(d))
                continue;

            float score = 0f;

            // Base: always prefer movement.
            score += 20f;

            // Forward bias (smooth modern motion)
            if (currentDir != Vector2Int.zero && d == currentDir)
                score += forwardBias;

            // Reverse penalty to stop “sit on wall + 180 forever”.
            if (!_inPanic && !_isStuck && currentDir != Vector2Int.zero && d == reverseDir)
                score -= reversePenalty;

            // Goal pull (brain destination).
            if (_hasDestination && goalDirectionBias > 0f)
                score += GoalAlignmentScore(d) * (goalDirectionBias * 25f);

            // Shared heat (anti-crowd).
            if (useSharedHeatMap && sharedHeatAvoidanceWeight > 0f)
            {
                float h = GetSharedHeat(currentGrid + d);
                score -= h * (sharedHeatAvoidanceWeight * 10f);
            }

            // Per-entity heat (anti-loop). Also inject “current tile infinite bad”.
            float perHeat = GetPerEntityHeat(currentGrid + d);
            score -= perHeat * (perEntityHeatWeight * 2.5f);

            // Current tile infinite bad: if we are choosing a direction that causes us to bounce back into current tile soon,
            // we penalize the reverse heavily (handled above) and we also penalize any direction whose next tile is “too hot”
            // relative to current.
            if (!_inPanic)
            {
                // We add the current tile infinite heat as a constant penalty baseline so anything else wins.
                score -= (currentTileInfiniteHeat * 0.00001f); // tiny constant to break ties away from idle
            }

            // Corner-awareness: prefer directions that lead to more options (avoid dead ends).
            score += CountOpenNeighbors(currentGrid + d) * 3.5f;

            // Tie-break jitter (removes E/W bias).
            score += Random.Range(-tieBreakJitter, tieBreakJitter);

            if (score > bestScore)
            {
                bestScore = score;
                bestDir = d;
            }
        }

        // If we found a direction, commit. If not, allow reverse if it’s the only move.
        if (bestDir != Vector2Int.zero)
        {
            SetDesiredDirStable(bestDir);
            return;
        }

        // Fallback: if all else fails, do something deterministic.
        for (int i = 0; i < dirs.Count; i++)
        {
            if (!_motor.IsDirectionBlocked(dirs[i]))
            {
                SetDesiredDirStable(dirs[i]);
                return;
            }
        }
    }

    Vector2Int GetPlayerWithin3TilesDirection(Vector2Int myGrid)
    {
        if (_tracker == null || !_tracker.HasPlayers)
            return Vector2Int.zero;

        // “Infinitely good” means we pick the direction that reduces Manhattan distance most.
        int bestDist = int.MaxValue;
        Vector2Int bestDir = Vector2Int.zero;

        for (int i = 0; i < _tracker.Players.Count; i++)
        {
            var p = _tracker.Players[i];
            if (p == null || p.isDead) continue;

            Vector2Int pg = WorldToGrid(p.transform.position);
            int manhattan = Mathf.Abs(pg.x - myGrid.x) + Mathf.Abs(pg.y - myGrid.y);

            if (manhattan > 3)
                continue;

            // Evaluate which step reduces distance most.
            Vector2Int step = BestStepToward(myGrid, pg);
            if (step == Vector2Int.zero)
                continue;

            // Prefer the closest player within the 3-tile radius.
            if (manhattan < bestDist)
            {
                bestDist = manhattan;
                bestDir = step;
            }
        }

        return bestDir;
    }

    Vector2Int BestStepToward(Vector2Int from, Vector2Int to)
    {
        int dx = to.x - from.x;
        int dy = to.y - from.y;

        // Avoid E/W bias: if tie, choose based on heat/options instead of always X.
        if (Mathf.Abs(dx) == Mathf.Abs(dy) && dx != 0 && dy != 0)
        {
            Vector2Int candA = new Vector2Int(dx > 0 ? 1 : -1, 0);
            Vector2Int candB = new Vector2Int(0, dy > 0 ? 1 : -1);

            // choose lower heat + higher options
            Vector2Int fromGrid = from;
            float aScore = -GetPerEntityHeat(fromGrid + candA) + CountOpenNeighbors(fromGrid + candA);
            float bScore = -GetPerEntityHeat(fromGrid + candB) + CountOpenNeighbors(fromGrid + candB);

            return (aScore >= bScore) ? candA : candB;
        }

        if (Mathf.Abs(dx) >= Mathf.Abs(dy) && dx != 0)
            return new Vector2Int(dx > 0 ? 1 : -1, 0);

        if (dy != 0)
            return new Vector2Int(0, dy > 0 ? 1 : -1);

        return Vector2Int.zero;
    }

    float GoalAlignmentScore(Vector2Int dir)
    {
        Vector3 toGoal = _desiredDestination - transform.position;
        toGoal.y = 0f;

        if (toGoal.sqrMagnitude < 0.0001f)
            return 0f;

        // Convert to a cardinal goal direction, avoiding axis bias on ties.
        Vector2Int goalDir;
        float ax = Mathf.Abs(toGoal.x);
        float az = Mathf.Abs(toGoal.z);

        if (Mathf.Abs(ax - az) < 0.001f)
        {
            // tie: prefer direction with lower heat
            Vector2Int candA = new Vector2Int(toGoal.x >= 0f ? 1 : -1, 0);
            Vector2Int candB = new Vector2Int(0, toGoal.z >= 0f ? 1 : -1);

            Vector2Int cg = WorldToGrid(transform.position);
            float aScore = -GetPerEntityHeat(cg + candA) + CountOpenNeighbors(cg + candA);
            float bScore = -GetPerEntityHeat(cg + candB) + CountOpenNeighbors(cg + candB);

            goalDir = (aScore >= bScore) ? candA : candB;
        }
        else if (ax > az)
        {
            goalDir = new Vector2Int(toGoal.x >= 0f ? 1 : -1, 0);
        }
        else
        {
            goalDir = new Vector2Int(0, toGoal.z >= 0f ? 1 : -1);
        }

        if (dir == goalDir) return 1f;
        if (dir == new Vector2Int(-goalDir.x, -goalDir.y)) return -0.75f;
        return 0f;
    }

    void SetDesiredDirStable(Vector2Int dir)
    {
        if (dir == Vector2Int.zero)
            return;

        // Simple debounce to avoid thrashing.
        if (Time.time - _lastDirChangeAt < decisionCooldown * 0.35f && dir != _lastChosenDir)
            return;

        _motor.SetDesiredDirection(dir);

        if (dir != _lastChosenDir)
        {
            _lastChosenDir = dir;
            _lastDirChangeAt = Time.time;
        }
    }

    // --------------------------------------------------------------------
    // Per-entity heat tracking (last 1000 tiles)
    // --------------------------------------------------------------------
    void UpdatePerEntityHeatTracking()
    {
        Vector2Int tile = WorldToGrid(transform.position);
        if (tile == Vector2Int.zero && LevelRuntime.Active == null)
            return;

        // If same tile as last recorded, skip (prevents overcount while standing on center).
        if (_recentTiles.Count > 0)
        {
            // Peek last without LINQ
            Vector2Int last = default;
            foreach (var t in _recentTiles) last = t;
            if (last == tile)
                return;
        }

        _recentTiles.Enqueue(tile);
        if (_recentTiles.Count > perEntityHistorySize)
        {
            Vector2Int old = _recentTiles.Dequeue();

            if (_tileTouchCounts.TryGetValue(old, out int oldCount))
            {
                oldCount = Mathf.Max(0, oldCount - 1);
                if (oldCount == 0) _tileTouchCounts.Remove(old);
                else _tileTouchCounts[old] = oldCount;
            }

            // We do NOT decay heat aggressively; it’s meant to be a loop breaker.
            // (If you want decay, add it explicitly—right now you asked for strong avoidance.)
        }

        if (_tileTouchCounts.TryGetValue(tile, out int count))
            count++;
        else
            count = 1;

        _tileTouchCounts[tile] = count;

        // Heat update rule (your spec):
        // - track last 1000 tiles
        // - if tile touched > 10 times => increase its heat hard (10x-ish)
        float heat = 0f;

        // Passive spawn heat.
        if (IsSpawnTile(tile))
            heat += spawnTilePassiveHeat;

        if (count > perEntityTouchThreshold)
        {
            // “10x so it tries to avoid that tile”
            // We implement: base (count - threshold) * perExtra * 10 (hard ramp).
            int extra = count - perEntityTouchThreshold;
            heat += extra * perEntityHeatPerExtraTouch * 10f;
        }

        // Also add a small base heat proportional to count to discourage repeats even before threshold.
        heat += count * 0.75f;

        _tileHeat[tile] = heat;

        // Loop bonus into panic: if a tile is being hammered, panic should rise.
        if (count > perEntityTouchThreshold)
            _panic += Time.deltaTime * panicLoopBonus;
    }

    float GetPerEntityHeat(Vector2Int tile)
    {
        // “Current tile infinitely bad” is applied in steering as a separate concept.
        // Here we return stored per-tile heat.
        if (_tileHeat.TryGetValue(tile, out float h))
            return h;
        return 0f;
    }

    bool IsSpawnTile(Vector2Int tile)
    {
        // Optional detection: either collider layer or tag.
        var rt = LevelRuntime.Active;
        if (rt == null) return false;

        Vector3 wp = GridToWorldCenter(tile);
        wp.y = transform.position.y;

        // Tag-based
        if (!string.IsNullOrEmpty(enemySpawnTag))
        {
            Collider[] cols = Physics.OverlapSphere(wp, rt.cellSize * 0.35f);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null && cols[i].CompareTag(enemySpawnTag))
                    return true;
            }
        }

        // Layer-based
        if (enemySpawnMask.value != 0)
        {
            Collider[] cols = Physics.OverlapSphere(wp, rt.cellSize * 0.35f, enemySpawnMask, QueryTriggerInteraction.Collide);
            if (cols != null && cols.Length > 0)
                return true;
        }

        return false;
    }

    // --------------------------------------------------------------------
    // Shared heat map (crowd avoidance)
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
            if (e == null || e == this) continue;

            Vector2Int gp = WorldToGrid(e.transform.position);
            AddSharedHeat(gp, sharedHeatRadius, 1f);
        }

        // Discourage staying in place unless Assault is camping.
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
                if (d > radius) continue;

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
        // Cooldown suppression after a panic break.
        if (Time.time < _panicSuppressedUntil)
        {
            _panic = Mathf.Max(0f, _panic - Time.deltaTime * 60f); // drain a bit while suppressed
            return;
        }

        // Floor space scan (tight spaces raise panic faster).
        if (Time.time >= _nextFloorScanAt)
        {
            _nextFloorScanAt = Time.time + floorSpaceScanInterval;
            _lastFloorSpaceCount = EstimateLocalFloorSpace(WorldToGrid(transform.position), floorSpaceScanMaxNodes);
        }

        float gain = panicBaseGainPerSecond;

        if (_lastFloorSpaceCount <= floorSpaceCrampedThreshold)
        {
            // Smaller floor space => faster panic.
            float t = 1f - Mathf.Clamp01(_lastFloorSpaceCount / Mathf.Max(1f, (float)floorSpaceCrampedThreshold));
            gain += panicTightSpaceGainPerSecond * t;
        }

        // Being stuck ramps panic.
        if (_isStuck)
            gain += 35f;

        // Assault camping should not panic quickly.
        if (brainType == EnemyBrainType.Assault && _assaultState == AssaultState.Holding)
            gain *= 0.25f;

        _panic = Mathf.Clamp(_panic + gain * Time.deltaTime, 0f, panicMax);
    }

    bool ShouldEnterPanicNow()
    {
        if (_inPanic) return false;
        if (Time.time < _panicSuppressedUntil) return false;
        return _panic >= panicMax;
    }

    void EnterPanic()
    {
        _inPanic = true;

        if (enableGhostOnPanic && _enemy != null)
            _enemy.isGhost = true;

        _pendingGhostDisableAfterDoor = disableGhostAfterFirstDoorOrPortal;

        // Hard reset overrides so we don’t stall before using tickets.
        _doorOverrideArmed = true;
        _bestDistanceToTarget = float.MaxValue;
        _lastProgressTime = Time.time;

        // Force immediate A*.
        _nextRepathTime = Time.time;
        _pathFollower.ClearPath();
        _pathPending = false;
    }

    void ExitPanic()
    {
        _inPanic = false;
        _panic = 0f;
        _pendingGhostDisableAfterDoor = false;
    }

    void ForceExploreGoalNow()
    {
        // Pick a reachable point away from recent heat / crowding.
        Vector3 basePos = transform.position;

        // “Modern roam”: sample points in a ring; pick the one with lowest heat and decent options.
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
            if (useSharedHeatMap) score -= GetSharedHeat(g) * 5f;
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
    // Door/portal override arming (progress-based)
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
        // Treat any meaningful teleport as a door/portal pass.
        if (Vector3.Distance(fromPos, toPos) <= 1f)
            return;

        OnPassedThroughDoorOrPortal();
    }

    public void OnPassedThroughDoorOrPortal()
    {
        // Panic flow requirement:
        // - if panic enabled ghost -> disable ghost after first door/portal
        // - then do environment check (re-evaluate) so we don't beeline.
        if (_inPanic && _pendingGhostDisableAfterDoor)
        {
            _pendingGhostDisableAfterDoor = false;

            if (_enemy != null && enableGhostOnPanic)
                _enemy.isGhost = false;

            // Flag for immediate post-door/portal recheck next Update().
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

        // If stuck while in A* mode, force a repath and allow local steering to pick a new corner.
        if (_isStuck)
        {
            _nextRepathTime = Time.time;
            _pathFollower.ClearPath();
            _pathPending = false;

            // Also ramp panic a bit; this is “modern” friction to avoid dead AI.
            _panic = Mathf.Min(panicMax, _panic + Time.deltaTime * 80f);
        }
    }

    // --------------------------------------------------------------------
    // Utility: floor space scan (BFS)
    // --------------------------------------------------------------------
    int EstimateLocalFloorSpace(Vector2Int start, int maxNodes)
    {
        var rt = LevelRuntime.Active;
        if (rt == null)
            return maxNodes;

        // If A* graph exists, use walkability from nearest nodes.
        var astar = AstarPath.active;
        if (astar == null)
            return maxNodes;

        GraphNode startNode = astar.GetNearest(GridToWorldCenter(start), NNConstraint.None).node;
        if (startNode == null || !startNode.Walkable)
            return 0;

        // BFS within connectivity, capped.
        Queue<GraphNode> q = new Queue<GraphNode>(maxNodes);
        HashSet<uint> visited = new HashSet<uint>();

        q.Enqueue(startNode);
        visited.Add(startNode.NodeIndex);

        int count = 0;

        while (q.Count > 0 && count < maxNodes)
        {
            GraphNode n = q.Dequeue();
            if (n == null || !n.Walkable) continue;

            count++;

            n.GetConnections(conn =>
            {
                if (conn == null || !conn.Walkable) return;
                if (visited.Count >= maxNodes) return;

                if (visited.Add(conn.NodeIndex))
                    q.Enqueue(conn);
            });
        }

        return count;
    }

    // --------------------------------------------------------------------
    // World/Grid helpers
    // --------------------------------------------------------------------
    Vector2Int WorldToGrid(Vector3 worldPos)
    {
        var rt = LevelRuntime.Active;
        if (rt == null) return Vector2Int.zero;

        float cell = rt.cellSize;
        Vector3 origin = rt.gridOrigin;

        int x = Mathf.RoundToInt((worldPos.x - origin.x) / cell);
        int y = Mathf.RoundToInt((worldPos.z - origin.z) / cell);
        return new Vector2Int(x, y);
    }

    Vector3 GridToWorldCenter(Vector2Int grid)
    {
        var rt = LevelRuntime.Active;
        if (rt == null) return transform.position;

        return rt.gridOrigin + new Vector3(grid.x * rt.cellSize, rt.levelBoundsXZ.center.y, grid.y * rt.cellSize);
    }

    bool IsNearCellCenter(float eps)
    {
        var rt = LevelRuntime.Active;
        if (rt == null) return true;

        Vector2Int g = WorldToGrid(transform.position);
        Vector3 c = GridToWorldCenter(g);
        c.y = transform.position.y;

        float dx = Mathf.Abs(transform.position.x - c.x);
        float dz = Mathf.Abs(transform.position.z - c.z);

        return (dx <= eps && dz <= eps);
    }

    Vector3 ClampToLevel(Vector3 pos)
    {
        var rt = LevelRuntime.Active;
        if (rt == null) return pos;

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
        var astar = AstarPath.active;
        Vector3 candidate = ClampToLevel(near);

        if (astar == null)
            return candidate;

        const int attempts = 10;

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

    // --------------------------------------------------------------------
    // Local graph “options” for corner-awareness
    // --------------------------------------------------------------------
    int CountOpenNeighbors(Vector2Int cell)
    {
        int count = 0;
        if (IsNeighborOpen(cell, Vector2Int.up)) count++;
        if (IsNeighborOpen(cell, Vector2Int.down)) count++;
        if (IsNeighborOpen(cell, Vector2Int.left)) count++;
        if (IsNeighborOpen(cell, Vector2Int.right)) count++;
        return count;
    }

    bool IsNeighborOpen(Vector2Int from, Vector2Int dir)
    {
        var astar = AstarPath.active;
        var rt = LevelRuntime.Active;

        if (astar == null || rt == null)
            return true; // if no graph, don’t block exploration artificially

        Vector2Int to = from + dir;

        GraphNode a = astar.GetNearest(GridToWorldCenter(from), NNConstraint.None).node;
        GraphNode b = astar.GetNearest(GridToWorldCenter(to), NNConstraint.None).node;

        if (a == null || b == null) return false;
        if (!a.Walkable || !b.Walkable) return false;

        // Check if a is connected to b (approx).
        bool connected = false;
        a.GetConnections(conn =>
        {
            if (connected) return;
            if (conn == null) return;
            if (conn == b) connected = true;
        });

        return connected;
    }
}
