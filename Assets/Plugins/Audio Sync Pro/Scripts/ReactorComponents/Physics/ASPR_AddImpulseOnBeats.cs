using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Physics")]
    public class ASPR_AddImpulseOnBeats : MonoBehaviour, ASP_IAudioReaction
    {
        [HideInInspector]
        public new string name = "Add Impulse On Beats!";
        public string info = "Apply an impulse to Rigidbodies on audio beats (Play Mode Only).";

        public List<Rigidbody> targetRigidbodies;

        public Vector3 impulseIntensity = new Vector3(0f, 2f, 0f); // Adjust this value to control the impulse force

        [ASP_FloatSlider(0f, 2f)]
        public float sensitivity = .25f; // Adjust this value to control how sensitive the beat detection is

        [ASP_SpectrumDataSlider(0f, .1f, "spectrumDataForSlider")]
        public Vector4 frequencyRangeAndThreshold = new Vector4(0f, 5000f, 0f, 0.04f);

        private Dictionary<Rigidbody, Vector3> initialVelocities = new Dictionary<Rigidbody, Vector3>();
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

        // New variables for beat detection
        private const int energyBufferSize = 43; // Number of past energies to consider
        private Queue<float> energyBuffer = new Queue<float>();

        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            if (targetRigidbodies == null)
            {
                targetRigidbodies = new List<Rigidbody>();
            }
            foreach (var rigidbody in targetRigidbodies)
            {
                if (rigidbody != null && !initialVelocities.ContainsKey(rigidbody))
                {
                    initialVelocities[rigidbody] = rigidbody.linearVelocity;
                }
            }

            isInitialized = true;
        }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!IsActive) return;

            if (targetTransform.TryGetComponent<Rigidbody>(out Rigidbody targetRigidbody) && !targetRigidbodies.Contains(targetRigidbody))
            {
                targetRigidbodies.Add(targetRigidbody);
                if (!initialVelocities.ContainsKey(targetRigidbody))
                {
                    initialVelocities[targetRigidbody] = targetRigidbody.linearVelocity;
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


            if (Time.time - lastBeatTime < beatCooldown)
            {
                return;
            }

                if (averageSpectrumInWindow > frequencyRangeAndThreshold.w) // Ensure cooldown has passed
            {
                foreach (var rigidbody in targetRigidbodies)
                {
                    if (rigidbody != null)
                    {
                        rigidbody.AddForce(impulseIntensity, ForceMode.Impulse); // Apply impulse force on beat
                    }
                }

                lastBeatTime = Time.time; // Update the last beat time
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
            if (!isInitialized) return;

            foreach (var rigidbody in targetRigidbodies)
            {
                if (rigidbody != null && initialVelocities.ContainsKey(rigidbody))
                {
                    rigidbody.linearVelocity = initialVelocities[rigidbody];
                }
            }
        }
    }
}
