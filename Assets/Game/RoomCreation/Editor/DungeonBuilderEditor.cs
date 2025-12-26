#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DungeonBuilder))]
public class DungeonBuilderEditor : Editor
{
    private int runToEndMaxSteps = 10000;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        DungeonBuilder builder = (DungeonBuilder)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Generation Controls", EditorStyles.boldLabel);

        if (!builder.IterativeEnabled)
        {
            if (GUILayout.Button("Build"))
            {
                builder.Build();
                EditorUtility.SetDirty(builder);
                SceneView.RepaintAll();
            }

            return;
        }

        // Iterative controls
        EditorGUILayout.LabelField(
            "Iterative Status",
            $"running={builder.IterativeIsRunning}, complete={builder.IterativeIsComplete}, step={builder.IterativeStepIndex}");

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(builder.IterativeIsRunning ? "Restart Iterative" : "Begin Iterative"))
        {
            builder.BeginIterative();
            EditorUtility.SetDirty(builder);
            SceneView.RepaintAll();
        }
        if (GUILayout.Button("Step"))
        {
            builder.StepIterative();
            EditorUtility.SetDirty(builder);
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndHorizontal();

        runToEndMaxSteps = Mathf.Max(0, EditorGUILayout.IntField("Run To End Max Steps", runToEndMaxSteps));
        if (GUILayout.Button("Run To End"))
        {
            builder.RunIterativeToEnd(runToEndMaxSteps);
            EditorUtility.SetDirty(builder);
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Reset Iterative State"))
        {
            builder.ResetIterative();
            EditorUtility.SetDirty(builder);
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Build (Full Run)"))
        {
            builder.Build();
            EditorUtility.SetDirty(builder);
            SceneView.RepaintAll();
        }
    }
}
#endif
