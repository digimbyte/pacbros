using UnityEngine;

public class autoHide : MonoBehaviour
{
    private void Awake()
    {
        // Disable any MeshRenderer components on this object and children so meshes are not rendered
        var renderers = GetComponentsInChildren<MeshRenderer>(includeInactive: true);
        foreach (var r in renderers)
        {
            if (r != null)
                r.enabled = false;
        }

        // Clear MeshFilter meshes on this object and children to fully disable mesh data
        var filters = GetComponentsInChildren<MeshFilter>(includeInactive: true);
        foreach (var f in filters)
        {
            if (f != null)
                f.sharedMesh = null;
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }
}
