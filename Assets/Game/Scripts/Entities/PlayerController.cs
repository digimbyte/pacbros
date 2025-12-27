using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerController : MonoBehaviour
{
    [Header("Target")]
    public GridMotor motor;
    [Header("Heat Speed Scaling")]
    [Tooltip("If true, motor.speedMultiplier is driven by heat stage multipliers.")]
    public bool scaleSpeedWithHeat = true;

    [Tooltip("Speed multipliers for heat stages 0..8.")]
    public float[] stageSpeedMultipliers = new float[9]
    {
        1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f
    };

    private float _baseSpeedMultiplier = 1f;

    [Header("Debug")]
    [Tooltip("Current input vector after merging WASD + Arrow keys (new Input System).")]
    public Vector2 currentInput;

    void Reset()
    {
        motor = GetComponent<GridMotor>();
    }

    void Awake()
    {
        if (motor == null)
            motor = GetComponent<GridMotor>();

        if (motor != null)
        {
            _baseSpeedMultiplier = motor.speedMultiplier;
        }
    }

    void OnEnable()
    {
        Heat.OnStageChanged += HandleHeatStageChanged;
        ApplyHeatStage(Heat.Stage);
    }

    void OnDisable()
    {
        Heat.OnStageChanged -= HandleHeatStageChanged;
    }

    void OnValidate()
    {
        EnsureStageArrayLength(9);
        for (int i = 0; i < stageSpeedMultipliers.Length; i++)
        {
            stageSpeedMultipliers[i] = Mathf.Max(0f, stageSpeedMultipliers[i]);
        }
    }

    void Update()
    {
        if (motor == null) return;

        var kb = Keyboard.current;
        if (kb == null)
        {
            currentInput = Vector2.zero;
            motor.SetDesiredInput(currentInput);
            return;
        }

        float h = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  h -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;

        float v = 0f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  v -= 1f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    v += 1f;

        Vector2 input = new Vector2(h, v);
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        currentInput = input;
        motor.SetDesiredInput(currentInput);
        // GridMotor runs its own Update for movement.
    }

    private void HandleHeatStageChanged(int stage)
    {
        ApplyHeatStage(stage);
    }

    private void ApplyHeatStage(int stage)
    {
        if (!scaleSpeedWithHeat || motor == null) return;

        int clamped = Mathf.Clamp(stage, 0, stageSpeedMultipliers.Length - 1);
        float mult = stageSpeedMultipliers != null && stageSpeedMultipliers.Length > 0
            ? stageSpeedMultipliers[clamped]
            : 1f;

        motor.speedMultiplier = _baseSpeedMultiplier * mult;
    }

    private void EnsureStageArrayLength(int length)
    {
        if (stageSpeedMultipliers == null || stageSpeedMultipliers.Length != length)
        {
            var resized = new float[length];
            for (int i = 0; i < length; i++)
            {
                resized[i] = stageSpeedMultipliers != null && i < stageSpeedMultipliers.Length
                    ? stageSpeedMultipliers[i]
                    : 1f;
            }
            stageSpeedMultipliers = resized;
        }
    }
}
