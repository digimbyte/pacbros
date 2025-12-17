// ASPR_ScaleOnBeats.cs

using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Transforms", "Beats")]
    public class ASPR_ScaleOnBeats : MonoBehaviour, ASP_IAudioReaction
    {
        public new string name = "Scale On Beats!";
        public string info = "Scale Transforms by a multiplier every Beat!";

        public bool useTargetTransform = true;
        [HideInInspector]
        public List<Transform> affectedTransforms;

        [ASP_ScaleVector3]
        public Vector3 scaleMultiplier = new Vector3(1.5f, 1.5f, 1.5f); // Adjust this value to control how much the transform scales on a beat

        [ASP_FloatSlider(0f, 2f)]
        public float sensitivity = .25f; // Adjust this value to control how sensitive the beat detection is

        [ASP_SpectrumDataSlider(0f, .1f, "spectrumDataForSlider")]
        public Vector4 frequencyRangeAndThreshold = new Vector4(0f, 5000f, 0f, 0.04f);
        // frequencyRangeAndThreshold.x = Min Frequency (Hz)
        // frequencyRangeAndThreshold.y = Max Frequency (Hz)
        // frequencyRangeAndThreshold.z = Current Average Spectrum Value (for debugging/display)
        // frequencyRangeAndThreshold.w = Threshold for beat detection

        private Dictionary<Transform, Vector3> initialScales = new Dictionary<Transform, Vector3>();
        private bool isInitialized = false;
        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }

        private float[] spectrumDataForSlider = new float[512];
        private float lastBeatTime = 0f; // Time of the last beat
        public float beatCooldown = 0.25f; // Cooldown duration in seconds

        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            affectedTransforms ??= new List<Transform>(); // Initialize if null
            if (useTargetTransform)
            {
                affectedTransforms.Clear();
            }
            initialScales.Clear();
            foreach (var transform in affectedTransforms)
            {
                if (transform != null && !initialScales.ContainsKey(transform))
                {
                    initialScales[transform] = transform.localScale;
                }
            }

            isInitialized = true;
        }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!IsActive) return;

            if (useTargetTransform && !affectedTransforms.Contains(targetTransform))
            {
                affectedTransforms.Add(targetTransform);
                if (!initialScales.ContainsKey(targetTransform))
                {
                    initialScales[targetTransform] = targetTransform.localScale;
                }
            }

            UpdateSpectrumData(spectrumData);

            // Calculate frequency per bin
            float sampleRate = AudioSettings.outputSampleRate;
            float freqPerBin = sampleRate / 2f / spectrumData.Length;

            // Define logarithmic frequency range
            float minLogFreq = Mathf.Log10(frequencyRangeAndThreshold.x + 1f); // +1 to avoid log(0)
            float maxLogFreq = Mathf.Log10(frequencyRangeAndThreshold.y + 1f);

            // Analyze spectrum data within the frequency window using logarithmic scaling
            float averageSpectrumInWindow = 0f;
            int count = 0;

            for (int i = 0; i < spectrumData.Length; i++)
            {
                float freq = i * freqPerBin;
                float logFreq = Mathf.Log10(freq + 1f); // +1 to avoid log(0)

                if (logFreq >= minLogFreq && logFreq <= maxLogFreq)
                {
                    averageSpectrumInWindow += spectrumData[i];
                    count++;
                }
            }
            averageSpectrumInWindow *= 100;
            if (count > 0)
            {
                averageSpectrumInWindow /= count; // Normalize by the number of bins
            }

            averageSpectrumInWindow *= sensitivity;
            frequencyRangeAndThreshold.z = averageSpectrumInWindow;

            // Determine if a beat is detected
            if (averageSpectrumInWindow > frequencyRangeAndThreshold.w && (Time.time - lastBeatTime >= beatCooldown)) // Ensure cooldown has passed
            {
                foreach (var transform in affectedTransforms)
                {
                    if (transform != null && initialScales.ContainsKey(transform))
                    {
                        Vector3 initialScale = initialScales[transform];
                        Vector3 beatScale = Vector3.Scale(initialScale, scaleMultiplier);

                        // Set transform to beat scale
                        transform.localScale = beatScale;
                    }
                }

                // Reset the last beat time after successfully processing the beat
                lastBeatTime = Time.time;
            }
            else
            {
                // Return the objects to their original scales smoothly
                foreach (var transform in affectedTransforms)
                {
                    if (transform != null && initialScales.ContainsKey(transform))
                    {
                        transform.localScale = Vector3.Lerp(transform.localScale, initialScales[transform], 10f * Time.deltaTime);
                    }
                }
            }
        }

        private void UpdateSpectrumData(float[] spectrumData)
        {
            int length = Mathf.Min(spectrumDataForSlider.Length, spectrumData.Length);
            for (int i = 0; i < length; i++)
            {
                spectrumDataForSlider[i] = spectrumData[i];
            }
            for (int i = length; i < spectrumDataForSlider.Length; i++)
            {
                spectrumDataForSlider[i] = 0f;
            }
        }

        public void ResetToOriginalState(Transform targetTransform)
        {
            if (!isInitialized) return;

            foreach (var transform in affectedTransforms)
            {
                if (transform != null && initialScales.ContainsKey(transform))
                {
                    transform.localScale = initialScales[transform];
                }
            }
        }
    }
}
