/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
namespace TelePresent.AudioSyncPro
{

    public class ASP_AudioSourceEditorDisplayer
    {
        private const float Margin = 5f;
        private static AnimationCurve originalCurve1; 

        public static void InitialiseCurves(AudioSourcePlus audioSourcePlus)
        {
            // Set the first curve in the audiosource curves to be equal to .5f, 0f.
            audioSourcePlus.audioCurves[1] = new AnimationCurve(new Keyframe(0.5f, 0f));
        }

        public static void DrawAudioSourceProperties(AudioSourcePlus audioSourcePlus)
        {
            if (audioSourcePlus.audioSource == null)
            {
                return;
            }

            // Set the style for the label
            GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                fixedHeight = 30,
                alignment = TextAnchor.UpperCenter
            };

            // Set the style for the object field
            GUIStyle objectFieldStyle = new GUIStyle(EditorStyles.objectField)
            {
                fontSize = 20,
                fixedHeight = 70, // Combined height of the buttons
                alignment = TextAnchor.MiddleCenter
            };

            // Draw a background box
            GUILayout.BeginVertical("box");
            GUILayout.Space(10);

            // Draw the fancy label
            EditorGUILayout.LabelField("Audio Clip", labelStyle);
            GUILayout.Space(10);

            // Calculate the width for each element
            float totalWidth = EditorGUIUtility.currentViewWidth - 8 * Margin;
            float objectPickerWidth = totalWidth * 0.6f;
            float buttonsWidth = totalWidth * 0.4f;

            // Draw the larger object field with margins and add buttons
            GUILayout.BeginHorizontal();
            GUILayout.Space(Margin); // Left margin

            // Record object before change for Undo
            EditorGUI.BeginChangeCheck();
            AudioClip newClip = (AudioClip)EditorGUILayout.ObjectField(audioSourcePlus.audioSource.clip, typeof(AudioClip), false, GUILayout.Height(70), GUILayout.Width(objectPickerWidth));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(audioSourcePlus.audioSource, "Change Audio Clip");
                audioSourcePlus.audioSource.clip = newClip;
                EditorUtility.SetDirty(audioSourcePlus.audioSource);
            }

            // Stack the Play/Pause and Stop buttons vertically to the right of the object picker
            GUILayout.BeginVertical(GUILayout.Width(buttonsWidth));

            // Disable buttons if the audio clip is null
            EditorGUI.BeginDisabledGroup(audioSourcePlus.audioSource.clip == null);

            // Change button color based on playheadController state
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = audioSourcePlus.isPlaying ? new Color(1.2f, 0.8f, 0.8f) : new Color(0.8f, 1f, 0.8f);

            var playPauseIcon = audioSourcePlus.isPlaying ? EditorGUIUtility.IconContent("d_PauseButton On@2x").image : EditorGUIUtility.IconContent("d_PlayButton").image;
            if (GUILayout.Button(new GUIContent(audioSourcePlus.isPlaying ? "Pause" : "Play", playPauseIcon), GUILayout.Height(35)))
            {
                if (audioSourcePlus.isPlaying)
                {
                    audioSourcePlus.PauseAudio();
                }
                else
                {
                    audioSourcePlus.PlayAudio();
                }
            }

            // Reset button color
            GUI.backgroundColor = originalColor;

            // Disable the Stop button if the audio isn't playing
            if (audioSourcePlus.audioSource.clip != null)
            {
                GUI.enabled = audioSourcePlus.audioSource.isPlaying || audioSourcePlus.audioSource.time != 0;
                if (GUILayout.Button(new GUIContent("Stop", EditorGUIUtility.IconContent("d_PreMatQuad").image), GUILayout.Height(35)))
                {
                    audioSourcePlus.StopAudio();
                }
            }
            GUI.enabled = true; // Re-enable GUI for other controls

            EditorGUI.EndDisabledGroup();

