using UnityEngine;
using System.Collections.Generic;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Physics")]
    public class ASPR_AddForceOnVolume : MonoBehaviour, ASP_IAudioReaction
    {
        [HideInInspector]
        public new string name = "Add Force On Volume!";
        public string info = "Add a Force to Rigidbodies based on the audio volume (Play Mode Only).";
        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }
        public List<Rigidbody> targetRigidbodies;

        [SerializeField] private Vector3 impulseIntensity = new Vector3(2f, 2f, 2f);

        [ASP_FloatSlider(0.0f, 1f)]
        [SerializeField] private float smoothness = .25f; // Displayed as 0 to 1

        [ASP_FloatSlider(0.0f, 15f)]
        [SerializeField] public float sensitivity = 1.0f;

        [ASP_MinMaxSlider(0f, 1f)]
        [SerializeField] private Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f); // Volume range with min and max limits

        private bool isInitialized = false;

        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            // Initialization logic if needed
            isInitialized = true;
        }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!IsActive) return;
            if (!isInitialized) return;

            float volume = rmsValue * sensitivity;

            // Smooth the volume change using the smoothness factor
            volumeRange.x = Mathf.Lerp(volumeRange.x, volume, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

            // If the volume is within the specified range, apply the impulse
            if (volumeRange.x >= volumeRange.y)
            {
                // Calculate the impulse based on the audio volume
                Vector3 force = Vector3.Scale(impulseIntensity, Mathf.InverseLerp(volumeRange.y, volumeRange.z, volumeRange.x) * Vector3.one);

                // Apply the impulse to each Rigidbody in the list
                foreach (var rb in targetRigidbodies)
                {
                    if (rb != null)
                    {
                        rb.AddForce(force, ForceMode.Force);
                    }
                }
            }
        }

        public void ResetToOriginalState(Transform targetTransform)
        {
            // This method can be used to reset the Rigidbodies to a specific state if needed.
            // Currently, it does nothing since we aren't changing any persistent state.
        }
    }
}
