using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Transforms", "Volume")]
    public class ASPR_MoveOnVolume : MonoBehaviour, ASP_IAudioReaction
    {
        [HideInInspector]
        public new string name = "Move on Volume!";
        public string info = "Add an offset to the world position according to the volume";
        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }
        [SerializeField] private float volumeMultiplier = 5.0f;
        [ASP_FloatSlider(0.0f, 1f)]
        [SerializeField] private float smoothness = .25f;
        [ASP_FloatSlider(0.0f, 15f)]
        [SerializeField] public float sensitivity = 1.0f;
        [ASP_MinMaxSlider(0f, 1f)]
        [SerializeField] private Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f);
        [SerializeField] private Vector3 offsetDirection = Vector3.up;

        public bool useTargetTransform = true;  // Renamed to match other scripts
        [HideInInspector]
        public List<Transform> affectedTransforms;

        private Dictionary<Transform, Vector3> initialPositions = new Dictionary<Transform, Vector3>();
        private bool isInitialized = false;

        public void Initialize(Vector3 _initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            affectedTransforms ??= new List<Transform>(); // Initialize if null
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

            float volume = rmsValue * sensitivity;
            volumeRange.x = Mathf.Lerp(volumeRange.x, volume, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

            // Calculate the offset multiplier relative to the position of volumeRange.x within the range [volumeRange.y, volumeRange.z]
            float relativeMultiplier = Mathf.InverseLerp(volumeRange.y, volumeRange.z, volumeRange.x) * volumeMultiplier;
            Vector3 offset = offsetDirection.normalized * relativeMultiplier;

            foreach (var transform in affectedTransforms)
            {
                if (transform == null || !initialPositions.ContainsKey(transform)) continue;

                // Adjust transform's position based on volume range comparison
                Vector3 targetPosition = volumeRange.x > volumeRange.y
                    ? initialPositions[transform] + offset
                    : initialPositions[transform];

                float lerpFactor = volumeRange.x > volumeRange.y ? 10f * Time.deltaTime : smoothness * Time.deltaTime;
                transform.localPosition = Vector3.Lerp(transform.localPosition, targetPosition, lerpFactor);
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
