using UnityEngine;

/// <summary>
/// Player spawn marker.
/// </summary>
public class PlayerSpawnPoint : MonoBehaviour
{
    [Tooltip("If >= 0, this spawn point is reserved for that player index. If -1, any player can use it.")]
    public int playerIndex = -1;

    [Tooltip("Optional weight for random selection among eligible spawns.")]
    public float weight = 1f;
}
