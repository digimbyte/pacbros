/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace TelePresent.AudioSyncPro
{
    [CustomEditor(typeof(AudioReactor))]
    public class ASP_AudioReactorEditor : Editor
    {
        private SerializedProperty reactionComponentsProp;
        private AudioReactor audioReactor;
        private List<bool> foldouts = new List<bool>();
        private List<SerializedProperty> enabledProperties = new List<SerializedProperty>();
        private const string FoldoutKeyPrefix = "AudioReactorEditor_Foldout_";

        private void OnEnable()
        {
            audioReactor = (AudioReactor)target;
            reactionComponentsProp = serializedObject.FindProperty("reactionComponents");
            RebuildFoldoutsAndProperties();
        }

        private void RebuildFoldoutsAndProperties()
        {
            foldouts.Clear();
            enabledProperties.Clear();

            for (int i = 0; i < reactionComponentsProp.arraySize; i++)
            {
                string foldoutKey = $"{FoldoutKeyPrefix}{audioReactor.GetInstanceID()}_{i}";
                foldouts.Add(EditorPrefs.GetBool(foldoutKey, false));
                var componentProp = reactionComponentsProp.GetArrayElementAtIndex(i);

                if (componentProp.objectReferenceValue != null)
                {
                    var componentSerializedObject = new SerializedObject(componentProp.objectReferenceValue);
                    enabledProperties.Add(componentSerializedObject.FindProperty("enabled"));
                }
                else
                {
                    enabledProperties.Add(null);
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.Space(10);
            DrawMainSettings();
            EditorGUILayout.Space(15);
            DrawAudioControls();
            EditorGUILayout.Space(5);




            if (audioReactor.targetTransform == null)
            {
                EditorGUILayout.HelpBox("Target Transform is not set. Please assign a target to enable reaction components.", MessageType.Info);
            }
            else
            {
                DrawReactionComponents();
                EditorGUILayout.Space();
                if (GUILayout.Button(new GUIContent("Add Audio Reaction", EditorGUIUtility.IconContent("d_Toolbar Plus@2x").image)))
                {
                    ShowAddReactionComponentMenu();
                }
            }





            serializedObject.ApplyModifiedProperties();

        }

        private void DrawMainSettings()
        {
            EditorGUILayout.LabelField("Main Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("audioSourcePlus"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetTransform"));
        }

        private void DrawAudioControls()
        {
            if (audioReactor.audioSourcePlus == null)
            {
                if (audioReactor.audioSourcePlus == null)
                {
                    EditorGUILayout.HelpBox("AudioSourcePlus required for audio preview.", MessageType.Info);
                }
                return;

            }

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            var playPauseIcon = audioReactor.audioSourcePlus.audioSource.isPlaying
                ? EditorGUIUtility.IconContent("d_PauseButton On@2x").image
                : EditorGUIUtility.IconContent("d_PlayButton").image;

            float buttonWidth = (EditorGUIUtility.currentViewWidth - 30) / 2;

            GUI.backgroundColor = audioReactor.audioSourcePlus.audioSource.isPlaying
                ? new Color(1.2f, 0.8f, 0.8f)
                : new Color(0.8f, 1f, 0.8f);

            if (GUILayout.Button(new GUIContent(audioReactor.audioSourcePlus.audioSource.isPlaying ? "Pause" : "Play", playPauseIcon), GUILayout.Height(30), GUILayout.Width(buttonWidth)))
            {
                if (audioReactor.audioSourcePlus.audioSource.isPlaying)
                {
                    audioReactor.audioSourcePlus.PauseAudio();
                }
                else
                {
                    audioReactor.audioSourcePlus.PlayAudio();
                }
            }

            GUI.backgroundColor = Color.white;
            GUI.enabled = audioReactor.audioSourcePlus.audioSource.isPlaying || audioReactor.audioSourcePlus.audioSource.time != 0;

            if (GUILayout.Button(new GUIContent("Stop", EditorGUIUtility.IconContent("d_PreMatQuad").image), GUILayout.Height(30), GUILayout.Width(buttonWidth)))
            {
                audioReactor.audioSourcePlus.StopAudio();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawReactionComponents()
        {
            EditorGUILayout.LabelField("Reaction Components", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.Space(5);

            for (int i = 0; i < reactionComponentsProp.arraySize; i++)
            {
                var componentProp = reactionComponentsProp.GetArrayElementAtIndex(i);
                if (componentProp.objectReferenceValue != null)
                {
                    DrawReactionComponent(i, componentProp);
                }
            }
        }

        private void DrawReactionComponent(int index, SerializedProperty componentProp)
        {
            EnsureListSize(foldouts, index, false);
            EnsureListSize(enabledProperties, index, null);

            SerializedObject componentSerializedObject = new SerializedObject(componentProp.objectReferenceValue);
            SerializedProperty enabledProp = enabledProperties[index];
            SerializedProperty nameProp = componentSerializedObject.FindProperty("name");
            SerializedProperty infoProp = componentSerializedObject.FindProperty("info");

            DrawHeader(index, nameProp, enabledProp, componentProp);

            if (foldouts[index])
            {
                DrawComponentContents(componentSerializedObject, infoProp);
            }
        }

        private void DrawHeader(int index, SerializedProperty nameProp, SerializedProperty enabledProp, SerializedProperty componentProp)
        {
            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                padding = new RectOffset(4, 4, 8, 8),
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };

            EditorGUILayout.BeginHorizontal();

            Rect headerRect = GUILayoutUtility.GetRect(GUIContent.none, labelStyle, GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(headerRect, new Color(0.15f, 0.15f, 0.15f, 1));

            var arrowIcon = foldouts[index] ? (Texture2D)EditorGUIUtility.IconContent("IN foldout on").image : (Texture2D)EditorGUIUtility.IconContent("IN foldout").image;
            Rect textRect = new Rect(headerRect.x + 20, headerRect.y, headerRect.width - 100, headerRect.height);
            GUI.Label(textRect, new GUIContent(" " + (nameProp != null ? nameProp.stringValue : "Component"), arrowIcon), labelStyle);

            if (Event.current.type == EventType.MouseDown && textRect.Contains(Event.current.mousePosition))
            {
                foldouts[index] = !foldouts[index];
                string foldoutKey = $"{FoldoutKeyPrefix}{audioReactor.GetInstanceID()}_{index}";
                EditorPrefs.SetBool(foldoutKey, foldouts[index]);
                Event.current.Use();
            }

            DrawComponentCheckbox(componentProp, headerRect);
            DrawOptionsButton(index, headerRect);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawComponentCheckbox(SerializedProperty componentProp, Rect headerRect)
        {
            MonoBehaviour component = componentProp.objectReferenceValue as MonoBehaviour;
            if (component != null)
            {
                var isActiveProperty = component.GetType().GetProperty("IsActive");
                if (isActiveProperty != null && isActiveProperty.PropertyType == typeof(bool))
                {
                    bool isActive = (bool)isActiveProperty.GetValue(component, null);
                    Rect checkboxRect = new Rect(headerRect.x + headerRect.width - 60, headerRect.y + 5, 18, 18);

                    // Create a GUIContent with a tooltip for the isActive checkbox
                    GUIContent checkboxContent = new GUIContent("", "Toggle whether Reactor is active");

                    // Draw the checkbox with the tooltip
                    isActive = DrawRoundedBoolIndicator(checkboxRect, isActive, checkboxContent);

                    if (GUI.changed)
                    {
                        isActiveProperty.SetValue(component, isActive);
                        EditorUtility.SetDirty(component);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("No 'IsActive' property", EditorStyles.miniLabel, GUILayout.Width(120));
                }
            }
        }

        private bool DrawRoundedBoolIndicator(Rect position, bool isActive, GUIContent content)
        {
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.35f, 0.35f, 0.35f);

            Texture2D lessRoundedTexture = MakeRoundedRectTexture((int)position.width, (int)position.height, GUI.backgroundColor, 2);
            GUI.DrawTexture(position, lessRoundedTexture);
            GUI.backgroundColor = originalColor;

            // Draw tooltip content (hover tip)
            GUI.Label(position, content);

            if (isActive)
            {
                Handles.color = Color.white;
                Handles.DrawLine(new Vector2(position.x + 4, position.y + 10), new Vector2(position.x + 8, position.y + 14));
                Handles.DrawLine(new Vector2(position.x + 8, position.y + 14), new Vector2(position.x + 14, position.y + 4));
            }

            // Check for mouse click to toggle the value
            if (Event.current.type == EventType.MouseDown && position.Contains(Event.current.mousePosition))
            {
                isActive = !isActive;
                GUI.changed = true;
                Event.current.Use();
            }

            return isActive;
        }

        private bool DrawRoundedBoolIndicator(Rect position, bool isActive)
        {
            // Overloaded method without hover text
            return DrawRoundedBoolIndicator(position, isActive, GUIContent.none);
        }

        private void DrawOptionsButton(int index, Rect headerRect)
        {
            Rect optionsRect = new Rect(headerRect.x + headerRect.width - 30, headerRect.y, 30, headerRect.height);
            if (GUI.Button(optionsRect, EditorGUIUtility.IconContent("_Menu@2x").image))
            {
                ShowReactionComponentOptions(index);
            }
        }

        // Dictionary to store foldout states for different lists
        private Dictionary<string, bool> listFoldouts = new Dictionary<string, bool>();

        private void DrawComponentContents(SerializedObject componentSerializedObject, SerializedProperty infoProp)
        {
            if (componentSerializedObject == null) return;

            // Set custom background color for dropdown
            Color originalBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f);

            var dropdownStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(15, 5, 15, 15),
                margin = new RectOffset(10, 10, 0, 10),
            };

            EditorGUILayout.BeginVertical(dropdownStyle, GUILayout.ExpandWidth(true));
            GUI.backgroundColor = originalBackgroundColor;

            // Draw infoProp (if available)
            if (infoProp != null)
            {
                var infoStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    wordWrap = true
                };
                EditorGUILayout.LabelField(infoProp.stringValue, infoStyle, GUILayout.ExpandWidth(true));
            }

            EditorGUILayout.Space(10);

            // Find the useTargetTransform and affectedTransforms properties
            SerializedProperty useTargetTransformProp = componentSerializedObject.FindProperty("useTargetTransform");
            SerializedProperty affectedTransformsProp = componentSerializedObject.FindProperty("affectedTransforms");

            if (useTargetTransformProp != null)
            {
                // Draw the custom checkbox for useTargetTransform
                DrawCustomCheckbox(useTargetTransformProp);

                // If useTargetTransform is false, draw the custom list for affectedTransforms
                if (affectedTransformsProp != null && !useTargetTransformProp.boolValue)
                {
                    // Draw affectedTransforms using a custom list with a foldout
                    DrawCustomList(affectedTransformsProp);
                    GUILayout.Space(10);
                }
            }

            ASPR_PlayAnimationOnBeat playAnimationOnBeat = componentSerializedObject.targetObject as ASPR_PlayAnimationOnBeat;

            SerializedProperty property = componentSerializedObject.GetIterator().Copy();
            bool enterChildren = true;

            // Iterate through all properties of the component
            if (property != null && property.NextVisible(enterChildren))
            {
                do
                {
                    enterChildren = false;

                    // Skip certain properties we handle separately
                    if (property.name == "m_Script" || property.name == "name" || property.name == "info" ||
                        property.name == "useTargetTransform" || property.name == "affectedTransforms")
                        continue;

                    // Handle PlayAnimationOnBeat animation state dropdown
                    if (playAnimationOnBeat != null && property.name == "selectedAnimationState")
                    {
                        List<string> stateNames = playAnimationOnBeat.GetAnimatorStateNames();
                        int selectedIndex = stateNames.IndexOf(property.stringValue);
                        selectedIndex = Mathf.Max(0, selectedIndex);  // Ensure valid index

                        // Create a rect for the dropdown with a slight margin on the right
                        Rect dropdownRect = GUILayoutUtility.GetRect(EditorGUIUtility.labelWidth + 100, EditorGUIUtility.singleLineHeight);
                        dropdownRect.width -= 10;  // Add margin

                        selectedIndex = EditorGUI.Popup(dropdownRect, "Select Animation State", selectedIndex, stateNames.ToArray());

                        if (selectedIndex >= 0 && selectedIndex < stateNames.Count)
                        {
                            property.stringValue = stateNames[selectedIndex];
                        }
                    }
                    // Draw boolean properties using a custom checkbox
                    else if (property.propertyType == SerializedPropertyType.Boolean)
                    {
                        DrawCustomCheckbox(property);
                    }
                    // Draw other list properties with the custom list drawer
                    else if (property.isArray && property.propertyType != SerializedPropertyType.String)
                    {
                        DrawCustomList(property);
                    }
                    // Draw all other properties normally
                    else
                    {
                        EditorGUILayout.PropertyField(property, true);
                    }

                    GUILayout.Space(10);

                } while (property.NextVisible(false));  // Continue iteration safely through properties
            }

            componentSerializedObject.ApplyModifiedProperties();
            EditorGUILayout.EndVertical();
        }

        private void DrawCustomList(SerializedProperty listProp)
        {
            // Get or create the foldout state for this list
            if (!listFoldouts.ContainsKey(listProp.propertyPath))
            {
                listFoldouts[listProp.propertyPath] = false;
            }

            // Draw the foldout header
            listFoldouts[listProp.propertyPath] = EditorGUILayout.Foldout(listFoldouts[listProp.propertyPath], listProp.displayName, true);

            if (listFoldouts[listProp.propertyPath])
            {
                EditorGUI.indentLevel++;

                // Draw each element in the list
                for (int i = 0; i < listProp.arraySize; i++)
                {
                    SerializedProperty elementProp = listProp.GetArrayElementAtIndex(i);

                    EditorGUILayout.BeginHorizontal();

                    // Display the element with a text field or object field (depending on your type)
                    EditorGUILayout.PropertyField(elementProp, GUIContent.none);

                    // Add buttons for moving elements up and down
                    if (GUILayout.Button("↑", GUILayout.Width(20)))
                    {
                        listProp.MoveArrayElement(i, i - 1);
                    }
                    if (GUILayout.Button("↓", GUILayout.Width(20)))
                    {
                        listProp.MoveArrayElement(i, i + 1);
                    }

                    // Button to remove the element
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        listProp.DeleteArrayElementAtIndex(i);
                    }

                    EditorGUILayout.EndHorizontal();
                }

                // Button to add a new element
                if (GUILayout.Button("Add Element"))
                {
                    listProp.InsertArrayElementAtIndex(listProp.arraySize);
                }

                EditorGUI.indentLevel--;
            }
        }



        private void DrawCustomCheckbox(SerializedProperty property)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Boolean)
                return;

            GUIStyle boolStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(property.displayName, boolStyle, GUILayout.Width(EditorGUIUtility.labelWidth));

            Rect rect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(false));

            EditorGUI.BeginChangeCheck();
            bool newValue = DrawRoundedBoolIndicator(rect, property.boolValue); // No hover text
            if (EditorGUI.EndChangeCheck())
            {
                property.boolValue = newValue;
                property.serializedObject.ApplyModifiedProperties();
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void EnsureListSize<T>(List<T> list, int index, T defaultValue)
        {
            while (list.Count <= index)
            {
                list.Add(defaultValue);
            }
        }

        private void ShowAddReactionComponentMenu()
        {
            GenericMenu menu = new GenericMenu();
            var reactionTypes = GetAllReactionTypes();

            // Group the types by category and subcategory
            var categorizedTypes = reactionTypes.GroupBy(t => GetCategoryWithSubCategoryFromAttribute(t));

            foreach (var categoryGroup in categorizedTypes)
            {
                // Skip the "Templates" category
                if (categoryGroup.Key.StartsWith("Templates"))
                    continue;

                foreach (var type in categoryGroup)
                {
                    // Format the menu to show both category and subcategory
                    string menuName = $"{categoryGroup.Key}/{AddSpacesToCamelCase(type.Name)}";
                    menu.AddItem(new GUIContent(menuName), false, () => AddReactionComponent(type));
                }
            }

            menu.ShowAsContext();
        }

        // New helper method to retrieve both category and subcategory
        private string GetCategoryWithSubCategoryFromAttribute(System.Type type)
        {
            var categoryAttribute = type.GetCustomAttributes(typeof(ASP_ReactorCategoryAttribute), false).FirstOrDefault() as ASP_ReactorCategoryAttribute;
            if (categoryAttribute != null)
            {
                // Return formatted string with both category and subcategory (if subcategory is provided)
                return string.IsNullOrEmpty(categoryAttribute.SubCategory)
                    ? categoryAttribute.Category
                    : $"{categoryAttribute.Category}/{categoryAttribute.SubCategory}";
            }
            return "Uncategorized";
        }

        private List<System.Type> GetAllReactionTypes()
        {
            return System.AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => typeof(ASP_IAudioReaction).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                .ToList();
        }

        private string GetCategoryFromAttribute(System.Type type)
        {
            var categoryAttribute = type.GetCustomAttributes(typeof(ASP_ReactorCategoryAttribute), false).FirstOrDefault() as ASP_ReactorCategoryAttribute;
            return categoryAttribute?.Category ?? "Uncategorized";
        }

        private string AddSpacesToCamelCase(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            const string prefix = "ASPR_";
            if (text.StartsWith(prefix))
            {
                text = text.Substring(prefix.Length);
            }

            var newText = new System.Text.StringBuilder(text.Length * 2);
            newText.Append(text[0]);

            for (int i = 1; i < text.Length; i++)
            {
                if (char.IsUpper(text[i]) && text[i - 1] != ' ')
                {
                    newText.Append(' ');
                }
                newText.Append(text[i]);
            }
            return newText.ToString();
        }

        private void AddReactionComponent(System.Type type)
        {
            if (typeof(MonoBehaviour).IsAssignableFrom(type) && typeof(ASP_IAudioReaction).IsAssignableFrom(type))
            {
                Undo.RecordObject(audioReactor, "Add Reaction Component");

                var newComponent = audioReactor.gameObject.AddComponent(type) as MonoBehaviour;
                if (newComponent != null)
                {
                    newComponent.hideFlags = HideFlags.HideInInspector;
                    reactionComponentsProp.InsertArrayElementAtIndex(reactionComponentsProp.arraySize);
                    reactionComponentsProp.GetArrayElementAtIndex(reactionComponentsProp.arraySize - 1).objectReferenceValue = newComponent;

                    foldouts.Add(false);
                    enabledProperties.Add(new SerializedObject(newComponent).FindProperty("enabled"));

                    audioReactor.InitializeNewReaction(newComponent as ASP_IAudioReaction);

                    Undo.RegisterCreatedObjectUndo(newComponent, "Create Reaction Component");
                    EditorUtility.SetDirty(audioReactor);
                    serializedObject.ApplyModifiedProperties();
                }
            }
            else
            {
                Debug.LogError($"{type} does not implement IAudioReaction or is not a MonoBehaviour");
            }
        }

        private void RemoveReactionComponent(int index)
        {
            SerializedProperty componentProp = reactionComponentsProp.GetArrayElementAtIndex(index);

            if (componentProp.objectReferenceValue != null)
            {
                Undo.RecordObject(audioReactor, "Remove Reaction Component");

                MonoBehaviour component = componentProp.objectReferenceValue as MonoBehaviour;
                if (component is ASP_IAudioReaction audioReaction)
                {
                    audioReactor.ResetValueFromReactor(audioReaction);
                }

                componentProp.objectReferenceValue = null;
                Undo.DestroyObjectImmediate(component);

                reactionComponentsProp.DeleteArrayElementAtIndex(index);

                if (index < foldouts.Count) foldouts.RemoveAt(index);
                if (index < enabledProperties.Count) enabledProperties.RemoveAt(index);

                EditorUtility.SetDirty(audioReactor);
                serializedObject.ApplyModifiedProperties();
            }
        }

        private void ShowReactionComponentOptions(int index)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Remove"), false, () => RemoveReactionComponent(index));
            menu.AddItem(new GUIContent("Copy"), false, () => CopyReactionComponent(index));
            menu.AddItem(new GUIContent("Paste"), false, () => PasteReactionComponent(index));
            menu.ShowAsContext();
        }

        private static SerializedObject copiedComponentSerializedObject;

        private void CopyReactionComponent(int index)
        {
            SerializedProperty componentProp = reactionComponentsProp.GetArrayElementAtIndex(index);
            if (componentProp.objectReferenceValue != null)
            {
                copiedComponentSerializedObject = new SerializedObject(componentProp.objectReferenceValue);
            }
        }


        private void PasteReactionComponent(int index)
        {
            if (copiedComponentSerializedObject != null)
            {
                SerializedProperty componentProp = reactionComponentsProp.GetArrayElementAtIndex(index);
                if (componentProp.objectReferenceValue != null)
                {
                    SerializedObject targetSerializedObject = new SerializedObject(componentProp.objectReferenceValue);
                    CopySerializedPropertiesExcludingNames(copiedComponentSerializedObject, targetSerializedObject);
                    targetSerializedObject.ApplyModifiedProperties();
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(audioReactor);
                }
            }
            else
            {
                Debug.LogWarning("No component copied. Please copy a component before pasting.");
            }
        }

        private void CopySerializedPropertiesExcludingNames(SerializedObject source, SerializedObject target)
        {
            SerializedProperty copiedProperty = source.GetIterator();
            copiedProperty.NextVisible(true);

            while (copiedProperty.NextVisible(false))
            {
                if (copiedProperty.name == "name" || copiedProperty.name == "info")
                {
                    continue; // Skip copying 'name' and 'info' properties
                }

                SerializedProperty targetProperty = target.FindProperty(copiedProperty.propertyPath);
                if (targetProperty != null && targetProperty.propertyType == copiedProperty.propertyType)
                {
                    targetProperty.serializedObject.CopyFromSerializedProperty(copiedProperty);
                }
            }
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
            {
                pix[i] = col;
            }

            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();

            return result;
        }

        private Texture2D MakeRoundedRectTexture(int width, int height, Color color, int cornerRadius)
        {
            Texture2D texture = new Texture2D(width, height);
            Color[] pixels = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isCorner = (x < cornerRadius && y < cornerRadius && (cornerRadius - x) * (cornerRadius - x) + (cornerRadius - y) * (cornerRadius - y) > cornerRadius * cornerRadius) ||
                                    (x < cornerRadius && y >= height - cornerRadius && (cornerRadius - x) * (cornerRadius - x) + (y - (height - cornerRadius - 1)) * (y - (height - cornerRadius - 1)) > cornerRadius * cornerRadius) ||
                                    (x >= width - cornerRadius && y < cornerRadius && (x - (width - cornerRadius - 1)) * (x - (width - cornerRadius - 1)) + (cornerRadius - y) * (cornerRadius - y) > cornerRadius * cornerRadius) ||
                                    (x >= width - cornerRadius && y >= height - cornerRadius && (x - (width - cornerRadius - 1)) * (x - (width - cornerRadius - 1)) + (y - (height - cornerRadius - 1)) * (y - (height - cornerRadius - 1)) > cornerRadius * cornerRadius);

                    pixels[x + y * width] = isCorner ? Color.clear : color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return texture;
        }
    }
}
