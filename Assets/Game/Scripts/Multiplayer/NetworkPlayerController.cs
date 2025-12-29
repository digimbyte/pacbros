using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerController : NetworkBehaviour
{
    public float speed = 5f;
    public GridMotor motor;

    void Awake()
    {
        motor = GetComponent<GridMotor>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            var session = ClientSessionMarker.Instance;
            if (session != null)
            {
                session.networkPlayerController = this;
            }
        }
    }

    void Update()
    {
        if (!IsOwner) return;

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector2 input = new Vector2(h, v);

        // Deadzone
        if (input.magnitude < 0.1f)
        {
            motor.SetDesiredDirection(Vector2Int.zero);
            return;
        }

        // Determine cardinal direction based on dominant axis
        Vector2Int dir = Vector2Int.zero;
        if (Mathf.Abs(h) > Mathf.Abs(v))
        {
            dir = h > 0 ? Vector2Int.right : Vector2Int.left;
        }
        else if (Mathf.Abs(v) > Mathf.Abs(h))
        {
            dir = v > 0 ? Vector2Int.up : Vector2Int.down;
        }

        if (motor != null)
        {
            motor.SetDesiredDirection(dir);
        }
    }
}