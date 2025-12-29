using UnityEngine;

[RequireComponent(typeof(PlayerEntity))]
[RequireComponent(typeof(GridMotor))]
[DisallowMultipleComponent]
public class DoorCollisionGuard : MonoBehaviour
{
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
            _motor.HardTeleport(_lastFreePosition);
        else
            transform.position = _lastFreePosition;

        string doorName = gate != null ? gate.name : "<unknown door>";
        PlayerEventStack.Push(new PlayerEvent(PlayerEventType.DoorBlocked, _player, transform.position, doorName));

        if (logBlocks)
            Debug.LogWarning($"DoorCollisionGuard[{name}] blocked by '{doorName}'", this);
    }

    bool TryFindBlockingDoor(out AccessGate blockingGate)
    {
        blockingGate = null;

        LayerMask mask = doorLayers;
        if (mask == 0 && LevelRuntime.Active != null)
            mask = LevelRuntime.Active.doorLayers;
        if (mask == 0)
            mask = ~0; // fallback: search everything

        Collider[] hits = Physics.OverlapSphere(transform.position, Mathf.Max(0.1f, probeRadius), mask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;
            var gate = col.GetComponentInParent<AccessGate>();
            if (gate == null) continue;

            if (gate.ShouldBlock(new EntityIdentity(_player)))
            {
                blockingGate = gate;
                return true;
            }
        }

        return false;
    }
}
