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
using System.Collections.Generic;

namespace TelePresent.AudioSyncPro
{
    public class ASP_CustomCurveEditor : EditorWindow
    {
        [SerializeField]
        private float maxDistance = 500f;

        public static AudioSourcePlus audioSourcePlus;
        public static ASP_AudioWaveformEditor audioWaveformEditor;

        private bool[] showCurves;
        private Texture2D gradientTexture;
        private AudioRolloffMode currentRolloffMode;

        private const float Margin = 20.0f;
        private const float Padding = 15.0f;
        private const float ButtonHeight = 30.0f;
        private const float GraphHeight = 400f; 
        private const float FloatFieldWidth = 120f; // Width of the float field

        private static readonly string[] curveNames = { "Custom Rolloff", "Spatial Blend", "Spread", "Reverb Zone Mix" };

        public static void ShowWindow(AudioSourcePlus sourcePlus)
        {
            audioSourcePlus = sourcePlus;

            if (audioSourcePlus?.audioSource != null)
            {
                audioSourcePlus.audioSource.rolloffMode = AudioRolloffMode.Custom;
                Debug.Log("AudioSource rolloff mode set to Custom Rolloff.");
            }
            else
            {
                Debug.LogError("AudioSource is null. Cannot set rolloff mode.");
            }

            GetWindow<ASP_CustomCurveEditor>("Custom Curve Editor");
        }

        private void OnEnable()
        {
            if (audioSourcePlus == null)
            {
                Debug.LogWarning("audioSourcePlus was null in OnEnable.");
                return;
            }

            if (audioSourcePlus?.audioSource != null)
            {
                showCurves = new bool[audioSourcePlus.audioCurves.Count];
                for (int i = 0; i < showCurves.Length; i++)
                {
                    showCurves[i] = true;
                }
                CreateGradientTexture();
                currentRolloffMode = audioSourcePlus.audioSource.rolloffMode;
                maxDistance = audioSourcePlus.audioSource.maxDistance;
            }
            else
            {
                Debug.LogError("audioSourcePlus.audioSource is null in OnEnable.");
            }
        }

        private void CreateGradientTexture()
        {
            const int width = 1;
            const int height = 16;

            gradientTexture = new Texture2D(width, height);
            for (int y = 0; y < height; y++)
            {
                float t = (float)y / (height - 1);
                Color color = Color.Lerp(Color.black, new Color(0.15f, 0.15f, 0.15f), t);
                gradientTexture.SetPixel(0, y, color);
            }
            gradientTexture.Apply();
        }

        private void OnGUI()
        {
            if (audioSourcePlus == null || audioSourcePlus.audioSource == null)
            {
                EditorGUILayout.HelpBox("AudioSourcePlus or its AudioSource is not assigned.", MessageType.Error);
                return;
            }

            GUILayout.Label("Curve Editor", EditorStyles.boldLabel);

            float graphWidth = position.width - 2 * Margin;

            Rect curveRect = new Rect(Margin, Margin, graphWidth, GraphHeight);
            GUI.DrawTexture(curveRect, gradientTexture, ScaleMode.StretchToFill);

            Rect paddedRect = new Rect(curveRect.x + Padding, curveRect.y + Padding, curveRect.width - 2 * Padding, curveRect.height - 2 * Padding);

            GUI.BeginGroup(paddedRect);
            {
                Rect localRect = new Rect(0, 0, paddedRect.width, paddedRect.height);

                if (Event.current.type == EventType.Repaint)
                {
                    for (int i = 0; i < audioSourcePlus.audioCurves.Count; i++)
                    {
                        if (showCurves[i])
                        {
                            bool isSelected = (audioSourcePlus.audioCurves[i] == ASP_CurveEventHandler.selectedCurve);

                            string labelFormat = i switch
                            {
                                0 => "Time: {0:F1} Meters, Value: {1:F1}",
                                1 => "X-Value: {0:F1}, Y-Value: {1:F1}",
                                2 => "Y-Value: {1:F1} Degrees, X-Position: {0:F1}",
                                3 => "Reverb: {1:F1}",
                                _ => "Time: {0:F1}, Value: {1:F1}",
                            };

                            ASP_CurveDrawer.DrawCurve(
                                audioSourcePlus.audioCurves[i],
                                Color.HSVToRGB((float)i / audioSourcePlus.audioCurves.Count, 1.0f, 1.25f),
                                localRect,
                                isSelected,
                                maxDistance,
                                labelFormat
                            );
                        }
                    }
                    ASP_CurveDrawer.DrawAxisLabels(localRect, maxDistance);
                }
                ASP_CurveEventHandler.HandleCurveEvents(localRect, audioSourcePlus.audioCurves);
            }
            GUI.EndGroup();

            GUILayout.Space(GraphHeight + 20);

            GUILayout.BeginHorizontal();
            GUILayout.Space(Margin);
            for (int i = 0; i < audioSourcePlus.audioCurves.Count; i++)
            {
                bool previousState = showCurves[i];
                GUI.backgroundColor = showCurves[i] ? Color.HSVToRGB((float)i / audioSourcePlus.audioCurves.Count, 0.5f, 1.0f) : Color.white;
                showCurves[i] = GUILayout.Toggle(showCurves[i], curveNames[i], "Button", GUILayout.Height(ButtonHeight));

                if (previousState != showCurves[i])
                {
                    Repaint();
                }

                UpdateAudioSourceCurve(i);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(Margin);
            GUILayout.EndHorizontal();

            GUILayout.Space(20);

            GUILayout.BeginHorizontal();
            GUILayout.Space(Margin);

            if (GUILayout.Button($"Toggle Rolloff Mode: {currentRolloffMode}", GUILayout.Height(ButtonHeight)))
            {
                Undo.RecordObject(audioSourcePlus, "Toggle Rolloff Mode");
                ToggleRolloffMode();
                EditorUtility.SetDirty(audioSourcePlus.audioSource);
            }

            GUILayout.Space(Margin);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Space(Margin);

            GUILayout.BeginVertical();
            GUILayout.Label("Max Distance", GUILayout.Width(150));

            Rect floatFieldRect = GUILayoutUtility.GetRect(150, 18, GUILayout.Width(150));
            float previousMaxDistance = maxDistance;
            maxDistance = EditorGUI.FloatField(floatFieldRect, maxDistance);

            if (maxDistance != previousMaxDistance)
            {
                Undo.RecordObject(audioSourcePlus, "Change Max Distance");
                audioSourcePlus.audioSource.maxDistance = maxDistance;
                EditorUtility.SetDirty(audioSourcePlus.audioSource);
            }

            GUILayout.EndVertical();
            GUILayout.Space(Margin);
            GUILayout.EndHorizontal();

            GUILayout.Space(20);
        }

        private void ToggleRolloffMode()
        {
            if (audioSourcePlus == null || audioSourcePlus.audioSource == null) return;

            switch (currentRolloffMode)
            {
                case AudioRolloffMode.Logarithmic:
                    currentRolloffMode = AudioRolloffMode.Linear;
                    LoadLinearRolloffCurve();
                    break;
                case AudioRolloffMode.Linear:
                    currentRolloffMode = AudioRolloffMode.Custom;
                    LoadCustomRolloffCurve();
                    break;
                case AudioRolloffMode.Custom:
                    currentRolloffMode = AudioRolloffMode.Logarithmic;
                    LoadLogarithmicRolloffCurve();
                    break;
            }

            audioSourcePlus.audioSource.rolloffMode = currentRolloffMode;
            Repaint();
        }

        private void LoadLinearRolloffCurve()
        {
            if (audioSourcePlus == null || audioSourcePlus.audioSource == null) return;

            AnimationCurve linearCurve = new AnimationCurve(
                new Keyframe(0, 1) { inTangent = -1f, outTangent = -1f },
                new Keyframe(1, 0) { inTangent = -1f, outTangent = -1f }
            );

            Undo.RecordObject(audioSourcePlus, "Load Linear Rolloff Curve");
            audioSourcePlus.audioCurves[0] = linearCurve;
            audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, linearCurve);
            EditorUtility.SetDirty(audioSourcePlus.audioSource);
        }

