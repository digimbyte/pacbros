using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry for spawn point markers.
/// </summary>
public class SpawnPointsRegistry : MonoBehaviour
{
    [SerializeField] private List<PlayerSpawnPoint> playerSpawns = new();
    [SerializeField] private List<EnemySpawnPoint> enemySpawns = new();
    [SerializeField] private List<ItemSpawnPoint> itemSpawns = new();

    public IReadOnlyList<PlayerSpawnPoint> PlayerSpawns => playerSpawns;
    public IReadOnlyList<EnemySpawnPoint> EnemySpawns => enemySpawns;
    public IReadOnlyList<ItemSpawnPoint> ItemSpawns => itemSpawns;

    public void ClearAll()
    {
        playerSpawns.Clear();
        enemySpawns.Clear();
        itemSpawns.Clear();
    }

    public void Register(PlayerSpawnPoint p)
    {
        if (p == null) return;
        if (!playerSpawns.Contains(p)) playerSpawns.Add(p);
    }

    public void Register(EnemySpawnPoint e)
    {
        if (e == null) return;
        if (!enemySpawns.Contains(e)) enemySpawns.Add(e);
    }

    public void Register(ItemSpawnPoint i)
    {
        if (i == null) return;
        if (!itemSpawns.Contains(i)) itemSpawns.Add(i);
    }

    public void Unregister(PlayerSpawnPoint p) => playerSpawns.Remove(p);
    public void Unregister(EnemySpawnPoint e) => enemySpawns.Remove(e);
    public void Unregister(ItemSpawnPoint i) => itemSpawns.Remove(i);

    public PlayerSpawnPoint GetPlayerSpawnForIndex(int playerIndex)
    {
        // Exact match first.
        for (int i = 0; i < playerSpawns.Count; i++)
        {
            var s = playerSpawns[i];
            if (s != null && s.playerIndex == playerIndex)
                return s;
        }

        // Fallback to any.
        for (int i = 0; i < playerSpawns.Count; i++)
        {
            var s = playerSpawns[i];
            if (s != null && s.playerIndex < 0)
                return s;
        }

        return null;
    }

    public List<EnemySpawnPoint> GetEnemySpawns(int enemyTypeId = -1, bool? ghostEnemy = null)
    {
        var list = new List<EnemySpawnPoint>();
        for (int i = 0; i < enemySpawns.Count; i++)
        {
            var s = enemySpawns[i];
            if (s == null) continue;

            if (enemyTypeId >= 0 && s.enemyTypeId >= 0 && s.enemyTypeId != enemyTypeId)
                continue;

            if (ghostEnemy.HasValue && s.ghostEnemySpawn != ghostEnemy.Value)
                continue;

            list.Add(s);
        }
        return list;
    }

    public List<ItemSpawnPoint> GetItemSpawns()
    {
        // Convenience; callers can filter by itemPool content.
        return new List<ItemSpawnPoint>(itemSpawns);
    }
}
