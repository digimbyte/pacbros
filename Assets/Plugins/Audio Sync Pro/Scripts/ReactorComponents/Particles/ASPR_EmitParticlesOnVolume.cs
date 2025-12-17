using System.Collections.Generic;
using UnityEngine;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Particles")]
    public class ASPR_EmitParticlesOnVolume : MonoBehaviour, ASP_IAudioReaction
    {
        public new string name = "Emit Particles On Volume!";
        public string info = "This Component controls the particle emission rate based on Audio Volume.";

        public List<ParticleSystem> targetParticleSystems;

        [SerializeField] private float volumeMultiplier = 5.0f;

        [ASP_FloatSlider(0.0f, 1f)]
        [SerializeField] private float smoothness = .25f;

        [ASP_FloatSlider(0.0f, 15f)]
        [SerializeField] public float sensitivity = 1.0f;

        [ASP_MinMaxSlider(0f, 1f)]
        [SerializeField] private Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f);

        private bool isInitialized = false;
        private Dictionary<ParticleSystem, ParticleSystem.EmissionModule> emissionModules = new Dictionary<ParticleSystem, ParticleSystem.EmissionModule>();
        private Dictionary<ParticleSystem, float> initialEmissionRates = new Dictionary<ParticleSystem, float>();

        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }
        public void Initialize(Vector3 _initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            if (!IsActive) return;
            emissionModules.Clear();
            initialEmissionRates.Clear();
            if (targetParticleSystems != null && targetParticleSystems.Count > 0)
            {
                foreach (var ps in targetParticleSystems)
                {
                    if (ps != null)
                    {
                        var emissionModule = ps.emission;
                        emissionModules[ps] = emissionModule;
                        initialEmissionRates[ps] = emissionModule.rateOverTime.constant; // Store the initial emission rate
                    }
                }
            }
            isInitialized = true;
        }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!isInitialized || !IsActive || targetParticleSystems == null || targetParticleSystems.Count == 0) return;

            float volume = rmsValue * sensitivity;

            // Smooth the volume change using the smoothness factor
            volumeRange.x = Mathf.Lerp(volumeRange.x, volume, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

            // Check if the current volume is within the specified range
            if (volumeRange.x < volumeRange.y)
            {
                // Volume is below the minimum threshold; no emission rate change
                return;
            }

            // Calculate the emission rate relative to the position of volumeRange.x within the range [volumeRange.y, volumeRange.z]
            float relativeMultiplier = Mathf.InverseLerp(volumeRange.y, volumeRange.z, volumeRange.x) * volumeMultiplier;
            float emissionRate = relativeMultiplier;

            // Smoothly interpolate the emission rate for each ParticleSystem
            foreach (var ps in targetParticleSystems)
            {
                if (ps != null && emissionModules.ContainsKey(ps))
                {
                    var emissionModule = emissionModules[ps];
                    emissionModule.rateOverTime = emissionRate;
                }
            }
        }

        public void ResetToOriginalState(Transform targetTransform)
        {
            if (!isInitialized || targetParticleSystems == null || targetParticleSystems.Count == 0) return;

            foreach (var ps in targetParticleSystems)
            {
                if (ps != null && emissionModules.ContainsKey(ps))
                {
                    var emissionModule = emissionModules[ps];
                    if (initialEmissionRates.ContainsKey(ps))
                    {
                        // Reset the emission rate to the initial value
                        emissionModule.rateOverTime = initialEmissionRates[ps];
                    }
                }
            }

            volumeRange.x = 0f;
        }
    }
}
