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
        TryHandleTouch(other.gameObject);
    }

    void OnCollisionEnter(Collision collision)
    {
        TryHandleTouch(collision.gameObject);
    }

    void TryHandleTouch(GameObject other)
    {
        if (other == null) return;

        // Prefer component-based checks so different objects can be identified reliably.
        var pe = other.GetComponent<PlayerEntity>();
        if (pe != null && killPlayers)
        {
            HandleKillTarget(other);
            return;
        }

        var ee = other.GetComponent<EnemyEntity>();
        if (ee != null && killEnemies)
        {
            HandleKillTarget(other);
            return;
        }
    }

    void HandleKillTarget(GameObject target)
    {
        if (target == null) return;

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
}
