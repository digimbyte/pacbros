using UnityEngine;

/// <summary>
/// Single enum for all collectible/ownable items.
/// Stored directly in entity inventory arrays.
/// </summary>
public enum ItemId
{
    None = 0,

    // Keys / Keycards
    KeycardGreen = 100,
    KeycardYellow = 101,
    KeycardRed = 102,
    KeycardPurple = 103,

    // Gems (placeholder ids - extend as needed)
    GemBlue = 200,
    GemGreen = 201,
    GemRed = 202,
    GemPurple = 203,

    // Guns (placeholder ids - extend as needed)
    GunPeaShooter = 300,
    GunShotgun = 301,
    GunLaser = 302,

    // Upgrades (placeholder ids - extend as needed)
    UpgradeSpeed = 400,
    UpgradeDamage = 401,
    UpgradeRange = 402,
}
