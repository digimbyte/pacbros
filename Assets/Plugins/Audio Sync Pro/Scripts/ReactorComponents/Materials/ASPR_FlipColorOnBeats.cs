using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Materials")]
    public class ASPR_FlipColorOnBeats : MonoBehaviour, ASP_IAudioReaction
    {
        public new string name = "Flip Color On Beats!";
        public string info = "Flip Colors on materials every Beat!";

        public List<Renderer> renderers = new List<Renderer>();
        private Dictionary<Renderer, MaterialPropertyBlock> propertyBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();
        private Dictionary<Renderer, Color> initialColors = new Dictionary<Renderer, Color>();

        [ASP_FloatSlider(0f, 2f)]
        public float sensitivity = .25f; // Adjust this value to control how sensitive the beat detection is

        [ASP_SpectrumDataSlider(0f, .1f, "spectrumDataForSlider")]
        public Vector4 frequencyRangeAndThreshold = new Vector4(0f, 5000f, 0f, 0.04f);

        public int materialIndex = 0;

        private bool isColor1Active = true;
        public Color color1 = Color.white;
        public Color color2 = Color.black;

        [SerializeField] private string parameterName; // Will be determined based on the render pipeline

        [HideInInspector]
        [SerializeField] private bool isActive = true;

        public bool IsActive
        {
            get => isActive;
            set => isActive = value;
        }

        private float[] spectrumDataForSlider = new float[512];

        private float lastBeatTime = 0f;
        public float beatCooldown = 0.25f;

        private string GetColorPropertyName()
        {
            // Check if the built-in render pipeline is being used
            if (GraphicsSettings.defaultRenderPipeline == null)
            {
                return "_Color"; // Built-in or Standard
            }
            else
            {
                // Get the type name of the current Render Pipeline Asset
                string pipelineType = GraphicsSettings.defaultRenderPipeline.GetType().ToString();

                if (pipelineType.Contains("UniversalRenderPipelineAsset"))
                {
                    return "_BaseColor"; // URP
                }
                else if (pipelineType.Contains("HDRenderPipelineAsset"))
                {
                    return "_BaseColor"; // HDRP
                }
                else
                {
                    // Unknown or custom render pipeline
                    return "_Color"; // Fallback
                }
            }
        }

        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation)
        {
            initialColors.Clear();
            propertyBlocks.Clear();

            // Determine the correct color property name based on the render pipeline
            parameterName = GetColorPropertyName();

            foreach (var renderer in renderers)
            {
                if (renderer != null)
                {
                    Material[] materials = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;

                    if (materialIndex < materials.Length)
                    {
                        if (materials[materialIndex].HasProperty(parameterName))
                        {
                            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                            renderer.GetPropertyBlock(propertyBlock, materialIndex);
                            propertyBlocks[renderer] = propertyBlock;

                            Color initialColor = materials[materialIndex].GetColor(parameterName);
                            initialColors[renderer] = initialColor;

                            // Set initial color using MaterialPropertyBlock
                            propertyBlock.SetColor(parameterName, color1);
                            renderer.SetPropertyBlock(propertyBlock, materialIndex);
                        }
                    }
                }
            }
        }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (renderers.Count == 0 || !IsActive)
                return;

            UpdateSpectrumData(spectrumData);

            // Calculate frequency per bin
            float sampleRate = AudioSettings.outputSampleRate;
            float freqPerBin = sampleRate / 2f / spectrumData.Length;

            // Define logarithmic frequency range
            float minLogFreq = Mathf.Log10(frequencyRangeAndThreshold.x + 1f); // +1 to avoid log(0)
            float maxLogFreq = Mathf.Log10(frequencyRangeAndThreshold.y + 1f);

            // Analyze spectrum data within the frequency window using logarithmic scaling
            float averageSpectrumInWindow = 0f;
            int count = 0;

            for (int i = 0; i < spectrumData.Length; i++)
            {
                float freq = i * freqPerBin;
                float logFreq = Mathf.Log10(freq + 1f); // +1 to avoid log(0)

                if (logFreq >= minLogFreq && logFreq <= maxLogFreq)
                {
                    averageSpectrumInWindow += spectrumData[i];
                    count++;
                }
            }
            averageSpectrumInWindow *= 100;
            if (count > 0)
            {
                averageSpectrumInWindow /= count; // Normalize by the number of bins
            }

            averageSpectrumInWindow *= sensitivity;
            frequencyRangeAndThreshold.z = averageSpectrumInWindow;

            // Check if enough time has passed since the last beat
            if (Time.time - lastBeatTime >= beatCooldown) // Ensure cooldown has passed
            {
                // Determine if a beat is detected
                if (averageSpectrumInWindow > frequencyRangeAndThreshold.w)
                {
                    // Flip the active color
                    isColor1Active = !isColor1Active;
                    Color targetColor = isColor1Active ? color1 : color2;

                    // Apply the new color to all renderers
                    foreach (var renderer in renderers)
                    {
                        if (renderer != null && propertyBlocks.ContainsKey(renderer))
                        {
                            MaterialPropertyBlock propertyBlock = propertyBlocks[renderer];
                            propertyBlock.SetColor(parameterName, targetColor);
                            renderer.SetPropertyBlock(propertyBlock, materialIndex);
                        }
                    }

                    // Reset the last beat time after successfully processing the beat
                    lastBeatTime = Time.time;
                }
            }
        }


        private void UpdateSpectrumData(float[] spectrumData)
        {
            for (int i = 0; i < spectrumDataForSlider.Length; i++)
            {
                spectrumDataForSlider[i] = i < spectrumData.Length ? spectrumData[i] : 0f;
            }
        }

        public void ResetToOriginalState(Transform targetTransform)
        {
            foreach (var renderer in renderers)
            {
                if (renderer != null && propertyBlocks.ContainsKey(renderer) && initialColors.ContainsKey(renderer))
                {
                    MaterialPropertyBlock propertyBlock = propertyBlocks[renderer];

                    // Reset to the initial color using MaterialPropertyBlock
                    propertyBlock.SetColor(parameterName, initialColors[renderer]);
                    renderer.SetPropertyBlock(propertyBlock, materialIndex);
                }
            }
        }
    }
}
