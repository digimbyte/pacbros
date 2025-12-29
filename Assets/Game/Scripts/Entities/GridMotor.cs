using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class GridMotor : MonoBehaviour
{
    static readonly List<GridMotor> _pendingMotors = new();

    [Header("Grid")]
    public float cellSize = 1f;
    public Vector3 gridOrigin = Vector3.zero;
    public bool bindFromLevelRuntime = true;

    [Range(-1f, 1f)] public float gridOffsetX = 0f;
    [Range(-1f, 1f)] public float gridOffsetZ = 0f;

    [Header("Body")]
    public CharacterController characterController;

    [Header("Movement")]
    public float moveSpeed = 6f;
    public float acceleration = 60f;
    public float speedMultiplier = 1f;
    public bool instantAcceleration = true;
    public bool stopOnBlocked = true;
    public bool allowBufferedTurnOnBlock = true;
    public bool allowInstantReverse = false;

    [Header("Lane Snap")]
    public float turnAlignEpsilon = 0.3f;
    public float laneSnapMaxSpeed = 1000f;

    [Header("Collision")]
    public float cornerRadiusScale = 0.5f;
    public float skin = 0.02f;

    // Hard-coded layer IDs for collision detection
    const int WALL_LAYER = 6;  // Assuming wall layer is 8
    const int DOOR_LAYER = 10;  // Assuming door layer is 9
    const int PLAYER_LAYER = 8; // Assuming player layer is 10
    const int ENEMY_LAYER = 13; // Assuming enemy layer is 11

    private LayerMask wallMask;

    public delegate void TeleportDelegate(Vector3 fromPosition, Vector3 toPosition);
    public event TeleportDelegate OnTeleport;

    Vector2 _desiredInput;
    Vector2Int _moveDir;
    Vector2Int _queuedDir;
    Vector3 _velocity;
    float _moveProgress = 0f;
    Vector3 _startPos;

    /// <summary>
    /// Public access to current movement direction for door collision detection
    /// </summary>
    public Vector2Int MoveDirection => _moveDir;

    void Awake()
    {
        if (!characterController)
            characterController = GetComponent<CharacterController>();

        // Set up hard-coded layer masks
        wallMask = 1 << WALL_LAYER;

        TryRegisterWithLevel();
    }

    void OnEnable() => TryRegisterWithLevel();

    void OnDisable()
    {
        if (LevelRuntime.Active != null)
            LevelRuntime.Active.UnregisterMotor(this);

        _pendingMotors.Remove(this);
    }

    void TryRegisterWithLevel()
    {
        if (!bindFromLevelRuntime) return;

        if (LevelRuntime.Active != null)
            LevelRuntime.Active.RegisterMotor(this);
        else if (!_pendingMotors.Contains(this))
            _pendingMotors.Add(this);
    }

    internal void OnLevelReady(LevelRuntime level)
    {
        cellSize = level.cellSize;
        gridOrigin = level.gridOrigin;
        // No longer using level's layer masks - using hard-coded layers
    }

    public static void FlushPendingMotorsTo(LevelRuntime level)
    {
        foreach (var m in _pendingMotors)
            if (m) level.RegisterMotor(m);

        _pendingMotors.Clear();
    }

    public void SetDesiredInput(Vector2 input) => _desiredInput = input;

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector2Int inputDir = GetCardinalFromInput(_desiredInput, 0.15f);
        if (inputDir != Vector2Int.zero)
            _queuedDir = inputDir;

        // Start moving if stopped and have queued direction
        if (_moveDir == Vector2Int.zero && _queuedDir != Vector2Int.zero && !IsBlocked(_queuedDir))
        {
            _moveDir = _queuedDir;
            _queuedDir = Vector2Int.zero;
            _moveProgress = 0f;
            _startPos = transform.position;
        }

        // Move if moving
        if (_moveDir != Vector2Int.zero)
        {
            float moveAmount = moveSpeed * speedMultiplier * dt;
            float progressIncrement = moveAmount / cellSize;
            _moveProgress += progressIncrement;

            Vector3 targetPos = _startPos + new Vector3(_moveDir.x * cellSize, 0, _moveDir.y * cellSize);

            if (_moveProgress >= 1f)
            {
                // Snap to next grid cell
                Vector3 delta = targetPos - transform.position;
                if (characterController && characterController.enabled)
                    characterController.Move(delta);
                else
                    transform.position = targetPos;

                _startPos = transform.position;
                _moveProgress = 0f;

                // At grid intersection, check for turns or continue
                if (_queuedDir != Vector2Int.zero && !IsBlocked(_queuedDir))
                {
                    _moveDir = _queuedDir;
                    _queuedDir = Vector2Int.zero;
                }
                else if (IsBlocked(_moveDir))
                {
                    _moveDir = Vector2Int.zero;
                }
            }
            else
            {
                // Lerp position
                Vector3 newPos = Vector3.Lerp(_startPos, targetPos, _moveProgress);
                Vector3 delta = newPos - transform.position;
                if (characterController && characterController.enabled)
                    characterController.Move(delta);
                else
                    transform.position = newPos;
            }
        }
    }

    void ApplyLaneSnap(float dt)
    {
        Vector3 pos = transform.position;
        Vector3 origin = EffectiveOrigin();

        float targetX = SnapToLane(pos.x, origin.x, cellSize);
        pos.x = Mathf.MoveTowards(pos.x, targetX, laneSnapMaxSpeed * dt);

        float targetZ = SnapToLane(pos.z, origin.z, cellSize);
        pos.z = Mathf.MoveTowards(pos.z, targetZ, laneSnapMaxSpeed * dt);

        transform.position = pos;
    }

    bool IsTurnAligned(Vector2Int dir)
    {
        Vector3 pos = transform.position;
        Vector3 origin = EffectiveOrigin();

        if (dir.x != 0)
            return Mathf.Abs(pos.z - SnapToLane(pos.z, origin.z, cellSize)) <= turnAlignEpsilon;

        if (dir.y != 0)
            return Mathf.Abs(pos.x - SnapToLane(pos.x, origin.x, cellSize)) <= turnAlignEpsilon;

        return false;
    }

    bool IsBlocked(Vector2Int dir)
    {
        if (LevelRuntime.Active == null) return false;

        var lr = LevelRuntime.Active;
        Vector3 local = transform.position - lr.gridOrigin;

        int cx = Mathf.FloorToInt(local.x / lr.cellSize);
        int cz = Mathf.FloorToInt(local.z / lr.cellSize);

        int tx = cx + dir.x;
        int tz = cz + dir.y;

        Vector3 target =
            lr.gridOrigin +
            new Vector3((tx + 0.5f) * lr.cellSize, 0, (tz + 0.5f) * lr.cellSize);

        if (!lr.levelBoundsXZ.Contains(new Vector3(target.x, 0, target.z)))
            return true;

        return HasWallAtCell(tx, tz);
    }

    bool HasWallAtCell(int x, int z)
    {
        if (LevelRuntime.Active == null) return false;

        var lr = LevelRuntime.Active;
        Vector3 center =
            lr.gridOrigin +
            new Vector3((x + 0.5f) * lr.cellSize, transform.position.y, (z + 0.5f) * lr.cellSize);

        // Use sphere cast for wall collision detection only
        float radius = lr.cellSize * 0.3f;
        if (Physics.SphereCast(center + Vector3.up * 2f, radius, Vector3.down, out RaycastHit hit, 4f, wallMask))
        {
            if (hit.collider.gameObject != gameObject)
                return true;
        }

        // Entities NEVER block each other - only walls block movement
        return false;
    }

    public Vector3 EffectiveOrigin()
    {
        return gridOrigin + new Vector3(gridOffsetX * cellSize, 0, gridOffsetZ * cellSize);
    }

    static float SnapToLane(float v, float origin, float size)
    {
        float t = v / size;
        return Mathf.Round(t) * size;
    }

    static Vector2Int GetCardinalFromInput(Vector2 input, float deadzone)
    {
        if (input.sqrMagnitude < deadzone * deadzone)
            return Vector2Int.zero;

        return Mathf.Abs(input.x) >= Mathf.Abs(input.y)
            ? new Vector2Int(input.x >= 0 ? 1 : -1, 0)
            : new Vector2Int(0, input.y >= 0 ? 1 : -1);
    }

    public Vector3 GetVelocity() => _velocity;

    public void SetDesiredDirection(Vector2Int dir) => _queuedDir = dir;

    public Vector2Int GetCurrentDirection() => _moveDir;

    public bool IsDirectionBlocked(Vector2Int dir) => IsBlocked(dir);

    public void Teleport(Vector3 position)
    {
        Vector3 fromPosition = transform.position;

        // Temporarily disable trails and controller to avoid spawning effects or collisions during teleport
        var trails = GetComponent<Trails>();
        if (trails != null)
            trails.enabled = false;
        if (characterController != null)
            characterController.enabled = false;

        // Set position
        transform.position = position;
        _startPos = position;
        _moveProgress = 0f;
        _moveDir = Vector2Int.zero;
        _queuedDir = Vector2Int.zero;
        _velocity = Vector3.zero;

        // Notify listeners of teleport
        OnTeleport?.Invoke(fromPosition, position);

        // Re-enable after a frame
        StartCoroutine(ReEnableAfterTeleport(trails));
    }

    System.Collections.IEnumerator ReEnableAfterTeleport(Trails trails)
    {
        yield return null;
        if (trails != null)
            trails.enabled = true;
        if (characterController != null)
            characterController.enabled = true;
    }
}
