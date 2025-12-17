using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;

namespace TelePresent.AudioSyncPro
{
    public class ASP_DynamicPickerEditor
    {
        // Draw Dynamic Pickers for ASP_Marker with AudioSourcePlus support
        public void DrawDynamicPickers(List<ASP_DynamicPicker> dynamicPickers, ASP_Marker marker, AudioSourcePlus audioSourcePlus)
        {
            for (int i = dynamicPickers.Count - 1; i >= 0; i--) // Iterate backwards to avoid deletion issues
            {
                var dynamicPicker = dynamicPickers[i];
                EditorGUILayout.Space();
                DrawDynamicPickerInspector(dynamicPicker, i, dynamicPickers, audioSourcePlus);
            }
        }



        // Editor GUI for AudioSourcePlus version
        private void DrawDynamicPickerInspector(ASP_DynamicPicker dynamicPicker, int index, List<ASP_DynamicPicker> dynamicPickers, AudioSourcePlus audioSourcePlus)
        {
            if (dynamicPicker == null) return;

            GUIStyle boxStyle = CreateBoxStyle();
            boxStyle.margin = new RectOffset(0, 0, 2, 2);
            boxStyle.padding = new RectOffset(10, 10, 5, 5);

            EditorGUILayout.BeginVertical(boxStyle);
            try
            {
                GUIStyle boldHeaderStyle = new GUIStyle(EditorStyles.foldout)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize = 12,
                    normal = { textColor = Color.white / 1.1f }
                };

                EditorGUILayout.BeginHorizontal();
                dynamicPicker.IsFoldedOut = EditorGUILayout.Foldout(dynamicPicker.IsFoldedOut, dynamicPicker.GetHeader(), true, boldHeaderStyle);

                if (GUILayout.Button("X", GUILayout.Width(20), GUILayout.Height(20)))
                {
                    if (index >= 0 && index < dynamicPickers.Count)
                    {
                        Undo.RecordObject(audioSourcePlus, "Remove Dynamic Picker");
                        dynamicPickers.RemoveAt(index);
                        EditorUtility.SetDirty(audioSourcePlus);
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    return;
                }
                EditorGUILayout.EndHorizontal();

                if (dynamicPicker.IsFoldedOut)
                {
                    dynamicPicker.selectedGameObject = (GameObject)EditorGUILayout.ObjectField("GameObject", dynamicPicker.selectedGameObject, typeof(GameObject), true);
                    if (GUI.changed)
                    {
                        Undo.RecordObject(audioSourcePlus, "Change Selected GameObject");
                        EditorUtility.SetDirty(audioSourcePlus);
                    }

                    if (dynamicPicker.selectedGameObject != null)
                    {
                        var components = dynamicPicker.selectedGameObject.GetComponents<Component>().ToList();
                        components.Insert(0, null);
                        var componentNames = components.Select(component => component == null ? "GameObject" : component.GetType().Name).ToArray();
                        int selectedComponentIndex = components.IndexOf(dynamicPicker.selectedComponent);

                        if (selectedComponentIndex < 0 || selectedComponentIndex >= components.Count)
                            selectedComponentIndex = 0;

                        int newSelectedComponentIndex = EditorGUILayout.Popup("Select Component", selectedComponentIndex, componentNames);
                        if (newSelectedComponentIndex != selectedComponentIndex)
                        {
                            Undo.RecordObject(audioSourcePlus, "Change Selected Component");
                            dynamicPicker.selectedComponent = components[newSelectedComponentIndex];
                            dynamicPicker.selectedComponentName = componentNames[newSelectedComponentIndex];
                            dynamicPicker.selectedMethodName = null;
                            dynamicPicker.ClearMethodParameters();
                            EditorUtility.SetDirty(audioSourcePlus);

                            // Automatically select the first method
                            var target = dynamicPicker.selectedComponent != null ? (object)dynamicPicker.selectedComponent : dynamicPicker.selectedGameObject;
                            if (target != null)
                            {
                                var methods = target.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                                if (methods.Length > 0)
                                {
                                    dynamicPicker.selectedMethodName = methods[0].Name;
                                    var parameters = methods[0].GetParameters();
                                    dynamicPicker.methodParameters = new ASP_SerializedParameter[parameters.Length];
                                    for (int i = 0; i < parameters.Length; i++)
                                    {
                                        dynamicPicker.methodParameters[i] = new ASP_SerializedParameter();
                                    }
                                }
                            }
                        }

                        var targetForMethods = dynamicPicker.selectedComponent != null ? (object)dynamicPicker.selectedComponent : dynamicPicker.selectedGameObject;
                        if (targetForMethods != null)
                        {
                            var methods = targetForMethods.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                            var methodNames = Array.ConvertAll(methods, method => method.Name);
                            int selectedMethodIndex = Array.IndexOf(methodNames, dynamicPicker.selectedMethodName);
                            if (selectedMethodIndex < 0 || selectedMethodIndex >= methodNames.Length)
                                selectedMethodIndex = 0;

                            int newSelectedMethodIndex = EditorGUILayout.Popup("Select Method", selectedMethodIndex, methodNames);
                            if (newSelectedMethodIndex >= 0 && newSelectedMethodIndex < methods.Length)
                            {
                                var selectedMethod = methods[newSelectedMethodIndex];
                                if (newSelectedMethodIndex != selectedMethodIndex)
                                {
                                    Undo.RecordObject(audioSourcePlus, "Change Selected Method");
                                    dynamicPicker.selectedMethodName = methodNames[newSelectedMethodIndex];
                                    dynamicPicker.ClearMethodParameters();
                                    EditorUtility.SetDirty(audioSourcePlus);
                                }

                                if (!string.IsNullOrEmpty(dynamicPicker.selectedMethodName))
                                {
                                    var parameters = selectedMethod.GetParameters();
                                    if (dynamicPicker.methodParameters == null || dynamicPicker.methodParameters.Length != parameters.Length)
                                    {
                                        Undo.RecordObject(audioSourcePlus, "Change Method Parameters");
                                        dynamicPicker.methodParameters = new ASP_SerializedParameter[parameters.Length];
                                        for (int i = 0; i < parameters.Length; i++)
                                        {
                                            dynamicPicker.methodParameters[i] = new ASP_SerializedParameter();
                                        }
                                        EditorUtility.SetDirty(audioSourcePlus);
                                    }

                                    // Draw parameters for the selected method
                                    for (int i = 0; i < parameters.Length; i++)
                                    {
                                        var parameter = parameters[i];
                                        var parameterProp = dynamicPicker.methodParameters[i];
                                        DrawParameterField(parameter, parameterProp, audioSourcePlus);
                                        if (GUI.changed)
                                        {
                                            Undo.RecordObject(audioSourcePlus, "Change Method Parameter");
                                            EditorUtility.SetDirty(audioSourcePlus);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError("Error in DrawDynamicPickerInspector: " + e.Message);
            }
            finally
            {
                EditorGUILayout.EndVertical();
            }
        }

    
        // DrawParameterField overload for AudioSourcePlus
        private void DrawParameterField(System.Reflection.ParameterInfo parameter, ASP_SerializedParameter parameterProp, AudioSourcePlus audioSourcePlus)
        {
            if (parameterProp == null)
            {
                Debug.LogWarning("SerializedParameter is null, skipping field.");
                return;
            }

            Type parameterType = parameter.ParameterType;
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(audioSourcePlus, "Change Method Parameter");

            if (parameterType == typeof(int))
                parameterProp.intValue = EditorGUILayout.IntField(parameter.Name, parameterProp.intValue);
            else if (parameterType == typeof(float))
                parameterProp.floatValue = EditorGUILayout.FloatField(parameter.Name, parameterProp.floatValue);
            else if (parameterType == typeof(double))
                parameterProp.doubleValue = EditorGUILayout.DoubleField(parameter.Name, parameterProp.doubleValue);
            else if (parameterType == typeof(string))
                parameterProp.stringValue = EditorGUILayout.TextField(parameter.Name, parameterProp.stringValue);
            else if (parameterType == typeof(bool))
                parameterProp.boolValue = EditorGUILayout.Toggle(parameter.Name, parameterProp.boolValue);
            else if (parameterType == typeof(Vector3))
                parameterProp.vector3Value = EditorGUILayout.Vector3Field(parameter.Name, parameterProp.vector3Value);
            else if (parameterType == typeof(Vector2))
                parameterProp.vector2Value = EditorGUILayout.Vector2Field(parameter.Name, parameterProp.vector2Value);
            else if (parameterType == typeof(Vector4))
                parameterProp.vector4Value = EditorGUILayout.Vector4Field(parameter.Name, parameterProp.vector4Value);
            else if (parameterType == typeof(Vector3Int))
                parameterProp.vector3IntValue = EditorGUILayout.Vector3IntField(parameter.Name, parameterProp.vector3IntValue);
            else if (parameterType == typeof(Vector2Int))
                parameterProp.vector2IntValue = EditorGUILayout.Vector2IntField(parameter.Name, parameterProp.vector2IntValue);
            else if (parameterType == typeof(Color))
                parameterProp.colorValue = EditorGUILayout.ColorField(parameter.Name, parameterProp.colorValue);
            else if (parameterType == typeof(Quaternion))
                parameterProp.quaternionValue = Quaternion.Euler(EditorGUILayout.Vector3Field(parameter.Name, parameterProp.quaternionValue.eulerAngles));
            else if (parameterType == typeof(long))
                parameterProp.longValue = EditorGUILayout.LongField(parameter.Name, parameterProp.longValue);
            else if (parameterType == typeof(uint))
                parameterProp.intValue = EditorGUILayout.IntField(parameter.Name, parameterProp.intValue);
            else if (parameterType == typeof(ulong))
                parameterProp.longValue = EditorGUILayout.LongField(parameter.Name, parameterProp.longValue);
            else if (parameterType == typeof(Rect))
                parameterProp.rectValue = EditorGUILayout.RectField(parameter.Name, parameterProp.rectValue);
            else if (parameterType == typeof(RectInt))
                parameterProp.rectIntValue = EditorGUILayout.RectIntField(parameter.Name, parameterProp.rectIntValue);
            else if (parameterType == typeof(Bounds))
                parameterProp.boundsValue = EditorGUILayout.BoundsField(parameter.Name, parameterProp.boundsValue);
            else if (parameterType == typeof(BoundsInt))
                parameterProp.boundsIntValue = EditorGUILayout.BoundsIntField(parameter.Name, parameterProp.boundsIntValue);
            else if (parameterType == typeof(LayerMask))
                parameterProp.intValue = EditorGUILayout.LayerField(parameter.Name, parameterProp.intValue);
            else if (parameterType == typeof(AnimationCurve))
                parameterProp.animationCurveValue = EditorGUILayout.CurveField(parameter.Name, parameterProp.animationCurveValue);
            else if (parameterType == typeof(Gradient))
            {
                EditorGUI.BeginChangeCheck();
                Gradient gradient = parameterProp.gradientValue;
                Gradient newGradient = EditorGUILayout.GradientField(parameter.Name, gradient);
                if (EditorGUI.EndChangeCheck())
                    parameterProp.gradientValue = newGradient;
            }
            else if (parameterType == typeof(Transform))
                parameterProp.objectValue = EditorGUILayout.ObjectField(parameter.Name, parameterProp.objectValue, parameterType, true);
            else if (parameterType.IsEnum)
            {
                if (string.IsNullOrEmpty(parameterProp.enumValue))
                    parameterProp.enumValue = Enum.GetNames(parameterType).FirstOrDefault();
                var enumNames = Enum.GetNames(parameterType);
                int enumIndex = Array.IndexOf(enumNames, parameterProp.enumValue);
                if (enumIndex < 0)
                    enumIndex = 0;
                enumIndex = EditorGUILayout.Popup(parameter.Name, enumIndex, enumNames);
                parameterProp.enumValue = enumNames[enumIndex];
            }
            else if (typeof(UnityEngine.Object).IsAssignableFrom(parameterType))
                parameterProp.objectValue = EditorGUILayout.ObjectField(parameter.Name, parameterProp.objectValue, parameterType, true);
            else
                EditorGUILayout.LabelField(parameter.Name, $"Unsupported parameter type: {parameterType}");

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(audioSourcePlus);
        }

      
        // Helper to create a styled box for the inspector
        private GUIStyle CreateBoxStyle()
        {
            var boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, new Color(0.1f, 0.1f, 0.1f, 0.9f)) },
                border = new RectOffset(2, 2, 2, 2),
                margin = new RectOffset(15, 15, 10, 10),
                padding = new RectOffset(15, 15, 15, 15)
            };
            return boxStyle;
        }

        // Helper to generate a Texture2D with a solid color
        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = Enumerable.Repeat(col, width * height).ToArray();
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
    }
}
