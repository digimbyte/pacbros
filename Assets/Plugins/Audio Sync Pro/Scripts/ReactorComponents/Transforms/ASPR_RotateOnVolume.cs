using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Transforms", "Volume")]
    public class ASPR_RotateOnVolume : MonoBehaviour, ASP_IAudioReaction
    {
        public new string name = "Rotate On Volume!";
        public string info = "This Component adds a rotation offset based on Volume.";
        public bool RandomRotation = true; // Enable random rotation direction

        [SerializeField] private Vector3 rotationAxis = Vector3.up; // Default rotation axis set to Y-axis (0, 1, 0)
        [SerializeField] private float maxRotationAngle = 45.0f; // Maximum rotation angle
        [ASP_FloatSlider(0.0f, 1f)]
        public float smoothness = 0.25f; // Controls the smoothness of the rotation transition
        [ASP_FloatSlider(0.0f, 15f)]
        public float sensitivity = 1.0f; // Controls the sensitivity of the rotation to the volume
        [ASP_MinMaxSlider(0f, 1f)]
        public Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f); // X = current volume, Y = min volume, Z = max volume

        public bool useTargetTransform = true;
        [HideInInspector] public List<Transform> affectedTransforms;

        private Dictionary<Transform, Quaternion> initialRotations = new Dictionary<Transform, Quaternion>();
        private bool isInitialized = false;
        private float currentSineValue = 0.0f; // Controls the sine wave for random rotation

        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }
        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            if (affectedTransforms == null) affectedTransforms = new List<Transform>();
            affectedTransforms.Clear();
            initialRotations.Clear();
            foreach (var transform in affectedTransforms)
            {
                if (transform != null && !initialRotations.ContainsKey(transform))
                {
                    initialRotations[transform] = transform.localRotation; // Store the initial local rotation
                }
            }

            isInitialized = true;
        }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!IsActive || !isInitialized) return;

            // Add the target transform if using target transform mode and it's not already in the list
            if (useTargetTransform && !affectedTransforms.Contains(targetTransform))
            {
                affectedTransforms.Add(targetTransform);
                if (!initialRotations.ContainsKey(targetTransform))
                {
                    initialRotations[targetTransform] = targetTransform.localRotation;
                }
            }

            float volume = rmsValue * sensitivity;
            volumeRange.x = Mathf.Lerp(volumeRange.x, volume, Time.deltaTime * (1.0f / Mathf.Max(smoothness, 0.01f))); // Smooth volume change

            // Determine rotation axis and angle
            float relativeRotationAngle = Mathf.InverseLerp(volumeRange.y, volumeRange.z, volumeRange.x) * maxRotationAngle;
            Vector3 effectiveRotationAxis = GetEffectiveRotationAxis();

            foreach (var transform in affectedTransforms)
            {
                if (transform == null || !initialRotations.ContainsKey(transform)) continue;

                Quaternion rotationOffset = Quaternion.Euler(effectiveRotationAxis * relativeRotationAngle);
                Quaternion targetRotation = initialRotations[transform] * rotationOffset; // Offset from initial rotation

                // Smoothly interpolate towards the target rotation or reset to initial rotation
                transform.localRotation = Quaternion.Lerp(transform.localRotation,
                                                          volumeRange.x > volumeRange.y ? targetRotation : initialRotations[transform],
                                                          Time.deltaTime);
            }
        }

        private Vector3 GetEffectiveRotationAxis()
        {
            if (!RandomRotation) return rotationAxis; // Use the defined rotation axis if RandomRotation is disabled

            // Generate a random rotation direction modified by sine waves for randomness
            currentSineValue += Time.deltaTime;
            return new Vector3(
                Mathf.Sin(currentSineValue * 1.1f),
                Mathf.Sin(currentSineValue * 1.3f),
                Mathf.Sin(currentSineValue * 1.5f)
            ).normalized; // Ensure consistent rotation speed
        }

        public void ResetToOriginalState(Transform targetTransform)
        {
            if (!isInitialized) return;

            foreach (var transform in affectedTransforms)
            {
                if (transform != null && initialRotations.ContainsKey(transform))
                {
                    transform.localRotation = initialRotations[transform]; // Reset to initial rotation
                }
            }
        }
    }
}
