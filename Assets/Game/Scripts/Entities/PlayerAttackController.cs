using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerEntity))]
public class PlayerAttackController : MonoBehaviour
{
    [Header("Attack")]
    public float attackRange = 6f;
    public int ammoPerShot = 1;
    public LayerMask enemyLayers = ~0;
    public bool logHits;

    [Header("Projectile")]
    public string gunshotRegistryKey = "Gunshot";
    [Tooltip("How far in front of the player to spawn the projectile (meters).")]
    public float projectileSpawnOffset = 0.5f;

    PlayerEntity _player;
    bool _inputEnabled = true;

    void Awake()
    {
        _player = GetComponent<PlayerEntity>();
    }

    bool TryAcquireTarget(out Vector3 direction, out EnemyEntity enemy)
    {
        direction = Vector3.zero;
        float bestDistance = float.MaxValue;
        enemy = null;

        Vector3 origin = transform.position;
        Vector3[] dirs =
        {
            Vector3.right,   // east
            Vector3.left,    // west
            Vector3.forward, // north
            Vector3.back     // south
        };

        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 dir = dirs[i];
            if (Physics.Raycast(origin, dir, out RaycastHit hit, attackRange, enemyLayers, QueryTriggerInteraction.Ignore))
            {
                var hitEnemy = hit.collider != null ? hit.collider.GetComponentInParent<EnemyEntity>() : null;
                if (hitEnemy == null || hitEnemy.isDead)
                    continue;

                if (hit.distance < bestDistance)
                {
                    direction = dir;
                    bestDistance = hit.distance;
                    enemy = hitEnemy;
                }
            }
        }

        return direction != Vector3.zero;
    }

    void SpawnProjectile(Vector3 direction)
    {
        var runtime = LevelRuntime.Active;
        if (runtime == null)
            return;

        Vector3 spawnPos = transform.position + direction.normalized * Mathf.Max(0.1f, projectileSpawnOffset);
        Quaternion rotation = Quaternion.identity;

        if (direction == Vector3.right)
            rotation = Quaternion.identity;
        else if (direction == Vector3.left)
            rotation = Quaternion.Euler(0f, 180f, 0f);
        else if (direction == Vector3.forward)
            rotation = Quaternion.Euler(0f, 90f, 0f);
        else if (direction == Vector3.back)
            rotation = Quaternion.Euler(0f, -90f, 0f);
        else
            rotation = Quaternion.LookRotation(direction, Vector3.up);

        var projectile = runtime.InstantiateRegistryPrefab(gunshotRegistryKey, spawnPos, rotation);
        if (projectile == null && logHits)
            Debug.LogWarning($"PlayerAttackController[{name}] could not spawn projectile '{gunshotRegistryKey}'.");
    }

    void Update()
    {
        if (!_inputEnabled || _player == null || _player.isDead)
            return;

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.spaceKey.wasPressedThisFrame)
            TryAttack();
    }

    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;
    }

    void TryAttack()
    {
        if (_player.ammo < ammoPerShot || ammoPerShot <= 0)
            return;

        if (!TryAcquireTarget(out Vector3 direction, out EnemyEntity enemy))
            return;

        _player.ammo = Mathf.Max(0, _player.ammo - ammoPerShot);

        SpawnProjectile(direction);

        PlayerEventStack.Push(new PlayerEvent(PlayerEventType.Attack, _player, transform.position));

        if (logHits && enemy != null)
            Debug.Log($"PlayerAttackController[{name}] fired at '{enemy.name}' ({direction})", enemy);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
