using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerController : MonoBehaviour
{
    [Header("Target")]
    public GridMotor motor;

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
}
