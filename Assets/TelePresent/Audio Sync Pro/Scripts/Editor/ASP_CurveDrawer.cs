/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using UnityEditor;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{

    public static class ASP_CurveDrawer
    {
        private const float AnchorSize = 6.0f;
        private const float SaturationIncrease = 0.3f;

        public static void DrawCurve(AnimationCurve curve, Color baseColor, Rect rect, bool isSelected, float maxDistance, string labelFormat)
        {
            if (curve == null) return;

            const int segmentCount = 500;
            Vector3[] points = new Vector3[segmentCount];

            for (int i = 0; i < segmentCount; i++)
            {
                float t = (float)i / segmentCount;
                float x = rect.x + (t * maxDistance / maxDistance) * rect.width;
                float evaluatedY = curve.Evaluate(t) * rect.height;
                float y = rect.y + rect.height - Mathf.Clamp(evaluatedY, 0, rect.height);

                points[i] = new Vector3(x, y, 0);
            }

            Color drawColor = isSelected ? IncreaseSaturation(baseColor, SaturationIncrease) : baseColor;

            float lineWidth = isSelected ? 8f : 4f;
            Handles.color = drawColor;
            Handles.DrawAAPolyLine(lineWidth, points);

            DrawKeyframes(curve, drawColor, rect, isSelected, maxDistance, labelFormat);
        }

        public static void DrawAxisLabels(Rect rect, float maxDistance)
        {
            const float verticalPadding = -5f; // Vertical padding value
            const float horizontalPadding = 0f; // Horizontal padding value
            Color labelColor = new Color(1f, 1f, 1f, 0.5f); // White color with 50% transparency

            for (int i = 0; i <= 10; i++)
            {
                float yValue = (float)i / 10f;
                Vector2 labelPosition = new Vector2(rect.x - 10 - horizontalPadding, rect.y + rect.height - (yValue * rect.height));
                Handles.color = labelColor;
                Handles.Label(new Vector2(labelPosition.x + 10, labelPosition.y), yValue.ToString("F1"));
            }

            for (int i = 0; i <= 10; i++)
            {
                float xValue = (float)i / 10f * maxDistance;
                Vector2 labelPosition = new Vector2(rect.x + (xValue / maxDistance * rect.width), rect.y + rect.height + 5 + verticalPadding);
                Handles.color = labelColor;
                Handles.Label(new Vector2(labelPosition.x, labelPosition.y - 10), xValue.ToString("F1"));
            }
        }
        public static void DrawKeyframes(AnimationCurve curve, Color color, Rect rect, bool isSelected, float maxDistance, string labelFormat)
        {
            for (int i = 0; i < curve.length; i++)
            {
                Vector2 keyPos = new Vector2(curve[i].time * rect.width + rect.x, rect.y + rect.height - (curve[i].value * rect.height));
                Rect keyRect = new Rect(keyPos.x - 4, keyPos.y - 4, 8, 8);
                EditorGUI.DrawRect(keyRect, color);

                if (isSelected && i == ASP_CurveEventHandler.selectedKeyIndex)
                {
                    DrawKeyLabel(curve[i], rect, maxDistance, labelFormat);
                    DrawTangentAnchors(curve[i], rect, color);
                }
            }
        }
        public static void DrawAxisLabelsOutsideRect(Rect rect, float maxDistance)
        {
            const float labelOffset = 15f; // Offset for the labels from the curve rect

            // Draw vertical axis labels on the left side of the rect
            for (int i = 0; i <= 9; i++)
            {
                float yValue = (float)i / 10f;
                Vector2 labelPosition = new Vector2(rect.x - labelOffset, rect.y + rect.height - (yValue * rect.height));
                Handles.Label(labelPosition, yValue.ToString("F1"));
            }

            // Draw horizontal axis labels below the rect
            for (int i = 0; i <= 9; i++)
            {
                float xValue = (float)i / 10f * maxDistance;
                Vector2 labelPosition = new Vector2(rect.x + (xValue / maxDistance * rect.width), rect.y + rect.height + labelOffset);
                Handles.Label(labelPosition, xValue.ToString("F1"));
            }
        }

        public static void DrawKeyLabel(Keyframe key, Rect rect, float maxDistance, string labelFormat)
        {
            Vector2 keyPosition = new Vector2(key.time * rect.width + rect.x, rect.y + rect.height - (key.value * rect.height));

            // Customize label text based on format
            string labelText;
            if (labelFormat.Contains("Degrees"))
            {
                labelText = $"Degrees Spread: {key.value * 360:F1}";
            }
            else if (labelFormat.Contains("Distance"))
            {
                labelText = $"Distance: {key.time * maxDistance:F1}";
            }
            else if (labelFormat.Contains("Reverb"))
            {
                labelText = $"Reverb: {key.value:F1}";
            }
            else if (labelFormat.Contains("Spatial Blend"))
            {
                labelText = $"Spatial Blend / / (0 = 2D, 1 = 3D: {key.value:F1}";
            }
            else
            {
                labelText = string.Format(labelFormat, key.time * maxDistance, key.value);
            }

            // Replace '/' with newline character '\n'
            labelText = labelText.Replace("/", "\n");

            // Calculate label size
            GUIStyle labelStyle = new GUIStyle();
            labelStyle.wordWrap = true; // Enable word wrap
            labelStyle.normal.textColor = Color.white; // Set text color to white
            Vector2 labelSize = labelStyle.CalcSize(new GUIContent(labelText));

            // Adjust position if the label goes out of bounds
            float adjustedX = keyPosition.x + 10;
            float adjustedY = keyPosition.y - 20;

            // If the label goes too far right, move it left
            if (adjustedX + labelSize.x > rect.x + rect.width)
            {
                adjustedX = rect.x + rect.width - labelSize.x;
            }
            // If the label goes too far left, move it right
            if (adjustedX < rect.x)
            {
                adjustedX = rect.x;
            }
            // If the label goes too high, move it down
            if (adjustedY < rect.y)
            {
                adjustedY = keyPosition.y + 20;
            }
            // If the label goes too low, move it up
            if (adjustedY + labelSize.y > rect.y + rect.height)
            {
                adjustedY = rect.y + rect.height - labelSize.y;
            }

            // Draw the label at the adjusted position
            Rect labelRect = new Rect(adjustedX, adjustedY, labelSize.x, labelSize.y);
            GUI.Label(labelRect, labelText, labelStyle);
        }


        private static void DrawTangentAnchors(Keyframe key, Rect rect, Color color)
        {
            Vector2 inTangentPos = GetAnchorPosition(key, rect, true);
            Vector2 outTangentPos = GetAnchorPosition(key, rect, false);
            DrawAnchorPoint(inTangentPos, color);
            DrawAnchorPoint(outTangentPos, color);
        }

        private static Vector2 GetAnchorPosition(Keyframe key, Rect rect, bool isInTangent)
        {
            float tangent = isInTangent ? key.inTangent : key.outTangent;
            float offsetMultiplier = 2f;
            float dx = isInTangent ? -offsetMultiplier : offsetMultiplier;
            float dy = tangent * dx;

            float anchorTime = key.time + dx;
            float anchorValue = key.value + dy;

            float x = rect.x + (anchorTime * rect.width);
            float y = rect.y + rect.height - (anchorValue * rect.height);

            return new Vector2(x, y);
        }

        private static void DrawAnchorPoint(Vector2 position, Color color)
        {
            Rect anchorRect = new Rect(position.x - AnchorSize / 2, position.y - AnchorSize / 2, AnchorSize, AnchorSize);
            EditorGUI.DrawRect(anchorRect, color);
        }

        private static Color IncreaseSaturation(Color color, float increase)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            s = Mathf.Clamp01(s + increase);
            return Color.HSVToRGB(h, s, v);
        }
    }
}