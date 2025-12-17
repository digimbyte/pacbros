using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Transforms", "Volume")]
    public class ASPR_RotateSineOnVolume : MonoBehaviour, ASP_IAudioReaction
    {

        public new string name = "Rotate Sine on Volume!";
        public string info = "This Component rotates your Transform along a Sine according to Volume.";
        public enum RotationAxis { X, Y, Z } // Enum to choose the rotation axis

        [SerializeField] private RotationAxis rotationAxis = RotationAxis.Z; // Default to Z-axis
        [SerializeField] private float maxRotationAngle = 90.0f; // Maximum rotation angle (degrees)
        [SerializeField] private float sinePhaseOffset = 0.0f;   // Phase offset for the sine wave
        [SerializeField] private float sineSpeed = 1.0f; // Speed of the sine wave

        [ASP_FloatSlider(0.0f, 1f)]
        public float smoothness = .25f; // Slider to control smoothing

        [ASP_FloatSlider(0.0f, 15f)]
        public float sensitivity = 1.0f;

        [ASP_MinMaxSlider(0f, 1f)]
        public Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f); // X = current volume, Y = min volume, Z = max volume

        public bool useTargetTransform = true;
        [HideInInspector]
        public List<Transform> affectedTransforms;

        private Dictionary<Transform, Quaternion> initialRotations = new Dictionary<Transform, Quaternion>();
        private float currentSineValue = 0.0f;
        private float currentRotationAngle = 0.0f;
        private bool isInitialized = false;

        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }
        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            affectedTransforms ??= new List<Transform>(); // Initialize if null
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
            currentSineValue = sinePhaseOffset; // Start the sine wave at the phase offset
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

            if (!isInitialized || !IsActive) return;

            float volume = rmsValue * sensitivity;
            volumeRange.x = Mathf.Lerp(volumeRange.x, volume, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

            // Calculate the sine speed relative to the position of volumeRange.x within the range [volumeRange.y, volumeRange.z]
            float relativeSineSpeed = Mathf.InverseLerp(volumeRange.y, volumeRange.z, volumeRange.x) * sineSpeed;
            currentSineValue += relativeSineSpeed * Time.deltaTime;

            // Calculate the new rotation angle using a sine wave with the phase offset
            float targetRotationAngle = Mathf.Sin(currentSineValue) * maxRotationAngle;

            // Smoothly interpolate to the target rotation angle
            currentRotationAngle = targetRotationAngle;

            if (volumeRange.x > volumeRange.y)
            {
                foreach (var transform in affectedTransforms)
                {
                    if (transform != null && initialRotations.ContainsKey(transform))
                    {
                        ApplyRotation(transform, currentRotationAngle);
                    }
                }
            }
            else
            {
                foreach (var transform in affectedTransforms)
                {
                    if (transform != null && initialRotations.ContainsKey(transform))
                    {
                        transform.localRotation = Quaternion.Lerp(transform.localRotation, initialRotations[transform], smoothness * Time.deltaTime);
                    }
                }
            }
        }

        private void ApplyRotation(Transform targetTransform, float angle)
        {
            Quaternion targetRotation = Quaternion.identity;

            switch (rotationAxis)
            {
                case RotationAxis.X:
                    targetRotation = initialRotations[targetTransform] * Quaternion.Euler(angle, 0.0f, 0.0f);
                    break;
                case RotationAxis.Y:
                    targetRotation = initialRotations[targetTransform] * Quaternion.Euler(0.0f, angle, 0.0f);
                    break;
                case RotationAxis.Z:
                    targetRotation = initialRotations[targetTransform] * Quaternion.Euler(0.0f, 0.0f, angle);
                    break;
            }

            // Smoothly interpolate to the target rotation using Quaternion.Lerp
            targetTransform.localRotation = Quaternion.Lerp(targetTransform.localRotation, targetRotation, 1f * Time.deltaTime);
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
