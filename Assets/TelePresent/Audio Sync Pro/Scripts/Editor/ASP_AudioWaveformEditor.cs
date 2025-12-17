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
using System;

namespace TelePresent.AudioSyncPro
{
    [CustomEditor(typeof(AudioSourcePlus))]
    public class ASP_AudioWaveformEditor : Editor
    {
        private const float CurveHeight = 300f;
        private const float Padding = 10f;
        private const float Margin = 5f;
        private const float ButtonHeight = 30f;

        private bool showPlaybackSettings;
        private bool showSoundSettings;
        private bool show3DSettings;

        private float zoomLevel = 1f;
        private float viewStart = 0f;
        private float viewEnd = 1f;

        private float[] cachedWaveform;
        private AudioClip cachedClip;
        private AudioSourcePlus audioSourcePlus;
        private AudioSource audioSource;

        private Rect waveformRect;
        private ASP_AudioWaveformEditorInput inputHandler;
        private ASP_PlayheadController playheadController;
        private bool isDraggingView = false;
        private float previousPlayheadTime = 0f;

        private const int VolumeBufferSize = 1;
        private Queue<float> volumeBuffer = new Queue<float>(VolumeBufferSize);

        [SerializeField]
        private bool[] showCurves;

        private ASP_DynamicPickerEditor dynamicPickerEditor = new ASP_DynamicPickerEditor();

        private static readonly string[] curveNames = { "Volume Rolloff", "Spatial Blend", "Spread", "Reverb Zone Mix" };
        private static readonly string[] curveTooltips = {
            "Controls how the volume of the audio source decreases as the listener moves away from the source.",
            "Controls how much the 3D engine has an effect on the sound. A value of 0 is fully 2D, and a value of 1 is fully 3D.",
            "Sets the spread angle (in degrees) of a 3D sound in speaker space.",
            "Sets the amount of the audio source signal that is routed to the reverb zones."
        };

        private bool isEditingMarkerName = false;
        private int editingMarkerId = -1;
        public bool isManipulatingCurves = false;

        private void OnEnable()
        {
            audioSourcePlus = (AudioSourcePlus)target;
            audioSource = audioSourcePlus.audioSource;

            if (audioSource == null)
            {
                Debug.LogError("AudioSource is null on enable in AudioWaveformEditor.");
                return;
            }

            audioSourcePlus.ToggleAudioSourceVisibility(audioSourcePlus.showAudioSource);

            inputHandler = new ASP_AudioWaveformEditorInput(audioSourcePlus);
            playheadController = new ASP_PlayheadController(audioSourcePlus, inputHandler);

            ASP_CustomCurveEditor.audioWaveformEditor = this;

            InitializeCurves();
        }

        private void InitializeCurves()
        {
            if (audioSourcePlus.audioCurves != null)
            {
                int curveCount = audioSourcePlus.audioCurves.Count;
                showCurves = new bool[curveCount];
                for (int i = 0; i < curveCount; i++)
                {
                    showCurves[i] = true;
                }

                if (!audioSourcePlus.hasBeenInitialized)
                {
                    CopyAudioSourceSettings();
                    audioSourcePlus.hasBeenInitialized = true;
                    ASP_AudioSourceEditorDisplayer.InitialiseCurves(audioSourcePlus);
                }
            }
        }

        private void CopyAudioSourceSettings()
        {
            if (audioSource != null)
            {
                audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, audioSource.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
                audioSourcePlus.audioSource.spatialBlend = audioSource.spatialBlend;
                audioSourcePlus.audioSource.spread = audioSource.spread;
                audioSourcePlus.audioSource.reverbZoneMix = audioSource.reverbZoneMix;

                if (audioSource.rolloffMode == AudioRolloffMode.Logarithmic)
                {
                    audioSourcePlus.audioSource.rolloffMode = AudioRolloffMode.Custom;
                    LoadLogarithmicRolloffCurve();
                }
                else if (audioSource.rolloffMode == AudioRolloffMode.Custom)
                {
                    var customCurve = audioSourcePlus.audioSource.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                    for (int i = 0; i < customCurve.length; i++)
                    {
                        customCurve.SmoothTangents(i, 0.0f);
                    }
                    audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, customCurve);
                }
            }
        }

