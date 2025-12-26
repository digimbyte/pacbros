#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class TileNeighborEditorWindow : EditorWindow
{
    private enum Direction { Up, Right, Down, Left }

    private class Decision
    {
        public Tile tile;
        public int rotation; // 0..3 (90° steps)
        public TriState state;
    }

    private enum TriState { Unknown, Allowed, Blocked }

    private Tile targetTile;
    private List<Tile> allTiles = new List<Tile>();
    private Dictionary<Direction, List<Decision>> decisions = new Dictionary<Direction, List<Decision>>();
    private Direction currentDir = Direction.Up;
    private int candidateIndex;
    private bool showResolved = false;
    private bool lastShowResolved = false;

    // If enabled, Save will also write the reciprocal neighbor spec onto candidate tiles.
    // This keeps relationships bi-directional and prevents one-way definitions from producing contradictions.
    private bool mirrorToCandidates = true;

    private GameObject previewRoot;

    [MenuItem("PacBros/Rooms/Tile Neighbor Editor")]
    public static void Open()
    {
        GetWindow<TileNeighborEditorWindow>(false, "Tile Neighbor Editor");
    }

    private void OnEnable()
    {
        RefreshAllTiles();
        TryAutoSelectTile();
    }

    private void OnDisable()
    {
        CleanupPreview();
    }

    private void OnSelectionChange()
    {
        TryAutoSelectTile();
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        DrawTargetField();

        if (targetTile == null)
        {
            EditorGUILayout.HelpBox("Select a Tile prefab (with Tile component) to edit neighbors.", MessageType.Info);
            return;
        }

        bool toggled = EditorGUILayout.Toggle("Show Resolved", showResolved);
        if (toggled != showResolved)
        {
            int globalIndex = GetCurrentGlobalIndex();
            showResolved = toggled;
            lastShowResolved = showResolved;
            RemapCandidateIndex(globalIndex);
        }

        mirrorToCandidates = EditorGUILayout.ToggleLeft("Mirror decisions to candidate tiles (bi-directional)", mirrorToCandidates);

        DrawDirectionToolbar();
        DrawCandidateControls();
        DrawPreviewArea();
        DrawActions();
    }

    private void DrawTargetField()
    {
        EditorGUI.BeginChangeCheck();
        targetTile = (Tile)EditorGUILayout.ObjectField("Target Tile", targetTile, typeof(Tile), false);
        if (EditorGUI.EndChangeCheck())
        {
            LoadDecisionsFromTile();
            candidateIndex = 0;
        }

        if (GUILayout.Button("Refresh Tile List", GUILayout.Width(160)))
        {
            RefreshAllTiles();
            LoadDecisionsFromTile();
        }
    }

    private void DrawDirectionToolbar()
    {
        EditorGUILayout.Space();
        string[] labels = { "Up", "Right", "Down", "Left" };
        int dirInt = GUILayout.Toolbar((int)currentDir, labels);
        if (dirInt != (int)currentDir)
        {
            currentDir = (Direction)dirInt;
            candidateIndex = 0;
        }
    }

    private void DrawCandidateControls()
    {
        if (!decisions.TryGetValue(currentDir, out var list) || list.Count == 0)
        {
            EditorGUILayout.HelpBox("No candidate tiles found.", MessageType.Warning);
            return;
        }

        var visible = GetVisibleList(list);
        if (visible.Count == 0)
        {
            EditorGUILayout.HelpBox("All candidates resolved for this direction.", MessageType.Info);
            return;
        }

        candidateIndex = Mathf.Clamp(candidateIndex, 0, visible.Count - 1);
        var dec = visible[candidateIndex];

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Candidate {candidateIndex + 1}/{visible.Count}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Tile", dec.tile != null ? dec.tile.name : "<null>");
        EditorGUILayout.LabelField("Rotation", $"{dec.rotation * 90}°");
        EditorGUILayout.LabelField("State", dec.state.ToString());

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Prev"))
        {
            JumpToPreviousTileDistinct(dec.tile);
        }
        if (GUILayout.Button("Next"))
        {
            JumpToNextTileDistinct(dec.tile);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Accept"))
        {
            int prevGlobal = GetCurrentGlobalIndex();
            SetState(dec, TriState.Allowed);
            AdvanceAfterDecisionGlobal(prevGlobal, bulkNextDistinct: false, currentTile: dec.tile);
        }
        if (GUILayout.Button("Accept All"))
        {
            int prevGlobal = GetCurrentGlobalIndex();
            SetStateForTile(dec.tile, TriState.Allowed, onlyUnknown: true);
            AdvanceAfterDecisionGlobal(prevGlobal, bulkNextDistinct: true, currentTile: dec.tile);
        }
        if (GUILayout.Button("Deny"))
        {
            int prevGlobal = GetCurrentGlobalIndex();
            SetState(dec, TriState.Blocked);
            AdvanceAfterDecisionGlobal(prevGlobal, bulkNextDistinct: false, currentTile: dec.tile);
        }
        if (GUILayout.Button("Deny All"))
        {
            int prevGlobal = GetCurrentGlobalIndex();
            SetStateForTile(dec.tile, TriState.Blocked, onlyUnknown: true);
            AdvanceAfterDecisionGlobal(prevGlobal, bulkNextDistinct: true, currentTile: dec.tile);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Prev Rot"))
        {
            JumpRotation(dec.tile, -1);
        }
        if (GUILayout.Button("Next Rot"))
        {
            JumpRotation(dec.tile, 1);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPreviewArea()
    {
        var visible = GetVisibleListForCurrent();
        if (visible.Count == 0)
        {
            return;
        }

        candidateIndex = Mathf.Clamp(candidateIndex, 0, visible.Count - 1);
        var dec = visible[candidateIndex];
        GUILayout.Space(4f);
        Rect rect = GUILayoutUtility.GetRect(position.width - 20, 240f);
        GUI.Box(rect, GUIContent.none);

        Texture2D targetTex = ResolvePreview(targetTile);
        Texture2D candTex = ResolvePreview(dec.tile);

        float size = Mathf.Min(rect.width, rect.height) * 0.32f;
        Vector2 center = rect.center;
        Vector2 offset = DirectionToOffset(currentDir) * size; // no extra padding between tiles

        // Draw target (center)
        if (targetTex != null)
        {
            Rect tRect = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            GUI.DrawTexture(tRect, targetTex, ScaleMode.ScaleToFit, true);
            GUI.Label(new Rect(tRect.x, tRect.yMax - 14f, tRect.width, 14f), targetTile != null ? targetTile.name : "<target>", EditorStyles.whiteMiniLabel);
        }
        else
        {
            GUI.Label(new Rect(center.x - 40, center.y - 10, 80, 20), "No target preview", EditorStyles.centeredGreyMiniLabel);
        }

        // Draw candidate (offset + rotation)
        if (candTex != null)
        {
            Rect cRect = new Rect(center.x + offset.x - size * 0.5f, center.y + offset.y - size * 0.5f, size, size);
            Matrix4x4 prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(dec.rotation * 90f, cRect.center);
            GUI.DrawTexture(cRect, candTex, ScaleMode.ScaleToFit, true);
            GUI.matrix = prev;
            GUI.Label(new Rect(cRect.x, cRect.yMax - 14f, cRect.width, 14f), dec.tile != null ? dec.tile.name : "<candidate>", EditorStyles.whiteMiniLabel);
        }
        else
        {
            Rect cRect = new Rect(center.x + offset.x - 40, center.y + offset.y - 10, 80, 20);
            GUI.Label(cRect, "No candidate preview", EditorStyles.centeredGreyMiniLabel);
        }

        if (GUILayout.Button("Preview in Scene (target + candidate)"))
        {
            SpawnPreview(dec.tile, dec.rotation);
        }
    }

    private void DrawActions()
    {
        EditorGUILayout.Space();
        int unresolved = CountUnresolved();
        EditorGUILayout.LabelField($"Unresolved candidates: {unresolved}");
        if (GUILayout.Button("Save All Directions"))
        {
            SaveAllDirections();
        }
    }

    private void RefreshAllTiles()
    {
        allTiles.Clear();
        string[] guids = AssetDatabase.FindAssets("t:GameObject");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null)
            {
                continue;
            }

            var tile = go.GetComponent<Tile>();
            if (tile != null && !allTiles.Contains(tile))
            {
                allTiles.Add(tile);
            }
        }

        allTiles.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    private void TryAutoSelectTile()
    {
        if (Selection.activeObject is GameObject go)
        {
            var tile = go.GetComponent<Tile>();
            if (tile != null)
            {
                if (tile != targetTile)
                {
                    targetTile = tile;
                    LoadDecisionsFromTile();
                    candidateIndex = 0;
                }
                return;
            }
        }
    }

    private void LoadDecisionsFromTile()
    {
        decisions.Clear();
        if (targetTile == null)
        {
            return;
        }

        foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
        {
            var list = new List<Decision>();
            foreach (var tile in allTiles)
            {
                if (tile == null)
                {
                    continue;
                }
                for (int rot = 0; rot < 4; rot++)
                {
                    list.Add(new Decision { tile = tile, rotation = rot, state = TriState.Unknown });
                }
            }

            // Seed from existing neighbor specs
            Tile.NeighborSpec[] allowed = GetNeighborArray(targetTile, dir, blocked: false);
            if (allowed != null)
            {
                foreach (var spec in allowed)
                {
                    if (spec.tile == null)
                    {
                        continue;
                    }
                    var match = list.FirstOrDefault(d => d.tile == spec.tile && d.rotation == Mathf.Clamp(spec.rotation, 0, 3));
                    if (match != null)
                    {
                        match.state = TriState.Allowed;
                    }
                }
            }

            Tile.NeighborSpec[] blocked = GetNeighborArray(targetTile, dir, blocked: true);
            if (blocked != null)
            {
                foreach (var spec in blocked)
                {
                    if (spec.tile == null)
                    {
                        continue;
                    }
                    var match = list.FirstOrDefault(d => d.tile == spec.tile && d.rotation == Mathf.Clamp(spec.rotation, 0, 3));
                    if (match != null)
                    {
                        match.state = TriState.Blocked;
                    }
                }
            }

            AutoResolveFromCandidates(dir, list);
            decisions[dir] = list;
        }
    }

    private Tile.NeighborSpec[] GetNeighborArray(Tile tile, Direction dir, bool blocked)
    {
        switch (dir)
        {
            case Direction.Up: return blocked ? tile.upBlockedNeighbours : tile.upNeighbours;
            case Direction.Right: return blocked ? tile.rightBlockedNeighbours : tile.rightNeighbours;
            case Direction.Down: return blocked ? tile.downBlockedNeighbours : tile.downNeighbours;
            case Direction.Left: return blocked ? tile.leftBlockedNeighbours : tile.leftNeighbours;
            default: return null;
        }
    }

    private void SetNeighborArray(Tile tile, Direction dir, Tile.NeighborSpec[] specs, bool blocked)
    {
        switch (dir)
        {
            case Direction.Up: if (blocked) tile.upBlockedNeighbours = specs; else tile.upNeighbours = specs; break;
            case Direction.Right: if (blocked) tile.rightBlockedNeighbours = specs; else tile.rightNeighbours = specs; break;
            case Direction.Down: if (blocked) tile.downBlockedNeighbours = specs; else tile.downNeighbours = specs; break;
            case Direction.Left: if (blocked) tile.leftBlockedNeighbours = specs; else tile.leftNeighbours = specs; break;
        }
    }
    private void SaveAllDirections()
    {
        if (targetTile == null)
        {
            return;
        }

        int unresolved = CountUnresolved();
        if (unresolved > 0)
        {
            // IMPORTANT: we still save any explicit Allowed/Blocked decisions even if
            // there are unresolved candidates. Unknown decisions simply preserve the
            // existing neighbor arrays on the asset.
            Debug.LogWarning($"TileNeighborEditor: Saving with {unresolved} unresolved candidates remaining (Unknown entries will be left unchanged).");
        }

        // Record once so Undo works as expected.
        Undo.RecordObject(targetTile, "Save Tile Neighbors");

        foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
        {
            if (!decisions.TryGetValue(dir, out var list))
            {
                continue;
            }

            // Build merged allowed/blocked lists that include any pre-existing
            // neighbor specs on the target tile. Decisions take precedence
            // (they overwrite existing entries). Unknowns leave precomputed
            // entries intact.
            var existingAllowed = GetNeighborArray(targetTile, dir, blocked: false) ?? new Tile.NeighborSpec[0];
            var existingBlocked = GetNeighborArray(targetTile, dir, blocked: true) ?? new Tile.NeighborSpec[0];

            var mergedAllowed = new List<Tile.NeighborSpec>(existingAllowed);
            var mergedBlocked = new List<Tile.NeighborSpec>(existingBlocked);

            bool ContainsSpec(List<Tile.NeighborSpec> col, Tile t, int r)
            {
                return col.Any(s => s.tile == t && s.rotation == r);
            }

            void RemoveSpec(List<Tile.NeighborSpec> col, Tile t, int r)
            {
                col.RemoveAll(s => s.tile == t && s.rotation == r);
            }

            foreach (var d in list)
            {
                if (d.tile == null)
                {
                    continue;
                }

                // Decisions override existing entries
                if (d.state == TriState.Allowed)
                {
                    RemoveSpec(mergedBlocked, d.tile, d.rotation);
                    if (!ContainsSpec(mergedAllowed, d.tile, d.rotation))
                        mergedAllowed.Add(new Tile.NeighborSpec { tile = d.tile, rotation = d.rotation });
                }
                else if (d.state == TriState.Blocked)
                {
                    RemoveSpec(mergedAllowed, d.tile, d.rotation);
                    if (!ContainsSpec(mergedBlocked, d.tile, d.rotation))
                        mergedBlocked.Add(new Tile.NeighborSpec { tile = d.tile, rotation = d.rotation });
                }
                // Unknown: do not remove existing entries; preserve precomputed values
            }

            var allowed = mergedAllowed.ToArray();
            var blocked = mergedBlocked.ToArray();

            SetNeighborArray(targetTile, dir, allowed, blocked: false);
            SetNeighborArray(targetTile, dir, blocked, blocked: true);

            // Health check / optional auto-mirroring.
            // Without mirroring, definitions can easily become one-way (target allows candidate,
            // but candidate does not allow target), which creates contradictions in WFC.
            int totalConsidered = 0;
            int matchesFound = 0;
            int conflicts = 0;
            int missing = 0;

            foreach (var d in list)
            {
                if (d.tile == null || d.state == TriState.Unknown)
                    continue;

                totalConsidered++;
                Direction opp = Opposite(dir);

                // IMPORTANT (rotation normalization):
                // Decision.rotation is the candidate's rotation in the target's neighbor list (target at rot=0).
                // To store the reciprocal spec on the candidate (candidate at rot=0), we need the rotation of the
                // target that makes the pair align, which is the inverse of the decision rotation.
                int neededSpecRotation = (4 - (d.rotation % 4)) % 4;

                var candAllowedArr = GetNeighborArray(d.tile, opp, blocked: false) ?? new Tile.NeighborSpec[0];
                var candBlockedArr = GetNeighborArray(d.tile, opp, blocked: true) ?? new Tile.NeighborSpec[0];

                bool inAllowed = System.Array.Exists(candAllowedArr, s => s.tile == targetTile && s.rotation == neededSpecRotation);
                bool inBlocked = System.Array.Exists(candBlockedArr, s => s.tile == targetTile && s.rotation == neededSpecRotation);

                if ((inAllowed && d.state == TriState.Allowed) || (inBlocked && d.state == TriState.Blocked))
                {
                    matchesFound++;
                }
                else if ((inAllowed && d.state == TriState.Blocked) || (inBlocked && d.state == TriState.Allowed))
                {
                    matchesFound++;
                    conflicts++;
                }
                else
                {
                    missing++;
                }

                if (!mirrorToCandidates)
                    continue;

                // Mirror decisions back to candidate tiles so relationships are bi-directional.
                var candAllowed = new List<Tile.NeighborSpec>(candAllowedArr);
                var candBlocked = new List<Tile.NeighborSpec>(candBlockedArr);

                // Ensure the reciprocal spec uses the normalized/inverted rotation.
                RemoveSpec(candAllowed, targetTile, neededSpecRotation);
                RemoveSpec(candBlocked, targetTile, neededSpecRotation);

                if (d.state == TriState.Allowed)
                {
                    if (!ContainsSpec(candAllowed, targetTile, neededSpecRotation))
                        candAllowed.Add(new Tile.NeighborSpec { tile = targetTile, rotation = neededSpecRotation });
                }
                else if (d.state == TriState.Blocked)
                {
                    if (!ContainsSpec(candBlocked, targetTile, neededSpecRotation))
                        candBlocked.Add(new Tile.NeighborSpec { tile = targetTile, rotation = neededSpecRotation });
                }

                Undo.RecordObject(d.tile, "Save Mirrored Neighbor");
                SetNeighborArray(d.tile, opp, candAllowed.ToArray(), blocked: false);
                SetNeighborArray(d.tile, opp, candBlocked.ToArray(), blocked: true);
                EditorUtility.SetDirty(d.tile);
                EditorUtility.SetDirty(d.tile.gameObject);

                if (PrefabUtility.IsPartOfPrefabInstance(d.tile.gameObject))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(d.tile);
                }

                if (PrefabUtility.IsPartOfPrefabAsset(d.tile.gameObject))
                {
                    PrefabUtility.SavePrefabAsset(d.tile.gameObject);
                }
            }

            Debug.Log($"TileNeighborEditor: Health check for '{targetTile.name}' {dir}: decisions={totalConsidered}, matches={matchesFound}, conflicts={conflicts}, missing={missing}");
        }

        // Make sure Unity persists changes for both prefab assets and prefab instances.
        if (PrefabUtility.IsPartOfPrefabInstance(targetTile.gameObject))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(targetTile);
        }

        EditorUtility.SetDirty(targetTile);
        EditorUtility.SetDirty(targetTile.gameObject);

        if (PrefabUtility.IsPartOfPrefabAsset(targetTile.gameObject))
        {
            PrefabUtility.SavePrefabAsset(targetTile.gameObject);

            string prefabPath = AssetDatabase.GetAssetPath(targetTile.gameObject);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                // Helps ensure the Project/Inspector reflects the updated arrays immediately.
                AssetDatabase.ImportAsset(prefabPath);
            }
        }

        AssetDatabase.SaveAssets();
    }

    private void SetState(Decision dec, TriState state)
    {
        if (dec == null)
        {
            return;
        }
        dec.state = state;
    }

    private void SetStateForTile(Tile tile, TriState state, bool onlyUnknown)
    {
        if (!decisions.TryGetValue(currentDir, out var list))
        {
            return;
        }

        foreach (var d in list)
        {
            if (d.tile != tile)
            {
                continue;
            }

            if (onlyUnknown && d.state != TriState.Unknown)
            {
                continue;
            }

            d.state = state;
        }
    }

    private void JumpToNextTileDistinct(Tile currentTile)
    {
        var visible = GetVisibleListForCurrent();
        if (visible.Count == 0)
        {
            return;
        }

        int count = visible.Count;
        for (int i = 1; i <= count; i++)
        {
            int idx = (candidateIndex + i) % count;
            if (visible[idx].tile != currentTile)
            {
                candidateIndex = idx;
                return;
            }
        }
    }

    private void JumpToPreviousTileDistinct(Tile currentTile)
    {
        var visible = GetVisibleListForCurrent();
        if (visible.Count == 0)
        {
            return;
        }

        int count = visible.Count;
        for (int i = 1; i <= count; i++)
        {
            int idx = (candidateIndex - i + count) % count;
            if (visible[idx].tile != currentTile)
            {
                candidateIndex = idx;
                return;
            }
        }
    }

    private bool JumpRotation(Tile tile, int delta)
    {
        var visible = GetVisibleListForCurrent();
        if (visible.Count == 0)
        {
            return false;
        }

        var sameTileIndices = new List<int>();
        for (int i = 0; i < visible.Count; i++)
        {
            if (visible[i].tile == tile)
            {
                sameTileIndices.Add(i);
            }
        }

        if (sameTileIndices.Count == 0)
        {
            return false;
        }

        int current = candidateIndex;
        int pos = sameTileIndices.IndexOf(current);
        if (pos < 0)
        {
            candidateIndex = sameTileIndices[0];
            return candidateIndex != current;
        }

        int nextPos = (pos + delta + sameTileIndices.Count) % sameTileIndices.Count;
        candidateIndex = sameTileIndices[nextPos];
        return candidateIndex != current;
    }

    private void AdvanceAfterDecisionGlobal(int previousGlobalIndex, bool bulkNextDistinct, Tile currentTile)
    {
        if (!decisions.TryGetValue(currentDir, out var list) || list.Count == 0)
        {
            candidateIndex = 0;
            return;
        }

        int nextGlobal;
        if (showResolved)
        {
            if (bulkNextDistinct && currentTile != null)
            {
                nextGlobal = FindNextDistinctTileIndex(list, previousGlobalIndex, currentTile);
            }
            else
            {
                // When showing resolved, advance one global step (wrap) regardless of state.
                nextGlobal = (previousGlobalIndex + 1) % list.Count;
            }
        }
        else
        {
            nextGlobal = FindNextUnresolvedIndex(list, previousGlobalIndex + 1);
            if (nextGlobal < 0)
            {
                nextGlobal = FindNextUnresolvedIndex(list, 0);
            }

            if (nextGlobal < 0)
            {
                candidateIndex = 0;
                return;
            }
        }

        var visibleIndices = GetVisibleIndices(list);
        if (visibleIndices.Count == 0)
        {
            candidateIndex = 0;
            return;
        }

        int pos = visibleIndices.IndexOf(nextGlobal);
        if (pos < 0)
        {
            // If target global is hidden, pick nearest visible after it (for unresolved mode)
            pos = 0;
            for (int i = 0; i < visibleIndices.Count; i++)
            {
                if (visibleIndices[i] >= nextGlobal)
                {
                    pos = i;
                    break;
                }
            }
        }

        candidateIndex = Mathf.Clamp(pos, 0, visibleIndices.Count - 1);
    }

    private int FindNextDistinctTileIndex(List<Decision> list, int startGlobalIndex, Tile currentTile)
    {
        int count = list.Count;
        for (int i = 1; i <= count; i++)
        {
            int idx = (startGlobalIndex + i) % count;
            if (list[idx].tile != currentTile)
            {
                return idx;
            }
        }
        return startGlobalIndex;
    }

    private int FindNextUnresolvedIndex(List<Decision> list, int startIndex)
    {
        for (int i = startIndex; i < list.Count; i++)
        {
            if (list[i].state == TriState.Unknown)
            {
                return i;
            }
        }
        return -1;
    }

    private void JumpToFirstOfNextTileDistinct(int previousVisibleIndex, Tile currentTile)
    {
        var visible = GetVisibleListForCurrent();
        if (visible.Count == 0)
        {
            candidateIndex = 0;
            return;
        }

        int start = Mathf.Clamp(previousVisibleIndex, 0, visible.Count - 1);
        int count = visible.Count;
        for (int i = 1; i <= count; i++)
        {
            int idx = (start + i) % count;
            if (visible[idx].tile != currentTile)
            {
                candidateIndex = FindFirstIndexOfTile(visible, visible[idx].tile);
                return;
            }
        }

        candidateIndex = FindFirstIndexOfTile(visible, visible[0].tile);
    }

    private int FindFirstIndexOfTile(List<Decision> list, Tile tile)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].tile == tile)
            {
                return i;
            }
        }
        return 0;
    }

    private List<Decision> GetVisibleListForCurrent()
    {
        if (!decisions.TryGetValue(currentDir, out var list) || list.Count == 0)
        {
            return new List<Decision>();
        }
        return GetVisibleList(list);
    }

    private List<Decision> GetVisibleList(List<Decision> source)
    {
        if (showResolved)
        {
            return source;
        }
        return source.Where(d => d.state == TriState.Unknown).ToList();
    }

    private List<int> GetVisibleIndices(List<Decision> source)
    {
        var result = new List<int>();
        for (int i = 0; i < source.Count; i++)
        {
            if (showResolved || source[i].state == TriState.Unknown)
            {
                result.Add(i);
            }
        }
        return result;
    }

    private void AutoResolveFromCandidates(Direction dir, List<Decision> list)
    {
        foreach (var dec in list)
        {
            if (dec.state != TriState.Unknown || dec.tile == null)
            {
                continue;
            }

            TriState mirrored = MirrorStateFromCandidate(dec.tile, dir, dec.rotation);
            if (mirrored != TriState.Unknown)
            {
                dec.state = mirrored;
            }
        }
    }

    private TriState MirrorStateFromCandidate(Tile candidate, Direction dir, int rotation)
    {
        Direction opp = Opposite(dir);

        // When candidate is rotated by `rotation`, the neighbor spec stored on the
        // candidate (which records the neighbor's rotation when the candidate is
        // unrotated) needs to be compared against the inverse of that rotation.
        // For a target tile at rotation 0 the condition is: (spec.rotation + rotation) % 4 == 0
        int neededSpecRotation = (4 - (rotation % 4)) % 4;

        Tile.NeighborSpec[] allowed = GetNeighborArray(candidate, opp, blocked: false);
        if (allowed != null)
        {
            foreach (var spec in allowed)
            {
                if (spec.tile == targetTile && spec.rotation == neededSpecRotation)
                {
                    return TriState.Allowed;
                }
            }
        }

        Tile.NeighborSpec[] blocked = GetNeighborArray(candidate, opp, blocked: true);
        if (blocked != null)
        {
            foreach (var spec in blocked)
            {
                if (spec.tile == targetTile && spec.rotation == neededSpecRotation)
                {
                    return TriState.Blocked;
                }
            }
        }

        return TriState.Unknown;
    }

    private Direction Opposite(Direction dir)
    {
        switch (dir)
        {
            case Direction.Up: return Direction.Down;
            case Direction.Down: return Direction.Up;
            case Direction.Left: return Direction.Right;
            case Direction.Right: return Direction.Left;
            default: return Direction.Up;
        }
    }

    private int GetCurrentGlobalIndex()
    {
        if (!decisions.TryGetValue(currentDir, out var list) || list.Count == 0)
        {
            return 0;
        }

        var visible = GetVisibleList(list);
        if (visible.Count == 0)
        {
            return 0;
        }

        int idx = Mathf.Clamp(candidateIndex, 0, visible.Count - 1);
        var current = visible[idx];
        int global = list.IndexOf(current);
        return Mathf.Max(global, 0);
    }

    private void RemapCandidateIndex(int previousGlobalIndex)
    {
        if (!decisions.TryGetValue(currentDir, out var list) || list.Count == 0)
        {
            candidateIndex = 0;
            return;
        }

        var visibleIndices = GetVisibleIndices(list);
        if (visibleIndices.Count == 0)
        {
            candidateIndex = 0;
            return;
        }

        int chosen = visibleIndices[0];
        foreach (int vi in visibleIndices)
        {
            if (vi >= previousGlobalIndex)
            {
                chosen = vi;
                break;
            }
        }

        int visiblePos = visibleIndices.IndexOf(chosen);
        candidateIndex = Mathf.Clamp(visiblePos, 0, visibleIndices.Count - 1);
    }

    private int CountUnresolved()
    {
        int total = 0;
        foreach (var kvp in decisions)
        {
            total += kvp.Value.Count(d => d.state == TriState.Unknown);
        }
        return total;
    }

    private void SpawnPreview(Tile candidate, int rotation)
    {
        CleanupPreview();

        if (targetTile == null || candidate == null)
        {
            return;
        }

        previewRoot = new GameObject("TileNeighborPreviewRoot");
        previewRoot.hideFlags = HideFlags.DontSave;

        GameObject targetGO = InstantiateTileGO(targetTile);
        if (targetGO == null)
        {
            Debug.LogWarning("TileNeighborEditor: Failed to instantiate target tile.");
            CleanupPreview();
            return;
        }

        GameObject candGO = InstantiateTileGO(candidate);
        if (candGO == null)
        {
            Debug.LogWarning("TileNeighborEditor: Failed to instantiate candidate tile.");
            CleanupPreview();
            return;
        }

        // Preserve original local transforms when parenting
        var tTransform = targetGO.transform;
        var cTransform = candGO.transform;

        Vector3 tPos = tTransform.localPosition;
        Quaternion tRot = tTransform.localRotation;
        Vector3 tScale = tTransform.localScale;

        Vector3 cPos = cTransform.localPosition;
        Quaternion cRot = cTransform.localRotation;
        Vector3 cScale = cTransform.localScale;

        tTransform.SetParent(previewRoot.transform, worldPositionStays: false);
        cTransform.SetParent(previewRoot.transform, worldPositionStays: false);

        tTransform.localPosition = tPos;
        tTransform.localRotation = tRot;
        tTransform.localScale = tScale;

        cTransform.localPosition = cPos;
        cTransform.localRotation = cRot;
        cTransform.localScale = cScale;

        // Now compute bounds with preserved offsets
        Vector3 dir = DirectionToVector3(currentDir);
        Bounds targetBounds = ComputeRendererBounds(targetGO);
        Bounds candBounds = ComputeRendererBounds(candGO);

        float targetHalf = AxisExtent(targetBounds, dir);
        float candHalf = AxisExtent(candBounds, dir);
        float padding = 0.05f * (targetHalf + candHalf);

        // Place candidate adjacent, preserving its own offset and applying rotation
        cTransform.localRotation = Quaternion.Euler(0f, rotation * 90f, 0f) * cRot;
        cTransform.localPosition = tTransform.localPosition + dir.normalized * (targetHalf + candHalf + padding) + cPos;

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            var renderer = previewRoot.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                sceneView.Frame(renderer.bounds, false);
            }
            sceneView.Focus();
        }
    }

    private void CleanupPreview()
    {
        if (previewRoot != null)
        {
            DestroyImmediate(previewRoot);
            previewRoot = null;
        }
    }

    private Bounds ComputeRendererBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            return new Bounds(go.transform.position, Vector3.zero);
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }
        return b;
    }

    private float AxisExtent(Bounds b, Vector3 dir)
    {
        Vector3 ad = new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z));
        return Vector3.Dot(b.extents, ad);
    }

    private GameObject InstantiateTileGO(Tile tile)
    {
        if (tile == null)
        {
            return null;
        }

        GameObject instance = null;
        var go = tile.gameObject;

        if (PrefabUtility.IsPartOfPrefabAsset(go))
        {
            instance = PrefabUtility.InstantiatePrefab(go) as GameObject;
        }
        else
        {
            instance = Object.Instantiate(go);
        }

        if (instance != null)
        {
            instance.hideFlags = HideFlags.DontSave;
        }

        return instance;
    }

    private Vector2 DirectionToOffset(Direction dir)
    {
        switch (dir)
        {
            case Direction.Up: return new Vector2(0f, -1f);
            case Direction.Right: return new Vector2(1f, 0f);
            case Direction.Down: return new Vector2(0f, 1f);
            case Direction.Left: return new Vector2(-1f, 0f);
            default: return Vector2.zero;
        }
    }

    private Vector3 DirectionToVector3(Direction dir)
    {
        switch (dir)
        {
            case Direction.Up: return Vector3.forward;
            case Direction.Right: return Vector3.right;
            case Direction.Down: return Vector3.back;
            case Direction.Left: return Vector3.left;
            default: return Vector3.forward;
        }
    }

    private Texture2D ResolvePreview(Tile tile)
    {
        if (tile == null)
        {
            return null;
        }

        if (tile.preview != null)
        {
            return tile.preview;
        }

        Texture2D tex = AssetPreview.GetAssetPreview(tile.gameObject);
        if (tex != null)
        {
            return tex;
        }

        return AssetPreview.GetMiniThumbnail(tile.gameObject);
    }
}
#endif
