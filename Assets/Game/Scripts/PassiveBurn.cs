using UnityEngine;

/// <summary>
/// Adds passive heat every time the shared Clock advances a whole second.
/// Heat added per second = tickHeat (0..20). Uses Heat.AddHeat().
/// </summary>
public class PassiveBurn : MonoBehaviour
{
    [Tooltip("Clock to read seconds from. If null, will search on the same GameObject.")]
    public Clock clock;

    [Tooltip("Heat added each time the clock's Seconds increments (0..200).")]
    [Range(0, 200)] public int tickHeat = 5;

    private int _lastSeconds;
    private bool _initialized;

    private void Awake()
    {
        if (clock == null)
        {
            clock = GetComponent<Clock>();
        }
    }

    private void OnEnable()
    {
        SyncSeconds();
    }

    private void OnValidate()
    {
        tickHeat = Mathf.Clamp(tickHeat, 0, 200);
    }

    private void Update()
    {
        if (clock == null)
        {
            return;
        }

        if (!_initialized)
        {
            SyncSeconds();
        }

        int current = clock.Seconds;
        if (current != _lastSeconds)
        {
            int delta = current - _lastSeconds;
            // If clock was reset backwards, just treat as 1 step to avoid huge negative.
            if (delta < 1) delta = 1;

            int add = tickHeat * delta;
            if (add != 0)
            {
                Heat.AddHeat(add);
            }

            _lastSeconds = current;
        }
    }

    private void SyncSeconds()
    {
        if (clock != null)
        {
            _lastSeconds = clock.Seconds;
            _initialized = true;
        }
    }
}
