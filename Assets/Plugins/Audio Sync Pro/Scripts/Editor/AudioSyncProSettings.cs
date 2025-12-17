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
    public class AudioSyncProSettings : EditorWindow
    {
        private static Color waveformOutlineColor = DefaultOutlineColor;
        private static Color waveformFillColor = DefaultFillColor;

        // Icons
        private Texture2D docsIcon;
        private Texture2D discordIcon;

        private static readonly Color DefaultOutlineColor = new Color32(0xFF, 0xD6, 0x07, 0xFF);
        private static readonly Color DefaultFillColor = new Color32(0xFF, 0xBB, 0x00, 0xFF);
        private static float markerProximity = DefaultMarkerProximity;
        private const float DefaultMarkerProximity = 0.01f;


        private List<AudioSourcePlus> audioSourcePlusList = new List<AudioSourcePlus>();
        private List<AudioSource> audioSourceList = new List<AudioSource>();

        private bool showAudioSourcePlusSection = false;
        private bool showAudioSourceSection = false;
        private Vector2 scrollPosition;

        [MenuItem("Tools/TelePresent/Audio Sync Pro Settings")]
        public static void ShowWindow()
        {
            GetWindow<AudioSyncProSettings>("Audio Sync Pro Settings");
        }

        static AudioSyncProSettings()
        {
            LoadSettings();
        }

        private void OnEnable()
        {
            InitializeLists();
            docsIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(DocsIconPath);
            discordIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(DiscordIconPath);

        }

        private const string IconsFolderPath = "Assets/TelePresent/Audio Sync Pro/Editor/";
        private const string DocsIconPath = IconsFolderPath + "docs.png";      // Adjust the file extension if different
        private const string DiscordIconPath = IconsFolderPath + "discord.png"; // Adjust the file extension if different

        private void OnGUI()
        {
            // Define styles
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleLeft,
                margin = new RectOffset(10, 10, 10, 10)
            };

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fixedHeight = 30,
                margin = new RectOffset(5, 5, 5, 5),
                padding = new RectOffset(10, 10, 0, 0)
            };

            GUIStyle iconButtonStyle = new GUIStyle(buttonStyle)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(15, 30, 0, 0)
            };

            // Begin scroll view
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            // Header Section
            GUILayout.BeginHorizontal();
            GUILayout.Label("Audio Sync Pro Settings", headerStyle);
            GUILayout.FlexibleSpace();

            // "Join Discord" Button
            if (discordIcon != null)
            {
                if (GUILayout.Button(new GUIContent(" Join Discord", discordIcon, "Join our Discord community!"), iconButtonStyle, GUILayout.Width(140)))
                {
                    OpenURL("https://discord.gg/DCWnPkRmTf");
                }
            }
            else
            {
                // Fallback if icon is missing
                if (GUILayout.Button(new GUIContent(" Join Discord", "Join our Discord community!"), iconButtonStyle, GUILayout.Width(140)))
                {
                    OpenURL("https://discord.gg/DCWnPkRmTf");
                }
            }
             
            // "Docs" Button
            if (docsIcon != null)
            {
                if (GUILayout.Button(new GUIContent(" Docs", docsIcon, "Open the documentation"), iconButtonStyle, GUILayout.Width(100)))
                {
                    OpenURL("https://telepresentgames.dk/Unity%20Asset/Audio%20Sync%20Pro%20Documentation.pdf");
                }
            }
            else
            {
                // Fallback if icon is missing
                if (GUILayout.Button(new GUIContent(" Docs", "Open the documentation"), iconButtonStyle, GUILayout.Width(100)))
                {
                    OpenURL("https://telepresentgames.dk/Unity%20Asset/Audio%20Sync%20Pro%20Documentation.pdf");
                }
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Welcome Message
            GUIStyle welcomeStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 14,
                padding = new RectOffset(20, 10, 5, 5)
            };
            EditorGUILayout.LabelField("Thank you for choosing Audio Sync Pro!", welcomeStyle);
            EditorGUILayout.Space(20);

            // Settings Sections
            DrawWaveformSettingsSection();
            DrawAudioSourcePlusSection();
            DrawAudioSourceSection();

            GUILayout.FlexibleSpace();

            // Add "Show Welcome Window" Button
            if (GUILayout.Button("Show Welcome Window", GUILayout.Height(30)))
            {
                AudioSyncProWelcomeWindow.ShowWindow();
            }
            GUILayout.Space(10);

            // Footer
            GUILayout.Label("", GUI.skin.horizontalSlider);
            GUIStyle footerStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                margin = new RectOffset(0, 0, 10, 10)
            };
            GUILayout.Label("� 2025 TelePresent Games", footerStyle);


            // End scroll view
            EditorGUILayout.EndScrollView();
        }


        private void OpenURL(string url)
        {
            try
            {
                Application.OpenURL(url);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to open URL: {url}. Exception: {ex.Message}");
            }
        }



        private void DrawWaveformSettingsSection()
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label("Waveform Settings", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Customize the look of your waveform!", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(10);

            // Outline Color Field
            waveformOutlineColor = EditorGUILayout.ColorField("Waveform Outline Color", waveformOutlineColor);
            if (GUI.changed)
            {
                SaveSettings();
            }
            EditorGUILayout.Space(5);

            // Fill Color Field
            waveformFillColor = EditorGUILayout.ColorField("Waveform Fill Color", waveformFillColor);
            
            EditorGUILayout.Space(10);
            GUILayout.Label("Marker Settings", EditorStyles.boldLabel);

            markerProximity = EditorGUILayout.Slider(
                new GUIContent(
                    "Minimum Marker Distance",
                    "Minimum allowed distance between markers in normalized timeline units (0–1)."
                ),
                markerProximity,
                0.00001f,
                0.1f
            );
            
            if (GUI.changed)
            {
                SaveSettings();
            }

            GUILayout.EndVertical();
            EditorGUILayout.Space(20);
        }

        private void InitializeLists()
        {
            if (audioSourcePlusList.Count == 0)
            {
                audioSourcePlusList.Add(null);
            }

            if (audioSourceList.Count == 0)
            {
                audioSourceList.Add(null);
            }
        }

        private static void LoadSettings()
        {
            if (EditorPrefs.HasKey("WaveformOutlineColor"))
            {
                ColorUtility.TryParseHtmlString("#" + EditorPrefs.GetString("WaveformOutlineColor"), out waveformOutlineColor);
            }
            else
            {
                waveformOutlineColor = DefaultOutlineColor;
            }

            if (EditorPrefs.HasKey("WaveformFillColor"))
            {
                ColorUtility.TryParseHtmlString("#" + EditorPrefs.GetString("WaveformFillColor"), out waveformFillColor);
            }
            else
            {
                waveformFillColor = DefaultFillColor;
            }
            if (EditorPrefs.HasKey("MarkerProximity"))
            {
                markerProximity = EditorPrefs.GetFloat("MarkerProximity", DefaultMarkerProximity);
            }
            else
            {
                markerProximity = DefaultMarkerProximity;
            }
        }

        private void SaveSettings()
        {
            EditorPrefs.SetString("WaveformOutlineColor", ColorUtility.ToHtmlStringRGBA(waveformOutlineColor));
            EditorPrefs.SetString("WaveformFillColor", ColorUtility.ToHtmlStringRGBA(waveformFillColor));
            EditorPrefs.SetFloat("MarkerProximity", markerProximity);

        }

        public static Color WaveformOutlineColor => waveformOutlineColor;
        public static Color WaveformFillColor => waveformFillColor;
        public static float MarkerProximity => Mathf.Max(markerProximity, 0.000001f);


        private void DrawAudioSourcePlusSection()
        {
            GUILayout.BeginVertical("box");

            showAudioSourcePlusSection = EditorGUILayout.Foldout(showAudioSourcePlusSection, "Modify Audio Source Plus Components", true);
            if (showAudioSourcePlusSection)
            {
                EditorGUILayout.Space(10);
                DrawAudioSourcePlusList();

                if (GUILayout.Button("Add AudioSourcePlus", GUILayout.Height(25)))
                {
                    audioSourcePlusList.Add(null);
                }
                EditorGUILayout.Space(10);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Gather All AudioSourcePlus from Scene", GUILayout.Height(25)))
                {
                    GatherAllAudioSourcePlus();
                }
                if (GUILayout.Button("Revert listed to Audio Sources", GUILayout.Height(25)))
                {
                    RevertAudioSourcePlusComponents();
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            EditorGUILayout.Space(20);
        }

        private void DrawAudioSourceSection()
        {
            GUILayout.BeginVertical("box");

            showAudioSourceSection = EditorGUILayout.Foldout(showAudioSourceSection, "Modify Audio Source Components", true);
            if (showAudioSourceSection)
            {
                EditorGUILayout.Space(10);
                DrawAudioSourceList();

                if (GUILayout.Button("Add AudioSource", GUILayout.Height(25)))
                {
                    audioSourceList.Add(null);
                }
                EditorGUILayout.Space(10);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Gather All AudioSources in Scene", GUILayout.Height(25)))
                {
                    GatherAllAudioSources();
                }
                if (GUILayout.Button("Convert All to AudioSourcePlus", GUILayout.Height(25)))
                {
                    ConvertAllToAudioSourcePlus();
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            EditorGUILayout.Space(20);
        }

        private void DrawAudioSourcePlusList()
        {
            for (int i = 0; i < audioSourcePlusList.Count; i++)
            {
                GUILayout.BeginHorizontal();

                audioSourcePlusList[i] = (AudioSourcePlus)EditorGUILayout.ObjectField(
                    $"AudioSourcePlus {i + 1}",
                    audioSourcePlusList[i],
                    typeof(AudioSourcePlus),
                    true
                );

                if (audioSourcePlusList[i] != null && GUILayout.Button("Revert to AudioSource", GUILayout.Width(150)))
                {
                    RevertAudioSourcePlusComponent(audioSourcePlusList[i]);
                    audioSourcePlusList.RemoveAt(i);
                    i--;
                }

                if (GUILayout.Button("X", GUILayout.Width(30)))
                {
                    audioSourcePlusList.RemoveAt(i);
                    i--;
                }

                GUILayout.EndHorizontal();
            }
        }

        private void DrawAudioSourceList()
        {
            for (int i = 0; i < audioSourceList.Count; i++)
            {
                GUILayout.BeginHorizontal();

                audioSourceList[i] = (AudioSource)EditorGUILayout.ObjectField(
                    $"AudioSource {i + 1}",
                    audioSourceList[i],
                    typeof(AudioSource),
                    true
                );

                if (audioSourceList[i] != null && GUILayout.Button("Make AudioSourcePlus", GUILayout.Width(150)))
                {
                    ConvertToAudioSourcePlus(audioSourceList[i]);
                    audioSourceList.RemoveAt(i);
                    i--;
                }

                if (GUILayout.Button("X", GUILayout.Width(30)))
                {
                    audioSourceList.RemoveAt(i);
                    i--;
                }

                GUILayout.EndHorizontal();
            }
        }

        private void GatherAllAudioSourcePlus()
        {
            AudioSourcePlus[] audioSources = Object.FindObjectsByType<AudioSourcePlus>(FindObjectsSortMode.None);
            audioSourcePlusList.Clear();
            audioSourcePlusList.AddRange(audioSources);
        }

        private void GatherAllAudioSources()
        {
            AudioSource[] allAudioSources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            audioSourceList.Clear();

            foreach (AudioSource audioSource in allAudioSources)
            {
                if (audioSource.GetComponent<AudioSourcePlus>() == null)
                {
                    audioSourceList.Add(audioSource);
                }
            }
        }

        private void ConvertToAudioSourcePlus(AudioSource audioSource)
        {
            if (audioSource != null)
            {
                Undo.RegisterCompleteObjectUndo(audioSource.gameObject, "Convert to AudioSourcePlus");

                AudioSourcePlus audioSourcePlus = audioSource.gameObject.AddComponent<AudioSourcePlus>();
                audioSourcePlus.audioSource = audioSource;

                EditorUtility.SetDirty(audioSourcePlus);
                audioSourcePlusList.Add(audioSourcePlus);
            }
        }

        private void ConvertAllToAudioSourcePlus()
        {
            for (int i = 0; i < audioSourceList.Count; i++)
            {
                ConvertToAudioSourcePlus(audioSourceList[i]);
            }
            audioSourceList.Clear();
        }

        private void RevertAudioSourcePlusComponent(AudioSourcePlus audioSourcePlus)
        {
            if (audioSourcePlus != null)
            {
                if (audioSourcePlus.audioSource != null)
                {
                    Undo.RegisterCompleteObjectUndo(audioSourcePlus.audioSource, "Revert AudioSourcePlus");
                    audioSourcePlus.audioSource.hideFlags = HideFlags.None;
                    EditorUtility.SetDirty(audioSourcePlus.audioSource);
                }

                Undo.RegisterCompleteObjectUndo(audioSourcePlus, "Revert AudioSourcePlus");
                audioSourcePlus.skipCustomDestruction = true;
                bool enabledState = audioSourcePlus.enabled;
                AudioSource audioSource = audioSourcePlus.audioSource;

                Undo.DestroyObjectImmediate(audioSourcePlus);
                audioSource.enabled = enabledState;
            }
        }

        private void RevertAudioSourcePlusComponents()
        {
            foreach (var audioSourcePlus in audioSourcePlusList)
            {
                RevertAudioSourcePlusComponent(audioSourcePlus);
            }
            audioSourcePlusList.Clear();
        }
    }
}
