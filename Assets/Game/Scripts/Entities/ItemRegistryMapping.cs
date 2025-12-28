using System.Collections.Generic;

public static class ItemRegistryMapping
{
    static readonly Dictionary<ItemId, string> RegistryKeys = new()
    {
        { ItemId.KeycardGreen, "Key_Green" },
        { ItemId.KeycardYellow, "Key_Yellow" },
        { ItemId.KeycardRed, "Key_Red" },
        { ItemId.KeycardPurple, "Key_Purple" },
    };

    public static bool TryGetRegistryKey(ItemId item, out string registryKey)
    {
        return RegistryKeys.TryGetValue(item, out registryKey);
    }
}
