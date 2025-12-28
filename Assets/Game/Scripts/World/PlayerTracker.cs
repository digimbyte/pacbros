using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks all PlayerEntity instances, records breadcrumb trails, and exposes player targets for AI.
/// Supports multiple simultaneous players and provides velocity estimates plus timed target rotation.
/// </summary>
public class PlayerTracker : MonoBehaviour
{
    public static PlayerTracker Instance { get; private set; }

    [Header("Breadcrumb Sampling")]
    [Tooltip("Seconds between breadcrumb samples per player.")]
    public float breadcrumbSampleInterval = 0.5f;
    [Tooltip("Minimum movement (meters) before a new breadcrumb is recorded.")]
    public float minBreadcrumbDistance = 0.35f;
    [Tooltip("Maximum number of breadcrumb entries stored per player.")]
    public int maxBreadcrumbsPerPlayer = 32;

    readonly List<PlayerEntity> _players = new();
    readonly Dictionary<PlayerEntity, Trail> _trails = new();

    class Trail
    {
        public readonly List<Vector3> breadcrumbs = new();
        public Vector3 lastPosition;
        public Vector3 velocity;
        public float lastSampleTime;
        public float nextSampleTime;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    void LateUpdate()
    {
        float now = Time.time;
        for (int i = _players.Count - 1; i >= 0; i--)
        {
            var player = _players[i];
            if (player == null || !player.isActiveAndEnabled)
            {
                Unregister(player);
                continue;
            }

            if (!_trails.TryGetValue(player, out var trail))
                continue;

            if (now < trail.nextSampleTime)
                continue;

            Vector3 pos = player.transform.position;
            float moved = (pos - trail.lastPosition).magnitude;
            if (trail.lastSampleTime <= 0f || moved >= minBreadcrumbDistance)
            {
                if (trail.breadcrumbs.Count >= maxBreadcrumbsPerPlayer)
                    trail.breadcrumbs.RemoveAt(0);
                trail.breadcrumbs.Add(pos);

                if (trail.lastSampleTime > 0f)
                {
                    float dt = now - trail.lastSampleTime;
                    if (dt > Mathf.Epsilon)
                        trail.velocity = (pos - trail.lastPosition) / dt;
                }

                trail.lastPosition = pos;
                trail.lastSampleTime = now;
            }

            trail.nextSampleTime = now + breadcrumbSampleInterval;
        }
    }

    public static PlayerTracker EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        Instance = FindObjectOfType<PlayerTracker>();
        if (Instance != null)
            return Instance;

        GameObject host = LevelRuntime.Active != null ? LevelRuntime.Active.gameObject : new GameObject("PlayerTracker");
        Instance = host.AddComponent<PlayerTracker>();
        return Instance;
    }

    public void Register(PlayerEntity player)
    {
        if (player == null || _players.Contains(player))
            return;

        _players.Add(player);
        _trails[player] = new Trail
        {
            lastPosition = player.transform.position,
            lastSampleTime = Time.time,
            nextSampleTime = Time.time + breadcrumbSampleInterval
        };
    }

    public void Unregister(PlayerEntity player)
    {
        if (player == null)
            return;

        _players.Remove(player);
        _trails.Remove(player);
    }

    public IReadOnlyList<PlayerEntity> Players => _players;

    public PlayerEntity GetRandomPlayer(PlayerEntity exclude = null)
    {
        int count = _players.Count;
        if (count == 0)
            return null;

        if (count == 1)
            return _players[0];

        const int maxAttempts = 4;
        for (int i = 0; i < maxAttempts; i++)
        {
            int idx = Random.Range(0, count);
            var candidate = _players[idx];
            if (candidate != null && candidate != exclude)
                return candidate;
        }

        // Fallback: return first valid even if it's the same as exclude
        for (int i = 0; i < count; i++)
        {
            var candidate = _players[i];
            if (candidate != null)
                return candidate;
        }

        return null;
    }

    public IReadOnlyList<Vector3> GetBreadcrumbs(PlayerEntity player)
    {
        if (player != null && _trails.TryGetValue(player, out var trail))
            return trail.breadcrumbs;
        return System.Array.Empty<Vector3>();
    }

    public Vector3 GetLastKnownPosition(PlayerEntity player)
    {
        if (player != null && _trails.TryGetValue(player, out var trail))
            return trail.lastPosition;
        return player != null ? player.transform.position : Vector3.zero;
    }

    public Vector3 GetVelocity(PlayerEntity player)
    {
        if (player != null && _trails.TryGetValue(player, out var trail))
            return trail.velocity;
        return Vector3.zero;
    }

    public bool HasPlayers => _players.Count > 0;
}
