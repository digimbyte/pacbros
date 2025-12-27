#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class TileAdjacencyAtlasEditorWindow : EditorWindow
{
    private const string DefaultAtlasFolder = "Assets/Game/RoomCreation/Atlases";
    private const string DefaultManualLevelPrefabFolder = "Assets/Game/Prefabs/ManualLevels";

    private enum Dir { Up, Right, Down, Left }
    private enum PaintMode { Tile, Placeable }

    private TileAdjacencyAtlas atlas;
    private string atlasPath;

    private Vector2 scrollPalette;
    private Vector2 scrollGrid;

    private readonly Dictionary<Tile.TileType, List<Tile>> tilesByType = new Dictionary<Tile.TileType, List<Tile>>();
    private readonly Dictionary<Tile.TileType, bool> typeFoldouts = new Dictionary<Tile.TileType, bool>();

    private Tile paintTile;
    private int paintRotation;

    // Rotated preview cache (generated in-memory textures).
    // Keyed by the source Texture2D instance id.
    private readonly Dictionary<int, Texture2D[]> rotatedPreviewCache = new Dictionary<int, Texture2D[]>();

    private const float PreviewInset = 2f;

    private PaintMode paintMode;
    private GameObject paintPlaceable;
    private int paintPlaceableRotation;
    private string paintPlaceableKind = TileAdjacencyAtlas.PlaceableKind.SpawnPlayer;
    private string paintPlaceableMarker;
    private Color paintPlaceableColor = Color.white;

    private bool applyToAllTiles = true; // atlas is source of truth
    private bool showBakeReport;
    private string lastBakeReport;

    // Layout (resizable panes)
    private const float ToolbarHeight = 22f;
    private const float SplitterThickness = 4f;
    private const float CellSize = 72f;
    private const float PanePadding = 6f;

    private float leftPaneWidth = 360f;
    private float bottomPaneHeight = 220f;

    private bool draggingVSplit;
    private bool draggingHSplit;

    // Header / bottom scrolling so controls never get cropped in short windows.
    private Vector2 scrollHeader;
    private Vector2 scrollBottom;

    private int extendAmount = 1;

    private struct MarkerDef
    {
        public string kind;
        public string label;
        public Color color;
    }

        private static readonly MarkerDef[] MarkerDefs = new[]
        {
        new MarkerDef { kind = TileAdjacencyAtlas.PlaceableKind.None, label = "", color = Color.white },
        new MarkerDef { kind = TileAdjacencyAtlas.PlaceableKind.SpawnPlayer, label = "SP", color = new Color(0.15f, 0.85f, 0.15f) }, // Green
        new MarkerDef { kind = TileAdjacencyAtlas.PlaceableKind.Enemy, label = "E", color = Color.yellow },                         // Yellow
        new MarkerDef { kind = TileAdjacencyAtlas.PlaceableKind.Loot, label = "I", color = Color.cyan },
        new MarkerDef { kind = TileAdjacencyAtlas.PlaceableKind.Coin, label = "c", color = new Color(1f, 0.86f, 0.25f) },
        new MarkerDef { kind = TileAdjacencyAtlas.PlaceableKind.Gun, label = "G", color = new Color(0.6f, 0.8f, 1f) }
    };

        private static readonly string[] PlaceableKindOptions = new[]
        {
            TileAdjacencyAtlas.PlaceableKind.SpawnPlayer,
            TileAdjacencyAtlas.PlaceableKind.Enemy,
            TileAdjacencyAtlas.PlaceableKind.Loot,
            TileAdjacencyAtlas.PlaceableKind.Coin,
            TileAdjacencyAtlas.PlaceableKind.Gun
        };

    private static MarkerDef ResolveMarkerDef(string kind)
    {
        for (int i = 0; i < MarkerDefs.Length; i++)
        {
            if (MarkerDefs[i].kind == kind) return MarkerDefs[i];
        }
        return new MarkerDef { kind = kind, label = "", color = Color.white };
    }


    private void SetPaintMarker(string kind)
    {
        paintPlaceableKind = kind;
        var def = ResolveMarkerDef(kind);
        paintPlaceableMarker = def.label;
        paintPlaceableColor = def.color;
        // avoid carrying over prefab reference when switching markers (no prefab-kind exists)
        paintPlaceable = null;
    }

    private static bool HasPlaceable(TileAdjacencyAtlas.PlaceableCell p)
    {
        return p.prefab != null || !string.Equals(p.kind, TileAdjacencyAtlas.PlaceableKind.None, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(p.marker);
    }

    [MenuItem("PacBros/Rooms/Tile Adjacency Atlas Editor")]
    public static void Open()
    {
        GetWindow<TileAdjacencyAtlasEditorWindow>(false, "Tile Adjacency Atlas");
    }

    private void OnEnable()
    {
        RefreshTiles();
        SetPaintMarker(TileAdjacencyAtlas.PlaceableKind.SpawnPlayer);
    }

    private void OnDisable()
    {
        // Avoid leaking generated textures.
        foreach (var kv in rotatedPreviewCache)
        {
            var arr = kv.Value;
            if (arr == null) continue;
            for (int i = 1; i < arr.Length; i++)
            {
                if (arr[i] != null)
                {
                    DestroyImmediate(arr[i]);
                    arr[i] = null;
                }
            }
        }
        rotatedPreviewCache.Clear();
    }

    private void OnGUI()
    {
        // Toolbar
        Rect toolbarRect = new Rect(0f, 0f, position.width, ToolbarHeight);
        GUILayout.BeginArea(toolbarRect);
        DrawToolbar();
        GUILayout.EndArea();

        Rect contentRect = new Rect(0f, toolbarRect.yMax, position.width, Mathf.Max(0f, position.height - ToolbarHeight));

        // Vertical split: palette (left) vs atlas (right)
        float minLeft = 220f;
        float maxLeft = Mathf.Max(minLeft, position.width - 260f);
        leftPaneWidth = Mathf.Clamp(leftPaneWidth, minLeft, maxLeft);

        Rect leftRect = new Rect(contentRect.x, contentRect.y, leftPaneWidth, contentRect.height);
        Rect vSplitRect = new Rect(leftRect.xMax, contentRect.y, SplitterThickness, contentRect.height);
        Rect rightRect = new Rect(vSplitRect.xMax, contentRect.y, Mathf.Max(0f, contentRect.width - leftRect.width - SplitterThickness), contentRect.height);

        // Right side: header + grid + bake/report with horizontal split.
        // Header gets a fixed target height, but can scroll internally so its controls never get cut off.
        float headerHeight = atlas == null ? 56f : 160f;
        headerHeight = Mathf.Min(headerHeight, Mathf.Max(40f, rightRect.height * 0.35f));
        Rect headerRect = new Rect(rightRect.x, rightRect.y, rightRect.width, headerHeight);

        float minBottom = 100f; // a bit smaller to free space for the grid
        float maxBottom = Mathf.Max(minBottom, rightRect.height - headerRect.height - 60f);
        bottomPaneHeight = Mathf.Clamp(bottomPaneHeight, minBottom, maxBottom);

        Rect hSplitRect = new Rect(rightRect.x, rightRect.yMax - bottomPaneHeight - SplitterThickness, rightRect.width, SplitterThickness);

        // Add a bit of breathing room between panes so content doesn't feel glued together.
        float gridTop = headerRect.yMax + PanePadding;
        float gridBottom = Mathf.Max(gridTop, hSplitRect.y - PanePadding);
        Rect gridRect = new Rect(rightRect.x, gridTop, rightRect.width, Mathf.Max(0f, gridBottom - gridTop));

        float bottomTop = hSplitRect.yMax + PanePadding;
        Rect bottomRect = new Rect(rightRect.x, bottomTop, rightRect.width, Mathf.Max(0f, rightRect.yMax - bottomTop));

        // Draw panes
        GUILayout.BeginArea(leftRect);
        DrawPalette();
        GUILayout.EndArea();

        GUILayout.BeginArea(headerRect);
        scrollHeader = EditorGUILayout.BeginScrollView(scrollHeader, GUILayout.ExpandHeight(true));
        DrawAtlasHeader();
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();

        GUILayout.BeginArea(gridRect);
        DrawGrid();
        GUILayout.EndArea();

        GUILayout.BeginArea(bottomRect);
        scrollBottom = EditorGUILayout.BeginScrollView(scrollBottom, GUILayout.ExpandHeight(true));
        DrawBakeSection();
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();

        // Splitters
        EditorGUIUtility.AddCursorRect(vSplitRect, MouseCursor.ResizeHorizontal);
        EditorGUI.DrawRect(vSplitRect, new Color(0f, 0f, 0f, 0.22f));

        EditorGUIUtility.AddCursorRect(hSplitRect, MouseCursor.ResizeVertical);
        EditorGUI.DrawRect(hSplitRect, new Color(0f, 0f, 0f, 0.22f));

        // Handle drags
        HandleLayoutDragging(vSplitRect, hSplitRect, rightRect, headerRect);

        // Hotkeys
        HandleHotkeys();
    }

    private void HandleHotkeys()
    {
        Event ev = Event.current;
        if (ev == null) return;

        // Don't steal keys while typing in text fields.
        if (EditorGUIUtility.editingTextField) return;

        if (ev.type == EventType.KeyDown)
        {
            // R rotates the current paint rotation by +90°.
            if (ev.keyCode == KeyCode.R)
            {
                if (paintMode == PaintMode.Tile)
                    paintRotation = (paintRotation + 1) % 4;
                else
                    paintPlaceableRotation = (paintPlaceableRotation + 1) % 4;

                ev.Use();
                Repaint();
            }
        }
    }

    private void HandleLayoutDragging(Rect vSplitRect, Rect hSplitRect, Rect rightRect, Rect headerRect)
    {
        Event ev = Event.current;
        if (ev == null) return;

        // Begin drags
        if (ev.type == EventType.MouseDown && ev.button == 0)
        {
            if (vSplitRect.Contains(ev.mousePosition))
            {
                draggingVSplit = true;
                ev.Use();
                return;
            }

            if (hSplitRect.Contains(ev.mousePosition))
            {
                draggingHSplit = true;
                ev.Use();
                return;
            }
        }

        // Dragging
        if (ev.type == EventType.MouseDrag && ev.button == 0)
        {
            if (draggingVSplit)
            {
                leftPaneWidth = Mathf.Clamp(ev.mousePosition.x, 220f, Mathf.Max(220f, position.width - 260f));
                ev.Use();
                Repaint();
                return;
            }

            if (draggingHSplit)
            {
                // bottomPaneHeight is measured from the bottom of rightRect
                float bottomFromMouse = (rightRect.yMax - ev.mousePosition.y);
                float maxBottom = Mathf.Max(120f, rightRect.height - headerRect.height - 60f);
                bottomPaneHeight = Mathf.Clamp(bottomFromMouse, 120f, maxBottom);
                ev.Use();
                Repaint();
                return;
            }
        }

        // End drags
        if (ev.type == EventType.MouseUp)
        {
            if (draggingVSplit || draggingHSplit)
            {
                draggingVSplit = false;
                draggingHSplit = false;
                ev.Use();
            }
        }
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("New", EditorStyles.toolbarButton))
        {
            CreateNewAtlas();
        }

        if (GUILayout.Button("Load", EditorStyles.toolbarButton))
        {
            LoadAtlasFromFile();
        }

        GUI.enabled = atlas != null;
        if (GUILayout.Button("Save", EditorStyles.toolbarButton))
        {
            SaveAtlas();
        }
        GUI.enabled = true;

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Refresh Tiles", EditorStyles.toolbarButton))
        {
            RefreshTiles();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawAtlasHeader()
    {
        if (atlas == null)
        {
            EditorGUILayout.HelpBox("No atlas loaded. Create or load one.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();
        atlas.name = EditorGUILayout.TextField("Atlas Name", atlas.name);
        EditorGUILayout.LabelField($"Size: {atlas.width} x {atlas.height}", EditorStyles.boldLabel);

        EditorGUILayout.Space();
        // Extend / crop tools are tucked behind a foldout so they don't permanently eat vertical space.
        showExtendTools = EditorGUILayout.Foldout(showExtendTools, "Extend / Crop Atlas", true);
        if (showExtendTools)
        {
            EditorGUI.indentLevel++;
            extendAmount = Mathf.Max(1, EditorGUILayout.IntField("Blocks to Add", extendAmount));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button($"← West (+{extendAmount})"))
            {
                atlas.ExtendWest(extendAmount);
                MarkDirty();
            }
            if (GUILayout.Button($"East (+{extendAmount}) →"))
            {
                atlas.ExtendEast(extendAmount);
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button($"↓ South (+{extendAmount})"))
            {
                atlas.ExtendSouth(extendAmount);
                MarkDirty();
            }
            if (GUILayout.Button($"North (+{extendAmount}) ↑"))
            {
                atlas.ExtendNorth(extendAmount);
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (GUILayout.Button("Crop Atlas to Tiles"))
            {
                atlas.Crop();
                MarkDirty();
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        paintMode = (PaintMode)EditorGUILayout.EnumPopup("Paint Mode", paintMode);

        // Paint tools can also be collapsed when you just want to inspect the grid.
        showPaintTools = EditorGUILayout.Foldout(showPaintTools, "Paint Options", true);
        if (!showPaintTools)
            return;

        EditorGUI.indentLevel++;
        if (paintMode == PaintMode.Tile)
        {
            paintRotation = EditorGUILayout.IntSlider("Paint Rotation (90° steps)", paintRotation, 0, 3);
        }
        else
        {
            int curIdx = Array.IndexOf(PlaceableKindOptions, paintPlaceableKind);
            if (curIdx < 0) curIdx = 0;
            int sel = EditorGUILayout.Popup("Placeable Kind", curIdx, PlaceableKindOptions);
            var newKind = PlaceableKindOptions[Mathf.Clamp(sel, 0, PlaceableKindOptions.Length - 1)];
            if (newKind != paintPlaceableKind)
            {
                SetPaintMarker(newKind);
            }

            EditorGUILayout.BeginHorizontal();
            foreach (var def in MarkerDefs)
            {
                var prev = GUI.color;
                GUI.color = def.color;
                if (GUILayout.Button(def.label, GUILayout.Width(36), GUILayout.Height(22)))
                {
                    SetPaintMarker(def.kind);
                }
                GUI.color = prev;
            }
            EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("Marker", paintPlaceableMarker ?? "-", EditorStyles.boldLabel);
                Rect swatch = GUILayoutUtility.GetRect(48, 16, GUILayout.Width(48), GUILayout.Height(16));
                EditorGUI.DrawRect(swatch, paintPlaceableColor);

            paintPlaceableRotation = EditorGUILayout.IntSlider("Paint Rotation (90° steps)", paintPlaceableRotation, 0, 3);
        }
        EditorGUI.indentLevel--;
    }

    private void DrawPalette()
    {
        EditorGUILayout.LabelField("Palette (Grouped by TileType)", EditorStyles.boldLabel);

        int totalTiles = tilesByType.Values.Sum(v => v != null ? v.Count : 0);
        if (totalTiles == 0)
        {
            EditorGUILayout.HelpBox("No Tile prefabs found.", MessageType.Warning);
            return;
        }

        scrollPalette = EditorGUILayout.BeginScrollView(scrollPalette, GUILayout.ExpandHeight(true));

        foreach (Tile.TileType type in Enum.GetValues(typeof(Tile.TileType)))
        {
            if (!tilesByType.TryGetValue(type, out var list) || list == null || list.Count == 0)
                continue;

            if (!typeFoldouts.ContainsKey(type))
                typeFoldouts[type] = true;

            typeFoldouts[type] = EditorGUILayout.Foldout(typeFoldouts[type], $"{type} ({list.Count})", true);
            if (!typeFoldouts[type])
                continue;

            const int columns = 6;
            const float thumbSize = 72f;

            int col = 0;
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < list.Count; i++)
            {
                var tile = list[i];
                if (tile == null) continue;

                Rect btnRect = GUILayoutUtility.GetRect(thumbSize, thumbSize, GUILayout.Width(thumbSize), GUILayout.Height(thumbSize));
                if (GUI.Button(btnRect, GUIContent.none))
                {
                    paintTile = tile;
                }

                Texture2D tex = ResolvePreview(tile);
                if (tex != null)
                {
                    DrawRotatedPreviewClipped(btnRect, tex, TileAdjacencyAtlas.NormalizeRot(paintRotation));
                }
                else
                {
                    GUI.Label(btnRect, tile.name, EditorStyles.centeredGreyMiniLabel);
                }

                if (paintTile == tile)
                {
                    var prev = GUI.color;
                    GUI.color = Color.yellow;
                    GUI.DrawTexture(new Rect(btnRect.x + 2, btnRect.y + 2, 4, btnRect.height - 4), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(btnRect.xMax - 6, btnRect.y + 2, 4, btnRect.height - 4), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(btnRect.x + 2, btnRect.y + 2, btnRect.width - 4, 4), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(btnRect.x + 2, btnRect.yMax - 6, btnRect.width - 4, 4), Texture2D.whiteTexture);
                    GUI.color = prev;
                }

                col++;
                if (col >= columns)
                {
                    col = 0;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawGrid()
    {
        if (atlas == null)
            return;

        EditorGUILayout.LabelField("Atlas Grid", EditorStyles.boldLabel);
        scrollGrid = EditorGUILayout.BeginScrollView(scrollGrid, GUILayout.ExpandHeight(true));
        for (int y = atlas.height - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < atlas.width; x++)
            {
                var cell = atlas.GetCell(x, y);
                var placeable = atlas.GetPlaceable(x, y);

                Rect rect = GUILayoutUtility.GetRect(CellSize, CellSize, GUILayout.Width(CellSize), GUILayout.Height(CellSize));
                Rect corner = new Rect(rect.xMax - 18f, rect.y + 2f, 16f, 16f);
                Rect mainRect = new Rect(rect.x, rect.y, rect.width - (corner.width + 4f), rect.height);

                Event ev = Event.current;

                // Middle-click: eyedropper.
                if (ev.type == EventType.MouseDown && ev.button == 2 && rect.Contains(ev.mousePosition))
                {
                    if (paintMode == PaintMode.Tile)
                    {
                        if (cell.tile != null)
                        {
                            paintTile = cell.tile;
                            paintRotation = TileAdjacencyAtlas.NormalizeRot(cell.rotationIndex);
                        }
                    }
                    else
                    {
                        if (HasPlaceable(placeable))
                        {
                            paintPlaceable = null;
                            paintPlaceableKind = !string.Equals(placeable.kind, TileAdjacencyAtlas.PlaceableKind.None, StringComparison.OrdinalIgnoreCase)
                                ? placeable.kind
                                : TileAdjacencyAtlas.PlaceableKind.None;

                            paintPlaceableMarker = !string.IsNullOrEmpty(placeable.marker)
                                ? placeable.marker
                                : ResolveMarkerDef(paintPlaceableKind).label;

                            paintPlaceableColor = placeable.markerColor.a > 0.001f
                                ? placeable.markerColor
                                : ResolveMarkerDef(paintPlaceableKind).color;

                            paintPlaceableRotation = TileAdjacencyAtlas.NormalizeRot(placeable.rotationIndex);
                        }
                    }
                    Repaint();
                    ev.Use();
                }
                // Right-click: clear based on mode (Ctrl+Right clears both).
                else if (ev.type == EventType.MouseDown && ev.button == 1 && rect.Contains(ev.mousePosition))
                {
                    bool clearBoth = ev.control;
                    if (paintMode == PaintMode.Tile || clearBoth)
                        atlas.SetCell(x, y, null, 0);
                    if (paintMode == PaintMode.Placeable || clearBoth)
                        atlas.SetPlaceable(x, y, null, 0);

                    MarkDirty();
                    ev.Use();
                }
                else
                {
                    if (GUI.Button(mainRect, GUIContent.none))
                    {
                        if (paintMode == PaintMode.Tile)
                        {
                            if (paintTile == null)
                                atlas.SetCell(x, y, null, 0);
                            else
                                atlas.SetCell(x, y, paintTile, paintRotation);
                        }
                        else
                        {
                            // Only allow placeables on existing tile cells.
                            if (cell.tile != null)
                            {
                                var def = ResolveMarkerDef(paintPlaceableKind);
                                atlas.SetPlaceable(
                                    x,
                                    y,
                                    null,
                                    paintPlaceableRotation,
                                    paintPlaceableKind,
                                    def.kind == TileAdjacencyAtlas.PlaceableKind.None ? null : def.label,
                                    def.color);
                            }
                        }
                        MarkDirty();
                    }
                }

                GUI.Box(rect, GUIContent.none);

                if (cell.tile != null)
                {
                    Texture2D tex = ResolvePreview(cell.tile);
                    if (tex != null)
                    {
                        DrawRotatedPreviewClipped(mainRect, tex, TileAdjacencyAtlas.NormalizeRot(cell.rotationIndex));
                    }

                    GUIStyle cornerStyle = new GUIStyle(GUI.skin.button)
                    {
                        fontSize = 10,
                        alignment = TextAnchor.MiddleCenter,
                        fixedHeight = corner.height,
                        fixedWidth = corner.width
                    };

                    string rotLabel = (TileAdjacencyAtlas.NormalizeRot(cell.rotationIndex) + 1).ToString();
                    if (GUI.Button(corner, rotLabel, cornerStyle))
                    {
                        int newRot = (cell.rotationIndex + 1) % 4;
                        atlas.SetCell(x, y, cell.tile, newRot);
                        MarkDirty();
                    }
                }

                // Overlay marker for placeable (draw AFTER tile preview so it's on top)
                if (HasPlaceable(placeable))
                {
                    var def = ResolveMarkerDef(placeable.kind);
                    string markerLabel = !string.IsNullOrEmpty(placeable.marker) ? placeable.marker : def.label;
                    Color col = placeable.markerColor.a > 0.001f ? placeable.markerColor : def.color;

                    var markerStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 16,
                        normal = { textColor = col }
                    };

                    GUI.Label(mainRect, markerLabel, markerStyle);
                }

                string cellLabel = cell.tile != null ? cell.tile.name : "-";
                if (cell.tile != null && cell.rotationIndex != 0)
                    cellLabel += $" R{cell.rotationIndex}";
                if (HasPlaceable(placeable))
                    cellLabel += " +";
                GUI.Label(rect, cellLabel, EditorStyles.whiteMiniLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private bool showPlaceables;

    private Vector2 scrollPlaceables;

    // Foldouts to keep header more compact; some rarely-used tools are hidden behind these.
    private bool showExtendTools;
    private bool showPaintTools = true;

    private void DrawPlaceablesList()
    {
        if (atlas == null) return;

        int count = atlas.placeables != null ? atlas.placeables.Count : 0;
        showPlaceables = EditorGUILayout.Foldout(showPlaceables, $"Placeables ({count})", true);
        if (!showPlaceables) return;

        if (count == 0)
        {
            EditorGUILayout.HelpBox("No placeables placed.", MessageType.None);
            return;
        }

        // Limit height to avoid pushing content off screen.
        scrollPlaceables = EditorGUILayout.BeginScrollView(scrollPlaceables, GUILayout.MaxHeight(150));
        for (int i = 0; i < atlas.placeables.Count; i++)
        {
            var p = atlas.placeables[i];
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"({p.x},{p.y})", GUILayout.Width(60));
            EditorGUILayout.LabelField(p.marker ?? "-", GUILayout.Width(60));

            Rect swatch = GUILayoutUtility.GetRect(18, 14, GUILayout.Width(18), GUILayout.Height(14));
            EditorGUI.DrawRect(swatch, p.markerColor.a > 0.001f ? p.markerColor : ResolveMarkerDef(p.kind).color);

            EditorGUILayout.LabelField($"R{TileAdjacencyAtlas.NormalizeRot(p.rotationIndex)}", GUILayout.Width(28));
            if (GUILayout.Button("X", GUILayout.Width(24)))
            {
                atlas.SetPlaceable(p.x, p.y, null, 0);
                MarkDirty();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawBakeSection()
    {
        EditorGUILayout.Space(6);
        DrawPlaceablesList();
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Bake (REPLACE)", EditorStyles.boldLabel);

        if (atlas == null)
        {
            EditorGUILayout.HelpBox("Load or create an atlas to bake adjacency rules.", MessageType.Info);
            return;
        }

        applyToAllTiles = EditorGUILayout.ToggleLeft("Apply to ALL tiles (tiles not in atlas become invalid)", applyToAllTiles);

        if (GUILayout.Button("Bake Atlas → Tile Neighbor Arrays (REPLACE)", GUILayout.Height(28)))
        {
            BakeReplace();
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Manual Level", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("This instantiates the atlas grid into the current scene as a manual layout.", MessageType.None);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Spawn Atlas To Scene"))
        {
            SpawnAtlasToScene();
        }
        if (GUILayout.Button("Save Prefab From Atlas"))
        {
            SavePrefabFromAtlas();
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(lastBakeReport))
        {
            showBakeReport = EditorGUILayout.Foldout(showBakeReport, "Last Bake Report", true);
            if (showBakeReport)
            {
                EditorGUILayout.TextArea(lastBakeReport, GUILayout.MinHeight(120));
            }
        }
    }

    private void RefreshTiles()
    {
        tilesByType.Clear();

        foreach (Tile.TileType type in Enum.GetValues(typeof(Tile.TileType)))
        {
            tilesByType[type] = new List<Tile>();
        }

        // Find all GameObject assets that have a Tile component on the ROOT.
        // If a prefab has Tile only on children, it is considered invalid and will be skipped.
        string[] guids = AssetDatabase.FindAssets("t:GameObject");
        int skippedChildOnly = 0;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;

            var rootTile = go.GetComponent<Tile>();
            if (rootTile != null)
            {
                tilesByType[rootTile.tileType].Add(rootTile);
                continue;
            }

            // Diagnostic: tile exists but is not on root.
            var childTiles = go.GetComponentsInChildren<Tile>(true);
            if (childTiles != null && childTiles.Length > 0)
            {
                skippedChildOnly++;
                Debug.LogWarning($"TileAdjacencyAtlasEditor: Prefab '{path}' has Tile component on a child object, not the root. It will not appear in the palette.");
            }
        }

        if (skippedChildOnly > 0)
        {
            Debug.LogWarning($"TileAdjacencyAtlasEditor: Skipped {skippedChildOnly} prefab(s) because Tile was not on the prefab root.");
        }

        foreach (Tile.TileType type in Enum.GetValues(typeof(Tile.TileType)))
        {
            if (!tilesByType.TryGetValue(type, out var list) || list == null)
                continue;

            list.RemoveAll(t => t == null);
            list = list.Distinct().ToList();
            list.Sort((a, b) => string.Compare(a != null ? a.name : "", b != null ? b.name : "", StringComparison.Ordinal));
            tilesByType[type] = list;
        }
    }

    private void CreateNewAtlas()
    {
        atlas = CreateInstance<TileAdjacencyAtlas>();
        atlas.name = "TileAdjacencyAtlas_New";
        atlas.Resize(6, 6);
        atlasPath = null;
        paintTile = null;
        paintRotation = 0;
        lastBakeReport = null;
    }

    private void LoadAtlasFromFile()
    {
        string path = EditorUtility.OpenFilePanel("Load Tile Adjacency Atlas", "Assets", "asset");
        if (string.IsNullOrEmpty(path))
            return;

        string projectPath = FileUtil.GetProjectRelativePath(path);
        var loaded = AssetDatabase.LoadAssetAtPath<TileAdjacencyAtlas>(projectPath);
        if (loaded != null)
        {
            atlas = loaded;
            atlasPath = projectPath;
            lastBakeReport = null;
            // Sanitize loaded atlas to remove legacy '*' markers and explicit None entries.
            if (atlas.placeables != null)
            {
                atlas.placeables.RemoveAll(p => string.Equals(p.kind, TileAdjacencyAtlas.PlaceableKind.None, StringComparison.OrdinalIgnoreCase));
                for (int i = 0; i < atlas.placeables.Count; i++)
                {
                    var p = atlas.placeables[i];
                    if (!string.IsNullOrEmpty(p.marker) && p.marker.Trim() == "*")
                    {
                        p.marker = null;
                        atlas.placeables[i] = p;
                    }
                }
            }
        }
    }

    private void SaveAtlas()
    {
        if (atlas == null)
            return;

        if (string.IsNullOrEmpty(atlasPath))
        {
            EnsureFolder(DefaultAtlasFolder);
            atlasPath = EditorUtility.SaveFilePanelInProject(
                "Save Tile Adjacency Atlas",
                atlas.name,
                "asset",
                "Choose location for TileAdjacencyAtlas asset",
                DefaultAtlasFolder);

            if (string.IsNullOrEmpty(atlasPath))
                return;

            AssetDatabase.CreateAsset(atlas, atlasPath);
        }

        // Sanitize placeables: remove any explicit None entries before saving (None is used as a transient 'clear' value).
        if (atlas.placeables != null)
        {
                atlas.placeables.RemoveAll(p => string.Equals(p.kind, TileAdjacencyAtlas.PlaceableKind.None, StringComparison.OrdinalIgnoreCase));
        }

        EditorUtility.SetDirty(atlas);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private void MarkDirty()
    {
        if (atlas != null)
            EditorUtility.SetDirty(atlas);
    }

    private void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder);
        string leaf = Path.GetFileName(folder);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent.Replace("\\", "/"));
        }
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private Texture2D ResolvePreview(Tile tile)
    {
        if (tile == null) return null;
        if (tile.preview != null) return tile.preview;

        Texture2D tex = AssetPreview.GetAssetPreview(tile.gameObject);
        if (tex != null) return tex;
        return AssetPreview.GetMiniThumbnail(tile.gameObject);
    }

    private void SpawnAtlasToScene()
    {
        if (atlas == null)
            return;

        var root = BuildSceneRootFromAtlas();
        if (root != null)
        {
            Selection.activeObject = root;
            EditorGUIUtility.PingObject(root);
        }
    }

    private void SavePrefabFromAtlas()
    {
        if (atlas == null)
            return;

        EnsureFolder(DefaultManualLevelPrefabFolder);

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Manual Level Prefab",
            atlas.name + "_ManualLevel",
            "prefab",
            "Choose location for manual level prefab",
            DefaultManualLevelPrefabFolder);

        if (string.IsNullOrEmpty(path))
            return;

        GameObject root = BuildSceneRootFromAtlas();
        if (root == null)
            return;

        PrefabUtility.SaveAsPrefabAsset(root, path);
        DestroyImmediate(root);
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
    }

    private GameObject BuildSceneRootFromAtlas()
    {
        // Root container
        string rootName = string.IsNullOrEmpty(atlas.name) ? "TileAdjacencyAtlas_ManualLevel" : atlas.name + "_ManualLevel";
        GameObject root = new GameObject(rootName);
        Undo.RegisterCreatedObjectUndo(root, "Create Manual Level From Atlas");

        if (atlas.cells == null || atlas.cells.Count == 0)
        {
            return root;
        }

        // Instantiate each placed tile.
        for (int i = 0; i < atlas.cells.Count; i++)
        {
            var c = atlas.cells[i];
            if (c.tile == null)
                continue;

            // Keep prefab connection when possible.
            GameObject instance = null;
            if (PrefabUtility.IsPartOfPrefabAsset(c.tile.gameObject))
            {
                instance = PrefabUtility.InstantiatePrefab(c.tile.gameObject) as GameObject;
            }
            else
            {
                instance = Instantiate(c.tile.gameObject);
            }

            if (instance == null)
                continue;

            Undo.RegisterCreatedObjectUndo(instance, "Place Atlas Tile");

            Transform prefabT = c.tile.transform;
            Vector3 prefabLocalPos = prefabT.localPosition;
            Quaternion prefabLocalRot = prefabT.localRotation;
            Vector3 prefabLocalScale = prefabT.localScale;

            Transform t = instance.transform;
            t.SetParent(root.transform, worldPositionStays: false);
            t.localPosition = new Vector3(c.x, 0f, c.y) + prefabLocalPos;
            t.localRotation = Quaternion.Euler(0f, TileAdjacencyAtlas.NormalizeRot(c.rotationIndex) * 90f, 0f) * prefabLocalRot;
            t.localScale = prefabLocalScale;
        }

        // Instantiate placeables (items/spawns/etc)
        if (atlas.placeables != null && atlas.placeables.Count > 0)
        {
            GameObject pr = new GameObject("__Placeables");
            Undo.RegisterCreatedObjectUndo(pr, "Create Placeables Root");
            pr.transform.SetParent(root.transform, worldPositionStays: false);

            for (int i = 0; i < atlas.placeables.Count; i++)
            {
                var p = atlas.placeables[i];
                if (p.prefab == null) continue;

                // Keep prefab connection when possible.
                GameObject inst = null;
                if (PrefabUtility.IsPartOfPrefabAsset(p.prefab))
                    inst = PrefabUtility.InstantiatePrefab(p.prefab) as GameObject;
                else
                    inst = Instantiate(p.prefab);

                if (inst == null) continue;

                Undo.RegisterCreatedObjectUndo(inst, "Place Atlas Placeable");

                Transform prefabT = p.prefab.transform;
                Vector3 prefabLocalPos = prefabT.localPosition;
                Quaternion prefabLocalRot = prefabT.localRotation;
                Vector3 prefabLocalScale = prefabT.localScale;

                Transform t = inst.transform;
                t.SetParent(pr.transform, worldPositionStays: false);
                t.localPosition = new Vector3(p.x, 0f, p.y) + prefabLocalPos;
                t.localRotation = Quaternion.Euler(0f, TileAdjacencyAtlas.NormalizeRot(p.rotationIndex) * 90f, 0f) * prefabLocalRot;
                t.localScale = prefabLocalScale;
            }
        }

        return root;
    }

    // ----------------- bake -----------------

    private struct SpecKey : IEquatable<SpecKey>
    {
        public Tile tile;
        public int rot;

        public bool Equals(SpecKey other)
        {
            return tile == other.tile && rot == other.rot;
        }

        public override bool Equals(object obj)
        {
            return obj is SpecKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((tile != null ? tile.GetHashCode() : 0) * 397) ^ rot;
            }
        }
    }

    private void BakeReplace()
    {
        // Build a fast cell map.
        var cellMap = new Dictionary<Vector2Int, TileAdjacencyAtlas.Cell>();
        if (atlas.cells != null)
        {
            for (int i = 0; i < atlas.cells.Count; i++)
            {
                var c = atlas.cells[i];
                if (c.tile == null) continue;
                cellMap[new Vector2Int(c.x, c.y)] = c;
            }
        }

        // Decide scope.
        List<Tile> scopeTiles;
        if (applyToAllTiles)
        {
            // Atlas is the source of truth; include every discovered tile AND any tiles referenced by the atlas.
            scopeTiles = tilesByType.Values.SelectMany(v => v)
                .Concat(cellMap.Values.Select(v => v.tile))
                .Where(t => t != null)
                .Distinct()
                .ToList();
        }
        else
        {
            scopeTiles = cellMap.Values.Select(v => v.tile).Where(t => t != null).Distinct().ToList();
        }

        if (scopeTiles.Count == 0)
        {
            EditorUtility.DisplayDialog("Bake Atlas", "No tiles found in bake scope.", "OK");
            return;
        }

        // Allocate per-tile adjacency sets for each base direction.
        var allowed = new Dictionary<Tile, Dictionary<Dir, HashSet<SpecKey>>>();
        for (int i = 0; i < scopeTiles.Count; i++)
        {
            var t = scopeTiles[i];
            allowed[t] = new Dictionary<Dir, HashSet<SpecKey>>
            {
                { Dir.Up, new HashSet<SpecKey>() },
                { Dir.Right, new HashSet<SpecKey>() },
                { Dir.Down, new HashSet<SpecKey>() },
                { Dir.Left, new HashSet<SpecKey>() }
            };
        }

        // Add adjacency from atlas (process only Up+Right, and always write reciprocal).
        foreach (var kv in cellMap)
        {
            var pos = kv.Key;
            var a = kv.Value;

            // Up
            var upPos = pos + new Vector2Int(0, 1);
            if (cellMap.TryGetValue(upPos, out var bUp))
            {
                AddPair(allowed, a, bUp, Dir.Up);
            }

            // Right
            var rPos = pos + new Vector2Int(1, 0);
            if (cellMap.TryGetValue(rPos, out var bRight))
            {
                AddPair(allowed, a, bRight, Dir.Right);
            }
        }

        // Coverage report & warnings.
        var warnings = new List<string>();
        var report = new System.Text.StringBuilder();
        report.AppendLine($"Atlas '{atlas.name}': baked scope tiles = {scopeTiles.Count}");
        report.AppendLine($"Atlas filled cells = {cellMap.Count}");
        report.AppendLine($"Mode: REPLACE (tiles or adjacencies not in atlas are invalid)");
        report.AppendLine();

        foreach (var t in scopeTiles.OrderBy(t => t.tileType).ThenBy(t => t.name, StringComparer.Ordinal))
        {
            int up = allowed[t][Dir.Up].Count;
            int right = allowed[t][Dir.Right].Count;
            int down = allowed[t][Dir.Down].Count;
            int left = allowed[t][Dir.Left].Count;

            report.AppendLine($"{t.tileType} :: {t.name} => U:{up} R:{right} D:{down} L:{left}");

            if (up == 0 || right == 0 || down == 0 || left == 0)
            {
                warnings.Add($"{t.name} has an empty neighbor list in at least one direction (U:{up} R:{right} D:{down} L:{left}).");
            }
        }

        if (warnings.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("WARNINGS:");
            for (int i = 0; i < warnings.Count; i++)
                report.AppendLine("- " + warnings[i]);
        }

        lastBakeReport = report.ToString();
        Debug.Log(lastBakeReport);

        string confirmMsg = warnings.Count > 0
            ? $"This bake will REPLACE neighbor arrays on {scopeTiles.Count} tile(s).\n\nThere are {warnings.Count} warning(s) (see Console / Last Bake Report).\n\nProceed?"
            : $"This bake will REPLACE neighbor arrays on {scopeTiles.Count} tile(s).\n\nProceed?";

        if (!EditorUtility.DisplayDialog("Bake Atlas (REPLACE)", confirmMsg, "Bake", "Cancel"))
            return;

        // Apply to tiles.
        Undo.RecordObjects(scopeTiles.ToArray(), "Bake Tile Adjacency Atlas");

        for (int i = 0; i < scopeTiles.Count; i++)
        {
            var t = scopeTiles[i];
            ApplyAllowedToTile(t, allowed[t]);
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Bake Atlas", $"Baked adjacency into {scopeTiles.Count} tile(s).", "OK");
    }

    private static void AddPair(
        Dictionary<Tile, Dictionary<Dir, HashSet<SpecKey>>> allowed,
        TileAdjacencyAtlas.Cell a,
        TileAdjacencyAtlas.Cell b,
        Dir worldDirFromAToB)
    {
        if (a.tile == null || b.tile == null) return;
        if (!allowed.ContainsKey(a.tile) || !allowed.ContainsKey(b.tile)) return;

        int aRot = TileAdjacencyAtlas.NormalizeRot(a.rotationIndex);
        int bRot = TileAdjacencyAtlas.NormalizeRot(b.rotationIndex);

        // Add A -> B.
        Dir aBaseDir = RotateDir(worldDirFromAToB, -aRot);
        int specRotA = TileAdjacencyAtlas.NormalizeRot(bRot - aRot);
        allowed[a.tile][aBaseDir].Add(new SpecKey { tile = b.tile, rot = specRotA });

        // Add B -> A (reciprocal).
        Dir opp = Opposite(worldDirFromAToB);
        Dir bBaseDir = RotateDir(opp, -bRot);
        int specRotB = TileAdjacencyAtlas.NormalizeRot(aRot - bRot);
        allowed[b.tile][bBaseDir].Add(new SpecKey { tile = a.tile, rot = specRotB });
    }

    private static void ApplyAllowedToTile(Tile tile, Dictionary<Dir, HashSet<SpecKey>> allowed)
    {
        if (tile == null) return;

        Tile.NeighborSpec[] ToArray(HashSet<SpecKey> set)
        {
            var list = new List<Tile.NeighborSpec>(set.Count);
            foreach (var s in set.OrderBy(s => s.tile != null ? s.tile.name : "", StringComparer.Ordinal).ThenBy(s => s.rot))
            {
                if (s.tile == null) continue;
                list.Add(new Tile.NeighborSpec { tile = s.tile, rotation = TileAdjacencyAtlas.NormalizeRot(s.rot) });
            }
            return list.ToArray();
        }

        tile.upNeighbours = ToArray(allowed[Dir.Up]);
        tile.rightNeighbours = ToArray(allowed[Dir.Right]);
        tile.downNeighbours = ToArray(allowed[Dir.Down]);
        tile.leftNeighbours = ToArray(allowed[Dir.Left]);

        // Atlas is the source of truth: clear editor-only blocked arrays too.
        tile.upBlockedNeighbours = Array.Empty<Tile.NeighborSpec>();
        tile.rightBlockedNeighbours = Array.Empty<Tile.NeighborSpec>();
        tile.downBlockedNeighbours = Array.Empty<Tile.NeighborSpec>();
        tile.leftBlockedNeighbours = Array.Empty<Tile.NeighborSpec>();

        EditorUtility.SetDirty(tile);
        EditorUtility.SetDirty(tile.gameObject);

        if (PrefabUtility.IsPartOfPrefabInstance(tile.gameObject))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(tile);
        }

        if (PrefabUtility.IsPartOfPrefabAsset(tile.gameObject))
        {
            PrefabUtility.SavePrefabAsset(tile.gameObject);

            string prefabPath = AssetDatabase.GetAssetPath(tile.gameObject);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                AssetDatabase.ImportAsset(prefabPath);
            }
        }
    }

    private static Dir Opposite(Dir d)
    {
        return d switch
        {
            Dir.Up => Dir.Down,
            Dir.Down => Dir.Up,
            Dir.Left => Dir.Right,
            Dir.Right => Dir.Left,
            _ => Dir.Up
        };
    }

    private static Dir RotateDir(Dir d, int rot)
    {
        int r = ((rot % 4) + 4) % 4;
        return (Dir)(((int)d + r) % 4);
    }

    private void DrawRotatedPreviewClipped(Rect bounds, Texture2D source, int rotIndex)
    {
        if (source == null) return;

        rotIndex = TileAdjacencyAtlas.NormalizeRot(rotIndex);
        Texture2D tex = GetRotatedPreview(source, rotIndex);
        if (tex == null) return;

        // Clip strictly to the destination bounds.
        GUI.BeginGroup(bounds);
        Rect localBounds = new Rect(0f, 0f, bounds.width, bounds.height);
        Rect inset = InsetRect(localBounds, PreviewInset);
        Rect fit = GetAspectFitRect(inset, tex.width, tex.height);
        DrawTextureWithPointFiltering(fit, tex, true);
        GUI.EndGroup();
    }

    private static Rect InsetRect(Rect r, float inset)
    {
        if (inset <= 0f) return r;
        return new Rect(r.x + inset, r.y + inset, Mathf.Max(0f, r.width - inset * 2f), Mathf.Max(0f, r.height - inset * 2f));
    }

    private Texture2D GetRotatedPreview(Texture2D source, int rotIndex)
    {
        if (source == null) return null;
        rotIndex = TileAdjacencyAtlas.NormalizeRot(rotIndex);

        int key = source.GetInstanceID();
        if (!rotatedPreviewCache.TryGetValue(key, out var arr) || arr == null || arr.Length != 4)
        {
            arr = new Texture2D[4];
            arr[0] = source;
            rotatedPreviewCache[key] = arr;
        }

        if (rotIndex == 0) return source;
        if (arr[rotIndex] != null) return arr[rotIndex];

        // Build rotated textures from a readable copy (AssetPreview textures are often non-readable).
        Texture2D readable = null;
        bool destroyReadable = false;
        try
        {
            readable = MakeReadableCopy(source);
            destroyReadable = readable != null && readable != source;

            // Generate all 3 rotated variants in one shot.
            // NOTE: Swap 90/270 to match the 3D level's CCW rotation convention.
            var r1 = Rotate90CW(readable);
            var r2 = Rotate180(readable);
            var r3 = Rotate270CW(readable);

            arr[1] = r3;  // rotationIndex 1 = 270° CW (= 90° CCW)
            arr[2] = r2;  // rotationIndex 2 = 180°
            arr[3] = r1;  // rotationIndex 3 = 90° CW (= 270° CCW)

            return arr[rotIndex];
        }
        catch
        {
            // Fallback: if anything goes wrong, return the original preview so the editor stays usable.
            return source;
        }
        finally
        {
            if (destroyReadable && readable != null)
            {
                DestroyImmediate(readable);
            }
        }
    }

    private static Texture2D MakeReadableCopy(Texture2D src)
    {
        if (src == null) return null;

        // Fast path: if readable, use it directly.
        try
        {
            // Accessing isReadable can throw in some edge cases; wrap defensively.
            if (src.isReadable) return src;
        }
        catch { /* ignore */ }

        // Use sRGB read/write to preserve color space (Unity AssetPreview textures are usually in sRGB).
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture prev = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, false);
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.filterMode = FilterMode.Point;
            copy.wrapMode = TextureWrapMode.Clamp;
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0, false);
            copy.Apply(false, false);
            return copy;
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static Texture2D Rotate90CW(Texture2D src)
    {
        int sw = src.width;
        int sh = src.height;
        var srcPix = src.GetPixels();
        int dw = sh;
        int dh = sw;
        var dstPix = new Color[dw * dh];

        // Rotate 90° clockwise: new coords = (sh - 1 - y, x)
        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                int srcIdx = x + y * sw;
                int destX = sh - 1 - y;
                int destY = x;
                int dstIdx = destX + destY * dw;
                dstPix[dstIdx] = srcPix[srcIdx];
            }
        }

        var dst = new Texture2D(dw, dh, TextureFormat.RGBA32, false, false);
        dst.hideFlags = HideFlags.HideAndDontSave;
        dst.filterMode = FilterMode.Point;
        dst.wrapMode = TextureWrapMode.Clamp;
        dst.SetPixels(dstPix);
        dst.Apply(false, false);
        return dst;
    }

    private static Texture2D Rotate180(Texture2D src)
    {
        int sw = src.width;
        int sh = src.height;
        var srcPix = src.GetPixels();
        var dstPix = new Color[sw * sh];

        // Rotate 180°: new coords = (sw - 1 - x, sh - 1 - y)
        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                int srcIdx = x + y * sw;
                int destX = sw - 1 - x;
                int destY = sh - 1 - y;
                int dstIdx = destX + destY * sw;
                dstPix[dstIdx] = srcPix[srcIdx];
            }
        }

        var dst = new Texture2D(sw, sh, TextureFormat.RGBA32, false, false);
        dst.hideFlags = HideFlags.HideAndDontSave;
        dst.filterMode = FilterMode.Point;
        dst.wrapMode = TextureWrapMode.Clamp;
        dst.SetPixels(dstPix);
        dst.Apply(false, false);
        return dst;
    }

    private static Texture2D Rotate270CW(Texture2D src)
    {
        int sw = src.width;
        int sh = src.height;
        var srcPix = src.GetPixels();
        int dw = sh;
        int dh = sw;
        var dstPix = new Color[dw * dh];

        // Rotate 270° clockwise (= 90° CCW): new coords = (y, sw - 1 - x)
        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                int srcIdx = x + y * sw;
                int destX = y;
                int destY = sw - 1 - x;
                int dstIdx = destX + destY * dw;
                dstPix[dstIdx] = srcPix[srcIdx];
            }
        }

        var dst = new Texture2D(dw, dh, TextureFormat.RGBA32, false, false);
        dst.hideFlags = HideFlags.HideAndDontSave;
        dst.filterMode = FilterMode.Point;
        dst.wrapMode = TextureWrapMode.Clamp;
        dst.SetPixels(dstPix);
        dst.Apply(false, false);
        return dst;
    }

    // Return an inner rect that fits the texture's aspect ratio inside target, centered.
    private static Rect GetAspectFitRect(Rect target, float texWidth, float texHeight)
    {
        if (texWidth <= 0f || texHeight <= 0f) return target;
        float targetRatio = target.width / target.height;
        float texRatio = texWidth / texHeight;

        Rect r = new Rect(target.x, target.y, target.width, target.height);
        if (texRatio > targetRatio)
        {
            // Texture is wider; fit to width
            float h = target.width / texRatio;
            r.y += (target.height - h) * 0.5f;
            r.height = h;
        }
        else
        {
            // Texture is taller; fit to height
            float w = target.height * texRatio;
            r.x += (target.width - w) * 0.5f;
            r.width = w;
        }
        return r;
    }

    private static void DrawTextureWithPointFiltering(Rect dest, Texture tex, bool alpha)
    {
        if (tex == null) return;
        var tex2 = tex as Texture2D;
        FilterMode prevMode = FilterMode.Point;
        bool changed = false;
        if (tex2 != null)
        {
            prevMode = tex2.filterMode;
            if (prevMode != FilterMode.Point)
            {
                tex2.filterMode = FilterMode.Point;
                changed = true;
            }
        }

        try
        {
            GUI.DrawTexture(dest, tex, ScaleMode.ScaleToFit, alpha);
        }
        finally
        {
            if (changed && tex2 != null)
            {
                tex2.filterMode = prevMode;
            }
        }
    }
}
#endif
