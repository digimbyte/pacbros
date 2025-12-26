#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

[CustomEditor(typeof(TilesDatabase))]
public class TilesDatabaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TilesDatabase db = (TilesDatabase)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Import Tiles From Folder"))
        {
            string folder = EditorUtility.OpenFolderPanel("Select Folder (inside Assets)", Application.dataPath, "");
            if (string.IsNullOrEmpty(folder))
                return;

            if (!folder.StartsWith(Application.dataPath))
            {
                EditorUtility.DisplayDialog("Import Tiles", "Please select a folder inside this Unity project's Assets folder.", "OK");
                return;
            }

            string relative = "Assets" + folder.Substring(Application.dataPath.Length);

            string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { relative });
            int added = 0;

            Undo.RecordObject(db, "Import Tiles From Folder");

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null)
                    continue;

                var tile = go.GetComponent<Tile>();
                if (tile == null)
                    continue;

                if (db.tiles == null)
                    db.tiles = new System.Collections.Generic.List<Tile>();

                if (!db.tiles.Contains(tile))
                {
                    db.tiles.Add(tile);
                    added++;
                }
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(db);
                AssetDatabase.SaveAssets();
            }

            EditorUtility.DisplayDialog("Import Tiles", $"Imported {added} tile(s) into '{db.name}'.", "OK");
        }
    }
}
#endif
