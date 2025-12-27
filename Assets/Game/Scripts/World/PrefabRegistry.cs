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
        if (string.IsNullOrWhiteSpace(key) || _map == null) return null;
        _map.TryGetValue(key, out var prefab);
        return prefab;
    }
}
