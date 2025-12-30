using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Enemy-specific entity component.
/// Enemy type identification and state can live here.
/// </summary>
public class EnemyEntity : MonoBehaviour
{
    [Header("State")]
    [Tooltip("Used when an enemy is temporarily in ghost form (e.g., during panic mode).")]
    public bool isGhost;
    [Tooltip("True when this enemy has been marked dead by game systems.")]
    public bool isDead;

    [Header("Type")]
    [Tooltip("Enemy type identifier for spawn matching. -1 = any type.")]
    public int enemyTypeId = -1;

    [Header("Events")]
    public UnityEvent onKilled;
    public UnityEvent onRespawn;

    [Header("AI")]
    [Tooltip("Reference to the brain controller component.")]
    public EnemyBrainController brainController;

    [Header("Inventory")]
    public ItemId[] inventory = Array.Empty<ItemId>();

    public bool Has(ItemId item)
    {
        if (item == ItemId.None) return false;
        for (int i = 0; i < inventory.Length; i++)
        {
            if (inventory[i] == item) return true;
        }
        return false;
    }

    public int Count(ItemId item)
    {
        if (item == ItemId.None) return 0;
        int count = 0;
        for (int i = 0; i < inventory.Length; i++)
        {
            if (inventory[i] == item) count++;
        }
        return count;
    }

    public void Add(ItemId item)
    {
        if (item == ItemId.None) return;

        int oldLen = inventory.Length;
        ItemId[] next = new ItemId[oldLen + 1];
        for (int i = 0; i < oldLen; i++) next[i] = inventory[i];
        next[oldLen] = item;
        inventory = next;
    }

    public bool RemoveOne(ItemId item)
    {
        if (item == ItemId.None) return false;

        int idx = -1;
        for (int i = 0; i < inventory.Length; i++)
        {
            if (inventory[i] == item)
            {
                idx = i;
                break;
            }
        }

        if (idx < 0) return false;

        ItemId[] next = new ItemId[inventory.Length - 1];
        int w = 0;
        for (int i = 0; i < inventory.Length; i++)
        {
            if (i == idx) continue;
            next[w++] = inventory[i];
        }
        inventory = next;
        return true;
    }

    public KeycardColor HighestKeycardOwned()
    {
        if (Has(ItemId.Key_Purple)) return KeycardColor.Purple;
        if (Has(ItemId.Key_Red)) return KeycardColor.Red;
        if (Has(ItemId.Key_Yellow)) return KeycardColor.Yellow;
        if (Has(ItemId.Key_Green)) return KeycardColor.Green;
        return KeycardColor.None;
    }

    public bool HasKeycard(KeycardColor required, bool allowHigherKeys)
    {
        if (required == KeycardColor.None) return true;
        if (allowHigherKeys) return HighestKeycardOwned() >= required;

        return required switch
        {
            KeycardColor.Green => Has(ItemId.Key_Green),
            KeycardColor.Yellow => Has(ItemId.Key_Yellow),
            KeycardColor.Red => Has(ItemId.Key_Red),
            KeycardColor.Purple => Has(ItemId.Key_Purple),
            _ => false,
        };
    }

    void Awake()
    {
        if (brainController == null)
            brainController = GetComponent<EnemyBrainController>();
    }
}
