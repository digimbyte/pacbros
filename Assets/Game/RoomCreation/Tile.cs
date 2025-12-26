using UnityEngine;

public class Tile : MonoBehaviour
{
    [System.Serializable]
    public struct NeighborSpec
    {
        public Tile tile;
        [Range(0, 3)] public int rotation; // 0=0°,1=+90°,2=+180°,3=+270°
    }

    public enum TileType
    {
        Floor,
        Wall,
        Door,
        Portal,
        Border,
        BorderTunnel
    }

    [Header("Classification")]
    public TileType tileType = TileType.Floor;

    [Header("2D Preview")]
    public Texture2D preview;

    public NeighborSpec[] upNeighbours;
    public NeighborSpec[] rightNeighbours;
    public NeighborSpec[] downNeighbours;
    public NeighborSpec[] leftNeighbours;

    [Header("Blocked Neighbours (editor reference only)")]
    public NeighborSpec[] upBlockedNeighbours;
    public NeighborSpec[] rightBlockedNeighbours;
    public NeighborSpec[] downBlockedNeighbours;
    public NeighborSpec[] leftBlockedNeighbours;

    public bool IsWalkable => tileType == TileType.Floor
        || tileType == TileType.Door
        || tileType == TileType.Portal
        || tileType == TileType.BorderTunnel;
}