using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Events")]
    public class ASPR_CallEventOnBeat : MonoBehaviour, ASP_IAudioReaction
    {
        [HideInInspector]
        public new string name = "Call Event On Beats!";
        public string info = "Calls a UnityEvent every time a beat is detected (Play Mode Only).";

        [SerializeField]
        private UnityEvent onBeatDetected;  // UnityEvent to invoke on each beat

        [ASP_FloatSlider(0f, 2f)]
        public float sensitivity = .25f; // Adjust this value to control how sensitive the beat detection is

        [ASP_SpectrumDataSlider(0f, .1f, "spectrumDataForSlider")]
        public Vector4 frequencyRangeAndThreshold = new Vector4(0f, 5000f, 0f, 0.04f);

        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }

        private float[] spectrumDataForSlider = new float[512];
        private float lastBeatTime = 0f;  // Time of the last beat
        public float beatCooldown = 0.25f;  // Cooldown duration in seconds

        // New variables for beat detection
        private const int energyBufferSize = 43; // Number of past energies to consider
        private Queue<float> energyBuffer = new Queue<float>();

        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            // No setup needed
        }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!IsActive) return;

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

            if (Time.time - lastBeatTime < beatCooldown)
            {
                return;
            }

            if (averageSpectrumInWindow > frequencyRangeAndThreshold.w)
            {
                TriggerEventOnBeat();
                lastBeatTime = Time.time;
            }
        }

        private void TriggerEventOnBeat()
        {
            if (onBeatDetected != null)
            {
                Debug.Log("Beat Detected! Preparing to invoke event.");

                onBeatDetected.Invoke();  // Invoke the UnityEvent

                Debug.Log("UnityEvent invoked successfully.");
            }
            else
            {
                Debug.LogWarning("No UnityEvent assigned to Call Event On Beat!");
            }
        }


        private void UpdateSpectrumData(float[] spectrumData)
        {
            for (int i = 0; i < spectrumDataForSlider.Length; i++)
            {
                spectrumDataForSlider[i] = i < spectrumData.Length ? spectrumData[i] : 0f;
            }
        }

        public void ResetToOriginalState(Transform targetTransform)
        {
            // No reset needed
        }
    }
}
