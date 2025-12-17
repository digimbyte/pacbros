using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    public static class ASP_WaveformGenerator
    {
        public static float[] GenerateWaveform(AudioClip audioClip, int width)
        {
            float[] samples = new float[audioClip.samples * audioClip.channels];
            audioClip.GetData(samples, 0);

            int packSize = Mathf.CeilToInt((float)samples.Length / width);
            float[] waveform = new float[width];

            for (int i = 0; i < width; i++)
            {
                float sum = 0f;
                int startIndex = i * packSize;
                int packCount = Mathf.Min(packSize, samples.Length - startIndex);

                for (int j = 0; j < packCount; j++)
                {
                    sum += Mathf.Abs(samples[startIndex + j]);
                }
                waveform[i] = sum / packCount;
            }

            return NormalizeWaveform(waveform, 0.05f, 0.85f);
        }

        private static float[] NormalizeWaveform(float[] waveform, float minValue, float maxValue)
        {
            float min = Mathf.Min(waveform);
            float max = Mathf.Max(waveform);

            float range = max - min;
            float targetRange = maxValue - minValue;

            for (int i = 0; i < waveform.Length; i++)
            {
                waveform[i] = minValue + ((waveform[i] - min) / range) * targetRange;
            }

            return waveform;
        }
    }

    public static class WaveformDrawer
    {
        public static void DrawBackground(Rect rect, Color gradientStart, Color gradientEnd)
        {
            Texture2D gradientTexture = new Texture2D(1, (int)rect.height);
            for (int y = 0; y < rect.height; y++)
            {
                Color gradientColor = Color.Lerp(gradientEnd, gradientStart, y / rect.height);
                gradientTexture.SetPixel(0, y, gradientColor);
            }
            gradientTexture.Apply();

            EditorGUI.DrawPreviewTexture(rect, gradientTexture);
            Object.DestroyImmediate(gradientTexture);
        }

        public static void DrawGrid(Rect rect, float zoomLevel, Color lineColor = default(Color))
        {
            lineColor = lineColor == default(Color) ? new Color(0.7f, 0.7f, 0.7f, 0.3f) : lineColor;
            float lineSpacing = Mathf.Max(Mathf.RoundToInt(20f / zoomLevel), 15f);

            for (float x = 0; x < rect.width; x += lineSpacing)
            {
                EditorGUI.DrawRect(new Rect(rect.x + x, rect.y, 1, rect.height), lineColor);
            }
        }

        public static void DrawWaveform(Rect rect, float[] waveform, Color outlineColor, Color fillColor, bool fill = true, float simplificationFactor = 1.1f, float simplificationThreshold = 0.0001f)
        {
            if (waveform == null || waveform.Length < 2)
            {
                return;
            }

            Vector3[] linePoints = CalculateCombinedWaveformLinePoints(rect, waveform, simplificationFactor, simplificationThreshold);

            if (fill)
            {
                DrawWaveformFill(rect, linePoints, fillColor);
            }

            Handles.BeginGUI();
            Handles.color = outlineColor;
            Handles.DrawAAPolyLine(3.0f, linePoints);
            Handles.EndGUI();
        }

        private static Vector3[] CalculateCombinedWaveformLinePoints(Rect rect, float[] waveform, float simplificationFactor, float simplificationThreshold)
        {
            int simplifiedLength = Mathf.CeilToInt(waveform.Length / simplificationFactor);
            List<Vector3> linePoints = new List<Vector3>();
            float widthScale = rect.width / (simplifiedLength - 1);

            int previousIndex = 0;
            float x = rect.x;
            float y = rect.y + (1f - waveform[0]) * rect.height;
            linePoints.Add(new Vector3(x, y, 0));

            for (int i = 1; i < simplifiedLength; i++)
            {
                int index = Mathf.Min(Mathf.RoundToInt(i * simplificationFactor), waveform.Length - 1);
                float difference = Mathf.Abs(waveform[index] - waveform[previousIndex]);

                if (difference >= simplificationThreshold)
                {
                    x = rect.x + i * widthScale;
                    y = rect.y + (1f - waveform[index]) * rect.height;
                    linePoints.Add(new Vector3(x, y, 0));
                    previousIndex = index;
                }
            }

            if (linePoints.Count == 0 || linePoints[linePoints.Count - 1].x < rect.xMax)
            {
                x = rect.x + (waveform.Length - 1) * widthScale;
                y = rect.y + (1f - waveform[waveform.Length - 1]) * rect.height;
                linePoints.Add(new Vector3(x, y, 0));
            }

            return linePoints.ToArray();
        }

        private static void DrawWaveformFill(Rect rect, Vector3[] linePoints, Color fillColor)
        {
            Handles.BeginGUI();
            Handles.color = fillColor;

            for (int i = 0; i < linePoints.Length - 1; i++)
            {
                Vector3 start = linePoints[i];
                Vector3 end = linePoints[i + 1];

                Vector3[] rectangleVertices = new Vector3[]
                {
                    new Vector3(start.x, rect.yMax, 0),
                    new Vector3(end.x, rect.yMax, 0),
                    end,
                    start
                };

                Handles.DrawSolidRectangleWithOutline(rectangleVertices, fillColor, Color.clear);
            }

            Handles.EndGUI();
        }

        private static Color IntensifyAndDarkenColor(Color color, float intensityFactor)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            s = Mathf.Clamp01(s + (1f - s) * intensityFactor);
            v = Mathf.Clamp01(v - (0.2f * intensityFactor));
            Color intensifiedColor = Color.HSVToRGB(h, s, v);
            intensifiedColor.a = color.a;
            return intensifiedColor;
        }
    }
}
