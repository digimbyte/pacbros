using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pick-up payload that can apply heat/score/ammo and seed inventory.
///
/// - Pickup eligibility is controlled by booleans (players / ghosts).
/// - Effects can be applied only to the picker, or universally to all players and/or all ghosts.
/// </summary>
[DisallowMultipleComponent]
public class ItemPickup : MonoBehaviour
{
    [Serializable]
    public struct InventoryDelta
    {
        public ItemId item;
        [Tooltip("Positive = add, negative = remove.")]
        public int count;
    }
    [Header("Who can pick this up")]
    public bool PlayerPickup = true;
    public bool GhostPickup = false;

    [Header("Effect Targeting")]
    [Tooltip("If true, effect is applied to all players and/or all ghosts (based on the booleans below). If false, only the picker gets the effect.")]
    public bool effectIsUniversal = false;

    [Tooltip("When effectIsUniversal is true: apply effect to all PlayerEntity instances.")]
    public bool applyToAllPlayers = true;

    [Tooltip("When effectIsUniversal is true: apply effect to all entities where EntityBase.isGhost == true.")]
    public bool applyToAllGhosts = false;

    [Header("Values")]
    [Tooltip("Heat delta (kelvin). Positive = add, negative = subtract.")]
    public int heatValue;

    [Tooltip("Score delta (points). Positive = add, negative = subtract.")]
    public int scoreValue;

    [Tooltip("Lives delta. Positive = add, negative = subtract. (Applied to LevelRuntime.Active.currentLives if present.)")]
    public int livesValue;

    [Tooltip("Ammo delta (applies only to PlayerEntity targets). Positive = add, negative = subtract.")]
    public int ammoValue;

    [Header("Inventory")]
    [Tooltip("Items to add to the target entity's inventory when picked up.")]
    public ItemId[] inventory = Array.Empty<ItemId>();

    [Tooltip("Signed inventory edits. Positive count adds; negative count removes.")]
    public InventoryDelta[] inventoryDeltas = Array.Empty<InventoryDelta>();

    [Header("Consume")]
    [Tooltip("If true, the pickup object will be destroyed after applying.")]
    public bool destroyOnPickup = true;

    bool _consumed;

    void OnTriggerEnter(Collider other)
    {
        TryConsume(other);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        TryConsume(other);
    }

    void TryConsume(Component other)
    {
        if (_consumed) return;
        if (other == null) return;

        EntityBase picker = other.GetComponentInParent<EntityBase>();
        if (picker == null) return;

        if (!CanBePickedUpBy(picker)) return;

        Apply(picker);

        _consumed = true;
        if (destroyOnPickup)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }

    bool CanBePickedUpBy(EntityBase entity)
    {
        if (entity == null) return false;

        bool isPlayer = entity.GetComponent<PlayerEntity>() != null;
        bool isGhost = entity.isGhost;

        // If an entity qualifies for both categories, allow pickup if either is enabled.
        bool allowed = (isPlayer && PlayerPickup) || (isGhost && GhostPickup);
        return allowed;
    }

    void Apply(EntityBase picker)
    {
        // These systems are global/static in this project, so apply them once per pickup.
        ApplyGlobalValues();

        if (!effectIsUniversal)
        {
            ApplyPerEntityValues(picker);
            return;
        }

        // Universal per-entity effects.
        HashSet<EntityBase> targets = new();

        if (applyToAllPlayers)
        {
            var players = FindObjectsOfType<PlayerEntity>();
            for (int i = 0; i < players.Length; i++)
                targets.Add(players[i]);
        }

        if (applyToAllGhosts)
        {
            var entities = FindObjectsOfType<EntityBase>();
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (e != null && e.isGhost)
                    targets.Add(e);
            }
        }

        // If nothing is selected, fall back to just the picker.
        if (targets.Count == 0)
            targets.Add(picker);

        foreach (var t in targets)
            ApplyPerEntityValues(t);
    }

    void ApplyGlobalValues()
    {
        if (heatValue != 0)
            Heat.AddHeat(heatValue);

        if (scoreValue > 0)
            Score.AddPoints(scoreValue);
        else if (scoreValue < 0)
            Score.RemovePoints(-scoreValue);

        if (livesValue != 0 && LevelRuntime.Active != null)
        {
            LevelRuntime.Active.currentLives = Mathf.Max(0, LevelRuntime.Active.currentLives + livesValue);
        }
    }

    void ApplyPerEntityValues(EntityBase target)
    {
        if (target == null) return;

        if (ammoValue != 0)
        {
            var pe = target.GetComponent<PlayerEntity>();
            if (pe != null)
                pe.ammo = Mathf.Max(0, pe.ammo + ammoValue);
        }

        if (inventory != null)
        {
            for (int i = 0; i < inventory.Length; i++)
                target.Add(inventory[i]);
        }

        if (inventoryDeltas != null)
        {
            for (int i = 0; i < inventoryDeltas.Length; i++)
            {
                var d = inventoryDeltas[i];
                if (d.item == ItemId.None || d.count == 0)
                    continue;

                if (d.count > 0)
                {
                    for (int n = 0; n < d.count; n++)
                        target.Add(d.item);
                }
                else
                {
                    int removeCount = -d.count;
                    for (int n = 0; n < removeCount; n++)
                    {
                        if (!target.RemoveOne(d.item))
                            break;
                    }
                }
            }
        }
    }
}
