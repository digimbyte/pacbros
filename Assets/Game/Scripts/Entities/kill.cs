using UnityEngine;

/// <summary>
/// Kill zone helper.
/// Configure which entity types are affected (players / enemies) and whether this object
/// should self-destruct after a timeout or immediately after killing something.
/// </summary>
public class kill : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("If true, player entities (objects with `PlayerEntity`) are destroyed on touch.")]
    public bool killPlayers = true;
    [Tooltip("If true, enemy entities (objects with `EnemyEntity`) are destroyed on touch.")]
    public bool killEnemies = true;

    [Header("Behavior")]
    [Tooltip("If true, this kill object will destroy itself immediately after killing a target on touch.")]
    public bool destroySelfOnTouch = false;
    [Tooltip("If > 0, this kill object will automatically destroy itself after this many seconds from Start.")]
    public float selfDestructAfterSeconds = 0f;

    void Start()
    {
        if (selfDestructAfterSeconds > 0f)
            Destroy(gameObject, selfDestructAfterSeconds);
    }

    void OnTriggerEnter(Collider other)
    {
        TryHandleTouch(other.gameObject, transform.position, other.transform.position);
    }

    void OnCollisionEnter(Collision collision)
    {
        Vector3 contactPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position;
        TryHandleTouch(collision.gameObject, contactPoint, contactPoint);
    }

    void TryHandleTouch(GameObject other, Vector3 killPosition, Vector3 contactPosition)
    {
        if (other == null) return;

        // Prefer component-based checks so different objects can be identified reliably.
        var pe = other.GetComponent<PlayerEntity>();
        if (pe != null && killPlayers)
        {
            HandleKillTarget(other, killPosition, contactPosition);
            return;
        }

        var ee = other.GetComponent<EnemyEntity>();
        if (ee != null && killEnemies)
        {
            HandleKillTarget(other, killPosition, contactPosition);
            return;
        }
    }

    void HandleKillTarget(GameObject target, Vector3 killPosition, Vector3 contactPosition)
    {
        if (target == null) return;

        // Log the kill event
        string killHierarchy = GetFullHierarchyPath(gameObject);
        string targetHierarchy = GetFullHierarchyPath(target);
        Debug.Log($"Kill Event: KillObject='{killHierarchy}' killed Target='{targetHierarchy}' at position {contactPosition} (KillPos: {killPosition})");

        // Prefer marking the entity dead on its concrete entity component rather than destroying the GameObject.
        var identity = EntityIdentityUtility.From(target);
        if (identity.IsValid)
        {
            if (identity.player != null)
                identity.player.isDead = true;
            else if (identity.enemy != null)
                identity.enemy.isDead = true;
        }
        else if (destroySelfOnTouch == false)
        {
            // No entity component found; leave the target alone unless configured otherwise.
        }

        if (destroySelfOnTouch)
        {
            Destroy(gameObject);
        }
    }

    string GetFullHierarchyPath(GameObject obj)
    {
        if (obj == null) return "null";
        string path = obj.name;
        Transform parent = obj.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
}
