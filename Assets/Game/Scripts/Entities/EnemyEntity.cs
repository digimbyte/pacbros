using UnityEngine;

/// <summary>
/// Enemy-specific entity component.
/// AI / archetype info can live here.
/// </summary>
public class EnemyEntity : EntityBase
{
    [Header("Enemy")]
    [Tooltip("Optional: enemy type id (e.g. different ghosts, bosses, etc).")]
    public int enemyTypeId;
}
