using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Transforms", "Beats")]
    public class ASPR_MoveOnBeats : MonoBehaviour, ASP_IAudioReaction
    {
        [HideInInspector]
        public new string name = "Move On Beats!";
        [HideInInspector]
        public string info = "This Component moves the transform in a specified direction whenever there's a beat!";

        public bool useTargetTransform = true;
        [HideInInspector]
        public List<Transform> affectedTransforms;

        [SerializeField]
        private Vector3 moveOffset = Vector3.up;
        [ASP_FloatSlider(0.01f, 20f)]
        public float resetSpeed = 5f;

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

        private Dictionary<Transform, Vector3> initialPositions = new Dictionary<Transform, Vector3>();
        private bool isInitialized = false;

        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            affectedTransforms ??= new List<Transform>();
            if (useTargetTransform)
            {
                affectedTransforms.Clear();
            }
            initialPositions.Clear();
            foreach (var transform in affectedTransforms)
            {
                if (transform != null && !initialPositions.ContainsKey(transform))
                {
                    initialPositions[transform] = transform.localPosition;
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
                if (!initialPositions.ContainsKey(targetTransform))
                {
                    initialPositions[targetTransform] = targetTransform.localPosition;
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

            if (averageSpectrumInWindow > frequencyRangeAndThreshold.w && Time.time >= lastBeatTime + beatCooldown)
            {
                foreach (var transform in affectedTransforms)
                {
                    if (transform == null) continue;

                    Vector3 beatPosition = initialPositions[transform] + moveOffset;

                    transform.localPosition = beatPosition;
                }

                lastBeatTime = Time.time;
            }
            else
            {
                foreach (var transform in affectedTransforms)
                {
                    if (transform == null || !initialPositions.ContainsKey(transform)) continue;

                    transform.localPosition = Vector3.Lerp(transform.localPosition, initialPositions[transform], resetSpeed * Time.deltaTime);
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
                if (transform != null && initialPositions.ContainsKey(transform))
                {
                    transform.localPosition = initialPositions[transform];
                }
            }
        }
    }
}
