using System.Collections;
using UnityEngine;

[RequireComponent(typeof(PlayerEntity))]
[RequireComponent(typeof(GridMotor))]
public class PlayerLifeController : MonoBehaviour
{
    [Tooltip("Seconds to freeze movement/attack after death before teleporting to spawn.")]
    public float deathFreezeSeconds = 1f;

    [Tooltip("If true, log life-cycle events for debugging.")]
    public bool verboseLogging;

    PlayerEntity _player;
    GridMotor _motor;
    PlayerController _controller;
    PlayerAttackController _attack;

    PlayerSpawnPoint _spawnPoint;
    Vector3 _spawnPosition;
    Quaternion _spawnRotation = Quaternion.identity;
    bool _hasSpawnPosition;

    bool _handlingDeath;

    void Awake()
    {
        _player = GetComponent<PlayerEntity>();
        _motor = GetComponent<GridMotor>();
        _controller = GetComponent<PlayerController>();
        _attack = GetComponent<PlayerAttackController>();

        CacheSpawnFromRuntime();
    }

    void CacheSpawnFromRuntime()
    {
        var runtime = LevelRuntime.Active;
        if (runtime != null && runtime.lastPlayerSpawnPoint != null)
        {
            RegisterSpawnPoint(runtime.lastPlayerSpawnPoint);
        }
        else
        {
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _hasSpawnPosition = true;
        }
    }

    public void RegisterSpawnPoint(PlayerSpawnPoint spawnPoint)
    {
        if (spawnPoint == null)
            return;

        _spawnPoint = spawnPoint;
        _spawnPosition = spawnPoint.transform.position;
        _spawnRotation = spawnPoint.transform.rotation;
        _hasSpawnPosition = true;
        if (verboseLogging)
            Debug.Log($"PlayerLifeController[{name}] registered spawn '{spawnPoint.name}' at {_spawnPosition}", this);
    }

    void Update()
    {
        if (_player == null)
            return;

        if (_player.isDead && !_handlingDeath)
        {
            StartCoroutine(HandleDeathRoutine());
        }
    }

    IEnumerator HandleDeathRoutine()
    {
        _handlingDeath = true;
        SuspendInput();

        PlayerEventStack.Push(new PlayerEvent(PlayerEventType.Death, _player, transform.position));

        // Notify LevelRuntime only for the local player so external hooks (audio/UI)
        // can respond to the local player's death.
        if (_player != null && _player.isLocal && LevelRuntime.Active != null)
        {
            LevelRuntime.Active.NotifyLocalPlayerDeath(_player.gameObject);
        }

        if (deathFreezeSeconds > 0f)
            yield return new WaitForSeconds(deathFreezeSeconds);

        if (TryConsumeLife())
        {
            TeleportToSpawn();
            _player.isDead = false;
            ResumeInput();
            PlayerEventStack.Push(new PlayerEvent(PlayerEventType.Respawn, _player, transform.position));
        }
        else
        {
            PlayerEventStack.Push(new PlayerEvent(PlayerEventType.OutOfLives, _player, transform.position));
            // Notify LevelRuntime only for the local player so external hooks can respond.
            if (_player != null && _player.isLocal && LevelRuntime.Active != null)
            {
                LevelRuntime.Active.NotifyLocalPlayerOutOfLives(_player.gameObject);
            }
            if (verboseLogging)
                Debug.LogWarning($"PlayerLifeController[{name}] out of lives. Player remains disabled.", this);
        }

        _handlingDeath = false;
    }

    void TeleportToSpawn()
    {
        Vector3 targetPos = _hasSpawnPosition ? _spawnPosition : transform.position;
        Quaternion targetRot = _hasSpawnPosition ? _spawnRotation : transform.rotation;

        if (_motor != null)
        {
            _motor.Teleport(targetPos);
        }
        else
        {
            transform.position = targetPos;
        }

        transform.rotation = targetRot;
    }

    bool TryConsumeLife()
    {
        var runtime = LevelRuntime.Active;
        if (runtime == null || !runtime.enableRespawn)
            return true; // fallback: allow infinite respawns when runtime absent

        if (runtime.currentLives > 0)
        {
            runtime.currentLives = Mathf.Max(0, runtime.currentLives - 1);
            return true;
        }

        return false;
    }

    void SuspendInput()
    {
        if (_controller != null)
            _controller.enabled = false;

        if (_attack != null)
            _attack.SetInputEnabled(false);

        if (_motor != null)
            _motor.SetDesiredInput(Vector2.zero);
    }

    void ResumeInput()
    {
        if (_controller != null)
            _controller.enabled = true;

        if (_attack != null)
            _attack.SetInputEnabled(true);
    }
}
