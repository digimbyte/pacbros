/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TelePresent.AudioSyncPro
{
    [ExecuteInEditMode]
    public class AudioReactor : MonoBehaviour
    {
        public AudioSourcePlus audioSourcePlus;
        public Transform targetTransform;
        [SerializeField]
        public List<MonoBehaviour> reactionComponents;
        [SerializeField]
        private List<ASP_IAudioReaction> reactions;
        private Vector3 initialPosition;
        private Vector3 initialScale;
        private Quaternion initialRotation;
        private bool firstInitializationCompleted = false;
        private Transform tempTransform;
        private bool canUpdate = false;
#if UNITY_EDITOR
        private bool isEditorUpdateSubscribed = false;
#endif
        private bool isReactionsInitialized = false;

        private HashSet<MonoBehaviour> destroyedReactions = new HashSet<MonoBehaviour>();


        private AudioSourcePlus previousAudioSourcePlus;
        private void Awake()
        {
            if (targetTransform == null && !Application.isPlaying)
            {
                targetTransform = this.transform;
                if (audioSourcePlus == null)
                {
                    AudioSourcePlus[] audioSourcePluses = FindObjectsByType<AudioSourcePlus>(FindObjectsSortMode.None);
                    if (audioSourcePluses.Length == 1)
                    {
                        audioSourcePlus = audioSourcePluses[0];
                    }
                }
            }
            SubscribeToAudioEvents(audioSourcePlus);
            previousAudioSourcePlus = audioSourcePlus;

            EnsureReactionComponentsExist();
        }


        private void OnValidate()
        {
            if (Time.frameCount == 0) return;

            SubscribeToAudioEvents(audioSourcePlus);
            ReinitializeReactionsIfTransformChanged();
            CheckForExternalReactionComponents();

            // New check: Ensure the current GameObject has all reaction components.
            EnsureReactionComponentsExist();
        }
        private void EnsureReactionComponentsExist()
        {
            // Ensure that all reaction components in the list exist on this GameObject.
            if (reactionComponents != null)
            {
                for (int i = 0; i < reactionComponents.Count; i++)
                {
                    var component = reactionComponents[i];
                    if (component != null)
                    {
                        if (component.gameObject != this.gameObject)
                        {
                            // Copy component if it's from another GameObject
                            var newComponent = (MonoBehaviour)this.gameObject.AddComponent(component.GetType());

                            // Hide the new component in the inspector
                            newComponent.hideFlags = HideFlags.HideInInspector;

                            // Replace the reference with the new component
                            reactionComponents[i] = newComponent;
                        }
                        else
                        {
                            // Ensure hideFlags are applied for existing components on this GameObject
                            component.hideFlags = HideFlags.HideInInspector;
                        }
                    }
                }
                targetTransform = this.transform;
            }
        }


        private void OnEnable()
        {
            if (Time.frameCount == 0) return;
            SubscribeToAudioEvents(audioSourcePlus);
            ReinitializeReactionsIfTransformChanged();
            CheckForExternalReactionComponents();
            if (audioSourcePlus != null)
            {
                if (audioSourcePlus?.isPlaying == true && audioSourcePlus.reactorsShouldListen)
                {
                    OnAudioStarted();
                }
            }
        }

        private void CheckForExternalReactionComponents()
        {
            if (reactionComponents != null && reactionComponents.Count > 0)
            {
                List<MonoBehaviour> externalComponents = new List<MonoBehaviour>();

                foreach (var component in reactionComponents)
                {
                    if (component != null && component.gameObject != this.gameObject)
                    {
                        externalComponents.Add(component);
                    }
                }

                if (externalComponents.Count > 0)
                {
                    foreach (var externalComponent in externalComponents)
                    {
                        var newComponent = (MonoBehaviour)this.gameObject.AddComponent(externalComponent.GetType());
                        reactionComponents.Add(newComponent);
                        reactionComponents.Remove(externalComponent);
                    }
                }
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromAudioEvents(audioSourcePlus);
            UnsubscribeFromEditorUpdate();
        }

        private void OnDestroy()
        {
            if (Time.frameCount == 0) return;
            UnsubscribeFromAudioEvents(audioSourcePlus);
            ClearReactions();
        }

        public void InitializeNewReaction(ASP_IAudioReaction reaction)
        {
            if (reactions == null)
            {
                reactions = new List<ASP_IAudioReaction>();
            }
            reactions.Add(reaction);
            reaction.Initialize(initialPosition, initialScale, initialRotation);
        }

        private void InitializeReactions()
        {
            if (this == null || targetTransform == null || reactionComponents == null) return;

            reactions = reactionComponents.ConvertAll(x => x as ASP_IAudioReaction);
            if (reactions == null) return;

            initialPosition = targetTransform.localPosition;
            initialScale = targetTransform.localScale;
            initialRotation = targetTransform.localRotation;

            foreach (var reaction in reactions)
            {
                reaction?.Initialize(initialPosition, initialScale, initialRotation);
            }
        }

        private void OnAudioStarted()
        {
            if (this == null || audioSourcePlus == null) return;

            InitializeReactions();
            InitializeReactionsOnPlay();
            canUpdate = true;
            SubscribeToEditorUpdate();
        }

        private void OnAudioStopped()
        {
            canUpdate = false;
            ResetReactionsToOriginalState();
            UnsubscribeFromEditorUpdate();
        }

        private void LateUpdate()
        {
            if (canUpdate && Application.isPlaying && audioSourcePlus.reactorsShouldListen)
            {
                ReactToAudio();
            }
        }

#if UNITY_EDITOR
        private void EditorUpdate()
        {
            if (canUpdate && !Application.isPlaying && audioSourcePlus?.isPlaying == true && audioSourcePlus.reactorsShouldListen)
            {
                ReactToAudio();
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                ResetReactionsToOriginalState();
            }
        }
#endif

        public void ResetValueFromReactor(ASP_IAudioReaction reactor)
        {
            reactions?.Remove(reactor);
            reactor.ResetToOriginalState(targetTransform);
        }



        private void SubscribeToAudioEvents(AudioSourcePlus source)
        {
            if (source == null) return;

            source.OnAudioStarted += OnAudioStarted;
            source.OnAudioStopped += OnAudioStopped;
        }

        private void UnsubscribeFromAudioEvents(AudioSourcePlus source)
        {
            if (source == null) return;

            source.OnAudioStarted -= OnAudioStarted;
            source.OnAudioStopped -= OnAudioStopped;
        }

        private void SubscribeToEditorUpdate()
        {
#if UNITY_EDITOR
            if (!isEditorUpdateSubscribed)
            {
                EditorApplication.update += EditorUpdate;
                isEditorUpdateSubscribed = true;
            }
#endif
        }

        private void UnsubscribeFromEditorUpdate()
        {
#if UNITY_EDITOR
            if (isEditorUpdateSubscribed)
            {
                EditorApplication.update -= EditorUpdate;
                isEditorUpdateSubscribed = false;
            }
#endif
        }

        private void InitializeReactionsOnPlay()
        {
            if (!firstInitializationCompleted && targetTransform != null && audioSourcePlus?.isPlaying == true)
            {
                if (!isReactionsInitialized)
                {
                    isReactionsInitialized = true;
                    OnAudioStarted();
                    InitializeReactions();
                    firstInitializationCompleted = true;
                }
            }
        }

        private void ReinitializeReactionsIfTransformChanged()
        {
            if (targetTransform != tempTransform)
            {
                InitializeReactions();
            }
            tempTransform = targetTransform;
        }

        private void ClearReactions()
        {
            if (!Application.isPlaying && reactions != null)
            {
                foreach (var reaction in reactions)
                {
                    var monoBehaviour = reaction as MonoBehaviour;
                    if (monoBehaviour != null && monoBehaviour.gameObject != null && monoBehaviour.gameObject.activeInHierarchy && !destroyedReactions.Contains(monoBehaviour))
                    {
                        destroyedReactions.Add(monoBehaviour);

#if UNITY_EDITOR
                        DestroyImmediate(monoBehaviour, true);
#else
                        Destroy(monoBehaviour);
#endif
                    }
                }
                reactions.Clear();
                reactions = null;
                destroyedReactions.Clear();
            }
        }

        public void ResetReactionsToOriginalState()
        {
            if (reactions == null || targetTransform == null) return;

            foreach (var reaction in reactions)
            {
                reaction?.ResetToOriginalState(targetTransform);
            }
        }

        private void ReactToAudio()
        {
            if (reactions == null) return;

            foreach (var reaction in reactions)
            {
                if (reaction != null)
                {
                    if (!reaction.IsActive) continue;
                    reaction?.React(audioSourcePlus, targetTransform, audioSourcePlus.rmsValue, audioSourcePlus.spectrumData);
                }
            }
        }

        public void ToggleAllAudioReactions(bool isActive)
        {
            if (reactions == null) return;

            foreach (var reaction in reactions)
            {
                var monoBehaviour = reaction as MonoBehaviour;

                var audioReaction = reaction as ASP_IAudioReaction;
                if (audioReaction != null)
                {
                    audioReaction.IsActive = isActive;
                }
            }
        }

        private void Update()
        {
            if (audioSourcePlus != previousAudioSourcePlus)
            {
                // Unsubscribe from the previous AudioSourcePlus
                if (previousAudioSourcePlus != null)
                {
                    UnsubscribeFromAudioEvents(previousAudioSourcePlus);
                }

                // Subscribe to the new AudioSourcePlus
                if (audioSourcePlus != null)
                {
                    SubscribeToAudioEvents(audioSourcePlus);

                    // React to the current state of the new AudioSourcePlus
                    if (audioSourcePlus.isPlaying && audioSourcePlus.reactorsShouldListen)
                    {
                        OnAudioStarted();
                    }
                    else
                    {
                        OnAudioStopped();
                    }
                }
                else
                {
                    // If the new reference is null, stop reacting
                    OnAudioStopped();
                }

                // Update the previous reference
                previousAudioSourcePlus = audioSourcePlus;
            }
        }
    }
}
