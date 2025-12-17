/*******************************************************
Product - Audio Sync Pro
  Publisher - TelePresent Games
              http://TelePresentGames.dk
  Author    - Martin Hansen
  Created   - 2024
  (c) 2024 Martin Hansen. All rights reserved.
/*******************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;


#if UNITY_EDITOR
using UnityEditor.Compilation;

using UnityEditor;
#endif
using System.Collections;

namespace TelePresent.AudioSyncPro
{
    [ExecuteInEditMode]
    public class AudioSourcePlus : MonoBehaviour
    {
        [HideInInspector]
        public AudioSource audioSource;
        public event Action OnAudioStopped;
        public event Action OnAudioStarted;
        bool playingOneShot;

        [SerializeField]
        private int sampleSize = 1024;  // Default sample size

        public int SampleSize
        {
            get => sampleSize;
            set
            {
                if (sampleSize != value)
                {
                    sampleSize = value;

                    if (sampleSize == -1)
                    {
                        samples = null;
                        spectrumData = null;
                    }
                    else
                    {
                        if (sampleSize > 0)
                        {
                            samples = new float[sampleSize];
                            spectrumData = new float[sampleSize];
                        }
                        else
                        {
                            Debug.LogError(
                                "Invalid SampleSize value. Must be a positive integer or -1 for 'Disable Spectrum Data'.");
                        }
                    }
                }
            }
        }

        private float[] samples = new float[1024];
        public float[] spectrumData = new float[1024];
        public float rmsValue;
        [SerializeField]
        public List<ASP_Marker> markers = new List<ASP_Marker>();

        public float playheadPosition = 0f;
        public bool isPlaying => audioSource.isPlaying;
        public bool canUpdate = false;
        private bool wasPlaying = false;
        private bool wasPaused = false;
        private bool hasStopped = false;
#if UNITY_EDITOR
        private bool wasPlayingBeforePause = false;

        private bool pausedForCompilation = false;
#endif
        public List<AnimationCurve> audioCurves = new List<AnimationCurve>();
        private bool isDestroyed = false;
        public bool hasBeenInitialized = false;
        public bool showAudioSource;
        [SerializeField]
        public bool showWaveformTimeline = false;
        public bool skipCustomDestruction = false;
        public bool reactorsShouldListen = true;

        [SerializeField]
        public float[] precomputedRMSValues;

        [SerializeField]
        private float rmsInterval = 0.1f;

#if UNITY_EDITOR
        public static bool EditorApplicationQuit = false;

        static bool WantsToQuit()
        {
            EditorApplicationQuit = true;
            return true;
        }
#endif

        private AudioClip previousClip;

        void Start()
        {
            EnsureAudioSource();
            Application.runInBackground = true;
            previousClip = audioSource.clip;
        }

#if UNITY_EDITOR
        private void OnEnable()
        {
            EnsureAudioSource();
            if (audioSource != null)
                audioSource.enabled = true;

            EditorApplication.wantsToQuit += WantsToQuit;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.pauseStateChanged += OnPauseStateChanged;
            EditorApplication.update += Update;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            ComputePrecomputedRMSValues();

            previousClip = audioSource.clip;
        }

        private void OnDisable()
        {
            if (audioSource != null) audioSource.enabled = false;

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            EditorApplication.pauseStateChanged -= OnPauseStateChanged;
            EditorApplication.update -= Update;
        }
#endif

        private void EnsureAudioSource()
        {
            if (audioSource == null)
            {
                audioSource = gameObject.GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                }
            }
            ToggleAudioSourceVisibility(showAudioSource);
            if (audioCurves.Count == 0)
            {
                audioCurves.Add(audioSource.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
                audioCurves.Add(audioSource.GetCustomCurve(AudioSourceCurveType.ReverbZoneMix));
                audioCurves.Add(audioSource.GetCustomCurve(AudioSourceCurveType.SpatialBlend));
                audioCurves.Add(audioSource.GetCustomCurve(AudioSourceCurveType.Spread));
            }
        }

        public void ToggleAudioSourceVisibility(bool visibility)
        {
            if (visibility)
            {
                audioSource.hideFlags = HideFlags.None;
            }
            else
            {
                audioSource.hideFlags = HideFlags.HideInInspector;
            }
        }

        private void Update()
        {
            if (!audioSource)
                return;
            if (audioSource.clip is null)
                return;
#if UNITY_EDITOR
            if (!EditorApplication.isPaused)
            {
                if (audioSource.isPlaying)
                {
                    wasPlayingBeforePause = true;
                }
                else
                {
                    wasPlayingBeforePause = false;
                }
            }
#endif

            if (audioSource.clip != previousClip)
            {
                OnClipChanged();
                previousClip = audioSource.clip;
            }

            if (audioSource.isPlaying && !wasPlaying)
            {
                PlayAudio();
                OnAudioStarted?.Invoke();
                wasPlaying = true;
                wasPaused = false;
            }

            if (!audioSource.isPlaying && !wasPaused && wasPlaying)
            {
                PauseAudio();
                wasPaused = true;
                wasPlaying = false;
            }

            if (audioSource.isPlaying && wasPaused)
            {
                PlayAudio();
                wasPaused = false;
                wasPlaying = true;
            }

            if (!audioSource.isPlaying && !audioSource.time.Equals(0f) && !hasStopped)
            {
                StopAudio();
                InvokeStopped();
                playheadPosition = 0f;
                hasStopped = true;
                wasPlaying = false;
                wasPaused = false;
            }

            if (!canUpdate)
            {
                return;
            }

            if (audioSource.isPlaying && !playingOneShot && audioSource.clip)
            {
                CheckMarkers();
                rmsValue = GetPrecomputedRMSValue(audioSource.time);

                CalculateSpectrumData();

                hasStopped = false;
                return;
            }
            else if (audioSource.isPlaying && (playingOneShot || audioSource.clip == null))
            {
                CalculateRMSFromLiveData();
                hasStopped = false;
            }
        }

        private void OnClipChanged()
        {
            if (audioSource.clip == null)
            {
                canUpdate = false;
                precomputedRMSValues = null;
                playheadPosition = 0f;
                return;
            }

            ComputePrecomputedRMSValues();
            playheadPosition = 0f;
        }

        private void InvokeStopped()
        {
            OnAudioStopped?.Invoke();
        }

        private void CalculateSpectrumData()
        {
            if (SampleSize == -1)
            {
                spectrumData = new float[2];
                return;
            }

#if UNITY_WEBGL
    // Beat Detection not supported in WebGL
    spectrumData = new float[2];
    return;
#else
            // Create arrays to store spectrum data for both channels
            float[] leftChannelSpectrum = new float[sampleSize];
            float[] rightChannelSpectrum = new float[sampleSize];

            // Apply a window function (e.g., Hamming window) to reduce spectral leakage
            FFTWindow windowType = FFTWindow.Hamming;

            // Get spectrum data from both left and right channels
            audioSource.GetSpectrumData(leftChannelSpectrum, 0, windowType);
            audioSource.GetSpectrumData(rightChannelSpectrum, 1, windowType);

            // Combine spectrum data by averaging both channels
            for (int i = 0; i < sampleSize; i++)
            {
                spectrumData[i] = (leftChannelSpectrum[i] + rightChannelSpectrum[i]) / 2f;
            }
#endif
        }



        public void PlayOneShot(AudioClip clip)
        {
            if (audioSource == null)
            {
                return;
            }

            audioSource.PlayOneShot(clip);
            PlayAudio();
            playingOneShot = true;
        }

#if UNITY_EDITOR
        public void AddMarker(ASP_Marker marker)
        {
            markers.Add(marker);
            markers.Sort((a, b) => a.Time.CompareTo(b.Time));
            EditorUtility.SetDirty(this);
        }
#endif

        private void CheckMarkers()
        {
            foreach (var marker in markers)
            {
                if (audioSource.time >= marker.Time && audioSource.time >= marker.Time + .01f && !marker.IsTriggered)
                {
                    if (!Application.isPlaying && marker.ExecuteInEditMode == false)
                    {
                        continue;
                    }
                    marker.Trigger();
                    marker.justTriggered = true;
                    StartCoroutine(LerpColor(marker));
                }

                if (audioSource.time < marker.Time)
                {
                    marker.ResetTrigger();
                }
            }
        }

        private IEnumerator LerpColor(ASP_Marker marker)
        {
            float t = 0;
            Color targetColor = Color.yellow;
            Color startColor = marker.justTriggeredColor;
            Color currentColor = marker.justTriggeredColor;
            float speed = 10f;

            while (t < 1)
            {
                t += Time.deltaTime * speed;
                currentColor = Color.Lerp(startColor, targetColor, t);
                marker.justTriggeredColor = currentColor;
                yield return null;
            }

            marker.justTriggeredColor = startColor;
            marker.justTriggered = false;
        }

        public void PlayAudio()
        {
            if (audioSource != null)
            {
                if (!audioSource.isPlaying)
                {
                    audioSource.Play();
                }

                canUpdate = true;
                OnAudioStarted?.Invoke();
                hasStopped = false;
                wasPlaying = true;
                CheckMarkers();
            }
            else
            {
                Debug.LogError("AudioSource is null in PlayAudio(). Cannot play audio.");
            }
        }

        public void PauseAudio()
        {
            if (audioSource != null)
            {
                canUpdate = false;
                audioSource.Pause();
                InvokeStopped();
                hasStopped = true;
                wasPlaying = false;
                wasPaused = true;
            }
        }

        public void StopAudio()
        {
            if (audioSource != null)
            {
                playingOneShot = false;
                canUpdate = false;
                audioSource.Stop();
                InvokeStopped();
                hasStopped = true;
                wasPlaying = false;
                wasPaused = false;
                playheadPosition = 0f;
            }
        }
        public void CalculateRMSFromLiveData()
        {
            // If no samples array is initialized, create it
            if (samples == null || samples.Length != sampleSize)
            {
                samples = new float[sampleSize];
            }

            // Get the audio samples currently being played by the AudioSource
            audioSource.GetOutputData(samples, 0); // Get data from channel 0

            // Calculate RMS value from the retrieved samples
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i]; // Square each sample
            }

            // Calculate the mean of the squared samples and take the square root
            rmsValue = Mathf.Sqrt(sum / samples.Length);
        }

        public void ComputePrecomputedRMSValues()
        {
            if (audioSource == null || audioSource.clip == null)
            {
                return;
            }

            AudioClip clip = audioSource.clip;
            int totalSamples = clip.samples;
            int channels = clip.channels;
            float clipLength = clip.length;

            int rmsSampleCount = Mathf.CeilToInt(clipLength / rmsInterval);
            precomputedRMSValues = new float[rmsSampleCount];

            float[] clipData = new float[totalSamples * channels];
            clip.GetData(clipData, 0);

            int samplesPerInterval = Mathf.FloorToInt((rmsInterval * clip.frequency) * channels);

            for (int i = 0; i < rmsSampleCount; i++)
            {
                int startSample = i * samplesPerInterval;
                int sampleCount = Mathf.Min(samplesPerInterval, clipData.Length - startSample);

                float sum = 0f;
                for (int j = 0; j < sampleCount; j += channels)
                {
                    float sample = clipData[startSample + j];
                    sum += sample * sample;
                }

                float meanSquare = sum / (sampleCount / channels);
                precomputedRMSValues[i] = Mathf.Sqrt(meanSquare);
            }

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        public float GetPrecomputedRMSValue(float time)
        {
            if (precomputedRMSValues != null || precomputedRMSValues.Length != 0)
            {
                float clipLength = audioSource.clip.length;
                int totalSamples = precomputedRMSValues.Length;

                time = Mathf.Clamp(time, 0f, clipLength);

                float normalizedTime = time / clipLength;
                float exactIndex = normalizedTime * (totalSamples - 1);
                int index = Mathf.FloorToInt(exactIndex);

                if (index < precomputedRMSValues.Length - 1)
                {
                    float t = exactIndex - index;
                    return Mathf.Lerp(precomputedRMSValues[index], precomputedRMSValues[index + 1], t);
                }
                else
                {
                    return precomputedRMSValues[precomputedRMSValues.Length - 1];
                }
            }

            return 0f;
        }

        public void ToggleReactorsShouldListen(bool shouldListen)
        {
            reactorsShouldListen = shouldListen;
        }

        private void OnDestroy()
        {
            if (isDestroyed || Application.isPlaying)
            {
                return;
            }

#if UNITY_EDITOR
            if (Time.frameCount == 0 || EditorApplicationQuit)
            {
                return;
            }
#endif

            if (!isDestroyed)
            {
                StopAudio();
                InvokeStopped();
                if (!skipCustomDestruction)
                {
                    DestroyAudioSource();
                }
                isDestroyed = true;
            }
        }

        private void DestroyAudioSource()
        {
            if (audioSource != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(audioSource);
                    audioSource = null;
                }
                else
                {
#if UNITY_EDITOR
                    if (this.gameObject.activeInHierarchy)
                        DestroyImmediate(audioSource);
#endif
                    audioSource = null;
                }
            }
        }

#if UNITY_EDITOR
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (isPlaying && !Application.runInBackground)
                {
                    StopAudio();
                }
            }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                InvokeStopped();
            }
        }

        private void OnPauseStateChanged(PauseState state)
        {
            if (state == PauseState.Unpaused)
            {
                if (wasPlayingBeforePause)
                {
                    PlayAudio();
                    wasPlayingBeforePause = false;
                }
            }
        }

        // Pause the audio when compilation starts
        private void OnCompilationStarted(object obj)
        {
            if (audioSource.isPlaying)
            {
                pausedForCompilation = true;
                PauseAudio();
                Debug.Log("Compilation started: Audio paused.");
            }
        }

        // Resume the audio if it was paused due to compilation
        private void OnCompilationFinished(object obj)
        {
            if (pausedForCompilation)
            {
                pausedForCompilation = false;
                PlayAudio();
                Debug.Log("Compilation finished: Audio resumed.");
            }
        }
#endif
    }
}
