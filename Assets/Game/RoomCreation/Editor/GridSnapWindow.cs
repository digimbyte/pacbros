#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class GridSnapWindow : EditorWindow
{
    private enum SnapMode { Nearest, Floor, Ceil }

    private float gridSize = 1f;
    private bool ignoreY = false;
    private SnapMode snapMode = SnapMode.Nearest;
    private float clusterRadiusCells = 1f; // How far to look (in grid cells) to consider items part of the same cluster.

    [MenuItem("PacBros/Scene/Grid Snapping")]
    public static void Open()
    {
        GetWindow<GridSnapWindow>(false, "Grid Snapping");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Grid Settings", EditorStyles.boldLabel);
        gridSize = Mathf.Max(0.0001f, EditorGUILayout.FloatField("Grid Size", gridSize));
        ignoreY = EditorGUILayout.Toggle("Ignore/Lock Y", ignoreY);
        snapMode = (SnapMode)EditorGUILayout.EnumPopup("Snap Mode", snapMode);
        clusterRadiusCells = Mathf.Clamp(EditorGUILayout.FloatField("Cluster Radius (cells)", clusterRadiusCells), 0.1f, 4f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
        if (GUILayout.Button("Validate Selection"))
        {
            ValidateTransforms(GetSelectionTransforms());
        }
        if (GUILayout.Button("Snap Selection"))
        {
            SnapTransforms(GetSelectionTransforms(), resolveOverlap: false);
        }
        if (GUILayout.Button("Snap Selection + Resolve Overlap"))
        {
            SnapTransforms(GetSelectionTransforms(), resolveOverlap: true);
        }
        EditorGUILayout.Space();
        if (GUILayout.Button("Snap All Active"))
        {
            SnapTransforms(GetAllActiveTransforms(), resolveOverlap: false);
        }
        if (GUILayout.Button("Snap All Active + Resolve Overlap"))
        {
            SnapTransforms(GetAllActiveTransforms(), resolveOverlap: true);
        }
    }

    private List<Transform> GetSelectionTransforms()
    {
        var list = new List<Transform>();
        foreach (var obj in Selection.transforms)
        {
            if (obj != null && obj.gameObject.activeInHierarchy)
            {
                list.Add(obj);
            }
        }
        return list;
    }

    private List<Transform> GetAllActiveTransforms()
    {
        var list = new List<Transform>();
        foreach (var go in GameObject.FindObjectsOfType<GameObject>())
        {
            if (!go.activeInHierarchy)
            {
                continue;
            }
            list.Add(go.transform);
        }
        return list;
    }

    private void ValidateTransforms(List<Transform> transforms)
    {
        int offGrid = 0;
        foreach (var t in transforms)
        {
            if (t == null) continue;
            if (!IsSnapped(t.position))
            {
                offGrid++;
            }
        }
        Debug.Log($"GridSnap: {transforms.Count} checked, {offGrid} off-grid (grid {gridSize}, ignoreY={ignoreY}, mode={snapMode}).");
    }

    private void SnapTransforms(List<Transform> transforms, bool resolveOverlap)
    {
        if (transforms == null || transforms.Count == 0)
        {
            Debug.Log("GridSnap: No transforms to snap.");
            return;
        }

        // Stable left-to-right, top-to-bottom ordering reduces unexpected shuffles when resolving overlaps.
        transforms.Sort((a, b) =>
        {
            int z = a.position.z.CompareTo(b.position.z);
            if (z != 0) return z;
            int x = a.position.x.CompareTo(b.position.x);
            if (x != 0) return x;
            return a.position.y.CompareTo(b.position.y);
        });

        Undo.RecordObjects(transforms.ToArray(), "Grid Snap");

        Vector3 origin = ComputeOriginOffset(transforms[0].position);

        var snapInfos = new List<SnapInfo>(transforms.Count);
        foreach (var t in transforms)
        {
            if (t == null) continue;
            Vector3 snapped = SnapPosition(t.position, origin);
            Vector3Int cell = WorldToCell(snapped);
            snapInfos.Add(new SnapInfo { Transform = t, SnappedPosition = snapped, Cell = cell });
        }

        var clusters = BuildClusters(snapInfos);
        // Sort clusters by their minimum Z then X to keep deterministic placement order.
        clusters.Sort((a, b) =>
        {
            int z = a.MinCell.z.CompareTo(b.MinCell.z);
            if (z != 0) return z;
            return a.MinCell.x.CompareTo(b.MinCell.x);
        });

        var occupied = new HashSet<Vector3Int>();
        var results = new Dictionary<Transform, Vector3>();
        const int groupPadding = 1; // Keep at least 1 empty cell between clusters.

        foreach (var cluster in clusters)
        {
            ResolveInternalCollisions(cluster);

            Vector2Int offset = Vector2Int.zero;
            if (resolveOverlap)
            {
                offset = FindClusterOffset(cluster.Cells, occupied, groupPadding);
            }

            Vector3 translation = new Vector3(offset.x * gridSize, 0f, offset.y * gridSize);
            foreach (var info in cluster.Infos)
            {
                Vector3 finalPos = info.SnappedPosition + translation;
                results[info.Transform] = finalPos;
            }

            // Mark occupied cells including padding so later clusters do not butt up against this cluster.
            foreach (var cell in cluster.Cells)
            {
                Vector3Int shifted = new Vector3Int(cell.x + offset.x, cell.y, cell.z + offset.y);
                AddWithPadding(occupied, shifted, groupPadding);
            }
        }

        foreach (var kvp in results)
        {
            kvp.Key.position = kvp.Value;
        }
    }

    private Vector3 SnapPosition(Vector3 pos, Vector3 origin)
    {
        float x = SnapValue(pos.x, origin.x);
        float y = ignoreY ? pos.y : SnapValue(pos.y, origin.y);
        float z = SnapValue(pos.z, origin.z);
        return new Vector3(x, y, z);
    }

    private float SnapValue(float v, float origin)
    {
        float g = gridSize;
        float scaled = (v - origin) / g;
        switch (snapMode)
        {
            case SnapMode.Floor: return Mathf.Floor(scaled) * g + origin;
            case SnapMode.Ceil: return Mathf.Ceil(scaled) * g + origin;
            default: return Mathf.Round(scaled) * g + origin;
        }
    }

    private Vector3 ComputeOriginOffset(Vector3 sample)
    {
        float g = gridSize;
        float x = Mod(sample.x, g);
        float y = ignoreY ? 0f : Mod(sample.y, g);
        float z = Mod(sample.z, g);
        return new Vector3(x, y, z);
    }

    private float Mod(float value, float m)
    {
        return (value % m + m) % m;
    }

    private bool IsSnapped(Vector3 pos)
    {
        bool xOk = Mathf.Approximately(Mathf.Repeat(pos.x, gridSize), 0f);
        bool zOk = Mathf.Approximately(Mathf.Repeat(pos.z, gridSize), 0f);
        bool yOk = ignoreY || Mathf.Approximately(Mathf.Repeat(pos.y, gridSize), 0f);
        return xOk && yOk && zOk;
    }

    private Vector3Int WorldToCell(Vector3 pos)
    {
        return new Vector3Int(Mathf.RoundToInt(pos.x / gridSize), Mathf.RoundToInt(ignoreY ? 0f : pos.y / gridSize), Mathf.RoundToInt(pos.z / gridSize));
    }

    private struct SnapInfo
    {
        public Transform Transform;
        public Vector3 SnappedPosition;
        public Vector3Int Cell;
    }

    private class Cluster
    {
        public List<SnapInfo> Infos = new List<SnapInfo>();
        public List<Vector3Int> Cells = new List<Vector3Int>();
        public Vector3Int MinCell;
    }

    private List<Cluster> BuildClusters(List<SnapInfo> infos)
    {
        var cellToInfos = new Dictionary<Vector3Int, List<SnapInfo>>();
        foreach (var info in infos)
        {
            if (!cellToInfos.TryGetValue(info.Cell, out var list))
            {
                list = new List<SnapInfo>();
                cellToInfos[info.Cell] = list;
            }
            list.Add(info);
        }

        var clusters = new List<Cluster>();
        var visited = new HashSet<Vector3Int>();
        int neighborRange = Mathf.Max(1, Mathf.CeilToInt(clusterRadiusCells));

        foreach (var kvp in cellToInfos)
        {
            if (visited.Contains(kvp.Key)) continue;

            var cluster = new Cluster();
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(kvp.Key);
            visited.Add(kvp.Key);
            cluster.MinCell = kvp.Key;

            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                cluster.Cells.Add(cell);
                if (cellToInfos.TryGetValue(cell, out var infosAtCell))
                {
                    cluster.Infos.AddRange(infosAtCell);
                }

                for (int dx = -neighborRange; dx <= neighborRange; dx++)
                {
                    for (int dz = -neighborRange; dz <= neighborRange; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        float dist = Mathf.Sqrt(dx * dx + dz * dz);
                        if (dist > clusterRadiusCells + 0.001f) continue;

                        var neighbor = new Vector3Int(cell.x + dx, cell.y, cell.z + dz);
                        if (visited.Contains(neighbor)) continue;
                        if (!cellToInfos.ContainsKey(neighbor)) continue;
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    private void ResolveInternalCollisions(Cluster cluster)
    {
        // Ensure cells and positions inside a cluster are unique before we place the whole cluster.
        cluster.Infos.Sort((a, b) =>
        {
            int z = a.SnappedPosition.z.CompareTo(b.SnappedPosition.z);
            if (z != 0) return z;
            int x = a.SnappedPosition.x.CompareTo(b.SnappedPosition.x);
            return x != 0 ? x : a.SnappedPosition.y.CompareTo(b.SnappedPosition.y);
        });

        var occupied = new HashSet<Vector3Int>();
        var updated = new List<SnapInfo>(cluster.Infos.Count);

        foreach (var info in cluster.Infos)
        {
            Vector3Int targetCell = info.Cell;
            if (occupied.Contains(targetCell))
            {
                targetCell = FindInternalFree(targetCell, occupied);
            }

            occupied.Add(targetCell);

            Vector3Int delta = targetCell - info.Cell;
            Vector3 deltaWorld = new Vector3(delta.x * gridSize, ignoreY ? 0f : delta.y * gridSize, delta.z * gridSize);
            var adjusted = info;
            adjusted.Cell = targetCell;
            adjusted.SnappedPosition = info.SnappedPosition + deltaWorld;
            updated.Add(adjusted);
        }

        cluster.Infos = updated;
        cluster.Cells = new List<Vector3Int>(occupied);

        // Update MinCell
        Vector3Int min = updated[0].Cell;
        foreach (var info in updated)
        {
            var c = info.Cell;
            if (c.z < min.z || (c.z == min.z && c.x < min.x))
            {
                min = c;
            }
        }
        cluster.MinCell = min;
    }

    private Vector3Int FindInternalFree(Vector3Int start, HashSet<Vector3Int> occupied)
    {
        if (!occupied.Contains(start)) return start;

        int radius = 1;
        while (radius < 2000)
        {
            for (int x = -radius; x <= radius; x++)
            {
                int z = radius;
                var c1 = new Vector3Int(start.x + x, start.y, start.z + z);
                if (!occupied.Contains(c1)) return c1;
                z = -radius;
                var c2 = new Vector3Int(start.x + x, start.y, start.z + z);
                if (!occupied.Contains(c2)) return c2;
            }

            for (int z = -radius + 1; z <= radius - 1; z++)
            {
                int x = radius;
                var c1 = new Vector3Int(start.x + x, start.y, start.z + z);
                if (!occupied.Contains(c1)) return c1;
                x = -radius;
                var c2 = new Vector3Int(start.x + x, start.y, start.z + z);
                if (!occupied.Contains(c2)) return c2;
            }
            radius++;
        }

        return start;
    }

    private Vector2Int FindClusterOffset(List<Vector3Int> cells, HashSet<Vector3Int> occupied, int padding)
    {
        if (IsAreaFree(cells, occupied, padding))
        {
            return Vector2Int.zero;
        }

        int radius = 1;
        while (radius < 2000)
        {
            for (int x = -radius; x <= radius; x++)
            {
                int z = radius;
                if (IsAreaFreeWithOffset(cells, occupied, padding, x, z)) return new Vector2Int(x, z);
                z = -radius;
                if (IsAreaFreeWithOffset(cells, occupied, padding, x, z)) return new Vector2Int(x, z);
            }

            for (int z = -radius + 1; z <= radius - 1; z++)
            {
                int x = radius;
                if (IsAreaFreeWithOffset(cells, occupied, padding, x, z)) return new Vector2Int(x, z);
                x = -radius;
                if (IsAreaFreeWithOffset(cells, occupied, padding, x, z)) return new Vector2Int(x, z);
            }
            radius++;
        }

        return Vector2Int.zero;
    }

    private bool IsAreaFree(List<Vector3Int> cells, HashSet<Vector3Int> occupied, int padding)
    {
        return IsAreaFreeWithOffset(cells, occupied, padding, 0, 0);
    }

    private bool IsAreaFreeWithOffset(List<Vector3Int> cells, HashSet<Vector3Int> occupied, int padding, int offsetX, int offsetZ)
    {
        foreach (var cell in cells)
        {
            var shifted = new Vector3Int(cell.x + offsetX, cell.y, cell.z + offsetZ);
            for (int dx = -padding; dx <= padding; dx++)
            {
                for (int dz = -padding; dz <= padding; dz++)
                {
                    var probe = new Vector3Int(shifted.x + dx, shifted.y, shifted.z + dz);
                    if (occupied.Contains(probe))
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    private void AddWithPadding(HashSet<Vector3Int> occupied, Vector3Int cell, int padding)
    {
        for (int dx = -padding; dx <= padding; dx++)
        {
            for (int dz = -padding; dz <= padding; dz++)
            {
                occupied.Add(new Vector3Int(cell.x + dx, cell.y, cell.z + dz));
            }
        }
    }
}
#endif
