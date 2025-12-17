using UnityEngine;
using System.Collections.Generic;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Lights")]
    public class ASPR_LightIntensityWithVolume : MonoBehaviour, ASP_IAudioReaction
    {
        public new string name = "Light Intensity On Volume!";
        public string info = "This Component adds a multiplier to the Light Intensity based on Audio Volume.";
        public List<Light> targetLights;

        [SerializeField] private float intensityMultiplier = 2f;

        // Slider for smoothness, displayed as 0 to 1, internally clamped to 0 to 20
        [ASP_FloatSlider(0.0f, 1f)]
        [SerializeField] private float smoothness = .25f; // Displayed as 0 to 1

        [ASP_FloatSlider(0.0f, 15f)]
        [SerializeField] public float sensitivity = 1.0f;
        [ASP_MinMaxSlider(0f, 1f)]
        [SerializeField] private Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f); // Volume range with min and max limits

        private bool isInitialized = false;
        private Dictionary<Light, float> initialIntensities = new Dictionary<Light, float>();

        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }
        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            initialIntensities.Clear();
            if (targetLights != null && targetLights.Count > 0)
            {
                foreach (var light in targetLights)
                {
                    if (light != null)
                    {
                    
                        initialIntensities[light] = light.intensity;
                    }
                }
            }
            isInitialized = true;

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
                // Volume is below the minimum threshold; reset to initial intensity
                foreach (var light in targetLights)
                {
                           light.intensity = initialIntensities[light];
                }
                return;
            }
            if (targetLights == null || targetLights.Count == 0) return;
            foreach (var light in targetLights)
            {
                if (light != null)
                {
                    // Map the intensity so that volumeRange.y will be equal to the initialIntensity of the light.
                    // volumeRange.z will then equal the initialIntensity * intensityMultiplier. 
                    // Anything in between will be mapped accordingly.

                    float initialIntensity = initialIntensities[light];
                    if (initialIntensities[light] == 0)
                    {
                        initialIntensity = .01f;
                    }
                    float mappedIntensity = Mathf.Lerp(initialIntensity, initialIntensity * intensityMultiplier, Mathf.InverseLerp(volumeRange.y, volumeRange.z, volumeRange.x));

                        light.intensity = mappedIntensity;                         
               


                }
            }
        }

        public void ResetToOriginalState(Transform targetTransform)
        {
            if (!isInitialized || targetLights == null || targetLights.Count == 0) return;

            foreach (var light in targetLights)
            {
                if (light != null)
                {
                    if (initialIntensities.ContainsKey(light))
                    {
                        light.intensity = initialIntensities[light];
                    }
                }
            }
            volumeRange.x = 0f;
        }
    }
}
