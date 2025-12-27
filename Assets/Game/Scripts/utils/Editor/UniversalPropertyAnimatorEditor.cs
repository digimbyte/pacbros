using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UniversalPropertyAnimator))]
public class UniversalPropertyAnimatorEditor : Editor
{
    SerializedProperty p_targetKind;
    SerializedProperty p_targetComponent;
    SerializedProperty p_memberName;
    SerializedProperty p_targetRenderer;
    SerializedProperty p_materialIndex;
    SerializedProperty p_shaderProperty;
    SerializedProperty p_useLocalSpace;

    void OnEnable()
    {
        p_targetKind = serializedObject.FindProperty("targetKind");
        p_targetComponent = serializedObject.FindProperty("targetComponent");
        p_memberName = serializedObject.FindProperty("memberName");
        p_targetRenderer = serializedObject.FindProperty("targetRenderer");
        p_materialIndex = serializedObject.FindProperty("materialIndex");
        p_shaderProperty = serializedObject.FindProperty("shaderProperty");
        p_useLocalSpace = serializedObject.FindProperty("useLocalSpace");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(p_targetKind);

        var kind = (UniversalPropertyAnimator.TargetKind)p_targetKind.enumValueIndex;
        // Show resolved type for guidance if available
        var ua = target as UniversalPropertyAnimator;
        if (ua != null && !string.IsNullOrEmpty(ua.resolvedMemberTypeName))
        {
            EditorGUILayout.HelpBox($"Resolved member type: {ua.resolvedMemberTypeName}", MessageType.Info);
        }

        switch (kind)
        {
            case UniversalPropertyAnimator.TargetKind.ComponentMember:
                EditorGUILayout.PropertyField(p_targetComponent);
                EditorGUILayout.PropertyField(p_memberName);
                break;

            case UniversalPropertyAnimator.TargetKind.MaterialProperty:
                EditorGUILayout.PropertyField(p_targetRenderer);
                EditorGUILayout.PropertyField(p_materialIndex);
                EditorGUILayout.PropertyField(p_shaderProperty);
                break;

            case UniversalPropertyAnimator.TargetKind.TransformPosition:
            case UniversalPropertyAnimator.TargetKind.TransformRotation:
                EditorGUILayout.PropertyField(p_targetComponent);
                EditorGUILayout.PropertyField(p_useLocalSpace);
                break;
        }

        // Draw remaining properties (everything else)
        DrawRemainingProperties();

        serializedObject.ApplyModifiedProperties();
    }

    void DrawRemainingProperties()
    {
        // List properties to skip (already drawn)
        string[] skip = new[] {
            "m_Script",
            "targetKind",
            "targetComponent",
            "memberName",
            "targetRenderer",
            "materialIndex",
            "shaderProperty",
            "useLocalSpace"
        };

        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            bool s = false;
            foreach (var name in skip) if (prop.name == name) { s = true; break; }
            if (s) continue;
            EditorGUILayout.PropertyField(prop, true);
        }
    }
}
