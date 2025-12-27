using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PrefabRegistry", menuName = "PacBros/Registry/Prefab Registry", order = 20)]
public class PrefabRegistry : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string key;
        public GameObject prefab;
    }

    [Tooltip("List of key → prefab mappings.")]
    public List<Entry> entries = new List<Entry>();

    [Tooltip("Default/fallback prefab. Returned when a key is missing or maps to null.")]
    public GameObject defaultPrefab;

    Dictionary<string, GameObject> _map;

    public void BuildIndexIfNeeded()
    {
        if (_map != null) return;
        _map = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        if (entries == null) return;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (string.IsNullOrWhiteSpace(e.key) || e.prefab == null) continue;
            _map[e.key] = e.prefab; // last one wins
        }
    }

    public GameObject GetPrefab(string key)
    {
        BuildIndexIfNeeded();

        // Registry is authoritative: must always return a valid prefab or hard-error.
        GameObject ResolveOrFail()
        {
            if (defaultPrefab != null)
                return defaultPrefab;

            Debug.LogError($"PrefabRegistry '{name}' has no defaultPrefab assigned but GetPrefab was called with key='{key}'." , this);
            throw new InvalidOperationException($"PrefabRegistry '{name}' has no default prefab configured.");
        }

        if (_map == null)
            return ResolveOrFail();

        if (string.IsNullOrWhiteSpace(key))
            return ResolveOrFail();

        if (_map.TryGetValue(key, out var prefab) && prefab != null)
            return prefab;

        return ResolveOrFail();
    }
}
