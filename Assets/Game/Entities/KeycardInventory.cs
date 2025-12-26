using UnityEngine;

/// <summary>
/// Attach to an entity that can carry keycards.
/// This is intentionally lightweight and inspector-editable.
/// </summary>
public class KeycardInventory : MonoBehaviour
{
    [Header("Owned Keycards")]
    public bool hasGreen;
    public bool hasYellow;
    public bool hasRed;
    public bool hasPurple;

    public bool Has(KeycardColor color)
    {
        return color switch
        {
            KeycardColor.Green => hasGreen,
            KeycardColor.Yellow => hasYellow,
            KeycardColor.Red => hasRed,
            KeycardColor.Purple => hasPurple,
            _ => false,
        };
    }

    public KeycardColor HighestOwned()
    {
        if (hasPurple) return KeycardColor.Purple;
        if (hasRed) return KeycardColor.Red;
        if (hasYellow) return KeycardColor.Yellow;
        if (hasGreen) return KeycardColor.Green;
        return KeycardColor.None;
    }

    public bool HasAccess(KeycardColor required, bool allowHigherKeys)
    {
        if (required == KeycardColor.None) return true;
        if (allowHigherKeys) return HighestOwned() >= required;
        return Has(required);
    }
}
