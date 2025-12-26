using UnityEngine;

/// <summary>
/// Item spawn marker.
/// </summary>
public class ItemSpawnPoint : MonoBehaviour
{
    [Tooltip("Items eligible to spawn here.")]
    public ItemId[] itemPool;

    [Min(0)]
    public int count = 1;

    [Tooltip("Optional weight for random selection among eligible spawns.")]
    public float weight = 1f;
}
