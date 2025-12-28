using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TileAdjacencyAtlas", menuName = "PacBros/Rooms/Tile Adjacency Atlas", order = 12)]
public class TileAdjacencyAtlas : ScriptableObject
{
    [Min(1)] public int width = 4;
    [Min(1)] public int height = 4;

    public List<Cell> cells = new List<Cell>();

    // Placeables are non-tile objects you want to place into grid cells (spawns, items, etc).
    // They are stored separately so tile painting and adjacency baking stay tile-only.
    public List<PlaceableCell> placeables = new List<PlaceableCell>();

    public void Resize(int newWidth, int newHeight)
    {
        width = Mathf.Max(1, newWidth);
        height = Mathf.Max(1, newHeight);
        cells.RemoveAll(c => c.x < 0 || c.y < 0 || c.x >= width || c.y >= height);
        placeables.RemoveAll(p => p.x < 0 || p.y < 0 || p.x >= width || p.y >= height);
    }

    public void ExtendNorth(int rows)
    {
        rows = Mathf.Max(1, rows);
        height += rows;
    }

    public void ExtendSouth(int rows)
    {
        rows = Mathf.Max(1, rows);
        height += rows;
        // Shift all existing cells up by 'rows'
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            c.y += rows;
            cells[i] = c;
        }
        for (int i = 0; i < placeables.Count; i++)
        {
            var p = placeables[i];
            p.y += rows;
            placeables[i] = p;
        }
    }

    public void ExtendEast(int cols)
    {
        cols = Mathf.Max(1, cols);
        width += cols;
    }

    public void ExtendWest(int cols)
    {
        cols = Mathf.Max(1, cols);
        width += cols;
        // Shift all existing cells right by 'cols'
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            c.x += cols;
            cells[i] = c;
        }
        for (int i = 0; i < placeables.Count; i++)
        {
            var p = placeables[i];
            p.x += cols;
            placeables[i] = p;
        }
    }

    public void Crop()
    {
        if (cells == null || cells.Count == 0)
        {
            // No tiles: shrink to 1x1
            width = 1;
            height = 1;
            placeables.Clear();
            return;
        }

        // Find the bounding box of all placed tiles.
        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minY = int.MaxValue;
        int maxY = int.MinValue;

        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (c.tile == null) continue;
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y < minY) minY = c.y;
            if (c.y > maxY) maxY = c.y;
        }

        // If no valid tiles, shrink to 1x1.
        if (minX == int.MaxValue || maxX == int.MinValue || minY == int.MaxValue || maxY == int.MinValue)
        {
            width = 1;
            height = 1;
            cells.Clear();
            placeables.Clear();
            return;
        }

        int newWidth = (maxX - minX) + 1;
        int newHeight = (maxY - minY) + 1;

        // Shift all cells and placeables to origin.
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            c.x -= minX;
            c.y -= minY;
            cells[i] = c;
        }
        for (int i = 0; i < placeables.Count; i++)
        {
            var p = placeables[i];
            p.x -= minX;
            p.y -= minY;
            placeables[i] = p;
        }

        width = newWidth;
        height = newHeight;

        // Remove any placeables outside the new bounds (shouldn't happen, but defensive).
        placeables.RemoveAll(p => p.x < 0 || p.y < 0 || p.x >= width || p.y >= height);
    }

    public Cell GetCell(int x, int y)
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

    public bool HasCell(int x, int y)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].x == x && cells[i].y == y)
            {
                return true;
            }
        }
        return false;
    }

    public void SetCell(int x, int y, Tile tile, int rotationIndex = 0)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        int rot = NormalizeRot(rotationIndex);

        int index = cells.FindIndex(c => c.x == x && c.y == y);
        Cell cell = new Cell
        {
            x = x,
            y = y,
            tile = tile,
            rotationIndex = rot
        };

        if (tile == null)
        {
            if (index >= 0)
            {
                cells.RemoveAt(index);
            }
            // Note: placeables are stored separately and are not cleared here.
            return;
        }

        if (index >= 0)
        {
            cells[index] = cell;
        }
        else
        {
            cells.Add(cell);
        }
    }

    public PlaceableCell GetPlaceable(int x, int y)
    {
        for (int i = 0; i < placeables.Count; i++)
        {
            if (placeables[i].x == x && placeables[i].y == y)
            {
                return placeables[i];
            }
        }
        return default;
    }

    public bool HasPlaceable(int x, int y)
    {
        for (int i = 0; i < placeables.Count; i++)
        {
            if (placeables[i].x == x && placeables[i].y == y)
            {
                var p = placeables[i];
                return p.prefab != null || !string.Equals(p.kind, PlaceableKind.None, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(p.marker);
            }
        }
        return false;
    }

    public void SetPlaceable(
        int x,
        int y,
        GameObject prefab,
        int rotationIndex = 0,
        string kind = PlaceableKind.None,
        string marker = null,
        Color? markerColor = null)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;

        int rot = NormalizeRot(rotationIndex);
        int index = placeables.FindIndex(p => p.x == x && p.y == y);

        // If caller explicitly specifies `None`, remove any existing entry (treat as delete).
        if (kind == PlaceableKind.None)
        {
            if (index >= 0) placeables.RemoveAt(index);
            return;
        }

        // If no prefab, no marker, and kind is the default Prefab (common clear path), treat as delete.
        bool isEmptyDefaultClear = prefab == null && string.IsNullOrEmpty(marker) && kind == PlaceableKind.None;
        if (isEmptyDefaultClear)
        {
            if (index >= 0) placeables.RemoveAt(index);
            return;
        }

        PlaceableCell pcell = new PlaceableCell
        {
            x = x,
            y = y,
            prefab = prefab,
            rotationIndex = rot,
            kind = kind,
            marker = marker,
            markerColor = markerColor ?? Color.white
        };

        if (index >= 0)
            placeables[index] = pcell;
        else
            placeables.Add(pcell);
    }

    public static int NormalizeRot(int rot)
    {
        return ((rot % 4) + 4) % 4;
    }

    [Serializable]
    public struct Cell
    {
        public int x;
        public int y;
        public Tile tile;
        public int rotationIndex; // 0=0°,1=+90°,2=+180°,3=+270°
    }

    // Use string keys for placeable kinds so values can be data-matched reliably.
    // Keep constants names similar to previous enum members.
    public static class PlaceableKind
    {
        public const string None = "none";
        public const string SpawnPlayer = "SpawnPoint";
        public const string Enemy = "EnemySpawn";
        public const string Loot = "Loot";
        public const string Coin = "Coin";
        public const string Ammo = "Ammo";
    }

    [Serializable]
    public struct PlaceableCell
    {
        public int x;
        public int y;
        public GameObject prefab;
        public int rotationIndex; // 0=0°,1=+90°,2=+180°,3=+270°
        public string kind;
        public string marker;
        public Color markerColor;
    }
}

// (Placeable kinds are string constants in `PlaceableKind` above.)
