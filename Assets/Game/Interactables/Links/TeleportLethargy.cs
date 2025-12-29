using UnityEngine;

/// <summary>
/// Attach to an entity root to prevent immediate re-teleporting when moved into a portal/tunnel.
/// If absent, portal code will add this component automatically.
/// </summary>
public class TeleportLethargy : MonoBehaviour
{
    [Tooltip("Seconds after a teleport during which triggers will ignore this entity.")]
    public float lethargySeconds = 0.5f;

    // Time.time when last teleported. Initialize negative so IsRecentlyTeleported is false on start.
    public float lastTeleportTime = -10000f;
    // InstanceId of a portal/tunnel to ignore until the entity exits that trigger.
    public int ignoredPortalInstanceId = 0;

    public bool IsRecentlyTeleported()
    {
        return Time.time < lastTeleportTime + lethargySeconds;
    }

    public void MarkTeleportedNow()
    {
        lastTeleportTime = Time.time;
    }

    public void MarkTeleportedByPortal(int portalInstanceId)
    {
        ignoredPortalInstanceId = portalInstanceId;
        lastTeleportTime = Time.time;
    }

    public bool IsIgnoredByPortal(int portalInstanceId)
    {
        if (ignoredPortalInstanceId != portalInstanceId) return false;
        return Time.time < lastTeleportTime + lethargySeconds;
    }

    public void ClearIgnoredPortal(int portalInstanceId)
    {
        if (ignoredPortalInstanceId == portalInstanceId)
            ignoredPortalInstanceId = 0;
    }
}
