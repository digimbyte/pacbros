using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Materials")]
    public class ASPR_ModifyMaterialParameterDirectly : MonoBehaviour, ASP_IAudioReaction
    {
        [HideInInspector]
        public new string name = "Modify Material Parameter Directly";
        [HideInInspector]
        public string info = "Modify a material parameter directly based on the volume.";

        [SerializeField]
        private Material targetMaterial; // Directly choose a material to modify

        [SerializeField] private string parameterName = "_Exposure"; // Name of the material parameter to modify

        [SerializeField] private float parameterMultiplier = 2f;

        [ASP_FloatSlider(0.0f, 1f)]
        [SerializeField] private float smoothness = .25f; // Displayed as 0 to 1

        [ASP_FloatSlider(0.0f, 15f)]
        [SerializeField] public float sensitivity = 1.0f;

        [ASP_MinMaxSlider(0f, 1f)]
        [SerializeField] private Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f); // Volume range with min and max limits

        private float initialParameterValue;
        private float targetParameterValue;
        private float smoothedParameterValue;

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
            if (targetMaterial == null) return;

            if (targetMaterial.HasProperty(parameterName))
            {
                initialParameterValue = targetMaterial.GetFloat(parameterName);
                isInitialized = true;
            }
        }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!isInitialized || !IsActive) return;

            float volume = rmsValue * sensitivity;

            // Smooth the volume change using the smoothness factor
            volumeRange.x = Mathf.Lerp(volumeRange.x, volume, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

            // Check if the current volume is within the specified range
            if (volumeRange.x < volumeRange.y)
            {
                // Volume is below the minimum threshold; no material parameter change
                return;
            }

            if (targetMaterial != null && targetMaterial.HasProperty(parameterName))
            {
                targetParameterValue = Mathf.Lerp(initialParameterValue, initialParameterValue * parameterMultiplier, Mathf.InverseLerp(volumeRange.y, volumeRange.z, volumeRange.x));

                smoothedParameterValue = Mathf.Lerp(smoothedParameterValue, targetParameterValue, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

                targetMaterial.SetFloat(parameterName, smoothedParameterValue);
            }
        }

        public void ResetToOriginalState(Transform targetTransform)
        {
            if (!isInitialized) return;

            if (targetMaterial != null && targetMaterial.HasProperty(parameterName))
            {
                targetMaterial.SetFloat(parameterName, initialParameterValue);
            }
        }
    }
}
