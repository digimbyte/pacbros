using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Enemy-specific entity component.
/// AI / archetype info can live here.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class EnemyEntity : MonoBehaviour
{
    [Header("State")]
    public bool isGhost = true;
    [Tooltip("True when this enemy has been marked dead by game systems.")]
    public bool isDead;

    [Header("Events")]
    public UnityEvent onKilled;
    public UnityEvent onRespawn;

    [Header("Audio")]
    public AudioClip deathAudio;
    public AudioClip respawnAudio;
    public ItemId[] inventory = Array.Empty<ItemId>();
    public bool dropInventoryOnDeath = true;
    [Header("Enemy")]
    [Tooltip("Optional: enemy type id (e.g. different ghosts, bosses, etc).")]
    public int enemyTypeId;

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
        
    // Optional reference to a local `kill` component which should be disabled when this entity is dead.
    kill _killComponent;
    bool _lastDeadState = false;
    AudioSource _audioSource;

    void Start()
    {
        _killComponent = GetComponent<kill>();
        _audioSource = GetComponent<AudioSource>();
        _lastDeadState = isDead;
        if (_killComponent != null)
            _killComponent.enabled = !_lastDeadState;

        if (onKilled == null) onKilled = new UnityEvent();
        if (onRespawn == null) onRespawn = new UnityEvent();

        // Invoke event based on initial state
        if (isDead)
        {
            onKilled?.Invoke();
        }
        else
        {
            onRespawn?.Invoke();
        }
    }

    void Update()
    {
        if (isDead != _lastDeadState)
        {
            _lastDeadState = isDead;
            if (_killComponent != null)
                _killComponent.enabled = !_lastDeadState;

            if (isDead)
            {
                onKilled?.Invoke();
                if (deathAudio != null && _audioSource != null)
                    _audioSource.PlayOneShot(deathAudio);
                if (dropInventoryOnDeath)
                {
                    DropInventory();
                }
            }
            else
            {
                onRespawn?.Invoke();
                if (respawnAudio != null && _audioSource != null)
                    _audioSource.PlayOneShot(respawnAudio);
            }
        }
    }

    void DropInventory()
    {
        var runtime = LevelRuntime.Active;
        if (runtime == null) return;

        Vector3 pos = transform.position;
        Quaternion rot = Quaternion.identity;

        foreach (var item in inventory)
        {
            if (ItemRegistryMapping.TryGetRegistryKey(item, out string key))
            {
                runtime.InstantiateRegistryPrefab(key, pos, rot);
            }
        }
    }
}
