using System;
using UnityEngine;

/// <summary>
/// Player-specific entity component.
/// Multiplayer identifiers / stats can live here.
/// </summary>
public class PlayerEntity : MonoBehaviour
{
    [Header("State")]
    [Tooltip("Used when a player is temporarily in ghost form (e.g., post-death spectator).")]
    public bool isGhost;
    [Tooltip("True when this player has been marked dead by game systems.")]
    public bool isDead;

    [Header("Inventory")]
    public ItemId[] inventory = Array.Empty<ItemId>();
    [Header("Player")]
    public int playerIndex;

    [Tooltip("Optional: can be used to tag local-player controlled entities.")]
    public bool isLocal;

    [Header("Resources")]
    [Min(0)]
    public int ammo;
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
        if (Has(ItemId.KeycardPurple)) return KeycardColor.Purple;
        if (Has(ItemId.KeycardRed)) return KeycardColor.Red;
        if (Has(ItemId.KeycardYellow)) return KeycardColor.Yellow;
        if (Has(ItemId.KeycardGreen)) return KeycardColor.Green;
        return KeycardColor.None;
    }

    public bool HasKeycard(KeycardColor required, bool allowHigherKeys)
    {
        if (required == KeycardColor.None) return true;
        if (allowHigherKeys) return HighestKeycardOwned() >= required;

        return required switch
        {
            KeycardColor.Green => Has(ItemId.KeycardGreen),
            KeycardColor.Yellow => Has(ItemId.KeycardYellow),
            KeycardColor.Red => Has(ItemId.KeycardRed),
            KeycardColor.Purple => Has(ItemId.KeycardPurple),
            _ => false,
        };
    }

    void OnEnable()
    {
        var tracker = PlayerTracker.EnsureInstance();
        tracker?.Register(this);
    }

    void OnDisable()
    {
        if (PlayerTracker.Instance != null)
            PlayerTracker.Instance.Unregister(this);
    }
}
