#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RoomTemplate))]
public class RoomTemplateEditor : Editor
{
    private SerializedProperty widthProp;
    private SerializedProperty heightProp;
    private SerializedProperty pivotProp;
    private SerializedProperty cellsProp;

    private TilesDatabase palette;
    private int selectedTileIndex;
    private bool paintSkip;
    private bool paintSpawn;

    private void OnEnable()
    {
        widthProp = serializedObject.FindProperty("width");
        heightProp = serializedObject.FindProperty("height");
        pivotProp = serializedObject.FindProperty("pivot");
        cellsProp = serializedObject.FindProperty("cells");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        RoomTemplate template = (RoomTemplate)target;

        EditorGUILayout.LabelField("Template", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(widthProp);
        EditorGUILayout.PropertyField(heightProp);
        EditorGUILayout.PropertyField(pivotProp);

        if (GUILayout.Button("Resize"))
        {
            template.Resize(widthProp.intValue, heightProp.intValue);
            EditorUtility.SetDirty(template);
        }

        EditorGUILayout.Space();

        palette = (TilesDatabase)EditorGUILayout.ObjectField("Tiles Database", palette, typeof(TilesDatabase), false);
        selectedTileIndex = EditorGUILayout.IntSlider("Selected Tile", selectedTileIndex, 0, palette != null && palette.tiles != null && palette.tiles.Count > 0 ? palette.tiles.Count - 1 : 0);
        paintSkip = EditorGUILayout.Toggle("Paint Skip Spawn", paintSkip);
        paintSpawn = EditorGUILayout.Toggle("Paint Spawn Point", paintSpawn);

        EditorGUILayout.Space();

        DrawPalettePreview();
        EditorGUILayout.Space();
        DrawGrid(template);

        EditorGUILayout.Space();
        if (GUILayout.Button("Create Prefab From Template"))
        {
            CreatePrefabFromTemplate(template);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawPalettePreview()
    {
        if (palette == null || palette.tiles == null || palette.tiles.Count == 0)
        {
            EditorGUILayout.HelpBox("Assign a TilesDatabase to paint.", MessageType.Info);
            return;
        }

        Tile tile = palette.GetTile(selectedTileIndex);
        if (tile == null)
        {
            EditorGUILayout.HelpBox("Selected tile is null.", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField($"Painting: {tile.name} (index {selectedTileIndex})");
    }

    private void DrawGrid(RoomTemplate template)
    {
        if (palette == null || palette.tiles == null || palette.tiles.Count == 0)
        {
            EditorGUILayout.HelpBox("No palette assigned.", MessageType.Info);
            return;
        }

        for (int y = template.height - 1; y >= 0; y--)
        {
            EditorGUILayout.BeginHorizontal();
            for (int x = 0; x < template.width; x++)
            {
                RoomTemplate.RoomCell cell = template.GetCell(x, y);
                string label = cell.tile != null ? cell.tile.name : "-";
                if (cell.skipSpawn) label += " S";
                if (cell.spawnPoint) label += " P";

                if (GUILayout.Button(label, GUILayout.Width(64), GUILayout.Height(32)))
                {
                    Tile paintTile = palette.GetTile(selectedTileIndex);
                    template.SetCell(x, y, paintTile, paintSkip, paintSpawn);
                    EditorUtility.SetDirty(template);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void CreatePrefabFromTemplate(RoomTemplate template)
    {
        string path = EditorUtility.SaveFilePanelInProject("Save Room Prefab", template.name + "_Room", "prefab", "Choose location for room prefab");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        GameObject root = new GameObject(template.name + "_RoomRoot");
        var spawnPoints = new List<Transform>();

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

            Vector3 pos = new Vector3(cell.x, 0f, cell.y);
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(cell.tile.gameObject);
            go.transform.SetParent(root.transform);
            go.transform.position = pos;

            if (cell.spawnPoint)
            {
                spawnPoints.Add(go.transform);
            }
        }

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
    }
}
#endif
