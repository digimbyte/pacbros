using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(PlayerEntity))]
public class PlayerAttackController : MonoBehaviour
{
    [Header("Attack")]
    public float attackRange = 6f;
    public int ammoPerShot = 1;
    public LayerMask enemyLayers = ~0;
    public bool logHits;
    public float attackCooldown = 0.5f;

    [Header("Projectile")]
    public string gunshotRegistryKey = "Gunshot";
    [Tooltip("How far in front of the player to spawn the projectile (meters).")]
    public float projectileSpawnOffset = 0.5f;

    PlayerEntity _player;
    bool _inputEnabled = true;
    float _lastAttackTime = -1f;
    bool _attackPressed = false;

    void Awake()
    {
        _player = GetComponent<PlayerEntity>();
    }

    bool TryAcquireTargets(out Vector3 shootDirection, out List<EntityIdentity> targets)
    {
        shootDirection = Vector3.zero;
        targets = new List<EntityIdentity>();

        var runtime = LevelRuntime.Active;
        if (runtime == null) return false;

        float cellSize = runtime.cellSize;
        Vector3 gridOrigin = runtime.gridOrigin;
        LayerMask wallMask = runtime.wallLayers;
        if (wallMask == 0)
            wallMask = LayerMask.GetMask("wall");
        if (wallMask == 0)
            wallMask = -1;

        Vector3 origin = transform.position;
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

        foreach (Vector3 dir in dirs)
        {
            // Scan along the axis
            for (float dist = cellSize; dist <= attackRange; dist += cellSize)
            {
                Vector3 checkPos = origin + dir * dist;
                // Snap to grid center
                Vector3 gridPos = new Vector3(
                    Mathf.Floor((checkPos.x - gridOrigin.x) / cellSize) * cellSize + gridOrigin.x + cellSize * 0.5f,
                    gridOrigin.y, // ground level
                    Mathf.Floor((checkPos.z - gridOrigin.z) / cellSize) * cellSize + gridOrigin.z + cellSize * 0.5f
                );

                // Check for wall at this position
                Vector3 halfExtents = new Vector3(cellSize * 0.4f, 1f, cellSize * 0.4f);
                Collider[] colliders = Physics.OverlapBox(gridPos, halfExtents, Quaternion.identity, wallMask, QueryTriggerInteraction.Ignore);
                if (colliders.Length > 0)
                {
                    // Wall found, stop scanning this axis
                    if (logHits) Debug.Log($"PlayerAttackController: Wall detected at {gridPos}, stopping scan for dir {dir}");
                    break;
                }

                // Check for entities at this position
                Collider[] entityColliders = Physics.OverlapSphere(gridPos, cellSize * 0.5f, ~0, QueryTriggerInteraction.Ignore);
                foreach (Collider col in entityColliders)
                {
                    if (col == null) continue;
                    var identity = EntityIdentityUtility.From(col.gameObject);
                    if (identity.IsValid && !identity.IsDead && identity.Transform != transform) // don't shoot self
                    {
                        targets.Add(identity);
                        if (shootDirection == Vector3.zero)
                            shootDirection = dir;
                    }
                }
            }
        }

        return targets.Count > 0;
    }

    void SpawnProjectile(Vector3 direction)
    {
        var runtime = LevelRuntime.Active;
        if (runtime == null)
            return;

        Vector3 spawnPos = transform.position + direction.normalized * Mathf.Max(0.1f, projectileSpawnOffset);
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);

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

        if (keyboard.spaceKey.isPressed && !_attackPressed && Time.time > _lastAttackTime + attackCooldown)
        {
            _attackPressed = true;
            TryAttack();
        }
        else if (!keyboard.spaceKey.isPressed)
        {
            _attackPressed = false;
        }
    }

    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;
    }

    void TryAttack()
    {
        if (_player.ammo < ammoPerShot || ammoPerShot <= 0)
            return;

        _lastAttackTime = Time.time;

        Vector3 direction = Vector3.forward; // default direction
        List<EntityIdentity> targets = new List<EntityIdentity>();

        if (!TryAcquireTargets(out Vector3 shootDir, out targets))
            return;

        direction = shootDir;

        _player.ammo = Mathf.Max(0, _player.ammo - ammoPerShot);

        SpawnProjectile(direction);

        PlayerEventStack.Push(new PlayerEvent(PlayerEventType.Attack, _player, transform.position));

        // Kill all detected targets
        foreach (var target in targets)
        {
            if (target.player != null)
                target.player.isDead = true;
            else if (target.enemy != null)
                target.enemy.isDead = true;

            if (logHits)
                Debug.Log($"PlayerAttackController[{name}] killed '{target.Name}'", target.Transform);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
