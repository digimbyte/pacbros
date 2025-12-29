using UnityEngine;

[RequireComponent(typeof(PlayerEntity))]
[RequireComponent(typeof(GridMotor))]
[DisallowMultipleComponent]
public class DoorCollisionGuard : MonoBehaviour
{
    const int DOOR_LAYER = 9; // Hard-coded door layer ID

    [Tooltip("Radius (meters) used when probing for blocking doors around the player.")]
    public float probeRadius = 0.5f;

    [Tooltip("Optional explicit layer mask for door colliders. If zero, LevelRuntime.doorLayers is used.")]
    public LayerMask doorLayers;

    [Tooltip("If true, log whenever the player is pushed back by a locked door.")]
    public bool logBlocks;

    PlayerEntity _player;
    GridMotor _motor;
    Vector3 _lastFreePosition;

    void Awake()
    {
        _player = GetComponent<PlayerEntity>();
        _motor = GetComponent<GridMotor>();
        _lastFreePosition = transform.position;
    }

    void LateUpdate()
    {
        if (_player == null || _player.isDead)
        {
            _lastFreePosition = transform.position;
            return;
        }

        if (!TryFindBlockingDoor(out AccessGate gate))
        {
            _lastFreePosition = transform.position;
            return;
        }

        if (_motor != null)
            _motor.Teleport(_lastFreePosition);
        else
            transform.position = _lastFreePosition;

        string doorName = gate != null ? gate.name : "<unknown door>";
        PlayerEventStack.Push(new PlayerEvent(PlayerEventType.DoorBlocked, _player, transform.position, doorName));

        if (logBlocks)
            Debug.LogWarning($"DoorCollisionGuard[{name}] blocked by '{doorName}'", this);
    }

    bool TryFindBlockingDoor(out AccessGate gate)
    {
        gate = null;

        // Use sphere cast to detect doors in movement direction
        LayerMask doorMask = 1 << DOOR_LAYER; // Use hard-coded door layer
        if (doorLayers != 0) doorMask = doorLayers; // Fallback to configured mask

        Vector3 castOrigin = transform.position + Vector3.up * 0.5f;
        Vector3 castDirection = _motor != null ? new Vector3(_motor.MoveDirection.x, 0, _motor.MoveDirection.y) : transform.forward;
        float castDistance = Mathf.Max(0.5f, probeRadius);

        if (Physics.SphereCast(castOrigin, 0.1f, castDirection, out RaycastHit hit, castDistance, doorMask))
        {
            var doorGate = hit.collider.GetComponentInParent<AccessGate>();
            if (doorGate != null && doorGate.ShouldBlock(new EntityIdentity(_player)))
            {
                gate = doorGate;
                return true;
            }
        }

        return false;
    }
}
