using UnityEngine;

[CreateAssetMenu(menuName = "Game/MaterialTheme", fileName = "MaterialTheme")]
public class MaterialTheme : ScriptableObject
{
    [Tooltip("List of materials that form this theme. Materials must keep the same names as the originals so they can be swapped by name.")]
    public Material[] materials = new Material[0];
}
