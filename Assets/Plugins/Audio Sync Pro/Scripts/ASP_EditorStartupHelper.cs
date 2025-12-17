using System;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace TelePresent.AudioSyncPro
{
    [Serializable]
    public class ASP_EditorStartupHelper : ScriptableObject
    {
        private static ASP_EditorStartupHelper singletonInstance;

        public static ASP_EditorStartupHelper Singleton
        {
            get
            {
                if (singletonInstance == null)
                {
                    singletonInstance = Resources.Load<ASP_EditorStartupHelper>("ASP_EditorStartupHelper");
                    if (singletonInstance == null)
                    {
                        singletonInstance = CreateInstance<ASP_EditorStartupHelper>();
                    }
                }
                return singletonInstance;
            }
        }

        [SerializeField] private bool displayWelcomeMessageOnLaunch = true;
        [SerializeField] private bool firstInitialization = true;

        public static bool DisplayWelcomeOnLaunch
        {
            get => Singleton.displayWelcomeMessageOnLaunch;
            set
            {
                if (value != Singleton.displayWelcomeMessageOnLaunch)
                {
                    Singleton.displayWelcomeMessageOnLaunch = value;
                    PersistStartupPreferences();
                }
            }
        }

        public static bool FirstInitialization
        {
            get => Singleton.firstInitialization;
            set
            {
                if (value != Singleton.firstInitialization)
                {
                    Singleton.firstInitialization = value;
                    PersistStartupPreferences();
                }
            }
        }

        public static void PersistStartupPreferences()
        {
            if (!AssetDatabase.Contains(Singleton))
            {
                var temporaryCopy = CreateInstance<ASP_EditorStartupHelper>();
                EditorUtility.CopySerialized(Singleton, temporaryCopy);

                string assetPath = "Assets/TelePresent/Audio Sync Pro/Scripts/ScriptableObjects/Resources/ASP_EditorStartupHelper.asset";

                singletonInstance = Resources.Load<ASP_EditorStartupHelper>("ASP_EditorStartupHelper");
                if (singletonInstance == null)
                {
                    Debug.Log("Creating new ASP_EditorStartupHelper asset");
                    AssetDatabase.CreateAsset(temporaryCopy, assetPath);
                    AssetDatabase.Refresh();
                    singletonInstance = temporaryCopy;
                    return;
                }
                EditorUtility.CopySerialized(temporaryCopy, singletonInstance);
            }
            EditorUtility.SetDirty(Singleton);
        }
    }
}
#endif