using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Materials")]
    public class ASPR_SpriteSheetOnVolume : MonoBehaviour, ASP_IAudioReaction
    {
        [HideInInspector]
        public new string name = "Sprite Sheet on Volume!";
        public string info = "Switch sprite sheet frame according to the volume.";
        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }
        public List<Renderer> targetRenderers; // A list of target renderers
        private Dictionary<Renderer, MaterialPropertyBlock> propertyBlocks = new Dictionary<Renderer, MaterialPropertyBlock>(); // MaterialPropertyBlocks for each renderer
        private Dictionary<Renderer, Vector2> initialTextureScales = new Dictionary<Renderer, Vector2>(); // Store initial texture scales
        private Dictionary<Renderer, Vector2> initialTextureOffsets = new Dictionary<Renderer, Vector2>(); // Store initial texture offsets

        public Texture2D spriteSheet; // The sprite sheet texture
        public int columns = 4; // Number of columns in the sprite sheet
        public int rows = 4; // Number of rows in the sprite sheet
        private int totalFrames; // Total frames in the sprite sheet
        private int currentFrameIndex = 0; // Index of the currently displayed frame

        [ASP_FloatSlider(0.0f, 1f)]
        public float smoothness = .25f; // Slider to control smoothing

        [ASP_FloatSlider(0.0f, 20f)]
        public float sensitivity = 1.0f;

        [ASP_MinMaxSlider(0f, 1f)]
        public Vector3 volumeRange = new Vector3(0f, 0.3f, 0.7f); // X = current volume, Y = min volume, Z = max volume

        private Vector2 frameSize;
        private string texturePropertyName; // Will be determined based on the render pipeline

        public int materialIndex = 0; // Field to set the material index that will be affected

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
            initialTextureScales.Clear();
            initialTextureOffsets.Clear();

            // Cache the texture property name based on the render pipeline
            texturePropertyName = GetTexturePropertyName();

            totalFrames = columns * rows;
            frameSize = new Vector2(1.0f / columns, 1.0f / rows);

            if (targetRenderers != null && targetRenderers.Count > 0)
            {
                foreach (var renderer in targetRenderers)
                {
                    if (renderer == null) continue;

                    Material[] materials = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;
                    if (materialIndex < materials.Length)
                    {
                        Material material = materials[materialIndex];
                        // Use MaterialPropertyBlock to avoid modifying shared materials
                        MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(propertyBlock, materialIndex);
                        propertyBlocks[renderer] = propertyBlock;

                        Vector2 _initialScale = material.GetTextureScale(texturePropertyName);
                        Vector2 _initialOffset = material.GetTextureOffset(texturePropertyName);
                        initialTextureScales[renderer] = _initialScale;
                        initialTextureOffsets[renderer] = _initialOffset;

                        // Set initial sprite sheet using MaterialPropertyBlock
                        propertyBlock.SetTexture(texturePropertyName, spriteSheet);
                        propertyBlock.SetVector(texturePropertyName + "_ST", new Vector4(frameSize.x, frameSize.y, 0, 0));
                        renderer.SetPropertyBlock(propertyBlock, materialIndex);
                    }
                }
            }
        }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!IsActive || targetRenderers.Count == 0 || spriteSheet == null) return;

            float volume = rmsValue * sensitivity;
            volumeRange.x = Mathf.Lerp(volumeRange.x, volume, Time.deltaTime * (1.0f / Mathf.Clamp(smoothness, 0.01f, 10.0f)));

            if (volumeRange.x < volumeRange.y) return;

            float clampedVolume = Mathf.Clamp(volumeRange.x, volumeRange.y, volumeRange.z);
            int newFrameIndex = Mathf.Clamp(Mathf.FloorToInt((clampedVolume - volumeRange.y) / (volumeRange.z - volumeRange.y) * totalFrames), 0, totalFrames - 1);

            if (newFrameIndex != currentFrameIndex)
            {
                // Calculate the offset for the new frame
                int frameX = newFrameIndex % columns;
                int frameY = newFrameIndex / columns;

                // Adjust Y coordinate based on texture coordinate system
                Vector2 offset = new Vector2(frameX * frameSize.x, 1.0f - frameSize.y - frameY * frameSize.y);

                foreach (var renderer in targetRenderers)
                {
                    if (renderer != null && propertyBlocks.ContainsKey(renderer))
                    {
                        MaterialPropertyBlock propertyBlock = propertyBlocks[renderer];
                        propertyBlock.SetTexture(texturePropertyName, spriteSheet);
                        propertyBlock.SetVector(texturePropertyName + "_ST", new Vector4(frameSize.x, frameSize.y, offset.x, offset.y));
                        renderer.SetPropertyBlock(propertyBlock, materialIndex);
                    }
                }
                currentFrameIndex = newFrameIndex;
            }
        }

        public void ResetToOriginalState(Transform targetTransform)
        {
            if (targetRenderers == null || targetRenderers.Count == 0) return;

            foreach (var renderer in targetRenderers)
            {
                if (renderer != null && propertyBlocks.ContainsKey(renderer))
                {
                    MaterialPropertyBlock propertyBlock = propertyBlocks[renderer];

                    // Reset to the initial texture using MaterialPropertyBlock
                    propertyBlock.SetTexture(texturePropertyName, spriteSheet);
                    propertyBlock.SetVector(texturePropertyName + "_ST", new Vector4(initialTextureScales[renderer].x, initialTextureScales[renderer].y, initialTextureOffsets[renderer].x, initialTextureOffsets[renderer].y));
                    renderer.SetPropertyBlock(propertyBlock, materialIndex);
                }
            }
            currentFrameIndex = 0;
        }
    }
}
