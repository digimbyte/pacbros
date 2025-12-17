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

namespace TelePresent.AudioSyncPro
{
    [CustomPropertyDrawer(typeof(ASP_Vector2SliderAttribute))]
    public class Vector2ASPSliderDrawer : PropertyDrawer
    {
        private const float HandleWidth = 6f;
        private const float BarHeight = 20f;
        private const float BarPadding = 0f;
        private const float RightMargin = 10f;

        private bool isDraggingValue;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Vector2)
            {
                EditorGUI.LabelField(position, label.text, "Use Vector2 for ASP Slider");
                return;
            }

            ASP_Vector2SliderAttribute slider = (ASP_Vector2SliderAttribute)attribute;
            Vector2 vector = property.vector2Value;

            // Always draw the label to the left of the control
            position = EditorGUI.PrefixLabel(position, GUIContent.none);
            Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            EditorGUI.LabelField(labelRect, label); // Draw the label

            Rect controlRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, position.width - EditorGUIUtility.labelWidth - RightMargin, position.height);
            Rect sliderRect = new Rect(controlRect.x + BarPadding, controlRect.y, controlRect.width - 2 * BarPadding, BarHeight);

            DrawSliderBackground(sliderRect);

            float greenHandlePos = CalculateHandlePosition(sliderRect, vector.x, slider.minLimit, slider.maxLimit);
            float blueHandlePos = CalculateHandlePosition(sliderRect, vector.y, slider.minLimit, slider.maxLimit);

            // Draw the green fill based on x value (non-interactable)
            DrawGreenFill(greenHandlePos, sliderRect);

            // Draw the transparent blue fill based on y value (interactable)
            DrawBlueFill(blueHandlePos, sliderRect, vector.y, slider.minLimit, slider.maxLimit);

            DrawHandle(blueHandlePos, sliderRect.y, HandleWidth, sliderRect.height, IsMouseOverHandle(Event.current.mousePosition, blueHandlePos, sliderRect));

            if (IsMouseOverHandle(Event.current.mousePosition, blueHandlePos, sliderRect) || isDraggingValue)
                DrawValueLabel(blueHandlePos, sliderRect.y, vector.y);

            HandleMouseEvents(ref vector, sliderRect, slider.minLimit, slider.maxLimit);

            vector.y = Mathf.Clamp(vector.y, slider.minLimit, slider.maxLimit);

            // Ensure the x value is not interactable and remains unchanged
            property.vector2Value = new Vector2(vector.x, vector.y);
        }

        private void DrawSliderBackground(Rect sliderRect)
        {
            EditorGUI.DrawRect(sliderRect, new Color(0.1f, 0.1f, 0.1f));
        }

        private float CalculateHandlePosition(Rect sliderRect, float value, float minLimit, float maxLimit)
        {
            return Mathf.Lerp(sliderRect.x, sliderRect.xMax, Mathf.InverseLerp(minLimit, maxLimit, value));
        }

        private void DrawGreenFill(float handlePos, Rect sliderRect)
        {
            Rect fillRect = new Rect(sliderRect.x, sliderRect.y, handlePos - sliderRect.x, sliderRect.height);
            EditorGUI.DrawRect(fillRect, new Color(0.1f, 0.8f, 0.1f, 0.5f));
        }

        private void DrawBlueFill(float handlePos, Rect sliderRect, float value, float minLimit, float maxLimit)
        {
            float t = Mathf.InverseLerp(minLimit, maxLimit, value); // Normalize the value to be between 0 and 1
            Color startColor = new Color(0.1f, 0.2f, 0.8f, 0.4f); // Blue
            Color endColor = new Color(0.8f, 0.2f, 0.1f, 0.4f); // Warm color (Red)

            // Interpolate between the startColor (blue) and endColor (red) based on the normalized value 't'
            Color currentColor = Color.Lerp(startColor, endColor, t);

            Rect fillRect = new Rect(sliderRect.x, sliderRect.y, handlePos - sliderRect.x, sliderRect.height);
            EditorGUI.DrawRect(fillRect, currentColor);
        }

        private void DrawHandle(float xPos, float yPos, float width, float height, bool isHovered)
        {
            Color handleColor = isHovered ? Color.white : new Color(0.7f, 0.7f, 0.7f);
            Rect handleRect = new Rect(xPos - width / 2, yPos, width, height);
            EditorGUI.DrawRect(handleRect, handleColor);
        }

        private void DrawValueLabel(float xPos, float yPos, float value)
        {
            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter
            };

            float labelWidth = 50f;
            float labelHeight = 20f;

            Rect labelRect = new Rect(xPos - HandleWidth / 2 - labelWidth + 10f, yPos, labelWidth, labelHeight);
            EditorGUI.LabelField(labelRect, value.ToString("F2"), style);
        }

        private void HandleMouseEvents(ref Vector2 vector, Rect sliderRect, float minLimit, float maxLimit)
        {
            Event evt = Event.current;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (IsMouseOverHandle(evt.mousePosition, CalculateHandlePosition(sliderRect, vector.y, minLimit, maxLimit), sliderRect))
                    {
                        isDraggingValue = true;
                        GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive); // Capture mouse events
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (isDraggingValue)
                    {
                        isDraggingValue = false;
                        GUIUtility.hotControl = 0; // Release control
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (isDraggingValue)
                    {
                        vector.y = GetDraggedValue(evt.mousePosition.x, sliderRect, minLimit, maxLimit);
                        evt.Use();
                    }
                    break;
            }
        }

        private float GetDraggedValue(float mouseX, Rect sliderRect, float minLimit, float maxLimit)
        {
            return Mathf.Lerp(minLimit, maxLimit, Mathf.InverseLerp(sliderRect.x, sliderRect.xMax, mouseX));
        }

        private bool IsMouseOverHandle(Vector2 mousePosition, float handlePosition, Rect sliderRect)
        {
            float hoverArea = HandleWidth * 1.5f;
            return Mathf.Abs(mousePosition.x - handlePosition) < hoverArea / 2
                && mousePosition.y >= sliderRect.y
                && mousePosition.y <= sliderRect.y + sliderRect.height;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return BarHeight + EditorGUIUtility.standardVerticalSpacing;
        }
    }
}
