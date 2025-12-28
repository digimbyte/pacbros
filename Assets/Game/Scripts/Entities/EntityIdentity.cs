using UnityEngine;

public enum EntityKind
{
    None,
    Player,
    Enemy
}

/// <summary>
/// Lightweight identifier that wraps either a PlayerEntity or an EnemyEntity
/// so systems can branch explicitly without relying on a shared base class.
/// </summary>
public readonly struct EntityIdentity
{
    public readonly PlayerEntity player;
    public readonly EnemyEntity enemy;

    public EntityIdentity(PlayerEntity player)
    {
        this.player = player;
        enemy = null;
    }

    public EntityIdentity(EnemyEntity enemy)
    {
        player = null;
        this.enemy = enemy;
    }

    public EntityKind Kind => player != null ? EntityKind.Player : enemy != null ? EntityKind.Enemy : EntityKind.None;
    public bool IsValid => Kind != EntityKind.None;
    public Transform Transform => player != null ? player.transform : enemy != null ? enemy.transform : null;
    public string Name => player != null ? player.name : enemy != null ? enemy.name : string.Empty;
    public bool IsGhost => player != null ? player.isGhost : enemy != null && enemy.isGhost;
    public bool IsDead => player != null ? player.isDead : enemy != null && enemy.isDead;

    public bool HasKeycard(KeycardColor required, bool allowHigherKeys) =>
        player != null ? player.HasKeycard(required, allowHigherKeys) :
        enemy != null && enemy.HasKeycard(required, allowHigherKeys);

    public bool Has(ItemId item) =>
        player != null ? player.Has(item) :
        enemy != null && enemy.Has(item);

    public int Count(ItemId item) =>
        player != null ? player.Count(item) :
        enemy != null ? enemy.Count(item) : 0;

    public void Add(ItemId item)
    {
        if (player != null) player.Add(item);
        else enemy?.Add(item);
    }

    public bool RemoveOne(ItemId item)
    {
        if (player != null) return player.RemoveOne(item);
        if (enemy != null) return enemy.RemoveOne(item);
        return false;
    }

    public KeycardColor HighestKeycardOwned() =>
        player != null ? player.HighestKeycardOwned() :
        enemy != null ? enemy.HighestKeycardOwned() : KeycardColor.None;

    public int InstanceId => player != null ? player.GetInstanceID() : enemy != null ? enemy.GetInstanceID() : 0;
}

public static class EntityIdentityUtility
{
    public static EntityIdentity From(Component component)
    {
        if (component == null)
            return default;

        var player = component.GetComponentInParent<PlayerEntity>();
        if (player != null)
            return new EntityIdentity(player);

        var enemy = component.GetComponentInParent<EnemyEntity>();
        if (enemy != null)
            return new EntityIdentity(enemy);

        return default;
    }

    public static EntityIdentity From(GameObject go)
    {
        if (go == null) return default;
        return From(go.transform);
    }

    public static EntityIdentity From(Transform transform)
    {
        if (transform == null) return default;
        var player = transform.GetComponentInParent<PlayerEntity>();
        if (player != null)
            return new EntityIdentity(player);

        var enemy = transform.GetComponentInParent<EnemyEntity>();
        if (enemy != null)
            return new EntityIdentity(enemy);

        return default;
    }
}