        [MenuItem("CONTEXT/AudioSourcePlus/Toggle AudioSource Visibility")]
        private static void ToggleAudioSourceVisibility(MenuCommand command)
        {
            AudioSourcePlus audioSourcePlus = (AudioSourcePlus)command.context;
            audioSourcePlus.showAudioSource = !audioSourcePlus.showAudioSource;
            audioSourcePlus.ToggleAudioSourceVisibility(audioSourcePlus.showAudioSource);
            EditorUtility.SetDirty(audioSourcePlus);
        }

        [MenuItem("CONTEXT/AudioSourcePlus/Remove AudioSourcePlus (Keep AudioSource)")]
        private static void RemoveAudioSourcePlusSkipDestroy(MenuCommand command)
        {
            AudioSourcePlus audioSourcePlus = (AudioSourcePlus)command.context;
            audioSourcePlus.ToggleAudioSourceVisibility(true);
            audioSourcePlus.skipCustomDestruction = true;
            AudioSource _audioSource = audioSourcePlus.audioSource;
            DestroyImmediate(audioSourcePlus);
            _audioSource.enabled = true;
        }

        private void OnDisable()
        {
            inputHandler?.Dispose();
            playheadController?.Dispose();

            cachedWaveform = null;
            cachedClip = null;
            audioSource = null;
        }

        private bool includeAudioClip = false;
        private const float MarkerSaveLoadButtonHeight = 30f;

        public override void OnInspectorGUI()
        {
            if (audioSourcePlus?.audioSource == null)
            {
                EditorGUILayout.HelpBox("AudioSourcePlus or its AudioSource is not assigned.", MessageType.Error);
                return;
            }

            ASP_AudioSourceEditorDisplayer.DrawAudioSourceProperties(audioSourcePlus);
            DrawFoldout(ref showPlaybackSettings, "Playback Settings", _ => ASP_AudioSourceEditorDisplayer.DrawPlaybackSettings(audioSource));
            DrawFoldout(ref showSoundSettings, "Sound Settings", _ => ASP_AudioSourceEditorDisplayer.DrawSoundSettings(audioSourcePlus));

            DrawFoldout(ref show3DSettings, "3D Settings", _ =>
            {
                EnsureCurveEditorInitialized();

                ASP_AudioSourceEditorDisplayer.Draw3DSettings();
                EditorGUILayout.Space(15);

                DrawCurveEditor(audioSourcePlus);
                GUILayout.Space(20);

                DrawCurveToggleButtons();
                DrawToggleCurveModeButton();
            });

            if (audioSource.clip != null)
            {
                DrawWaveformTimeline();
            }

            HandleInput(audioSourcePlus);

            if (audioSource.clip != null)
            {
                previousPlayheadTime = audioSource.time;
            }

            Repaint();
        }

        private void EnsureCurveEditorInitialized()
        {
            if (ASP_CustomCurveEditor.audioSourcePlus == null)
            {
                ASP_CustomCurveEditor.audioSourcePlus = audioSourcePlus;
            }
        }

