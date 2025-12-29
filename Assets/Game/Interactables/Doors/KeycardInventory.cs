using UnityEngine;

/// <summary>
/// Legacy compatibility shim for older keycard inventory components.
/// New code uses `PlayerEntity`/`EnemyEntity` via `EntityIdentity`; this
/// component lets older prefabs remain compilable and functional.
/// </summary>
public class KeycardInventory : MonoBehaviour
{
    // Return whether this inventory has access to the requested key.
    public bool HasAccess(KeycardColor required, bool allowHigherKeys)
    {
        if (required == KeycardColor.None)
            return true;

        // Prefer PlayerEntity/EnemyEntity if present on the same or parent objects.
        var player = GetComponentInParent<PlayerEntity>();
        if (player != null)
            return player.HasKeycard(required, allowHigherKeys);

        var enemy = GetComponentInParent<EnemyEntity>();
        if (enemy != null)
            return enemy.HasKeycard(required, allowHigherKeys);

        // No known inventory available — deny access by default.
        return false;
    }
}
