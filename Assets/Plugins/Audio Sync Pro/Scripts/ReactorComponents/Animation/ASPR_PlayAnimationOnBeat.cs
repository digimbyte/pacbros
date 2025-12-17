using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor.Animations;
#endif

namespace TelePresent.AudioSyncPro
{
    [AddComponentMenu("GameObject/")]
    [ASP_ReactorCategory("Animation")]
    public class ASPR_PlayAnimationOnBeat : MonoBehaviour, ASP_IAudioReaction
    {
        [HideInInspector]
        public new string name = "Play Animation On Beats!";
        public string info = "Plays an animation from an Animator component every time a beat is detected (Play Mode Only).";

        public Animator animator;
        [SerializeField]
        private string selectedAnimationState;
        [SerializeField]
        private bool mustFinishBeforeRetrigger = true;

        private List<string> animatorStateNames;

        [ASP_FloatSlider(0f, 2f)]
        public float sensitivity = .25f; // Adjust this value to control how sensitive the beat detection is

        [ASP_SpectrumDataSlider(0f, .1f, "spectrumDataForSlider")]
        public Vector4 frequencyRangeAndThreshold = new Vector4(0f, 5000f, 0f, 0.04f);

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

        // New variables for beat detection
        private const int energyBufferSize = 43; // Number of past energies to consider
        private Queue<float> energyBuffer = new Queue<float>();

        private void OnValidate()
        {
            if (animator != null)
            {
#if UNITY_EDITOR
                UpdateAnimatorStateNames();
#endif
            }
        }

#if UNITY_EDITOR
        private void UpdateAnimatorStateNames()
        {
            animatorStateNames = new List<string>();

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            AnimatorController animatorController = animator.runtimeAnimatorController as AnimatorController;
            if (animatorController != null)
            {
                foreach (AnimatorControllerLayer layer in animatorController.layers)
                {
                    foreach (ChildAnimatorState state in layer.stateMachine.states)
                    {
                        animatorStateNames.Add(state.state.name);
                    }
                }
            }
        }
#endif

        public void Initialize(Vector3 initialPosition, Vector3 initialScale, Quaternion initialRotation) { }

        public void React(AudioSourcePlus audioSourcePlus, Transform targetTransform, float rmsValue, float[] spectrumData)
        {
            if (!IsActive || animator == null) return;

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

            if (Time.time - lastBeatTime < beatCooldown)
            {
                return;
            }

            if (averageSpectrumInWindow > frequencyRangeAndThreshold.w)
            {
                PlayAnimationState(selectedAnimationState);
                lastBeatTime = Time.time;
            }
        }

        private void PlayAnimationState(string stateName)
        {
            if (string.IsNullOrEmpty(stateName))
            {
                Debug.LogWarning("No animation state name provided.");
                return;
            }

            int layerIndex = GetLayerIndexContainingState(stateName);

            if (layerIndex == -1)
            {
                Debug.LogWarning($"Animator: State '{stateName}' could not be found in any layer.");
                return;
            }

            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
            if (mustFinishBeforeRetrigger)
            {
                if (!stateInfo.IsName(stateName) || stateInfo.normalizedTime >= 1f)
                {
                    animator.Play(stateName, layerIndex);
                }
            }
            else
            {
                animator.Play(stateName, layerIndex, 0f);
            }
        }

        private int GetLayerIndexContainingState(string stateName)
        {
#if UNITY_EDITOR
            AnimatorController animatorController = animator.runtimeAnimatorController as AnimatorController;
            if (animatorController != null)
            {
                for (int i = 0; i < animatorController.layers.Length; i++)
                {
                    foreach (ChildAnimatorState state in animatorController.layers[i].stateMachine.states)
                    {
                        if (state.state.name == stateName)
                        {
                            return i;
                        }
                    }
                }
            }
#else
            for (int i = 0; i < animator.layerCount; i++)
            {
                if (animator.HasState(i, Animator.StringToHash(stateName)))
                {
                    return i;
                }
            }
#endif
            return -1;
        }

        private void UpdateSpectrumData(float[] spectrumData)
        {
            for (int i = 0; i < spectrumDataForSlider.Length; i++)
            {
                spectrumDataForSlider[i] = i < spectrumData.Length ? spectrumData[i] : 0f;
            }
        }

        public void ResetToOriginalState(Transform targetTransform) { }

        public List<string> GetAnimatorStateNames()
        {
#if UNITY_EDITOR
            if (animatorStateNames == null)
            {
                UpdateAnimatorStateNames();
            }
            return animatorStateNames;
#else
            return new List<string>();
#endif
        }
    }
}
