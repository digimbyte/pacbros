using UnityEngine;

/// <summary>
/// Minimal shim for multiplayer: feed replicated input into a GridMotor.
/// (This file is intentionally small; wire it to your netcode layer however you want.)
/// </summary>
[DisallowMultipleComponent]
public class RemoteController : MonoBehaviour
{
    public GridMotor motor;

    // Set this from your netcode.
    public Vector2 replicatedInput;

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
        motor.SetDesiredInput(replicatedInput);
    }
}
