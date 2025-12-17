using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Transforms", "Beats")]
    public class ASPR_RotateOnBeats : MonoBehaviour, ASP_IAudioReaction
    {
        [HideInInspector]
        public new string name = "Rotate On Beats!";
        [HideInInspector]
        public string info = "This Component adds a rotation offset whenever there's a beat!";

        public bool useTargetTransform = true;
        [HideInInspector]
        public List<Transform> affectedTransforms;
        public float rotationMultiplier = 10.0f;
        [ASP_FloatSlider(0.01f, 20f)]
        public float resetSpeed = 3.5f;

        public bool RandomRotationDirection = true;
        public Vector3 rotationDirection = Vector3.up;
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
        private float lastBeatTime = 0f;
        public float beatCooldown = 0.25f;

        private Dictionary<Transform, Quaternion> initialRotations = new Dictionary<Transform, Quaternion>();
        private bool isInitialized = false;

        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            affectedTransforms ??= new List<Transform>();
            if (useTargetTransform)
            {
                affectedTransforms.Clear();
            }
            initialRotations.Clear();
            foreach (var transform in affectedTransforms)
            {
                if (transform != null && !initialRotations.ContainsKey(transform))
                {
                    initialRotations[transform] = transform.localRotation;
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
                if (!initialRotations.ContainsKey(targetTransform))
                {
                    initialRotations[targetTransform] = targetTransform.localRotation;
                }
            }

            if (!isInitialized) return;

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


            // Check if enough time has passed since the last beat and if the spectrum data is above the threshold
            if (Time.time >= lastBeatTime + beatCooldown && averageSpectrumInWindow > frequencyRangeAndThreshold.w)
            {
                foreach (var transform in affectedTransforms)
                {
                    if (transform == null) continue;

                    Vector3 direction = RandomRotationDirection ? Random.onUnitSphere : rotationDirection;
                    Quaternion rotationOffset = Quaternion.Euler(direction * rotationMultiplier);

                    transform.localRotation *= rotationOffset; // Apply the rotation offset
                }

                lastBeatTime = Time.time; // Update the last beat time
            }
            else
            {
                // Smoothly reset rotations to initial state
                foreach (var transform in affectedTransforms)
                {
                    if (transform == null || !initialRotations.ContainsKey(transform)) continue;

                    transform.localRotation = Quaternion.Lerp(transform.localRotation, initialRotations[transform], resetSpeed * Time.deltaTime);
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
                if (transform != null && initialRotations.ContainsKey(transform))
                {
                    transform.localRotation = initialRotations[transform];
                }
            }
        }
    }
}
