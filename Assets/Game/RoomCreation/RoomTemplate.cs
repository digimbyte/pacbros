using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "RoomTemplate", menuName = "PacBros/Rooms/Room Template", order = 11)]
public class RoomTemplate : ScriptableObject
{
    [Min(1)] public int width = 4;
    [Min(1)] public int height = 4;
    public Vector2Int pivot = Vector2Int.zero;
    public List<RoomCell> cells = new List<RoomCell>();

    public void Resize(int newWidth, int newHeight)
    {
        width = Mathf.Max(1, newWidth);
        height = Mathf.Max(1, newHeight);
        cells.RemoveAll(c => c.x >= width || c.y >= height);
    }

    public RoomCell GetCell(int x, int y)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].x == x && cells[i].y == y)
            {
                return cells[i];
            }
        }
        return default;
    }

    public void SetCell(int x, int y, Tile tile, bool skipSpawn = false, bool spawnPoint = false, int rotationIndex = 0)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        int rot = ((rotationIndex % 4) + 4) % 4;

        int index = cells.FindIndex(c => c.x == x && c.y == y);
        RoomCell cell = new RoomCell
        {
            x = x,
            y = y,
            tile = tile,
            skipSpawn = skipSpawn,
            spawnPoint = spawnPoint,
            rotationIndex = rot
        };

        if (index >= 0)
        {
            cells[index] = cell;
        }
        else
        {
            cells.Add(cell);
        }
    }

    [Serializable]
    public struct RoomCell
    {
        public int x;
        public int y;
        public Tile tile;
        public bool skipSpawn;
        public bool spawnPoint;
        public int rotationIndex; // 0=0°,1=+90°,2=+180°,3=+270°
    }
}
