using System.Collections.Generic;
using UnityEngine;

public class Trails : MonoBehaviour
{
    [Header("Smoke")]
    [Tooltip("ParticleSystem on this object used to emit smoke. Will be `Emit`-ed at the configured interval.")]
    public ParticleSystem smokeSystem;
    [Tooltip("Seconds between smoke emission bursts (while active).")]
    public float smokeInterval = 0.25f;
    [Tooltip("Kelvin threshold at or above which smoke starts looping (e.g. 5000).")]
    public int smokeStartKelvin = 5000;

    [Header("Fire")]
    [Tooltip("Registry key used with LevelRuntime.InstantiateRegistryPrefab to spawn fire prefabs.")]
    public string fireRegistryKey;
    [Tooltip("Heat stage index (0..8) at or above which fires will be spawned.")]
    public int fireStageThreshold = 5;
    [Tooltip("Kelvin threshold at or above which grid fire-trails are left while moving (e.g. 6000).")]
    public int fireTrailStartKelvin = 6000;
    [Tooltip("How many fire instances to spawn when threshold is reached.")]
    public int firesPerSpawn = 1;
    [Tooltip("Random radius (XZ) around this object to place spawned fires.")]
    public float fireSpawnRadius = 0.5f;

    float _smokeTimer;
    HashSet<int> _spawnedStages = new HashSet<int>();
    bool _smokeActive = false;
    bool _fireTrailActive = false;

    GridMotor _motor;
    Vector2Int _lastCell = new Vector2Int(int.MinValue, int.MinValue);
    float _minMoveSpeedToLeaveTrail = 0.1f;
    [Header("Spawn Timing")]
    [Tooltip("Delay (seconds) after leaving a tile before spawning fire there. Prevents spawning under actor.")]
    public float fireSpawnDelay = 0.2f;

    // Track pending and completed spawns to avoid duplicates.
    HashSet<Vector2Int> _pendingSpawnCells = new HashSet<Vector2Int>();
    HashSet<Vector2Int> _spawnedCells = new HashSet<Vector2Int>();
    [Tooltip("How many queued spawns to process per frame when catching up.")]
    public int spawnBatchSize = 8;

    struct PendingSpawn
    {
        public Vector2Int cell;
        public float readyTime;
        public Vector3 origin;
        public float cellSize;
    }

    List<PendingSpawn> _spawnQueue = new List<PendingSpawn>();
    Coroutine _spawnProcessorCoroutine;