        private void DrawCurveToggleButtons()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(Margin);
            for (int i = 0; i < audioSourcePlus.audioCurves.Count; i++)
            {
                DrawCurveToggleButton(i);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawCurveToggleButton(int i)
        {
            bool previousState = showCurves[i];
            GUI.backgroundColor = showCurves[i] ? Color.HSVToRGB((float)i / audioSourcePlus.audioCurves.Count, 0.5f, 1.0f) : Color.white;

            GUIContent buttonContent = new GUIContent(curveNames[i], curveTooltips[i]);
            showCurves[i] = GUILayout.Toggle(showCurves[i], buttonContent, "Button", GUILayout.Height(MarkerSaveLoadButtonHeight));

            if (previousState != showCurves[i])
            {
                Repaint();
            }

            UpdateAudioSourceCurve(i);
        }

        private void DrawToggleCurveModeButton()
        {
            GUILayout.BeginHorizontal();

            if (audioSourcePlus?.audioSource != null)
            {
                GUI.backgroundColor = new Color(1.1f, 1.1f, 1.1f);
                if (GUILayout.Button("Toggle Curve Mode", GUILayout.Height(MarkerSaveLoadButtonHeight)))
                {
                    ToggleRolloffMode();
                }

                GUI.backgroundColor = Color.white;
                GUILayout.Space(10);

                EditorGUIUtility.labelWidth = 100;
                float newMaxDistance = EditorGUILayout.FloatField("Max Distance:", audioSourcePlus.audioSource.maxDistance);
                newMaxDistance = Mathf.Max(0f, newMaxDistance);

                if (newMaxDistance != audioSourcePlus.audioSource.maxDistance)
                {
                    Undo.RecordObject(audioSourcePlus.audioSource, "Change Max Distance");
                    audioSourcePlus.audioSource.maxDistance = newMaxDistance;
                    EditorUtility.SetDirty(audioSourcePlus.audioSource);
                }
            }
            GUILayout.Space(Margin);
            GUILayout.EndHorizontal();
        }

        private void DrawWaveformTimeline()
        {
            bool show = audioSourcePlus.showWaveformTimeline;

            DrawWaveformTimelineButton(ref show, show ? "Close Timeline" : "Open Timeline", _ =>
            {
                if (audioSource.clip != null)
                {
                    UpdateWaveformCache(audioSource.clip);
                    DrawWaveform(cachedWaveform);
                    playheadController.DrawPlayhead(waveformRect, zoomLevel, viewStart, viewEnd);
                    DrawMarkers(audioSourcePlus.markers);
                    DrawSelectedMarkerInspector(audioSourcePlus.markers);
                }
            });

            if (show)
            {
                DrawMarkerSaveLoadButtons();
            }

            audioSourcePlus.showWaveformTimeline = show;
        }

        private void DrawMarkerSaveLoadButtons()
        {
            GUILayout.BeginHorizontal();

            float totalWidth = EditorGUIUtility.currentViewWidth - 20f;
            float saveButtonWidth = totalWidth * 0.35f;
            float audioButtonWidth = totalWidth * 0.15f;
            float loadButtonWidth = totalWidth * 0.475f;

            if (GUILayout.Button("Save Markers", GUILayout.Height(MarkerSaveLoadButtonHeight), GUILayout.Width(saveButtonWidth)))
            {
                SaveMarkerProfile();
            }

            GUIStyle toggleStyle = new GUIStyle(GUI.skin.button)
            {
                wordWrap = true,
                fontSize = 10
            };

            GUI.backgroundColor = includeAudioClip ? new Color(0.8f, 1.0f, 0.8f) : Color.white;
            includeAudioClip = GUILayout.Toggle(includeAudioClip, includeAudioClip ? "Including Audio Clip" : "Include Audio Clip",
                                                toggleStyle, GUILayout.Height(MarkerSaveLoadButtonHeight), GUILayout.Width(audioButtonWidth));
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Load Markers", GUILayout.Height(MarkerSaveLoadButtonHeight), GUILayout.Width(loadButtonWidth)))
            {
                LoadMarkerProfile();
            }

            GUILayout.EndHorizontal();
        }

        private void SaveMarkerProfile()
        {
            string savePath = EditorUtility.SaveFilePanelInProject("Save Marker Profile", "New MarkerProfile", "asset", "Please enter a file name to save the marker profile");

            if (!string.IsNullOrEmpty(savePath))
            {
                try
                {
                    var profile = ScriptableObject.CreateInstance<ASP_MarkerProfile>();
                    profile.markerList = new List<ASP_Marker>(audioSourcePlus.markers);
                    profile.SaveProfile();
                    AssetDatabase.CreateAsset(profile, savePath);
                    AssetDatabase.SaveAssets();

                    if (includeAudioClip && audioSourcePlus.audioSource.clip != null)
                    {
                        profile.audioClip = audioSourcePlus.audioSource.clip;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error saving Marker Profile: {ex.Message}");
                    EditorUtility.DisplayDialog("Error", $"Failed to save Marker Profile: {ex.Message}", "OK");
                }
                finally
                {
                    GUIUtility.ExitGUI();
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Cancelled", "Save operation was cancelled.", "OK");
                GUIUtility.ExitGUI();
            }
        }

        private void LoadMarkerProfile()
        {
            string loadPath = EditorUtility.OpenFilePanel("Load Marker Profile", "Assets", "asset");

            if (!string.IsNullOrEmpty(loadPath))
            {
                loadPath = FileUtil.GetProjectRelativePath(loadPath);
                var profile = AssetDatabase.LoadAssetAtPath<ASP_MarkerProfile>(loadPath);

                if (profile.audioClip != null)
                {
                    Undo.RecordObject(audioSourcePlus.audioSource, "Load Markers and Audio Clip");
                    audioSourcePlus.audioSource.clip = profile.audioClip;
                }

                if (profile?.markerList != null)
                {
                    Undo.RecordObject(audioSourcePlus, "Load Markers");

                    audioSourcePlus.markers.Clear();

                    foreach (var marker in profile.markerList)
                    {
                        var newMarker = DuplicateMarker(marker, audioSourcePlus);
                        audioSourcePlus.markers.Add(newMarker);
                    }

                    EditorUtility.SetDirty(audioSourcePlus);
                    Repaint();
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Failed to load the selected Marker Profile.", "OK");
                }
            }
            else
            {
                EditorUtility.DisplayDialog("Cancelled", "Load operation was cancelled.", "OK");
            }

        foreach (var marker in audioSourcePlus.markers)
            {
                   marker.RebindReferences();
            }
            GUIUtility.ExitGUI();
        }

        private ASP_Marker DuplicateMarker(ASP_Marker original, AudioSourcePlus audioSourcePlus)
        {
            ASP_Marker newMarker = original.DeepCopy();
            newMarker.AudioSourcePlusReference = audioSourcePlus;

            // Deep copy DynamicPickers
            newMarker.DynamicPickers = new List<ASP_DynamicPicker>();
            foreach (var picker in original.DynamicPickers)
            {
                var newPicker = new ASP_DynamicPicker
                {
                    GameObjectID = picker.GameObjectID,
                    selectedComponentName = picker.selectedComponentName,
                    selectedMethodName = picker.selectedMethodName,
                    methodParameters = picker.methodParameters != null ? (ASP_SerializedParameter[])picker.methodParameters.Clone() : null
                };
                newMarker.DynamicPickers.Add(newPicker);
            }

            return newMarker;
        }


        private void DrawCurveEditor(AudioSourcePlus _audioSourcePlus)
        {
            float graphWidth = EditorGUIUtility.currentViewWidth - 2 * Margin;
            Rect curveRect = GUILayoutUtility.GetRect(graphWidth, CurveHeight);

            EditorGUI.DrawRect(curveRect, new Color(0.15f, 0.15f, 0.15f));

            Rect paddedRect = new Rect(curveRect.x + Padding, curveRect.y + Padding, curveRect.width - 2 * Padding, curveRect.height - 2 * Padding);
            GUI.BeginGroup(paddedRect);
            Rect localRect = new Rect(0, 0, paddedRect.width, paddedRect.height);

            if (Event.current.type == EventType.Repaint)
            {
                for (int i = 0; i < _audioSourcePlus.audioCurves.Count; i++)
                {
                    if (showCurves[i])
                    {
                        bool isSelected = (_audioSourcePlus.audioCurves[i] == ASP_CurveEventHandler.selectedCurve);
                        string labelFormat = GetCurveLabelFormat(i);
                        ASP_CurveDrawer.DrawCurve(_audioSourcePlus.audioCurves[i], Color.HSVToRGB((float)i / _audioSourcePlus.audioCurves.Count * 1.1f, 1f, 2f), localRect, isSelected, _audioSourcePlus.audioSource.maxDistance, labelFormat);
                    }
                }
            }
            ASP_CurveEventHandler.HandleCurveEvents(localRect, _audioSourcePlus.audioCurves);
            GUI.EndGroup();

            DrawAxisLabelsOutsideRect(curveRect, _audioSourcePlus.audioSource.maxDistance);
        }

        private string GetCurveLabelFormat(int index)
        {
            return index switch
            {
                0 => "Distance: {0:F1}",
                1 => "Spatial Blend: {0:F1}",
                2 => "Degrees: {0:F1}",
                3 => "Reverb: {0:F1}",
                _ => "Value: {0:F1}",
            };
        }

        public static void DrawAxisLabelsOutsideRect(Rect rect, float maxDistance)
        {
            const float labelOffset = 10f;
            const int fontSize = 10;
            const float lineHeight = 5f;
            const float lineThickness = 1f;

            for (int i = 0; i <= 10; i++)
            {
                float xValue = (float)i / 10f * maxDistance;
                Vector2 labelPosition = new Vector2(rect.x + (xValue / maxDistance * rect.width), rect.y + rect.height + labelOffset);
                GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, normal = { textColor = Color.white } };

                if (i == 0)
                {
                    labelStyle.alignment = TextAnchor.MiddleLeft;
                }
                else if (i == 10)
                {
                    labelStyle.alignment = TextAnchor.MiddleRight;
                }
                else
                {
                    labelStyle.alignment = TextAnchor.MiddleCenter;
                }

                Vector2 lineStart = new Vector2(labelPosition.x, rect.y + rect.height);
                Vector2 lineEnd = new Vector2(labelPosition.x, rect.y + rect.height - lineHeight);
                Handles.color = Color.white;
                Handles.DrawAAPolyLine(lineThickness, lineStart, lineEnd);

                Handles.Label(labelPosition, xValue.ToString("F0"), labelStyle);
            }
        }

        [SerializeField]
        private int RollOffInt = 0;

        private void ToggleRolloffMode()
        {
            if (audioSourcePlus == null || audioSourcePlus.audioSource == null) return;

            switch (RollOffInt)
            {
                case 0:
                    LoadCustomRolloffCurve();
                    break;
                case 1:
                    LoadLogarithmicRolloffCurve();
                    break;
                case 2:
                    LoadLinearRolloffCurve();
                    break;
            }
            RollOffInt = (RollOffInt + 1) % 3;
            audioSourcePlus.audioSource.rolloffMode = AudioRolloffMode.Custom;

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
                new Keyframe(0, 1) { inTangent = -3f, outTangent = -3f },
                new Keyframe(0.01f, 0.7f) { inTangent = -2f, outTangent = -2f },
                new Keyframe(0.025f, 0.4f) { inTangent = -1.5f, outTangent = -1.5f },
                new Keyframe(0.05f, 0.2f) { inTangent = -1.2f, outTangent = -1.2f },
                new Keyframe(0.08f, 0.1f) { inTangent = -0.8f, outTangent = -0.8f },
                new Keyframe(0.11f, 0.07f) { inTangent = -0.6f, outTangent = -0.6f },
                new Keyframe(0.3f, 0.03f) { inTangent = -0.3f, outTangent = -0.3f },
                new Keyframe(1, 0f) { inTangent = 0f, outTangent = 0f }
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

        public void UpdateAudioSourceCurve(int index)
        {
            if (audioSourcePlus == null || audioSourcePlus.audioSource == null) return;

            if (audioSourcePlus.audioCurves[index] == null || audioSourcePlus.audioCurves[index].length == 0)
            {
                audioSourcePlus.audioCurves[index].AddKey(new Keyframe(0, 1));
            }

            Undo.RecordObject(audioSourcePlus, "Update Audio Source Curve");
            switch (index)
            {
                case 0:
                    audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, audioSourcePlus.audioCurves[index]);
                    break;
                case 1:
                    audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.SpatialBlend, audioSourcePlus.audioCurves[index]);
                    break;
                case 2:
                    audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.Spread, audioSourcePlus.audioCurves[index]);
                    break;
                case 3:
                    audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.ReverbZoneMix, audioSourcePlus.audioCurves[index]);
                    break;
            }
            EditorUtility.SetDirty(audioSourcePlus.audioSource);
        }

        private void DrawWaveformTimelineButton(ref bool show, string label, Action<SerializedObject> drawMethod)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(.8f, .8f, .8f, 1f) }
            };

            var buttonRect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            if (GUI.Button(buttonRect, label, buttonStyle))
            {
                show = !show;
            }

            if (show)
            {
                EditorGUI.indentLevel++;
                drawMethod(new SerializedObject(audioSourcePlus));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMarkers(List<ASP_Marker> markers)
        {
            Vector2 mousePosition = Event.current.mousePosition;

            foreach (var marker in markers)
            {
                marker.Time = marker.normalizedTimelinePosition * audioSource.clip.length;
                bool justTriggered = marker.justTriggered;
                float markerX = GetMarkerXPosition(marker.normalizedTimelinePosition);

                if (markerX >= waveformRect.x && markerX <= waveformRect.xMax)
                {
                    Vector2 center = new Vector2(markerX, waveformRect.y + waveformRect.height * 0.15f);
                    float size = 8f;

                    bool isHovered = Vector2.Distance(center, mousePosition) < size;

                    Color fillColor = justTriggered ? marker.justTriggeredColor :
                                      (marker.IsSelected ? new Color(1.2f, 0.8f, 0f, 1f) :
                                      new Color(0.5f, 0.5f, 0.5f, isHovered ? 0.8f : 0.4f));
                    Color borderColor = justTriggered ? marker.justTriggeredColor : Color.black;
                    Color circleColor = justTriggered ? marker.justTriggeredColor :
                                       (isHovered || marker.IsSelected ? new Color(1f, 1f, 1f, 1f) :
                                       new Color(1f, 1f, 1f, 0.5f));

                    Vector3[] diamondVertices = new Vector3[4]
                    {
                        new Vector3(center.x, center.y - size, 0),
                        new Vector3(center.x - size, center.y, 0),
                        new Vector3(center.x, center.y + size, 0),
                        new Vector3(center.x + size, center.y, 0)
                    };

                    Handles.color = fillColor;
                    Handles.DrawAAConvexPolygon(diamondVertices);
                    Handles.color = borderColor;
                    Handles.DrawAAPolyLine(2f, diamondVertices[0], diamondVertices[1], diamondVertices[2], diamondVertices[3], diamondVertices[0]);

                    float circleRadius = 3f;
                    Handles.color = circleColor;
                    Handles.DrawSolidDisc(center, Vector3.forward, circleRadius);

                    if ((isHovered || marker.IsSelected) && marker.MarkerName != "Marker Name (Click to Rename)")
                    {
                        GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
                        {
                            alignment = TextAnchor.UpperCenter,
                            wordWrap = true,
                            fontStyle = FontStyle.Bold,
                            normal = { textColor = justTriggered ? marker.justTriggeredColor : new Color(1f, 1f, 1f, marker.IsSelected ? 1f : 0.5f) }
                        };

                        Vector2 labelPosition = new Vector2(center.x, center.y + size + 4);
                        float maxLabelWidth = 100f;

                        Handles.BeginGUI();
                        Rect labelRect = new Rect(labelPosition.x - (maxLabelWidth / 2), labelPosition.y, maxLabelWidth, 40);

                        Color originalColor = GUI.color;
                        GUI.color = new Color(0f, 0f, 0f, 0.8f);
                        GUI.Box(labelRect, GUIContent.none);
                        GUI.color = originalColor;

                        GUI.Label(labelRect, marker.MarkerName, labelStyle);
                        Handles.EndGUI();
                    }

                    DrawMarkerLines(center, size, marker);
                }
            }
        }

        private void DrawMarkerLines(Vector2 center, float size, ASP_Marker marker)
        {
            float topLineLength = waveformRect.height * 0.15f;
            float bottomLineLength = waveformRect.height * 0.4f;
            int fadeSteps = 10;
            float topStepHeight = topLineLength / fadeSteps;
            float bottomStepHeight = bottomLineLength / fadeSteps;
            float maxAlpha = 1.0f;

            for (int i = 0; i < fadeSteps; i++)
            {
                float alpha = Mathf.Lerp(maxAlpha, 0, (float)i / fadeSteps);
                Handles.color = marker.justTriggered ? new Color(marker.justTriggeredColor.r, marker.justTriggeredColor.g, marker.justTriggeredColor.b, alpha) :
                                new Color(1f, 1f, 1f, alpha);

                Vector3 lineStartAbove = new Vector3(center.x, center.y - i * topStepHeight, 0);
                Vector3 lineEndAbove = new Vector3(center.x, center.y - (i + 1) * topStepHeight, 0);
                Handles.DrawLine(lineStartAbove, lineEndAbove);

                Vector3 lineStartBelow = new Vector3(center.x, center.y + i * bottomStepHeight, 0);
                Vector3 lineEndBelow = new Vector3(center.x, center.y + (i + 1) * bottomStepHeight, 0);
                Handles.DrawLine(lineStartBelow, lineEndBelow);
            }
        }

        private void DrawSelectedMarkerInspector(List<ASP_Marker> markers)
        {
            foreach (var marker in markers)
            {
                if (marker.IsSelected)
                {
                    GUIStyle markerBoxStyle = new GUIStyle(GUI.skin.box)
                    {
                        padding = new RectOffset(10, 10, 20, 0),
                        margin = new RectOffset(10, 10, 10, 0),
                        border = new RectOffset(2, 2, 2, 2),
                        normal = { background = Texture2D.blackTexture }
                    };

                    EditorGUILayout.BeginVertical(markerBoxStyle);

                    DrawMarkerInspectorHeader(marker);

                    dynamicPickerEditor.DrawDynamicPickers(marker.DynamicPickers, marker, audioSourcePlus);

                    if (GUILayout.Button("Add Event", GUILayout.Height(20)))
                    {
                        Undo.RecordObject(marker.AudioSourcePlusReference, "Add Dynamic Picker Event");
                        marker.DynamicPickers.Add(new ASP_DynamicPicker { selectedGameObject = audioSourcePlus.gameObject });
                        EditorUtility.SetDirty(marker.AudioSourcePlusReference);
                    }

                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void DrawMarkerInspectorHeader(ASP_Marker marker)
        {
            float totalWidth = EditorGUIUtility.currentViewWidth - 40;
            float markerNameWidth = totalWidth * 0.65f;
            float toggleAreaWidth = totalWidth * 0.3f;

            EditorGUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(markerNameWidth));
            DrawMarkerNameEditor(marker);
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginVertical(GUILayout.Width(toggleAreaWidth));
            GUILayout.FlexibleSpace();

            GUIStyle wrapLabelStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                alignment = TextAnchor.UpperCenter,
                fontSize = 10,
                contentOffset = new Vector2(-10, -15)
            };

            Rect labelRect = GUILayoutUtility.GetRect(new GUIContent("Execute in /n Edit Mode"), wrapLabelStyle, GUILayout.ExpandWidth(true));
            EditorGUI.LabelField(labelRect, "Execute in Edit Mode", wrapLabelStyle);

            float toggleWidth = 20;
            Rect toggleRect = new Rect(labelRect.x + (labelRect.width - toggleWidth) / 2, labelRect.yMax - 15, toggleWidth, 20);
            marker.ExecuteInEditMode = GUI.Toggle(toggleRect, marker.ExecuteInEditMode, "");

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private string newMarkerName = "";

        private void DrawMarkerNameEditor(ASP_Marker marker)
        {
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
                margin = new RectOffset(0, 0, 10, 10)
            };

            if (isEditingMarkerName && editingMarkerId == marker.GetHashCode())
            {
                EditorGUI.BeginChangeCheck();

                GUI.SetNextControlName("MarkerNameField");
                newMarkerName = EditorGUILayout.TextField(newMarkerName, GUILayout.ExpandWidth(true));

                if (GUI.GetNameOfFocusedControl() != "MarkerNameField")
                {
                    GUI.FocusControl("MarkerNameField");
                }

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(marker.AudioSourcePlusReference, "Rename Marker");
                    marker.MarkerName = newMarkerName;
                    EditorUtility.SetDirty(marker.AudioSourcePlusReference);
                }

                if (Event.current.type == EventType.KeyUp && Event.current.keyCode == KeyCode.Return)
                {
                    isEditingMarkerName = false;
                    GUI.FocusControl(null);
                    Event.current.Use();
                    EditorGUIUtility.editingTextField = false;
                    GUI.changed = true;
                }

                if (Event.current.type == EventType.MouseDown && !GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
                {
                    isEditingMarkerName = false;
                    GUI.FocusControl(null);
                    Event.current.Use();
                }
            }
            else
            {
                Rect labelRect = GUILayoutUtility.GetRect(new GUIContent(marker.MarkerName), labelStyle, GUILayout.ExpandWidth(true));
                EditorGUI.LabelField(labelRect, marker.MarkerName, labelStyle);

                if (Event.current.type == EventType.MouseDown && labelRect.Contains(Event.current.mousePosition))
                {
                    isEditingMarkerName = true;
                    editingMarkerId = marker.GetHashCode();
                    newMarkerName = marker.MarkerName;
                    GUI.FocusControl("MarkerNameField");
                    Event.current.Use();
                }
            }
        }

        private float GetMarkerXPosition(float normalizedPosition)
        {
            return waveformRect.x + ((normalizedPosition - viewStart) / (viewEnd - viewStart)) * waveformRect.width;
        }

        private bool isResizingWaveform = false;
        private float waveformHeight = 200;
        private const float resizeHandleHeight = 10f;

        private void DrawWaveform(float[] waveform)
        {
            var visibleWaveform = GetVisibleWaveform(waveform);

            waveformRect = GUILayoutUtility.GetRect(Mathf.RoundToInt(EditorGUIUtility.currentViewWidth * zoomLevel), waveformHeight);

            Color gradientEnd = CalculateBackgroundColorBasedOnVolume();
            WaveformDrawer.DrawBackground(waveformRect, new Color(0.1f, 0.1f, 0.1f), gradientEnd);
            WaveformDrawer.DrawGrid(waveformRect, zoomLevel);

            Color outlineColor = AudioSyncProSettings.WaveformOutlineColor;
            Color fillColor = AudioSyncProSettings.WaveformFillColor;

            WaveformDrawer.DrawWaveform(waveformRect, visibleWaveform, outlineColor, fillColor);

            DrawResizeHandle(waveformRect);
            inputHandler.HandleResizeHandle(ref isResizingWaveform, ref waveformHeight, waveformRect, resizeHandleHeight);
        }

        private bool IsMouseHoveringOverResizeHandle(Rect waveformRect)
        {
            return inputHandler.IsMouseHoveringOverResizeHandle(waveformRect, resizeHandleHeight);
        }

        private void DrawResizeHandle(Rect waveformRect)
        {
            if (IsMouseHoveringOverResizeHandle(waveformRect) || inputHandler.isResizingWaveform)
            {
                Rect handleRect = new Rect(waveformRect.x, waveformRect.yMax - resizeHandleHeight, waveformRect.width, resizeHandleHeight);
                EditorGUI.DrawRect(handleRect, new Color(0.7f, 0.7f, 1f, 1f));
            }
        }

        private float[] GetVisibleWaveform(float[] waveform)
        {
            int totalSamples = waveform.Length;
            viewStart = Mathf.Clamp01(viewStart);
            viewEnd = Mathf.Clamp(viewEnd, viewStart, 1f);

            int startSample = Mathf.FloorToInt(viewStart * totalSamples);
            int endSample = Mathf.CeilToInt(viewEnd * totalSamples);

            float[] visibleWaveform = new float[endSample - startSample];
            Array.Copy(waveform, startSample, visibleWaveform, 0, visibleWaveform.Length);

            return visibleWaveform;
        }

        private void UpdateWaveformCache(AudioClip clip)
        {
            if (clip != cachedClip)
            {
                cachedWaveform = ASP_WaveformGenerator.GenerateWaveform(clip, Mathf.RoundToInt(EditorGUIUtility.currentViewWidth));
                cachedClip = clip;
            }
        }

        private void HandleInput(AudioSourcePlus audioWaveform)
        {
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag || Event.current.type == EventType.MouseUp)
            {
                if (!waveformRect.Contains(Event.current.mousePosition))
                {
                    return;
                }
            }

            inputHandler.HandleMarkerInput(audioSourcePlus.markers, waveformRect, ref viewStart, ref viewEnd, audioSource.clip);
            inputHandler.HandleMouseInput(ref isDraggingView, ref viewStart, ref viewEnd, ref waveformRect);
            inputHandler.HandleKeyboardInput(audioWaveform);
            inputHandler.HandleMouseScroll(ref zoomLevel, ref viewStart, ref viewEnd, waveformRect);

            if (inputHandler.isBoxSelecting)
            {
                DrawSelectionBox(inputHandler.selectionBox);
            }
        }

        private Color CalculateBackgroundColorBasedOnVolume()
        {
            float currentVolume = GetCurrentVolume();
            float smoothedVolume = GetSmoothedVolume(currentVolume);

            return Color.Lerp(new Color(0f, 0f, 0f), new Color(.2f, .2f, 2f, 1), smoothedVolume);
        }

        private float GetCurrentVolume()
        {
            float[] samples = new float[256];
            audioSource.GetOutputData(samples, 0);

            float currentVolume = 0f;
            foreach (float sample in samples)
            {
                currentVolume += Mathf.Abs(sample);
            }

            return currentVolume / samples.Length;
        }

        private float GetSmoothedVolume(float currentVolume)
        {
            if (volumeBuffer.Count >= VolumeBufferSize)
            {
                volumeBuffer.Dequeue();
            }
            volumeBuffer.Enqueue(currentVolume);

            float smoothedVolume = 0f;
            foreach (float volume in volumeBuffer)
            {
                smoothedVolume += volume;
            }

            return smoothedVolume / volumeBuffer.Count;
        }

        private void DrawFoldout(ref bool show, string label, Action<SerializedObject> drawMethod)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var rect = GUILayoutUtility.GetRect(16f, 22f, GUILayout.ExpandWidth(true));
            var fullRect = EditorGUI.IndentedRect(rect);

            if (fullRect.Contains(Event.current.mousePosition) && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                show = !show;
                Event.current.Use();
            }

            EditorGUI.LabelField(fullRect, label, EditorStyles.boldLabel);

            if (fullRect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(fullRect, new Color(0.25f, 0.5f, 1f, 0.1f));
            }

            if (show)
            {
                EditorGUI.indentLevel++;
                drawMethod(new SerializedObject(audioSourcePlus));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSelectionBox(Rect selectionBox)
        {
            if (selectionBox.width > 0 && selectionBox.height > 0)
            {
                Handles.BeginGUI();
                Handles.color = new Color(1f, 1f, 1.5f, 0.8f);
                Handles.DrawSolidRectangleWithOutline(selectionBox, new Color(0.8f, 0.8f, 2f, 0.25f), Color.blue);
                Handles.EndGUI();
            }
        }
    }
}
