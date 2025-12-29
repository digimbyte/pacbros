using System.Collections.Generic;

public static class ItemRegistryMapping
{
    static readonly Dictionary<ItemId, string> RegistryKeys = new()
    {
        { ItemId.Key_Green, "Key_Green" },
        { ItemId.Key_Yellow, "Key_Yellow" },
        { ItemId.Key_Red, "Key_Red" },
        { ItemId.Key_Purple, "Key_Purple" },
    };

    public static bool TryGetRegistryKey(ItemId item, out string registryKey)
    {
        return RegistryKeys.TryGetValue(item, out registryKey);
    }
}