    void Start()
    {
        // Initialise references and heat state.
        _motor = GetComponent<GridMotor>();

        // Ensure the smoke system is not playing on awake. We'll enable it from heat events.
        if (smokeSystem != null)
        {
            var main = smokeSystem.main;
            main.loop = false;
            smokeSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        HandleHeatChanged(Heat.GetHeatUnits());
    }

    void OnEnable()
    {
        Heat.OnStageChanged += HandleStageChanged;
        Heat.OnHeatUnitsChanged += HandleHeatChanged;
    }

    void OnDisable()
    {
        Heat.OnStageChanged -= HandleStageChanged;
        Heat.OnHeatUnitsChanged -= HandleHeatChanged;
    }

    void Update()
    {
        // Smoke emission: only when smoke is active (heat >= smokeStartKelvin)
        if (smokeSystem != null && _smokeActive)
        {
            _smokeTimer += Time.deltaTime;
            if (_smokeTimer >= smokeInterval)
            {
                _smokeTimer = 0f;
                SpawnSmoke();
            }
        }

        // Fire trail: when active and we have a motor, spawn on grid when entering a new cell while moving.
        if (_fireTrailActive && _motor != null)
        {
            // Compute current cell consistent with GridMotor.CurrentCell logic.
            Vector3 origin = _motor.EffectiveOrigin();
            float cellSize = Mathf.Max(0.0001f, _motor.cellSize);
            int cx = Mathf.RoundToInt((transform.position.x - origin.x) / cellSize);
            int cz = Mathf.RoundToInt((transform.position.z - origin.z) / cellSize);
            Vector2Int cur = new Vector2Int(cx, cz);
            if (cur != _lastCell)
            {
                // When entering a new cell, schedule delayed spawns for all cells we left
                // (including _lastCell and intermediates). We'll compute the path in between.
                if (_lastCell.x != int.MinValue)
                {
                    EnqueueCellsBetween(_lastCell, cur, origin, cellSize);
                }
                _lastCell = cur;
            }
        }
    }

    void SpawnSmoke()
    {
        // Emit a single burst from the configured particle system at this transform's position.
        // If the particle system is on another GameObject, it will still emit from its own transform.
        if (smokeSystem == null) return;
        smokeSystem.Emit(1);
    }

    void HandleStageChanged(int newStage)
    {
        // Keep existing stage-based single-time fire spawns (legacy behaviour).
        if (newStage < fireStageThreshold) return;
        if (_spawnedStages.Contains(newStage)) return;
        _spawnedStages.Add(newStage);
        SpawnFires(newStage);
    }

    void HandleHeatChanged(int kelvin)
    {
        bool wantSmoke = kelvin >= smokeStartKelvin;
        if (wantSmoke && !_smokeActive)
        {
            _smokeActive = true;
            if (smokeSystem != null)
            {
                var main = smokeSystem.main;
                main.loop = true;
                smokeSystem.Play();
            }
        }
        else if (!wantSmoke && _smokeActive)
        {
            _smokeActive = false;
            if (smokeSystem != null)
            {
                smokeSystem.Stop();
            }
        }

        bool wantTrail = kelvin >= fireTrailStartKelvin;
        // If we're transitioning into trail mode, immediately place fire on current tile
        if (wantTrail && !_fireTrailActive)
        {
            _fireTrailActive = true;
            Vector2Int cur = new Vector2Int(int.MinValue, int.MinValue);
            if (_motor != null)
            {
                Vector3 origin = _motor.EffectiveOrigin();
                float cellSize = Mathf.Max(0.0001f, _motor.cellSize);
                int cx = Mathf.RoundToInt((transform.position.x - origin.x) / cellSize);
                int cz = Mathf.RoundToInt((transform.position.z - origin.z) / cellSize);
                cur = new Vector2Int(cx, cz);
            }
            else if (LevelRuntime.Active != null)
            {
                Vector3 origin = LevelRuntime.Active.gridOrigin;
                float cellSize = Mathf.Max(0.0001f, LevelRuntime.Active.cellSize);
                int cx = Mathf.RoundToInt((transform.position.x - origin.x) / cellSize);
                int cz = Mathf.RoundToInt((transform.position.z - origin.z) / cellSize);
                cur = new Vector2Int(cx, cz);
            }

            if (cur.x != int.MinValue)
            {
                // Do NOT spawn on the current cell. Record it as the last cell so
                // when we leave it we'll spawn a fire there (the tile left).
                _lastCell = cur;
            }
            else
            {
                // fallback: reset so next movement will spawn
                _lastCell = new Vector2Int(int.MinValue, int.MinValue);
            }
        }
        else if (!wantTrail)
        {
            _fireTrailActive = false;
            // reset last cell so trail restarts when re-enabled
            _lastCell = new Vector2Int(int.MinValue, int.MinValue);
            // clear pending/spawned cells so a new trail starts fresh
            _pendingSpawnCells.Clear();
            _spawnedCells.Clear();
        }
    }

    void SpawnFires(int stage)
    {
        Transform parent = LevelRuntime.Active != null ? LevelRuntime.Active.entitiesRoot : null;

        for (int i = 0; i < Mathf.Max(1, firesPerSpawn); i++)
        {
            Vector3 offset = new Vector3(Random.Range(-fireSpawnRadius, fireSpawnRadius), 0f, Random.Range(-fireSpawnRadius, fireSpawnRadius));
            Vector3 pos = transform.position + offset;

            if (!string.IsNullOrEmpty(fireRegistryKey) && LevelRuntime.Active != null)
            {
                LevelRuntime.Active.InstantiateRegistryPrefab(fireRegistryKey, pos, Quaternion.identity, parent);
            }
            else
            {
                Debug.LogWarning($"Trails: no fire registry key assigned or LevelRuntime not available to spawn fires at heat stage {stage}.", this);
            }
        }
    }

    void SpawnFireAtCell(Vector2Int cell, Vector3 origin, float cellSize)
    {
        Vector3 pos = origin + new Vector3(cell.x * cellSize, 0f, cell.y * cellSize);
        Transform parent = LevelRuntime.Active != null ? LevelRuntime.Active.entitiesRoot : null;

        if (!string.IsNullOrEmpty(fireRegistryKey) && LevelRuntime.Active != null)
        {
            LevelRuntime.Active.InstantiateRegistryPrefab(fireRegistryKey, pos, Quaternion.identity, parent);
        }
        else
        {
            Debug.LogWarning($"Trails: no fire registry key assigned or LevelRuntime not available to leave grid fire at {cell}.", this);
        }
    }

    void EnqueueCellsBetween(Vector2Int a, Vector2Int b, Vector3 origin, float cellSize)
    {
        // Bresenham line between integer grid points a -> b inclusive.
        int x0 = a.x, y0 = a.y;
        int x1 = b.x, y1 = b.y;
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        var line = new List<Vector2Int>();
        while (true)
        {
            line.Add(new Vector2Int(x0, y0));
            if (x0 == x1 && y0 == y1) break;
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }

        // Enqueue all points from start..(end-1) — these are the tiles we left.
        for (int i = 0; i < line.Count - 1; i++)
        {
            var cell = line[i];
            if (_spawnedCells.Contains(cell) || _pendingSpawnCells.Contains(cell))
                continue;
            ScheduleSpawnForCell(cell, origin, cellSize);
        }
    }

    void ScheduleSpawnForCell(Vector2Int cell, Vector3 origin, float cellSize)
    {
        if (_spawnedCells.Contains(cell) || _pendingSpawnCells.Contains(cell))
            return;

        _pendingSpawnCells.Add(cell);
        _spawnQueue.Add(new PendingSpawn { cell = cell, readyTime = Time.time + fireSpawnDelay, origin = origin, cellSize = cellSize });
        if (_spawnProcessorCoroutine == null)
            _spawnProcessorCoroutine = StartCoroutine(ProcessSpawnQueue());
    }

    System.Collections.IEnumerator ProcessSpawnQueue()
    {
        while (_spawnQueue.Count > 0)
        {
            float now = Time.time;
            // Collect ready indices
            int processed = 0;
            for (int i = 0; i < _spawnQueue.Count && processed < spawnBatchSize; )
            {
                if (_spawnQueue[i].readyTime <= now)
                {
                    var p = _spawnQueue[i];
                    // spawn if still pending and not already spawned
                    if (_pendingSpawnCells.Contains(p.cell) && !_spawnedCells.Contains(p.cell))
                    {
                        SpawnFireAtCell(p.cell, p.origin, p.cellSize);
                        _spawnedCells.Add(p.cell);
                    }
                    _pendingSpawnCells.Remove(p.cell);
                    _spawnQueue.RemoveAt(i);
                    processed++;
                }
                else
                {
                    i++; // skip not-ready
                }
            }

            if (processed == 0)
            {
                // Nothing ready yet; wait a frame and reevaluate.
                yield return null;
            }
            else
            {
                // Yield a frame to avoid stalls; allow multiple batches to catch up.
                yield return null;
            }
        }
        _spawnProcessorCoroutine = null;
    }
}
