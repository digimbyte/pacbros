using UnityEngine;

[CreateAssetMenu(fileName = "TileDefinition", menuName = "PacBros/Rooms/Tile Definition", order = 12)]
public class TileDefinition : ScriptableObject
{
    [Tooltip("Reference to the Tile prefab/component used by generators/builders.")]
    public Tile tile;

    [Tooltip("Optional identifier for tooling.")]
    public string id;

    [Tooltip("Relative weight for random selection (currently unused by engine).")]
    public int weight = 1;
}
