using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Transforms", "Volume")]
    public class ASPR_ScaleWithVolume : MonoBehaviour, ASP_IAudioReaction
    {
        public new string name = "Scale With Volume!";
        public string info = "Scale Transforms by a multiplier by Volume!";

        public bool useTargetTransform = true;
        [HideInInspector] // This hides the list unless we want to show it based on UseTargetTransform
        public List<Transform> affectedTransforms;

        [ASP_ScaleVector3]
        public Vector3 scaleMultiplier = new Vector3(1, 1, 1);

        [ASP_FloatSlider(0.0f, 1f)]
        public float smoothness = .25f; // Slider to control smoothing

        [ASP_FloatSlider(0.0f, 15f)]
        public float sensitivity = 1.0f;

        [ASP_MinMaxSlider(0f, 1f)]
        public Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f); // X = current volume, Y = min volume, Z = max volume

        private Dictionary<Transform, Vector3> initialScales = new Dictionary<Transform, Vector3>();
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

            if (!isInitialized) return;

            float volume = rmsValue * sensitivity; // Apply sensitivity
            volumeRange.x = Mathf.Lerp(volumeRange.x, volume, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

            // Calculate relative multiplier using component-wise multiplication with scaleMultiplier (Vector3)
            Vector3 relativeMultiplier = Vector3.Scale(scaleMultiplier, Vector3.one * Mathf.InverseLerp(volumeRange.y, volumeRange.z, volumeRange.x));

            foreach (var transform in affectedTransforms)
            {
                if (transform == null || !initialScales.ContainsKey(transform)) continue;

                Vector3 initialScale = initialScales[transform];
                Vector3 VolumeMultiplierScale = Vector3.Scale(initialScale, (Vector3.one + relativeMultiplier));

                if (volumeRange.x > volumeRange.y)
                {
                    transform.localScale = VolumeMultiplierScale;
                }
                else
                {
                    transform.localScale = Vector3.Lerp(transform.localScale, initialScale, smoothness * Time.deltaTime);
                }
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
