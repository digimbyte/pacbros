using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TilesDatabase", menuName = "PacBros/Rooms/Tiles Database", order = 10)]
public class TilesDatabase : ScriptableObject
{
    [Tooltip("Optional category label for grouping in editors.")]
    public string category = "Default";

    public List<Tile> tiles = new List<Tile>();
    public Tile defaultTile;

    public Tile GetTile(int index)
    {
        if (tiles == null || index < 0 || index >= tiles.Count)
        {
            return defaultTile;
        }
        return tiles[index];
    }
}
