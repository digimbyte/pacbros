using UnityEngine;

public class Cell : MonoBehaviour
{
    public bool collapsed { get; private set; }
    public Tile[] tileOptions { get; private set; }
    public Tile SelectedTile => collapsed && tileOptions != null && tileOptions.Length > 0 ? tileOptions[0] : null;
    public Vector2Int Coordinates { get; private set; }

    public void Initialize(Vector2Int coords, Tile[] options, bool startCollapsed = false)
    {
        Coordinates = coords;
        collapsed = startCollapsed;
        tileOptions = options;
    }

    public void RecreateCell(Tile[] options)
    {
        collapsed = false;
        tileOptions = options;
    }

    public void CollapseTo(Tile tile)
    {
        collapsed = true;
        tileOptions = new Tile[] { tile };
    }
}