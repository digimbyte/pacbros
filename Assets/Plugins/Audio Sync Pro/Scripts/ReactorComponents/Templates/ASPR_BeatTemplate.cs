using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    // Replace the category "Templates" with the appropriate category, such as "Lights", "Transforms", "Materials", etc.
    // Your Reaction Component won't appear in the dropdown menu while using the "Templates" category.
    [ASP_ReactorCategory("Templates")] // Example category
    public class ASPR_BeatTemplate : MonoBehaviour, ASP_IAudioReaction
    {
        // Replace with a descriptive name and information about what this Reaction does
        public new string name = "Beat-Based Reaction Template";
        public string info = "This Component is a template for creating beat-based reactions.";

        // For this template, we will use the transform component to demonstrate how to create a reaction component.
        // This is because there is custom drawing logic for the transform component in the ASP editor that you can leverage for your own custom components.
        // You can replace the transform component with any other component you want to react to audio input.

        // Use Target Transform makes the Reaction affect just the transform provided in the AudioReactor
        public bool useTargetTransform = true;

        // List of Transforms this component affects.
        [HideInInspector]
        public List<Transform> affectedTransforms;

        // Sensitivity slider for beat detection.
        [ASP_FloatSlider(0f, 2f)]
        public float sensitivity = .25f; // Adjust this value to control how sensitive the beat detection is

        // Frequency range and threshold for beat detection
        [ASP_SpectrumDataSlider(0f, 0.1f, "spectrumDataForSlider")]
        [SerializeField]
        private Vector4 frequencyRangeAndThreshold =
            new Vector4(
                0f,    // Min clamped frequency 
                5000f, // Max clamped frequency
                0f,    // Instant energy (for visualization)
                0.04f  // Beat Threshold (dynamic, based on average energy)
                );

        private Dictionary<Transform, Vector3> initialParameterValues = new Dictionary<Transform, Vector3>(); // Example dictionary for storing initial values
        private float[] spectrumDataForSlider = new float[512]; // Spectrum data for visual sliders
        private float lastBeatTime = 0f; // Time of the last beat
        public float beatCooldown = 0.25f; // Cooldown duration in seconds
        private bool isInitialized = false;


        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }

        // Initializes the component with the initial state of the affected objects.
        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            affectedTransforms ??= new List<Transform>(); // Initialize if null
            if (useTargetTransform)
            {
                affectedTransforms.Clear(); // Clear the list if we are using target transform
            }
            initialParameterValues.Clear(); // Clear the dictionary
            foreach (var transform in affectedTransforms)
            {
                // Perform initialization specific to the type of component, such as storing initial values
                if (transform != null && !initialParameterValues.ContainsKey(transform))
                {
                    // Example initialization logic, replace with actual implementation
                    initialParameterValues[transform] = transform.localPosition; // Store an example initial value
                }
            }

            isInitialized = true;
        }

        // Reacts to audio input and modifies the target components based on beats.
        // The rmsValue is a measure of the "loudness" of the audio input at the current frame.
        // The spectrumData is an array of values representing the audio spectrum.
        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!isInitialized || !IsActive) return;

            // If useTargetTransform is enabled, add the target transform to the list if it's not already there
            if (useTargetTransform && targetTransform != null && !affectedTransforms.Contains(targetTransform))
            {
                affectedTransforms.Add(targetTransform);
                if (!initialParameterValues.ContainsKey(targetTransform))
                {
                    initialParameterValues[targetTransform] = targetTransform.localPosition; // Store an example initial value
                }
            }

            UpdateSpectrumData(spectrumData);

            // Calculate frequency per bin
            float sampleRate = AudioSettings.outputSampleRate;
            float freqPerBin = sampleRate / 2f / spectrumData.Length;

            // Map frequency values to indices
            int minFrequencyIndex = Mathf.Clamp(Mathf.FloorToInt(frequencyRangeAndThreshold.x / freqPerBin), 0, spectrumData.Length - 1);
            int maxFrequencyIndex = Mathf.Clamp(Mathf.CeilToInt(frequencyRangeAndThreshold.y / freqPerBin), minFrequencyIndex, spectrumData.Length - 1);

            // Analyze spectrum data within the frequency window
            float averageSpectrumInWindow = 0f;
            int count = 0;

            for (int i = minFrequencyIndex; i <= maxFrequencyIndex; i++)
            {
                averageSpectrumInWindow += spectrumData[i];
                count++;
            }
            averageSpectrumInWindow *= sensitivity;
            frequencyRangeAndThreshold.z = averageSpectrumInWindow;

            //ensures the cooldown has passed
            if (Time.time - lastBeatTime < beatCooldown)
            {
                return;
            }

            // Check if a beat is detected
            if (averageSpectrumInWindow > frequencyRangeAndThreshold.w)
            {
                // Example reaction logic: Apply some change to the affected transforms
                foreach (var transform in affectedTransforms)
                {
                    if (transform != null && initialParameterValues.ContainsKey(transform))
                    {
                        // Apply your reaction logic here
                        // For example, move the transform upward on a beat
                        transform.localPosition = initialParameterValues[transform] + new Vector3(0f, 1f, 0f);
                    }
                }

                lastBeatTime = Time.time; // Update the last beat time
            }
            else
            {
                // Reset to initial state when no beat is detected
                foreach (var transform in affectedTransforms)
                {
                    if (transform != null && initialParameterValues.ContainsKey(transform))
                    {
                        transform.localPosition = initialParameterValues[transform];
                    }
                }
            }
        }

        private void UpdateSpectrumData(float[] spectrumData)
        {
            for (int i = 0; i < spectrumDataForSlider.Length; i++)
            {
                spectrumDataForSlider[i] = i < spectrumData.Length ? spectrumData[i] : 0f;
            }
        }

        // Resets the target components to their original states.
        public void ResetToOriginalState(Transform targetTransform)
        {
            if (!isInitialized) return;

            foreach (var transform in affectedTransforms)
            {
                if (transform != null && initialParameterValues.ContainsKey(transform))
                {
                    // Reset the components to their initial states
                    transform.localPosition = initialParameterValues[transform];
                }
            }
        }
    }
}
