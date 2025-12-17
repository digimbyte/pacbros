using UnityEditor;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [InitializeOnLoad]
    public class AudioSyncProWelcomeWindow : EditorWindow
    {
        private Texture2D docsIcon;
        private Texture2D discordIcon;

        private const string IconsFolderPath = "Assets/TelePresent/Audio Sync Pro/Editor/";
        private const string DocsIconPath = IconsFolderPath + "docs.png";
        private const string DiscordIconPath = IconsFolderPath + "discord.png";

        private double animationTimer;
        private double lastTime;
        private const float AnimationSpeed = 2f;

        static AudioSyncProWelcomeWindow()
        {
            EditorApplication.update -= TriggerWelcomeScreen;
            EditorApplication.update += TriggerWelcomeScreen;
        }

        private static void TriggerWelcomeScreen()
        {
            if (ASP_EditorStartupHelper.FirstInitialization)
            {
                ASP_EditorStartupHelper.FirstInitialization = false;
                ASP_EditorStartupHelper.PersistStartupPreferences();
                ShowWindow();
            }
            else if (ASP_EditorStartupHelper.DisplayWelcomeOnLaunch && EditorApplication.timeSinceStartup < 30f)
            {
                ShowWindow();
            }

            EditorApplication.update -= TriggerWelcomeScreen;
        }

        private static bool ShowOnStartup
        {
            get => ASP_EditorStartupHelper.DisplayWelcomeOnLaunch;
            set => ASP_EditorStartupHelper.DisplayWelcomeOnLaunch = value;
        }
        public static void ShowWindow()
        {
            var window = GetWindow<AudioSyncProWelcomeWindow>("Welcome to Audio Sync Pro");
            window.minSize = new Vector2(450, 400);
            window.Show();
        }

        private void OnEnable()
        {
            docsIcon = LoadIcon(DocsIconPath);
            discordIcon = LoadIcon(DiscordIconPath);

            lastTime = EditorApplication.timeSinceStartup;
            animationTimer = 0f;
            EditorApplication.update += UpdateAnimation;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateAnimation;
        }

        private Texture2D LoadIcon(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private void UpdateAnimation()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            double deltaTime = currentTime - lastTime;
            lastTime = currentTime;
            animationTimer += deltaTime * AnimationSpeed;
            Repaint();
        }

        private void OnGUI()
        {
            GUILayout.Space(15);
            DrawHeader();
            GUILayout.Space(20);
            DrawIntro();
            GUILayout.Space(15);
            DrawButtons();
            GUILayout.Space(15);
            DrawToggle();
            GUILayout.FlexibleSpace();
            DrawFooter();
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Welcome to Audio Sync Pro!", GetTitleStyle());
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawIntro()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "Thank you for choosing Audio Sync Pro! :) \n\nI hope this tool will prove to be a great way for you to work with audio in your project. \n\n Below, you'll find helpful resources and ways to reach out.",
                GetBodyStyle(),
                GUILayout.Width(position.width - 50)
            );
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawButtons()
        {
            DrawCenteredButton("Documentation", "Learn about features, setup, and best practices.", docsIcon, () => Application.OpenURL("https://telepresentgames.dk/Unity%20Asset/Audio%20Sync%20Pro%20Documentation.pdf"));
            DrawAnimatedDiscordButton();
        }

        private void DrawCenteredButton(string title, string description, Texture2D icon, System.Action onClick)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical();
            GUILayout.Label(description, GetDescriptionStyle(), GUILayout.Width(300));

            var content = icon != null ? new GUIContent($"  {title}", icon) : new GUIContent($"  {title}");
            if (GUILayout.Button(content, GetButtonStyle(), GUILayout.Width(300), GUILayout.Height(40)))
            {
                onClick?.Invoke();
            }

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawAnimatedDiscordButton()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical();
            GUILayout.Label("Ask questions, troubleshoot, and share your work!", GetDescriptionStyle(), GUILayout.Width(300));

            Color animatedColor = Color.Lerp(Color.white, new Color(1f, .5f, .5f), (Mathf.Sin((float)animationTimer) + 1f) / 2f);

            GUIStyle animatedButtonStyle = new GUIStyle(GetButtonStyle())
            {
                normal = { textColor = animatedColor },
                focused = { textColor = animatedColor },
                hover = { textColor = animatedColor },
                active = { textColor = animatedColor }
            };

            var content = discordIcon != null ? new GUIContent($"  Join Discord", discordIcon) : new GUIContent($"  Join Discord");
            if (GUILayout.Button(content, animatedButtonStyle, GUILayout.Width(300), GUILayout.Height(40)))
            {
                Application.OpenURL("https://discord.gg/DCWnPkRmTf");
            }

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private void DrawToggle()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            ShowOnStartup = GUILayout.Toggle(ShowOnStartup, "Show window on start-up", GUILayout.Width(250));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawFooter()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("© 2025 TelePresent Games", EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private GUIStyle GetTitleStyle()
        {
            return new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private GUIStyle GetBodyStyle()
        {
            return new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperCenter,
                wordWrap = true
            };
        }

        private GUIStyle GetDescriptionStyle()
        {
            return new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                padding = new RectOffset(0, 10, 5, 5)
            };
        }

        private GUIStyle GetButtonStyle()
        {
            return new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(10, 10, 5, 5)
            };
        }
    }
}