        private void LoadLogarithmicRolloffCurve()
        {
            if (audioSourcePlus == null || audioSourcePlus.audioSource == null) return;

            AnimationCurve logarithmicCurve = new AnimationCurve(
                new Keyframe(0, 1),
                new Keyframe(0.01f, 0.7f),
                new Keyframe(0.025f, 0.4f),
                new Keyframe(0.05f, 0.2f),
                new Keyframe(0.08f, 0.1f),
                new Keyframe(0.11f, 0.07f),
                new Keyframe(0.3f, 0.03f),
                new Keyframe(1, 0f)
            );

            for (int i = 0; i < logarithmicCurve.keys.Length; i++)
            {
                logarithmicCurve.SmoothTangents(i, 0.0f);
            }

            Undo.RecordObject(audioSourcePlus, "Load Logarithmic Rolloff Curve");
            audioSourcePlus.audioCurves[0] = logarithmicCurve;
            audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, logarithmicCurve);
            EditorUtility.SetDirty(audioSourcePlus.audioSource);
        }

        private void LoadCustomRolloffCurve()
        {
            if (audioSourcePlus == null || audioSourcePlus.audioSource == null) return;

            AnimationCurve customCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0));
            Undo.RecordObject(audioSourcePlus, "Load Custom Rolloff Curve");
            audioSourcePlus.audioCurves[0] = customCurve;
            audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, customCurve);
            EditorUtility.SetDirty(audioSourcePlus.audioSource);
        }

        private void UpdateAudioSourceCurve(int index)
        {
            if (audioSourcePlus == null || audioSourcePlus.audioSource == null) return;

            if (audioSourcePlus.audioCurves[index] == null || audioSourcePlus.audioCurves[index].length == 0)
            {
                audioSourcePlus.audioCurves[index].AddKey(new Keyframe(0, 1));
            }

            switch (index)
            {
                case 0:
                    Undo.RecordObject(audioSourcePlus, "Update Custom Rolloff Curve");
                    audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, audioSourcePlus.audioCurves[index]);
                    break;
                case 1:
                    Undo.RecordObject(audioSourcePlus, "Update Spatial Blend Curve");
                    audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.SpatialBlend, audioSourcePlus.audioCurves[index]);
                    break;
                case 2:
                    Undo.RecordObject(audioSourcePlus, "Update Spread Curve");
                    audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.Spread, audioSourcePlus.audioCurves[index]);
                    break;
                case 3:
                    Undo.RecordObject(audioSourcePlus, "Update Reverb Zone Mix Curve");
                    audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, audioSourcePlus.audioCurves[index]);
                    break;
            }

            EditorUtility.SetDirty(audioSourcePlus.audioSource);
        }

        private void OnDisable()
        {
            if (gradientTexture != null)
            {
                DestroyImmediate(gradientTexture);
            }
        }
    }
}