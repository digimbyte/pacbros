using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Materials")]
    public class ASPR_SwitchTextureOnVolume : MonoBehaviour, ASP_IAudioReaction
    {
        [HideInInspector]
        public new string name = "Switch Texture on Volume!";
        public string info = "Switch texture on the material according to the volume.";
        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }
        public List<Renderer> targetRenderers; // A list of target renderers
        public ASP_TextureList textureList; // A list of textures to switch between
        private int currentTextureIndex = 0; // Index of the currently applied texture

        [ASP_FloatSlider(0.0f, 1f)]
        public float smoothness = .25f; // Slider to control smoothing

        [ASP_FloatSlider(0.0f, 20f)]
        public float sensitivity = 1.0f;

        [ASP_MinMaxSlider(0f, 1f)]
        public Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f); // X = current volume, Y = min volume, Z = max volume

        public int materialIndex = 0; // Field to set the material index that will be affected

        // Store MaterialPropertyBlocks and original textures for each renderer
        private List<MaterialPropertyBlock> propertyBlocks = new List<MaterialPropertyBlock>();
        private List<Texture> originalTextures = new List<Texture>();

        // Cache the texture property name based on the render pipeline
        private string texturePropertyName;

        private string GetTexturePropertyName()
        {
            // Check if the built-in render pipeline is being used
            if (GraphicsSettings.defaultRenderPipeline == null)
            {
                return "_MainTex"; // Built-in or Standard
            }
            else
            {
                // Get the type name of the current Render Pipeline Asset
                string pipelineType = GraphicsSettings.defaultRenderPipeline.GetType().ToString();

                if (pipelineType.Contains("UniversalRenderPipelineAsset"))
                {
                    return "_BaseMap"; // URP
                }
                else if (pipelineType.Contains("HDRenderPipelineAsset"))
                {
                    return "_BaseColorMap"; // HDRP
                }
                else
                {
                    // Unknown or custom render pipeline
                    return "_MainTex"; // Fallback
                }
            }
        }

        public void Initialize(Vector3 _initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            propertyBlocks.Clear();
            originalTextures.Clear();

            // Cache the texture property name
            texturePropertyName = GetTexturePropertyName();

            // Initialize MaterialPropertyBlocks for each renderer
            if (targetRenderers != null && targetRenderers.Count > 0)
            {
                foreach (var renderer in targetRenderers)
                {
                    if (renderer != null)
                    {
                        // Create a new MaterialPropertyBlock
                        MaterialPropertyBlock block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block, materialIndex);

                        // Get the original texture and store it
                        Material[] materials = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;
                        if (materialIndex < materials.Length)
                        {
                            Texture originalTexture = materials[materialIndex].GetTexture(texturePropertyName);
                            originalTextures.Add(originalTexture);

                            // Set the original texture in the property block
                            block.SetTexture(texturePropertyName, originalTexture);
                            renderer.SetPropertyBlock(block, materialIndex);

                            // Add the property block to the list
                            propertyBlocks.Add(block);
                        }
                    }
                }
            }
        }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!IsActive || propertyBlocks.Count == 0 || textureList == null || textureList.textures.Count == 0)
                return;

            float volume = rmsValue * sensitivity; // Apply sensitivity

            // Smooth the volume change and assign to the X value of volumeRange
            volumeRange.x = Mathf.Lerp(volumeRange.x, volume, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

            // Check if the current volume is within the specified range
            if (volumeRange.x < volumeRange.y)
            {
                // Volume is below the minimum threshold; no texture change
                return;
            }

            // Clamp the volume to the range [Y, Z] for texture switching
            float clampedVolume = Mathf.Clamp(volumeRange.x, volumeRange.y, volumeRange.z);

            // Determine the texture index based on the clamped volume
            int textureCount = textureList.textures.Count;
            int newTextureIndex = Mathf.Clamp(Mathf.FloorToInt((clampedVolume - volumeRange.y) / (volumeRange.z - volumeRange.y) * textureCount), 0, textureCount - 1);

            // Switch to the new texture if it has changed
            if (newTextureIndex != currentTextureIndex)
            {
                for (int i = 0; i < targetRenderers.Count; i++)
                {
                    var renderer = targetRenderers[i];
                    var block = propertyBlocks[i];

                    // Set the new texture in the property block
                    block.SetTexture(texturePropertyName, textureList.textures[newTextureIndex]);
                    renderer.SetPropertyBlock(block, materialIndex);
                }
                currentTextureIndex = newTextureIndex;
            }
        }

        public void ResetToOriginalState(Transform targetTransform)
        {
            if (targetRenderers == null || targetRenderers.Count == 0)
                return;

            for (int i = 0; i < targetRenderers.Count; i++)
            {
                var renderer = targetRenderers[i];
                var block = propertyBlocks[i];

                if (originalTextures.Count > i)
                {
                    // Reset to the original texture
                    block.SetTexture(texturePropertyName, originalTextures[i]);
                    renderer.SetPropertyBlock(block, materialIndex);
                }
            }
            currentTextureIndex = 0;
        }
    }
}
