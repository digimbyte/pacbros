using UnityEngine;

public class GimbalDirection : MonoBehaviour
{
    [Tooltip("Target transform to rotate. If null, the component's own transform is used.")]
    public Transform target;

    [Tooltip("Absolute Z angle in degrees to set each frame (not additive).")]
    public float zAngle = 0f;

    [Tooltip("If true, rotate in local space; otherwise rotate in world space.")]
    public bool localSpace = true;

    void Start()
    {
        if (target == null)
            target = this.transform;
    }

    void Update()
    {
        if (target == null) return;
        if (localSpace)
        {
            Vector3 e = target.localEulerAngles;
            e.z = zAngle;
            target.localEulerAngles = e;
        }
        else
        {
            Vector3 e = target.eulerAngles;
            e.z = zAngle;
            target.eulerAngles = e;
        }
    }
}
