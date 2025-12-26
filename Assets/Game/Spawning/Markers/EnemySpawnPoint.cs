using UnityEngine;

/// <summary>
/// Enemy spawn marker.
/// </summary>
public class EnemySpawnPoint : MonoBehaviour
{
    [Tooltip("If >= 0, only enemies of this type should spawn here. If -1, any enemy type can use it.")]
    public int enemyTypeId = -1;

    [Tooltip("If true, this spawn is intended for ghost enemies.")]
    public bool ghostEnemySpawn;

    [Tooltip("Optional weight for random selection among eligible spawns.")]
    public float weight = 1f;
}
