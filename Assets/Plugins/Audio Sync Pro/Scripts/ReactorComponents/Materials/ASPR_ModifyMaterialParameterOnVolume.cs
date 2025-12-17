using UnityEngine;
using System.Collections.Generic;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Materials")]
    public class ASPR_ModifyMaterialParameterOnVolume : MonoBehaviour, ASP_IAudioReaction
    {
        [HideInInspector]
        public new string name = "Modify Material Parameter On Volume";
        [HideInInspector]
        public string info = "Modify a material parameter based on the volume.";

        [SerializeField]
        private List<Renderer> targetRenderers;

        [SerializeField]
        private int materialIndex = 0; // Index of the material to modify in each renderer's material array

        [SerializeField] private string parameterName = "_Exposure"; // Name of the material parameter to modify

        [SerializeField] private float parameterMultiplier = 2f;


        [ASP_FloatSlider(0.0f, 1f)]
        [SerializeField] private float smoothness = .25f; 

        [ASP_FloatSlider(0.0f, 15f)]
        [SerializeField] public float sensitivity = 1.0f;
        [ASP_MinMaxSlider(0f, 1f)]
        [SerializeField] private Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f); // Volume range with min and max limits

        private Dictionary<Renderer, MaterialPropertyBlock> propertyBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();
        private Dictionary<Renderer, float> initialParameterValues = new Dictionary<Renderer, float>();
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
            if (targetRenderers == null || targetRenderers.Count == 0) return;
            initialParameterValues.Clear();
            propertyBlocks.Clear();

            foreach (var renderer in targetRenderers)
            {
                if (renderer == null || materialIndex >= renderer.sharedMaterials.Length) continue;

                Material originalMaterial = renderer.sharedMaterials[materialIndex];

                // Use MaterialPropertyBlock to avoid modifying shared materials
                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock, materialIndex);
                propertyBlocks[renderer] = propertyBlock;

                if (originalMaterial.HasProperty(parameterName))
                {
                    float initialValue = originalMaterial.GetFloat(parameterName);
                    initialParameterValues[renderer] = initialValue;
                }
            }

            isInitialized = true;
        }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!isInitialized || !IsActive) return;

            float volume = rmsValue * sensitivity;
            volumeRange.x = Mathf.Lerp(volumeRange.x, volume, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

            if (volumeRange.x < volumeRange.y) return;

            if (targetRenderers == null || targetRenderers.Count == 0) return;

            foreach (var renderer in targetRenderers)
            {
                if (renderer == null || !initialParameterValues.ContainsKey(renderer)) continue;

                MaterialPropertyBlock propertyBlock = propertyBlocks[renderer];
                if (propertyBlock == null) continue;

                float initialValue = initialParameterValues[renderer];
                targetParameterValue = Mathf.Lerp(initialValue, initialValue * parameterMultiplier, Mathf.InverseLerp(volumeRange.y, volumeRange.z, volumeRange.x));
                smoothedParameterValue = Mathf.Lerp(smoothedParameterValue, targetParameterValue, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

                // Set the parameter value in the property block
                propertyBlock.SetFloat(parameterName, smoothedParameterValue);

                // Apply the property block to the renderer
                renderer.SetPropertyBlock(propertyBlock, materialIndex);
            }
        }

        public void ResetToOriginalState(Transform targetTransform)
        {
            if (!isInitialized) return;

            foreach (var renderer in targetRenderers)
            {
                if (renderer == null || !propertyBlocks.ContainsKey(renderer)) continue;

                MaterialPropertyBlock propertyBlock = propertyBlocks[renderer];

                // Clear the MaterialPropertyBlock to restore the original shared material state
                propertyBlock.Clear();
                renderer.SetPropertyBlock(propertyBlock, materialIndex);
            }
        }
    }
}
