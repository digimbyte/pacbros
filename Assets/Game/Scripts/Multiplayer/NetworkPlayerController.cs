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

        // Determine cardinal direction
        Vector2Int dir = Vector2Int.zero;
        if (Mathf.Abs(h) > Mathf.Abs(v))
        {
            dir = new Vector2Int(Mathf.RoundToInt(Mathf.Sign(h)), 0);
        }
        else if (Mathf.Abs(v) > Mathf.Abs(h))
        {
            dir = new Vector2Int(0, Mathf.RoundToInt(Mathf.Sign(v)));
        }

        if (motor != null)
        {
            motor.SetDesiredDirection(dir);
        }
    }
}