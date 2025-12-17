using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    // Replace the category "Templates" with the appropriate category, such as "Lights", "Transforms", "Materials", etc.
    // Your Reaction Component won't appear in the dropdown menu while using the "Templates" category.
    [ASP_ReactorCategory("Templates")] // Example category
    public class ASPR_VolumeTemplate : MonoBehaviour, ASP_IAudioReaction
    {
        // Replace with a descriptive name and information about what this Reaction does
        public new string name = "Volume-Based Reaction Template";
        public string info = "This Component is a template for creating volume-based reactions.";


        //For this template, we will use the transform component to demonstrate how to create a reaction component.
        //This is because there is custom drawing logic for the transform component in the ASP editor, that you can leverage for your own custom components.
        //You can replace the transform component with any other component you want to react to audio input.
        //Use Target Transforms makes the Reaction affect just the transform provided in the AudioReactor
        public bool useTargetTransform = true;

        // List of Transforms this component might affects.
        public List<Transform> affectedTransforms;

        // Example parameter: Adjust this to suit the specific component, such as Light Intensity, Scale, Material Property, etc.
        [SerializeField] private float parameterMultiplier = 1f;

        // Smoothness slider (0 to 1 for display, but clamped to 0 to 20 internally)
        [ASP_FloatSlider(0.0f, 1f)]
        [SerializeField] private float smoothness = 0.25f;

        // Sensitivity slider (adjust based on reaction type)
        [ASP_FloatSlider(0.0f, 15f)]
        [SerializeField] public float sensitivity = 1.0f;

        // Volume range with min and max limits. The x Value is reserved to display the current volume.
        [ASP_MinMaxSlider(0f, 1f)]
        [SerializeField] private Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f); // X = current volume, Y = min volume, Z = max volume

        private bool isInitialized = false;
        private Dictionary<Transform, float> initialParameterValues = new Dictionary<Transform, float>(); // Example dictionary for storing initial values

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
                    initialParameterValues[transform] = transform.localPosition.magnitude; // Store an example initial value
                }
            }

            isInitialized = true;
        }

        // Reacts to audio input and modifies the target components based on volume.
        // The RMS is a measure of the "loudness" of the audio input at the current frame.
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
                    initialParameterValues[targetTransform] = targetTransform.localPosition.magnitude; // Store an example initial value
                }
            }

            float volume = rmsValue * sensitivity;

            // Smooth the volume change using the smoothness factor
            volumeRange.x = Mathf.Lerp(volumeRange.x, volume, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

            // Check if the current volume is within the specified range
            if (volumeRange.x < volumeRange.y)
            {
                // Volume is below the minimum threshold; no parameter change
                return;
            }

            foreach (var transform in affectedTransforms)
            {
                // Perform reaction specific to the type of component, such as adjusting intensity, scale, material property, etc.
                if (transform != null && initialParameterValues.ContainsKey(transform))
                {
                    // Example reaction logic, replace with actual implementation
                    float initialValue = initialParameterValues[transform];
                    float targetValue = Mathf.Lerp(initialValue, initialValue * parameterMultiplier, Mathf.InverseLerp(volumeRange.y, volumeRange.z, volumeRange.x));

                    // Apply the calculated target value to the component (this is a placeholder example)
                    transform.localPosition = transform.localPosition.normalized * targetValue;
                }
            }
        }

        // Resets the target components to their original states.
        public void ResetToOriginalState(Transform targetTransform)
        {
            if (!isInitialized) return;

            foreach (var transform in affectedTransforms)
            {
                // Reset the components to their initial states
                if (transform != null && initialParameterValues.ContainsKey(transform))
                {
                    // Example reset logic, replace with actual implementation
                    transform.localPosition = transform.localPosition.normalized * initialParameterValues[transform]; // Reset to initial value
                }
            }
        }
    }
}
