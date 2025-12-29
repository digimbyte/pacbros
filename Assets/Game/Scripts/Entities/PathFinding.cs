using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple path follower: feed a list of grid cells and it will drive a GridMotor smoothly.
/// This doesn't assume any specific A* package; your actual pathfinder can call SetPath().
/// </summary>
[DisallowMultipleComponent]
public class PathFinding : MonoBehaviour
{
    public GridMotor motor;
    [Header("Debug")]
    [Tooltip("When enabled, PathFinding will log assigned paths and motor commands.")]
    public bool verboseDebug = false;

    [Header("Path")]
    public float waypointReachRadius = 0.15f;

    readonly List<Vector2Int> _cells = new();
    int _idx;

    public bool HasActivePath => _cells.Count > 0 && _idx < _cells.Count;
    public bool IsIdle => !HasActivePath;
    public int RemainingWaypoints => Mathf.Max(0, _cells.Count - Mathf.Clamp(_idx, 0, _cells.Count));

    public Vector2Int CurrentTargetCell
    {
        get
        {
            if (!HasActivePath) return default;
            return _cells[Mathf.Clamp(_idx, 0, _cells.Count - 1)];
        }
    }

    void Reset()
    {
        motor = GetComponent<GridMotor>();
    }

    void Awake()
    {
        if (motor == null)
            motor = GetComponent<GridMotor>();

        // Runtime fallback: try parent/children in case components are arranged differently.
        if (motor == null)
            motor = GetComponentInParent<GridMotor>();
        if (motor == null)
            motor = GetComponentInChildren<GridMotor>();
    }

    public void SetPath(IList<Vector2Int> cells)
    {
        _cells.Clear();
        if (cells != null)
        {
            for (int i = 0; i < cells.Count; i++)
                _cells.Add(cells[i]);
        }
        _idx = 0;
    }

    public void ClearPath()
    {
        _cells.Clear();
        _idx = 0;
    }

    public void SetPathFromWorldPoints(IList<Vector3> worldPoints)
    {
        if (worldPoints == null || worldPoints.Count == 0)
        {
            ClearPath();
            return;
        }

        Vector3 origin = motor != null ? motor.EffectiveOrigin() : Vector3.zero;
        float cellSize = motor != null ? Mathf.Max(0.01f, motor.cellSize) : 1f;

        var buffer = new List<Vector2Int>(worldPoints.Count);
        Vector2Int? last = null;
        for (int i = 0; i < worldPoints.Count; i++)
        {
            Vector2Int cell = WorldToCell(worldPoints[i], origin, cellSize);
            if (last.HasValue && last.Value == cell)
                continue;

            buffer.Add(cell);
            last = cell;
        }

        SetPath(buffer);
        if (verboseDebug)
            Debug.Log($"PathFinding: SetPathFromWorldPoints on '{name}' -> {buffer.Count} cells.", this);

        if (motor == null)
        {
            // Try to recover motor reference at the moment a path is assigned.
            motor = GetComponent<GridMotor>() ?? GetComponentInParent<GridMotor>() ?? GetComponentInChildren<GridMotor>();
            if (motor == null)
            {
                Debug.LogWarning($"PathFinding: SetPathFromWorldPoints called but no GridMotor found on '{name}'. Path will not drive movement.", this);
            }
            else if (verboseDebug)
            {
                Debug.Log($"PathFinding: resolved GridMotor for '{name}' -> '{motor.name}'", this);
            }
        }
    }

    static Vector2Int WorldToCell(Vector3 world, Vector3 origin, float cellSize)
    {
        float inv = 1f / Mathf.Max(0.0001f, cellSize);
        int x = Mathf.RoundToInt((world.x - origin.x) * inv);
        int z = Mathf.RoundToInt((world.z - origin.z) * inv);
        return new Vector2Int(x, z);
    }

    void Update()
    {
        if (motor == null)
        {
            // Attempt to recover motor reference every frame until found (cheap check).
            motor = GetComponent<GridMotor>() ?? GetComponentInParent<GridMotor>() ?? GetComponentInChildren<GridMotor>();
            if (motor == null) return;
        }
        if (_cells.Count == 0) return;

        Vector3 pos = transform.position;
        Vector2Int targetCell = _cells[Mathf.Clamp(_idx, 0, _cells.Count - 1)];
        Vector3 origin = motor.EffectiveOrigin();
        Vector3 targetPos = new Vector3(
            origin.x + targetCell.x * motor.cellSize,
            origin.y,
            origin.z + targetCell.y * motor.cellSize);

        Vector3 delta = targetPos - pos;
        delta.y = 0f;

        if (delta.magnitude <= waypointReachRadius)
        {
            _idx++;
            if (_idx >= _cells.Count)
                return;

            targetCell = _cells[_idx];
            targetPos = new Vector3(origin.x + targetCell.x * motor.cellSize, origin.y, origin.z + targetCell.y * motor.cellSize);
            delta = targetPos - pos;
            delta.y = 0f;
        }

        // Drive motor via cardinal direction toward next cell.
        Vector2Int dir;
        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.z))
            dir = new Vector2Int(delta.x >= 0f ? 1 : -1, 0);
        else
            dir = new Vector2Int(0, delta.z >= 0f ? 1 : -1);

        if (motor != null)
        {
            if (verboseDebug) Debug.Log($"PathFinding: '{name}' issuing direction {dir} to motor '{motor.name}'.", this);
            motor.SetDesiredDirection(dir);
        }
        else if (verboseDebug)
        {
            Debug.LogWarning($"PathFinding: '{name}' wanted to issue direction {dir} but motor is null.", this);
        }
    }
}