            GUILayout.EndVertical();
            GUILayout.Space(Margin); // Right margin for buttons
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.EndVertical();
        }


        public static void DrawPlaybackSettings(AudioSource audioSource)
        {
            if (audioSource == null)
            {
                Debug.LogError("AudioSource is null.");
                return;
            }

            float buttonWidth = (EditorGUIUtility.currentViewWidth - 4 * Margin) / 2;

            bool isMuted = audioSource.mute;
            bool isBypassingEffects = audioSource.bypassEffects;

            DrawToggleButtons(
                ref isMuted, "Muted", "Mute", buttonWidth,
                ref isBypassingEffects, "Bypassing Effects", "Bypass Effects", buttonWidth
            );

            audioSource.mute = isMuted;
            audioSource.bypassEffects = isBypassingEffects;

            GUILayout.Space(Margin);

            bool isBypassingListenerEffects = audioSource.bypassListenerEffects;
            bool isBypassingReverbZones = audioSource.bypassReverbZones;

            DrawToggleButtons(
                ref isBypassingListenerEffects, "Bypassing Listener Effects", "Bypass Listener Effects", buttonWidth,
                ref isBypassingReverbZones, "Bypassing Reverb Zones", "Bypass Reverb Zones", buttonWidth
            );

            audioSource.bypassListenerEffects = isBypassingListenerEffects;
            audioSource.bypassReverbZones = isBypassingReverbZones;

            GUILayout.Space(Margin);

            bool isPlayOnAwake = audioSource.playOnAwake;
            bool isLooping = audioSource.loop;

            DrawToggleButtons(
                ref isPlayOnAwake, "Playing On Awake", "Play On Awake", buttonWidth,
                ref isLooping, "Looping", "Loop", buttonWidth
            );

            audioSource.playOnAwake = isPlayOnAwake;
            audioSource.loop = isLooping;
        }

        public static void DrawSoundSettings(AudioSourcePlus audioSourcePlus)
        {
            if (audioSourcePlus.audioSource == null)
            {
                Debug.LogError("AudioSource is null.");
                return;
            }

            audioSourcePlus.audioSource.outputAudioMixerGroup = (AudioMixerGroup)EditorGUILayout.ObjectField("Output Audio Mixer Group", audioSourcePlus.audioSource.outputAudioMixerGroup, typeof(AudioMixerGroup), false);
            audioSourcePlus.audioSource.priority = EditorGUILayout.IntSlider("Priority", audioSourcePlus.audioSource.priority, 0, 256);
            audioSourcePlus.audioSource.volume = EditorGUILayout.Slider("Volume", audioSourcePlus.audioSource.volume, 0f, 1f);
            audioSourcePlus.audioSource.pitch = EditorGUILayout.Slider("Pitch", audioSourcePlus.audioSource.pitch, -3f, 3f);
            audioSourcePlus.audioSource.panStereo = EditorGUILayout.Slider("Stereo Pan", audioSourcePlus.audioSource.panStereo, -1f, 1f);

            // Add Sample Size Dropdown
            string[] sampleSizeOptions = { "256", "512", "1024", "2048", "Disable Spectrum Analysis" };
            int[] sampleSizeValues = { 256, 512, 1024, 2048, -1 }; // Use -1 for "Disable Spectrum Data"
            int currentSampleSizeIndex = Array.IndexOf(sampleSizeValues, audioSourcePlus.SampleSize);

            int newSampleSizeIndex = EditorGUILayout.Popup("Sample Size", currentSampleSizeIndex, sampleSizeOptions);
            if (newSampleSizeIndex != currentSampleSizeIndex)
            {
                Undo.RecordObject(audioSourcePlus, "Change Sample Size");
                audioSourcePlus.SampleSize = sampleSizeValues[newSampleSizeIndex];
                EditorUtility.SetDirty(audioSourcePlus);
            }
#if UNITY_WEBGL
            // Display an info box next to the dropdown when in WebGL
            EditorGUILayout.HelpBox("Sample Analysis is not supported on WEBGL - Beat Reactors will not work.", MessageType.Info);
#endif
        }


        public static void Draw3DSettings()
        {
            AudioSourcePlus audioSourcePlus = ASP_CustomCurveEditor.audioSourcePlus;
            // Track previous values to detect changes
            float prevSpread = audioSourcePlus.audioSource.spread;
            float prevReverbZoneMix = audioSourcePlus.audioSource.reverbZoneMix;
            float prevSpatialBlend = audioSourcePlus.isPlaying ? 0f : audioSourcePlus.audioSource.spatialBlend;

            ManageCurve1Locking(audioSourcePlus);

            // Add a slider for doppler effect
            float newDopplerLevel = EditorGUILayout.Slider("Doppler Level", audioSourcePlus.audioSource.dopplerLevel, 0f, 5f);
            if (newDopplerLevel != audioSourcePlus.audioSource.dopplerLevel)
            {
                audioSourcePlus.audioSource.dopplerLevel = newDopplerLevel;
            }

            // Retrieve the panLevelCustomCurve from the AudioSource
            AnimationCurve panLevelCustomCurve = audioSourcePlus.audioSource.GetCustomCurve(AudioSourceCurveType.SpatialBlend);

            bool disableSpatialGroup = panLevelCustomCurve != null && panLevelCustomCurve.keys.Length > 1 || audioSourcePlus.isPlaying;
            if (disableSpatialGroup)
            {
                switch (audioSourcePlus.isPlaying)
                {
                    case true:
                        EditorGUILayout.LabelField("Spatial Blend", "Locked During Audio Preview");
                        break;
                    case false:
                        EditorGUILayout.LabelField("Spatial Blend", "Controlled by curve");
                        break;
                }
            }
            else
            {
                float newSpatialBlend = EditorGUILayout.Slider("Spatial Blend", audioSourcePlus.audioSource.spatialBlend, 0f, 1f);
                if (newSpatialBlend != prevSpatialBlend)
                {
                    Undo.RecordObject(ASP_CustomCurveEditor.audioSourcePlus, "Delete Keyframe");
                    audioSourcePlus.audioSource.spatialBlend = newSpatialBlend;
                    UpdateCurve(1, newSpatialBlend, 0f, 1);
                    EditorUtility.SetDirty(ASP_CustomCurveEditor.audioSourcePlus);
                }
            }

            AnimationCurve spreadCustomCurve = audioSourcePlus.audioSource.GetCustomCurve(AudioSourceCurveType.Spread);

            bool disableSpreadGroup = spreadCustomCurve != null && spreadCustomCurve.keys.Length > 1;

            if (disableSpreadGroup)
            {
                EditorGUILayout.LabelField("Spread", "Controlled by curve");
            }
            else
            {
                float newSpread = EditorGUILayout.Slider("Spread", audioSourcePlus.audioSource.spread, 0f, 360f);
                if (newSpread != prevSpread)
                {
                    Undo.RecordObject(ASP_CustomCurveEditor.audioSourcePlus, "Delete Keyframe");

                    audioSourcePlus.audioSource.spread = newSpread;
                    UpdateCurve(2, newSpread, 0f, 360f);
                    EditorUtility.SetDirty(ASP_CustomCurveEditor.audioSourcePlus);
                }
            }

            AnimationCurve reverbMixCustomCurve = audioSourcePlus.audioSource.GetCustomCurve(AudioSourceCurveType.ReverbZoneMix);

            bool disableReverbGroup = reverbMixCustomCurve != null && reverbMixCustomCurve.keys.Length > 1;

            if (disableReverbGroup)
            {
                EditorGUILayout.LabelField("Reverb Zone Mix", "Controlled by curve");
            }
            else
            {
                float newReverbZoneMix = EditorGUILayout.Slider("Reverb Zone Mix", audioSourcePlus.audioSource.reverbZoneMix, 0f, 1.1f);
                if (newReverbZoneMix != prevReverbZoneMix)
                {
                    Undo.RecordObject(ASP_CustomCurveEditor.audioSourcePlus, "Delete Keyframe");
                    UpdateCurve(3, newReverbZoneMix, 0f, 1.1f);
                    audioSourcePlus.audioSource.reverbZoneMix = newReverbZoneMix;
                    EditorUtility.SetDirty(ASP_CustomCurveEditor.audioSourcePlus);
                }
            }
        }

        private static void ManageCurve1Locking(AudioSourcePlus audioSourcePlus)
        {
            AnimationCurve curve1 = audioSourcePlus.audioCurves[1];

            if (audioSourcePlus.isPlaying)
            {
                if (originalCurve1 == null)
                {
                    originalCurve1 = new AnimationCurve(curve1.keys);
                }

                // Create a new keyframe with value 0 and set it to the curve
                Keyframe[] keys = curve1.keys;
                for (int i = 0; i < keys.Length; i++)
                {
                    keys[i].value = 0f;
                }
                curve1.keys = keys;

                // Apply the modified curve to the AudioSource
                audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve1); // Replace with correct curve type if needed
            }
            else
            {
                // When isPlaying is false, restore the original curve
                if (originalCurve1 != null)
                {
                    curve1.keys = originalCurve1.keys;
                    audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve1); // Replace with correct curve type if needed
                    originalCurve1 = null; // Clear the saved curve
                }
            }
        }

        public static void UpdateCurve(int curveInt, float value, float minValue, float maxValue)
        {
            AnimationCurve curve = ASP_CustomCurveEditor.audioSourcePlus.audioCurves[curveInt];

            // Normalize the value within the min-max range
            float normalizedValue = Mathf.InverseLerp(minValue, maxValue, value);

            // Move the keyframe to the desired position without resetting the curve
            if (curve.keys.Length > 0)
            {
                Keyframe key = curve.keys[0];
                key.value = normalizedValue;
                key.time = 0.5f;
                curve.MoveKey(0, key);
            }

            // Apply the updated curve back to the AudioSource
            ASP_CustomCurveEditor.audioSourcePlus.audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve); // Replace with correct curve type if needed

            // Mark the AudioSource as dirty to ensure the change is saved
            EditorUtility.SetDirty(ASP_CustomCurveEditor.audioSourcePlus);
        }

        private static void DrawStateButton(ref bool state, string labelOn, string labelOff, float buttonWidth)
        {
            var originalColor = GUI.backgroundColor;

            // Increased brightness of the button colors
            GUI.backgroundColor = state ? new Color(0.7f, 1.2f, .7f) : new Color(1.2f, 0.7f, .7f);

            if (GUILayout.Button(state ? labelOn : labelOff, GUILayout.Width(buttonWidth)))
            {
                state = !state;
            }

            GUI.backgroundColor = originalColor;
        }

        private static void DrawToggleButtons(ref bool state1, string labelOn1, string labelOff1, float buttonWidth1,
                                              ref bool state2, string labelOn2, string labelOff2, float buttonWidth2)
        {
            GUILayout.BeginHorizontal();
            DrawStateButton(ref state1, labelOn1, labelOff1, buttonWidth1);
            DrawStateButton(ref state2, labelOn2, labelOff2, buttonWidth2);
            GUILayout.EndHorizontal();
        }
    }
}