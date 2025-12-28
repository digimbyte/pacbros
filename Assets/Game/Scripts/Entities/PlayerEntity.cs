using UnityEngine;

/// <summary>
/// Player-specific entity component.
/// Multiplayer identifiers / stats can live here.
/// </summary>
public class PlayerEntity : EntityBase
{
    [Header("Player")]
    public int playerIndex;

    [Tooltip("Optional: can be used to tag local-player controlled entities.")]
    public bool isLocal;

    [Header("Resources")]
    [Min(0)]
    public int ammo;
}
