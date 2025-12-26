using UnityEngine;

[DisallowMultipleComponent]
public class PlayerController : MonoBehaviour
{
    public GridMotor motor;

    [Header("Input")]
    public bool useLegacyInputAxes = true;
    public string horizontalAxis = "Horizontal";
    public string verticalAxis = "Vertical";

    // If you want to drive input from netcode/new-input-system,
    // set useLegacyInputAxes=false and assign this externally.
    [HideInInspector] public Vector2 externalInput;

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

        Vector2 input;
        if (useLegacyInputAxes)
            input = new Vector2(Input.GetAxisRaw(horizontalAxis), Input.GetAxisRaw(verticalAxis));
        else
            input = externalInput;

        motor.SetDesiredInput(input);
        // motor runs in its own Update
    }
}
