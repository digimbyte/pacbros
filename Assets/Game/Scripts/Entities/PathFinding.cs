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

    [Header("Path")]
    public float waypointReachRadius = 0.15f;

    readonly List<Vector2Int> _cells = new();
    int _idx;

    void Reset()
    {
        motor = GetComponent<GridMotor>();
    }

    void Awake()
    {
        if (motor == null)
            motor = GetComponent<GridMotor>();
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

    void Update()
    {
        if (motor == null) return;
        if (_cells.Count == 0) return;

        Vector3 pos = transform.position;
        Vector2Int targetCell = _cells[Mathf.Clamp(_idx, 0, _cells.Count - 1)];
        Vector3 origin = motor.EffectiveOrigin();
        Vector3 targetPos = new Vector3(
            origin.x + targetCell.x * motor.cellSize,
            pos.y,
            origin.z + targetCell.y * motor.cellSize);

        Vector3 delta = targetPos - pos;
        delta.y = 0f;

        if (delta.magnitude <= waypointReachRadius)
        {
            _idx++;
            if (_idx >= _cells.Count)
                return;

            targetCell = _cells[_idx];
            targetPos = new Vector3(origin.x + targetCell.x * motor.cellSize, pos.y, origin.z + targetCell.y * motor.cellSize);
            delta = targetPos - pos;
            delta.y = 0f;
        }

        // Drive motor via cardinal direction toward next cell.
        Vector2Int dir;
        if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.z))
            dir = new Vector2Int(delta.x >= 0f ? 1 : -1, 0);
        else
            dir = new Vector2Int(0, delta.z >= 0f ? 1 : -1);

        motor.SetDesiredDirection(dir);
    }
}
