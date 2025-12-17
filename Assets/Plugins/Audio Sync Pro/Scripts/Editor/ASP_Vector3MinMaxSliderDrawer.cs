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
    [CustomPropertyDrawer(typeof(ASP_MinMaxSliderAttribute))]
    public class ASP_Vector3MinMaxSliderDrawer : PropertyDrawer
    {
        private const float HandleWidth = 6f;
        private const float BarHeight = 20f;
        private const float BarPadding = 0f;
        private const float RightMargin = 10f; 

        private bool isDraggingMin;
        private bool isDraggingMax;
        private bool isDraggingRange;
        private float rangeOffset;

        public ASP_Vector3MinMaxSliderDrawer()
        {
            EditorApplication.update += Repaint;
        }

        ~ASP_Vector3MinMaxSliderDrawer()
        {
            EditorApplication.update -= Repaint;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Vector3)
            {
                EditorGUI.LabelField(position, label.text, "Use Vector3 for Min-Max Slider");
                return;
            }

            ASP_MinMaxSliderAttribute minMaxSlider = (ASP_MinMaxSliderAttribute)attribute;
            Vector3 vector = property.vector3Value;

            // Always draw the label to the left of the control
            position = EditorGUI.PrefixLabel(position, GUIContent.none);
            Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            EditorGUI.LabelField(labelRect, label); // Draw the label

            Rect controlRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, position.width - EditorGUIUtility.labelWidth - RightMargin, position.height);
            Rect sliderRect = new Rect(controlRect.x + BarPadding, controlRect.y, controlRect.width - 2 * BarPadding, BarHeight);

            DrawSliderBackground(sliderRect);

            float minHandlePos = CalculateHandlePosition(sliderRect, vector.y, minMaxSlider.minLimit, minMaxSlider.maxLimit);
            float maxHandlePos = CalculateHandlePosition(sliderRect, vector.z, minMaxSlider.minLimit, minMaxSlider.maxLimit);
            float fillHandlePos = CalculateHandlePosition(sliderRect, vector.x, minMaxSlider.minLimit, minMaxSlider.maxLimit);

            // Draw the green fill bar first
            DrawFillRange(fillHandlePos, sliderRect);
            // Draw the selected blue range above the green fill
            DrawSelectedRange(minHandlePos, maxHandlePos, sliderRect);

            DrawHandle(minHandlePos, sliderRect.y, HandleWidth, sliderRect.height, IsMouseOverHandle(Event.current.mousePosition, minHandlePos, sliderRect));
            DrawHandle(maxHandlePos, sliderRect.y, HandleWidth, sliderRect.height, IsMouseOverHandle(Event.current.mousePosition, maxHandlePos, sliderRect));

            if (IsMouseOverHandle(Event.current.mousePosition, minHandlePos, sliderRect) || isDraggingMin)
                DrawValueLabel(minHandlePos, sliderRect.y, vector.y, true);
            if (IsMouseOverHandle(Event.current.mousePosition, maxHandlePos, sliderRect) || isDraggingMax)
                DrawValueLabel(maxHandlePos, sliderRect.y, vector.z, false);

            HandleMouseEvents(ref vector, sliderRect, minHandlePos, maxHandlePos, minMaxSlider.minLimit, minMaxSlider.maxLimit);

            vector.y = Mathf.Clamp(vector.y, minMaxSlider.minLimit, vector.z);
            vector.z = Mathf.Clamp(vector.z, vector.y, minMaxSlider.maxLimit);

            property.vector3Value = vector;
        }

        private void DrawSliderBackground(Rect sliderRect)
        {
            EditorGUI.DrawRect(sliderRect, new Color(0.1f, 0.1f, 0.1f));
        }

        private float CalculateHandlePosition(Rect sliderRect, float value, float minLimit, float maxLimit)
        {
            return Mathf.Lerp(sliderRect.x, sliderRect.xMax, Mathf.InverseLerp(minLimit, maxLimit, value));
        }

        private void DrawSelectedRange(float minHandlePos, float maxHandlePos, Rect sliderRect)
        {
            Rect rangeRect = new Rect(minHandlePos, sliderRect.y, maxHandlePos - minHandlePos, sliderRect.height);
            EditorGUI.DrawRect(rangeRect, new Color(0.1f, 0.2f, 0.8f, 0.4f));
        }

        private void DrawFillRange(float fillHandlePos, Rect sliderRect)
        {
            Rect fillRect = new Rect(sliderRect.x, sliderRect.y, fillHandlePos - sliderRect.x, sliderRect.height);
            EditorGUI.DrawRect(fillRect, new Color(0.1f, 0.8f, 0.1f, 0.5f));
        }

        private void DrawHandle(float xPos, float yPos, float width, float height, bool isHovered)
        {
            Color handleColor = isHovered ? Color.white : new Color(0.7f, 0.7f, 0.7f);
            Rect handleRect = new Rect(xPos - width / 2, yPos, width, height);
            EditorGUI.DrawRect(handleRect, handleColor);
        }

        private void DrawValueLabel(float xPos, float yPos, float value, bool isLeftHandle)
        {
            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter
            };

            float labelWidth = 50f;
            float labelHeight = 20f;

            Rect labelRect = isLeftHandle
                ? new Rect(xPos + HandleWidth / 2 - 10f, yPos, labelWidth, labelHeight)
                : new Rect(xPos - HandleWidth / 2 - labelWidth + 10f, yPos, labelWidth, labelHeight);

            EditorGUI.LabelField(labelRect, value.ToString("F2"), style);
        }

        private void HandleMouseEvents(ref Vector3 vector, Rect sliderRect, float minHandlePos, float maxHandlePos, float minLimit, float maxLimit)
        {
            Event evt = Event.current;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (IsMouseOverHandle(evt.mousePosition, minHandlePos, sliderRect))
                    {
                        isDraggingMin = true;
                        GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                        evt.Use();
                    }
                    else if (IsMouseOverHandle(evt.mousePosition, maxHandlePos, sliderRect))
                    {
                        isDraggingMax = true;
                        GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive); 
                        evt.Use();
                    }
                    else if (IsMouseOverRange(evt.mousePosition, minHandlePos, maxHandlePos, sliderRect))
                    {
                        isDraggingRange = true;
                        rangeOffset = evt.mousePosition.x - minHandlePos;
                        GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (isDraggingMin || isDraggingMax || isDraggingRange)
                    {
                        isDraggingMin = isDraggingMax = isDraggingRange = false;
                        GUIUtility.hotControl = 0; // Release control
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (isDraggingMin)
                    {
                        vector.y = GetDraggedValue(evt.mousePosition.x, sliderRect, minLimit, maxLimit, vector.z);
                        evt.Use();
                    }
                    else if (isDraggingMax)
                    {
                        vector.z = GetDraggedValue(evt.mousePosition.x, sliderRect, minLimit, maxLimit, maxLimit, vector.y);
                        evt.Use();
                    }
                    else if (isDraggingRange)
                    {
                        DragRange(ref vector, evt.mousePosition.x, sliderRect, minLimit, maxLimit);
                        evt.Use();
                    }
                    break;
            }
        }

        private float GetDraggedValue(float mouseX, Rect sliderRect, float minLimit, float maxLimit, float upperBound, float lowerBound = float.MinValue)
        {
            float value = Mathf.Lerp(minLimit, maxLimit, Mathf.InverseLerp(sliderRect.x, sliderRect.xMax, mouseX));
            return Mathf.Clamp(value, lowerBound, upperBound);
        }

        private void DragRange(ref Vector3 vector, float mouseX, Rect sliderRect, float minLimit, float maxLimit)
        {
            float rangeWidth = vector.z - vector.y;
            float newMinValue = Mathf.Lerp(minLimit, maxLimit, Mathf.InverseLerp(sliderRect.x, sliderRect.xMax, mouseX - rangeOffset));
            newMinValue = Mathf.Clamp(newMinValue, minLimit, maxLimit - rangeWidth);
            vector.y = newMinValue;
            vector.z = newMinValue + rangeWidth;
        }

        private bool IsMouseOverHandle(Vector2 mousePosition, float handlePosition, Rect sliderRect)
        {
            float hoverArea = HandleWidth * 1.5f;
            return Mathf.Abs(mousePosition.x - handlePosition) < hoverArea / 2
                && mousePosition.y >= sliderRect.y
                && mousePosition.y <= sliderRect.y + sliderRect.height;
        }

        private bool IsMouseOverRange(Vector2 mousePosition, float minHandlePos, float maxHandlePos, Rect sliderRect)
        {
            return mousePosition.x > minHandlePos + HandleWidth / 2
                && mousePosition.x < maxHandlePos - HandleWidth / 2
                && mousePosition.y >= sliderRect.y
                && mousePosition.y <= sliderRect.y + sliderRect.height;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return BarHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        private void Repaint()
        {
            if (EditorWindow.focusedWindow != null)
            {
                EditorWindow.focusedWindow.Repaint();
            }
        }
    }
}
