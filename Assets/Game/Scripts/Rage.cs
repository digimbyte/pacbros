using System;
using UnityEngine;
using Nova;

/// <summary>
/// Drives a UIBlock2D's RadialFill.FillAngle directly from global heat.
/// 0 heat → angleAtZero, kelvinFull heat → angleAtFull (default 0 → 0, 8000 → -75).
/// </summary>
[DefaultExecutionOrder(100)]
public class Rage : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("UIBlock2D owning the radial fill to drive from heat.")]
    public UIBlock2D block;

    [Tooltip("Optional UniversalPropertyAnimator whose duration will be driven by the energy mapping.")]
    public UniversalPropertyAnimator durationAnimator;

    [Header("Mapping")]
    [Tooltip("Kelvin value that corresponds to 'full rage' (max angle).")]
    public float kelvinFull = 8000f;

    [Tooltip("FillAngle at 0 heat.")]
    public float angleAtZero = 0f;

    [Tooltip("FillAngle at full heat (e.g. -75).")]
    public float angleAtFull = -75f;

    [Header("Energy Mapping")]    
    [Tooltip("Energy value at 0 heat (e.g. 5).")]
    public float energyAtZero = 5f;

    [Tooltip("Energy value at full heat (e.g. 0.2).")]
    public float energyAtFull = 0.2f;

    /// <summary>
    /// Current energy value mapped from heat (0..kelvinFull → energyAtZero..energyAtFull).
    /// This is updated every time Apply() runs.
    /// </summary>
    public float Energy { get; private set; } = 5f;

    private void OnEnable()
    {
        Heat.OnHeatUnitsChanged += HandleHeatChanged;
        Apply(Heat.GetHeatUnits());
    }

    private void OnDisable()
    {
        Heat.OnHeatUnitsChanged -= HandleHeatChanged;
    }

    private void LateUpdate()
    {
        // Ensure the UI is continuously driven and not left stale if heat is poked elsewhere.
        Apply(Heat.GetHeatUnits());
    }

    private void HandleHeatChanged(int kelvin)
    {
        Apply(kelvin);
    }

    private void Apply(int kelvin)
    {
        if (block == null)
        {
            return;
        }

        float t = kelvinFull > 0f ? Mathf.Clamp01(kelvin / kelvinFull) : 0f;

        // 1) Rage UI angle: 0..kelvinFull → angleAtZero..angleAtFull (0 → 0, 8000 → -75 by default).
        float angle = Mathf.Lerp(angleAtZero, angleAtFull, t);

        // RadialFill is a struct; modify a local copy, then assign it back.
        var rf = block.RadialFill;
        rf.FillAngle = angle;
        block.RadialFill = rf;

        // 2) Energy mapping: 0..kelvinFull → energyAtZero..energyAtFull (default 0 → 5, 8000 → 0.2).
        Energy = Mathf.Lerp(energyAtZero, energyAtFull, t);

        // If a UniversalPropertyAnimator is wired, drive its duration from Energy every frame.
        if (durationAnimator != null)
        {
            // UniversalPropertyAnimator enforces duration > 0 via [Min] attribute; do the same at runtime.
            durationAnimator.duration = Mathf.Max(0.0001f, Energy);
        }
    }
}
