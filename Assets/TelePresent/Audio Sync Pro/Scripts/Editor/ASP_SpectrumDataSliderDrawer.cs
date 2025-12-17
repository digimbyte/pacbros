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
using System.Reflection;

namespace TelePresent.AudioSyncPro
{
    [CustomPropertyDrawer(typeof(ASP_SpectrumDataSliderAttribute))]
    public class ASP_SpectrumDataSliderDrawer : PropertyDrawer
    {
        private const float HandleWidth = 6f;
        private const float BarHeight = 40f;
        private const float BarPadding = 10f;
        private const float RightMargin = 10f;
        private const int NumberOfBars = 50;
        private const float VerticalSliderWidth = 10f;

        private bool isDraggingHorizontalMin;
        private bool isDraggingHorizontalMax;
        private bool isDraggingHorizontalRange;
        private bool isDraggingVerticalMax;

        private Vector2 initialMousePosition;
        private Vector4 initialRange;

        private const float MinDecibels = -80f;
        private const float MaxDecibels = 0f;
        private const float Sensitivity = 1.0f;

        private float[] smoothedAmplitudes = new float[NumberOfBars];
        private const float SmoothingFactor = .1f;

        // Cached variables to optimize performance
        private float[] reducedSpectrumData = new float[NumberOfBars];

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Vector4)
            {
                EditorGUI.LabelField(position, label.text, "Use Vector4 for SpectrumDataSlider");
                return;
            }

            ASP_SpectrumDataSliderAttribute sliderAttribute = (ASP_SpectrumDataSliderAttribute)attribute;
            Vector4 vector = property.vector4Value;

            float[] spectrumData = GetSpectrumData(property, sliderAttribute.spectrumDataFieldName);

            if (spectrumData == null || spectrumData.Length == 0)
            {
                EditorGUI.LabelField(position, label.text, "Spectrum data not found or empty");
                return;
            }

            ReduceSpectrumDataMelScale(spectrumData);

            // Apply smoothing
            for (int i = 0; i < reducedSpectrumData.Length; i++)
            {
                smoothedAmplitudes[i] = Mathf.Lerp(smoothedAmplitudes[i], reducedSpectrumData[i], 1 - SmoothingFactor);
            }

            position = EditorGUI.PrefixLabel(position, GUIContent.none);
            Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            EditorGUI.LabelField(labelRect, label);

            float totalControlWidth = position.width - EditorGUIUtility.labelWidth - RightMargin;

