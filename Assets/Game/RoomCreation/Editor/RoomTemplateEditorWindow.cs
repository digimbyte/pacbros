#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class RoomTemplateEditorWindow : EditorWindow
{
    private const string DefaultPrefabFolder = "Assets/Game/Prefabs/Rooms";

    private RoomTemplate template;
    private string templatePath;
    private Vector2 scrollPalette;
    private Vector2 scrollGrid;

    private List<TilesDatabase> databases = new List<TilesDatabase>();
    private Dictionary<TilesDatabase, bool> dbFoldouts = new Dictionary<TilesDatabase, bool>();
    private Tile paintTile;
    private bool paintSkip;
    private bool paintSpawn;
    private int paintRotation;

    [MenuItem("PacBros/Rooms/Room Template Editor")]
    public static void Open()
    {
        GetWindow<RoomTemplateEditorWindow>(false, "Room Template Editor");
    }

    private void OnEnable()
    {
        RefreshDatabases();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawTemplateHeader();
        DrawPalette();
        DrawGrid();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("New", EditorStyles.toolbarButton))
        {
            CreateNewTemplate();
        }

        if (GUILayout.Button("Load", EditorStyles.toolbarButton))
        {
            LoadTemplateFromFile();
        }

        GUI.enabled = template != null;
        if (GUILayout.Button("Save (Template + Prefab)", EditorStyles.toolbarButton))
        {
            SaveTemplateAndPrefab();
        }
        GUI.enabled = true;

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Refresh Tiles", EditorStyles.toolbarButton))
        {
            RefreshDatabases();
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawTemplateHeader()
    {
        if (template == null)
        {
            EditorGUILayout.HelpBox("No template loaded. Create or load one.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();
        template.name = EditorGUILayout.TextField("Template Name", template.name);
        template.width = Mathf.Max(1, EditorGUILayout.IntField("Width", template.width));
        template.height = Mathf.Max(1, EditorGUILayout.IntField("Height", template.height));
        template.pivot = EditorGUILayout.Vector2IntField("Pivot", template.pivot);

        if (GUILayout.Button("Resize"))
        {
            template.Resize(template.width, template.height);
            MarkDirty();
        }

        EditorGUILayout.Space();
        paintSkip = EditorGUILayout.Toggle("Paint Skip Spawn", paintSkip);
        paintSpawn = EditorGUILayout.Toggle("Paint Spawn Point", paintSpawn);
        paintRotation = EditorGUILayout.IntSlider("Rotation (90° steps)", paintRotation, 0, 3);
    }

    private void DrawPalette()
    {
        EditorGUILayout.LabelField("Palette", EditorStyles.boldLabel);
        if (databases.Count == 0)
        {
            EditorGUILayout.HelpBox("No TilesDatabase assets found.", MessageType.Warning);
            return;
        }

        scrollPalette = EditorGUILayout.BeginScrollView(scrollPalette, GUILayout.Height(180));
        foreach (var db in databases)
        {
            if (!dbFoldouts.ContainsKey(db))
            {
                dbFoldouts[db] = true;
            }

            dbFoldouts[db] = EditorGUILayout.Foldout(dbFoldouts[db], $"{db.category} ({db.name})", true);
            if (!dbFoldouts[db])
            {
                continue;
            }

            if (db.tiles == null)
            {
                continue;
            }

            int columns = 6;
            int col = 0;
            const float thumbSize = 72f;
            EditorGUILayout.BeginHorizontal();
            foreach (var tile in db.tiles)
            {
                if (tile == null)
                {
                    continue;
                }

                Rect btnRect = GUILayoutUtility.GetRect(thumbSize, thumbSize, GUILayout.Width(thumbSize), GUILayout.Height(thumbSize));
                if (GUI.Button(btnRect, GUIContent.none))
                {
                    paintTile = tile;
                }

                // draw preview texture if available
                if (tile.preview != null)
                {
                    GUI.DrawTexture(btnRect, tile.preview, ScaleMode.ScaleToFit, true);
                }
                else
                {
                    GUI.Label(btnRect, tile.name, EditorStyles.centeredGreyMiniLabel);
                }

                // highlight selection
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
        if (template == null)
        {
            return;
        }

        EditorGUILayout.LabelField("Grid", EditorStyles.boldLabel);
        scrollGrid = EditorGUILayout.BeginScrollView(scrollGrid, GUILayout.Height(400));
        const float cellSize = 72f;

        for (int y = template.height - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < template.width; x++)
            {
                var cell = template.GetCell(x, y);
                Rect rect = GUILayoutUtility.GetRect(cellSize, cellSize, GUILayout.Width(cellSize), GUILayout.Height(cellSize));


                // carve out a small corner button so rotate control is separate and visible
                Rect corner = new Rect(rect.xMax - 18f, rect.y + 2f, 16f, 16f);
                Rect mainRect = new Rect(rect.x, rect.y, rect.width - (corner.width + 4f), rect.height);

                // handle right-click to clear cell (consume event so main button doesn't also trigger)
                Event ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 1 && rect.Contains(ev.mousePosition))
                {
                    template.SetCell(x, y, null, false, false, 0);
                    MarkDirty();
                    ev.Use();
                }
                else
                {
                    if (GUI.Button(mainRect, GUIContent.none))
                    {
                        if (paintTile == null)
                        {
                            template.SetCell(x, y, null, false, false, 0);
                        }
                        else
                        {
                            template.SetCell(x, y, paintTile, paintSkip, paintSpawn, paintRotation);
                        }
                        MarkDirty();
                    }
                }

                // full cell border / background
                GUI.Box(rect, GUIContent.none);

                if (cell.tile != null && cell.tile.preview != null)
                {
                    Texture2D tex = cell.tile.preview;
                    Matrix4x4 prev = GUI.matrix;
                    float angle = cell.rotationIndex * 90f;
                    // draw rotated preview centered in the mainRect area
                    GUIUtility.RotateAroundPivot(angle, mainRect.center);
                    GUI.DrawTexture(mainRect, tex, ScaleMode.ScaleToFit, true);
                    GUI.matrix = prev;
                }

                // rotation corner button is distinct and shows numeric indicator (1-4)
                if (cell.tile != null)
                {
                    GUIStyle cornerStyle = new GUIStyle(GUI.skin.button);
                    cornerStyle.fontSize = 10;
                    cornerStyle.alignment = TextAnchor.MiddleCenter;
                    cornerStyle.fixedHeight = corner.height;
                    cornerStyle.fixedWidth = corner.width;
                    string cornerLabel = ((cell.rotationIndex % 4) + 1).ToString();
                    if (GUI.Button(corner, cornerLabel, cornerStyle))
                    {
                        int newRot = (cell.rotationIndex + 1) % 4;
                        template.SetCell(x, y, cell.tile, cell.skipSpawn, cell.spawnPoint, newRot);
                        MarkDirty();
                    }
                }

                string label = cell.tile != null ? cell.tile.name : "-";
                if (cell.skipSpawn) label += " S";
                if (cell.spawnPoint) label += " P";
                if (cell.tile != null && cell.rotationIndex != 0) label += $" R{cell.rotationIndex}";

                GUI.Label(rect, label, EditorStyles.whiteMiniLabel);
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private void RefreshDatabases()
    {
        databases.Clear();
        dbFoldouts.Clear();
        string[] guids = AssetDatabase.FindAssets("t:TilesDatabase");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var db = AssetDatabase.LoadAssetAtPath<TilesDatabase>(path);
            if (db != null)
            {
                databases.Add(db);
            }
        }
    }

    private void CreateNewTemplate()
    {
        template = CreateInstance<RoomTemplate>();
        template.name = "RoomTemplate_New";
        template.Resize(4, 4);
        templatePath = null;
    }

    private void LoadTemplateFromFile()
    {
        string path = EditorUtility.OpenFilePanel("Load Room Template", "Assets", "asset");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        string projectPath = FileUtil.GetProjectRelativePath(path);
        var loaded = AssetDatabase.LoadAssetAtPath<RoomTemplate>(projectPath);
        if (loaded != null)
        {
            template = loaded;
            templatePath = projectPath;
        }
    }

    private void SaveTemplateAndPrefab()
    {
        if (template == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(templatePath))
        {
            EnsureFolder(DefaultPrefabFolder);
            templatePath = EditorUtility.SaveFilePanelInProject("Save Room Template", template.name, "asset", "Choose location for RoomTemplate asset", DefaultPrefabFolder);
            if (string.IsNullOrEmpty(templatePath))
            {
                return;
            }

            AssetDatabase.CreateAsset(template, templatePath);
        }

        EditorUtility.SetDirty(template);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        SavePrefabAlongsideTemplate();
    }

    private void MarkDirty()
    {
        if (template != null)
        {
            EditorUtility.SetDirty(template);
        }
    }

    private void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folder);
        string leaf = Path.GetFileName(folder);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent.Replace("\\", "/"));
        }
        AssetDatabase.CreateFolder(parent, leaf);
    }

    private void SavePrefabAlongsideTemplate()
    {
        string folder = string.IsNullOrEmpty(templatePath) ? DefaultPrefabFolder : Path.GetDirectoryName(templatePath);
        EnsureFolder(folder);

        string prefabName = template.name + "_Room.prefab";
        string prefabPath = Path.Combine(folder, prefabName).Replace("\\", "/");

        GameObject root = new GameObject(template.name + "_RoomRoot");
        var instance = root.AddComponent<RoomTemplateInstance>();
        instance.template = template;

        Vector3 pivotOffset = new Vector3(-template.pivot.x, 0f, -template.pivot.y);

        foreach (var cell in template.cells)
        {
            if (cell.skipSpawn)
            {
                continue;
            }

            if (cell.tile == null)
            {
                continue;
            }

            Vector3 pos = new Vector3(cell.x, 0f, cell.y) + pivotOffset;
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(cell.tile.gameObject);
            Vector3 originalLocal = go.transform.localPosition;
            Quaternion originalRot = go.transform.localRotation;
            go.transform.SetParent(root.transform);
            go.transform.localPosition = pos + originalLocal;
            go.transform.localRotation = Quaternion.Euler(0f, cell.rotationIndex * 90f, 0f) * originalRot;
        }

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
    }
}
#endif