            // Vertical Slider (left)
            Rect verticalSliderRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, VerticalSliderWidth, BarHeight);
            DrawVerticalSlider(verticalSliderRect, ref vector, sliderAttribute);

            // Horizontal Slider (right, next to the vertical slider)
            Rect horizontalSliderRect = new Rect(verticalSliderRect.xMax + BarPadding, position.y, totalControlWidth - VerticalSliderWidth - BarPadding, BarHeight);
            DrawHorizontalSlider(horizontalSliderRect, smoothedAmplitudes, ref vector);

            property.vector4Value = vector;

            EditorUtility.SetDirty(property.serializedObject.targetObject);
            property.serializedObject.ApplyModifiedProperties();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            RepaintInspector(property);
        }

        private void DrawHorizontalSlider(Rect sliderRect, float[] amplitudes, ref Vector4 vector)
        {
            DrawSliderBackground(sliderRect);

            float sampleRate = AudioSettings.outputSampleRate;
            float minLimit = 0f;
            float maxLimit = sampleRate / 2f;

            float spectrumDataWidth = sliderRect.width / NumberOfBars;

            for (int i = 0; i < amplitudes.Length; i++)
            {
                float amplitude = amplitudes[i];

                // Convert amplitude to decibels using power
                float power = amplitude * amplitude;
                float amplitude_dB = 10f * Mathf.Log10(power + 1e-10f); // Adjusted epsilon to prevent -Infinity

                // Apply sensitivity
                amplitude_dB *= Sensitivity;

                // Normalize amplitude between MinDecibels and MaxDecibels
                float normalizedAmplitude = Mathf.InverseLerp(MinDecibels, MaxDecibels, amplitude_dB);

                // Clamp normalized amplitude to [0,1]
                normalizedAmplitude = Mathf.Clamp01(normalizedAmplitude);

                float barHeight = normalizedAmplitude * BarHeight;

                Rect barRect = new Rect(sliderRect.x + i * spectrumDataWidth, sliderRect.y + BarHeight - barHeight, spectrumDataWidth, barHeight);

                // Only draw the bar if the amplitude is above a small threshold to avoid drawing bars when audio is silent
                if (normalizedAmplitude > 0.001f)
                {
                    EditorGUI.DrawRect(barRect, new Color(0.2f, 1f, 0.5f, 0.8f));
                }
            }

            float minHandlePos = CalculateHandlePosition(sliderRect, vector.x, minLimit, maxLimit);
            float maxHandlePos = CalculateHandlePosition(sliderRect, vector.y, minLimit, maxLimit);

            DrawSelectedRange(minHandlePos, maxHandlePos, sliderRect);

            DrawHandle(minHandlePos, sliderRect.y, HandleWidth, sliderRect.height, IsMouseOverHandle(Event.current.mousePosition, minHandlePos, sliderRect, true));
            DrawHandle(maxHandlePos, sliderRect.y, HandleWidth, sliderRect.height, IsMouseOverHandle(Event.current.mousePosition, maxHandlePos, sliderRect, false));

            HandleHorizontalMouseEvents(ref vector, sliderRect, minHandlePos, maxHandlePos, minLimit, maxLimit);
        }

        private void DrawVerticalSlider(Rect sliderRect, ref Vector4 vector, ASP_SpectrumDataSliderAttribute sliderAttribute)
        {
            DrawSliderBackground(sliderRect);

            // Visual fill based on z value
            float zHandlePos = CalculateVerticalHandlePosition(sliderRect, vector.z, sliderAttribute.verticalMinLimit, sliderAttribute.verticalMaxLimit);
            DrawVerticalFill(zHandlePos, sliderRect);

            // Only one draggable handle for the max value (w)
            float maxHandlePos = CalculateVerticalHandlePosition(sliderRect, vector.w, sliderAttribute.verticalMinLimit, sliderAttribute.verticalMaxLimit);

            // Determine handle color based on comparison of w and z values
            Color handleColor = vector.w < vector.z ? new Color(1f, 0.5f, 0f) : new Color(0.7f, 0.7f, 0.7f);

            DrawVerticalHandle(sliderRect.x, maxHandlePos, sliderRect.width, HandleWidth, IsMouseOverVerticalHandle(Event.current.mousePosition, maxHandlePos, sliderRect), handleColor);

            // Handle mouse events for only the max handle (w)
            HandleSingleVerticalMouseEvents(ref vector, sliderRect, maxHandlePos, sliderAttribute.verticalMinLimit, sliderAttribute.verticalMaxLimit);
        }

        // Drawing functions
        private void DrawSliderBackground(Rect sliderRect)
        {
            EditorGUI.DrawRect(sliderRect, new Color(0.1f, 0.1f, 0.1f));
        }

        private void DrawSelectedRange(float minHandlePos, float maxHandlePos, Rect sliderRect)
        {
            Rect rangeRect = new Rect(minHandlePos, sliderRect.y, maxHandlePos - minHandlePos, sliderRect.height);
            EditorGUI.DrawRect(rangeRect, new Color(0.1f, 0.2f, 0.8f, 0.4f));
        }

        private void DrawHandle(float xPos, float yPos, float width, float height, bool isHovered)
        {
            Color handleColor = isHovered ? Color.white : new Color(0.7f, 0.7f, 0.7f);
            Rect handleRect = new Rect(xPos - width / 2, yPos, width, height);
            EditorGUI.DrawRect(handleRect, handleColor);
        }

        private void DrawVerticalFill(float zHandlePos, Rect sliderRect)
        {
            Rect fillRect = new Rect(sliderRect.x, zHandlePos, sliderRect.width, sliderRect.yMax - zHandlePos);
            EditorGUI.DrawRect(fillRect, Color.green);
        }

        private void DrawVerticalHandle(float xPos, float yPos, float width, float height, bool isHovered, Color handleColor)
        {
            handleColor = isHovered ? Color.white : handleColor;
            Rect handleRect = new Rect(xPos, yPos - height / 2, width, height);
            EditorGUI.DrawRect(handleRect, handleColor);
        }

        private float CalculateHandlePosition(Rect sliderRect, float value, float minLimit, float maxLimit)
        {
            return Mathf.Lerp(sliderRect.x, sliderRect.xMax, Mathf.InverseLerp(minLimit, maxLimit, value));
        }

        private float CalculateVerticalHandlePosition(Rect sliderRect, float value, float minLimit, float maxLimit)
        {
            return Mathf.Lerp(sliderRect.yMax, sliderRect.y, Mathf.InverseLerp(minLimit, maxLimit, value));
        }

        private void HandleHorizontalMouseEvents(ref Vector4 vector, Rect sliderRect, float minHandlePos, float maxHandlePos, float minLimit, float maxLimit)
        {
            Event evt = Event.current;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (sliderRect.Contains(evt.mousePosition))
                    {
                        if (IsMouseOverHandle(evt.mousePosition, minHandlePos, sliderRect, true))
                        {
                            isDraggingHorizontalMin = true;
                            initialMousePosition = evt.mousePosition;
                            initialRange = vector;
                            GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                            evt.Use();
                        }
                        else if (IsMouseOverHandle(evt.mousePosition, maxHandlePos, sliderRect, false))
                        {
                            isDraggingHorizontalMax = true;
                            initialMousePosition = evt.mousePosition;
                            initialRange = vector;
                            GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                            evt.Use();
                        }
                        else if (IsMouseOverRange(evt.mousePosition, minHandlePos, maxHandlePos))
                        {
                            isDraggingHorizontalRange = true;
                            initialMousePosition = evt.mousePosition;
                            initialRange = vector;
                            GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                            evt.Use();
                        }
                    }
                    break;

                case EventType.MouseUp:
                    if (isDraggingHorizontalMin || isDraggingHorizontalMax || isDraggingHorizontalRange)
                    {
                        isDraggingHorizontalMin = false;
                        isDraggingHorizontalMax = false;
                        isDraggingHorizontalRange = false;
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (isDraggingHorizontalMin)
                    {
                        float delta = evt.mousePosition.x - initialMousePosition.x;
                        vector.x = Mathf.Clamp(initialRange.x + delta / sliderRect.width * (maxLimit - minLimit), minLimit, vector.y);
                        evt.Use();
                    }
                    else if (isDraggingHorizontalMax)
                    {
                        float delta = evt.mousePosition.x - initialMousePosition.x;
                        vector.y = Mathf.Clamp(initialRange.y + delta / sliderRect.width * (maxLimit - minLimit), vector.x, maxLimit);
                        evt.Use();
                    }
                    else if (isDraggingHorizontalRange)
                    {
                        float delta = evt.mousePosition.x - initialMousePosition.x;
                        float rangeWidth = initialRange.y - initialRange.x;
                        vector.x = Mathf.Clamp(initialRange.x + delta / sliderRect.width * (maxLimit - minLimit), minLimit, maxLimit - rangeWidth);
                        vector.y = vector.x + rangeWidth;
                        evt.Use();
                    }
                    break;
            }
        }

        private void HandleSingleVerticalMouseEvents(ref Vector4 vector, Rect sliderRect, float maxHandlePos, float minLimit, float maxLimit)
        {
            Event evt = Event.current;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (sliderRect.Contains(evt.mousePosition) || IsMouseOverVerticalHandle(evt.mousePosition, maxHandlePos, sliderRect))
                    {
                        isDraggingVerticalMax = true;
                        initialMousePosition = evt.mousePosition;
                        initialRange = vector;
                        GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (isDraggingVerticalMax)
                    {
                        isDraggingVerticalMax = false;
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (isDraggingVerticalMax)
                    {
                        float delta = evt.mousePosition.y - initialMousePosition.y;
                        vector.w = Mathf.Clamp(initialRange.w - delta / sliderRect.height * (maxLimit - minLimit), minLimit, maxLimit);
                        evt.Use();
                    }
                    break;
            }
        }

        private bool IsMouseOverHandle(Vector2 mousePosition, float handlePosition, Rect sliderRect, bool isMinHandle)
        {
            float hoverArea = HandleWidth * 1.5f;

            if (isMinHandle)
            {
                return mousePosition.x >= handlePosition - hoverArea
                    && mousePosition.x <= handlePosition + hoverArea / 2
                    && mousePosition.y >= sliderRect.y
                    && mousePosition.y <= sliderRect.y + sliderRect.height;
            }
            else
            {
                return mousePosition.x >= handlePosition - hoverArea / 2
                    && mousePosition.x <= handlePosition + hoverArea
                    && mousePosition.y >= sliderRect.y
                    && mousePosition.y <= sliderRect.y + sliderRect.height;
            }
        }

        private bool IsMouseOverRange(Vector2 mousePosition, float minHandlePos, float maxHandlePos)
        {
            return mousePosition.x > minHandlePos && mousePosition.x < maxHandlePos;
        }

        private bool IsMouseOverVerticalHandle(Vector2 mousePosition, float handlePosition, Rect sliderRect)
        {
            float hoverArea = HandleWidth * 1.5f;
            return Mathf.Abs(mousePosition.y - handlePosition) < hoverArea / 2
                && mousePosition.x >= sliderRect.x
                && mousePosition.x <= sliderRect.x + sliderRect.width;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return BarHeight + EditorGUIUtility.standardVerticalSpacing;
        }

        private float[] GetSpectrumData(SerializedProperty property, string fieldName)
        {
            object targetObject = property.serializedObject.targetObject;
            FieldInfo field = targetObject.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field?.GetValue(targetObject) as float[];
        }

        private void ReduceSpectrumDataMelScale(float[] spectrumData)
        {
            int spectrumLength = spectrumData.Length;
            float sampleRate = AudioSettings.outputSampleRate;
            float maxFrequency = sampleRate / 2f;

            // Compute Mel frequency boundaries
            float minMel = HzToMel(0f); // Starting from 0 Hz
            float maxMel = HzToMel(maxFrequency);
            float deltaMel = (maxMel - minMel) / NumberOfBars;

            for (int i = 0; i < NumberOfBars; i++)
            {
                float melLow = minMel + i * deltaMel;
                float melHigh = melLow + deltaMel;

                float freqLow = MelToHz(melLow);
                float freqHigh = MelToHz(melHigh);

                int indexLow = Mathf.Clamp(Mathf.FloorToInt(freqLow / maxFrequency * spectrumLength), 0, spectrumLength - 1);
                int indexHigh = Mathf.Clamp(Mathf.CeilToInt(freqHigh / maxFrequency * spectrumLength), 0, spectrumLength - 1);

                float sum = 0;
                int count = 0;

                for (int j = indexLow; j <= indexHigh; j++)
                {
                    sum += spectrumData[j];
                    count++;
                }

                reducedSpectrumData[i] = count > 0 ? sum / count : 0f;
            }
        }

        private float HzToMel(float hz)
        {
            return 2595f * Mathf.Log10(1f + hz / 700f);
        }

        private float MelToHz(float mel)
        {
            return 700f * (Mathf.Pow(10f, mel / 2595f) - 1f);
        }

        private void RepaintInspector(SerializedProperty property)
        {
            if (property.serializedObject.targetObject != null)
            {
                EditorUtility.SetDirty(property.serializedObject.targetObject);
                EditorApplication.update += RepaintInspectorWindow;
            }
        }

        private void RepaintInspectorWindow()
        {
            EditorApplication.update -= RepaintInspectorWindow;

            if (EditorWindow.focusedWindow != null)
            {
                EditorWindow.focusedWindow.Repaint();
            }
        }
    }
}
