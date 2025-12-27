using System;
using UnityEngine;

/// <summary>
/// Global heat manager.
/// Maintains heat in kelvin from 0..9000. Stage index is computed as floor(kelvin / 1000).
/// Use the static API to add/subtract heat from anywhere: Heat.AddHeat(deltaKelvin).
/// </summary>
public class Heat : MonoBehaviour
{
    // We have 8 usable stages (0..8). Keep StageCount=9 so Stage indices are 0..8.
    private const int StageCount = 9;
    private const int KelvinPerStage = 1000;
    private const int MinKelvin = 0;
    // Allow overages up to 9000 K even though stages only go 0..8 (8000).
    private const int MaxKelvin = 9000;
    private const int OverchargedThreshold = 8500;

    [Header("Heat Settings")]
    [Tooltip("Current heat in kelvin. Not quantized; valid range 0..9000.")]
    [Range(MinKelvin, MaxKelvin)]
    public int currentHeat = 0;

    [Space]
    [Tooltip("Per-stage targets. Index 0..8 correspond to stages 0..8. Targets will be enabled/disabled when the stage changes.")]
    public GameObject[] stageTargets = new GameObject[StageCount];

    [Tooltip("If true, all targets with index <= current stage will be enabled. Otherwise only the target matching the current stage is enabled.")]
    public bool enableAllUpToStage = false;

    [Space]
    [Tooltip("Target to enable when heat is over the overcharged threshold (8500 K).")]
    public GameObject overchargedTarget;
    

    // Static backing field used by the static API.
    private static int s_heatUnits = 0;

    // Optional instance reference (not required to use static API).
    private static Heat s_instance;

    /// <summary>
    /// Singleton-like access to the active Heat component (may be null if none in scene).
    /// </summary>
    public static Heat Instance => s_instance;

    // Events
    public static event Action<int> OnHeatUnitsChanged; // new units
    public static event Action<int> OnStageChanged; // new stage index 0..9

    #region Properties
    public static int HeatUnits
    {
        get => s_heatUnits;
        private set
        {
            int clamped = Mathf.Clamp(value, MinKelvin, MaxKelvin);
            if (s_heatUnits == clamped) return;
            int oldStage = Stage;
            s_heatUnits = clamped;
            OnHeatUnitsChanged?.Invoke(s_heatUnits);
            int newStage = Stage;
            if (newStage != oldStage)
                OnStageChanged?.Invoke(newStage);

            // Keep inspector-exposed field in sync so scene view reflects current heat.
            if (s_instance != null)
            {
                s_instance.currentHeat = s_heatUnits;
            }
        }
    }

    public static int Stage => Mathf.Clamp(s_heatUnits / KelvinPerStage, 0, StageCount - 1);
    #endregion

    private void Awake()
    {
        // If multiple Heat instances exist, last Awake wins as authoritative initializer.
        s_instance = this;
        // Initialize static backing field from inspector value.
        s_heatUnits = Mathf.Clamp(currentHeat, MinKelvin, MaxKelvin);
        // Ensure the inspector targets reflect the current stage immediately.
        UpdateStageTargets(Stage);
        UpdateOvercharged(s_heatUnits);
        
    }

    private void OnValidate()
    {
        currentHeat = Mathf.Clamp(currentHeat, MinKelvin, MaxKelvin);
    }

    private void OnEnable()
    {
        OnStageChanged += HandleStageChanged;
    }

    private void OnDisable()
    {
        OnStageChanged -= HandleStageChanged;
    }

    private int _lastObservedHeat = int.MinValue;

    // Only sync inspector slider into static heat while the game is running.
    private void LateUpdate()
    {
        if (!Application.isPlaying) return;

        // If inspector currentHeat was changed at runtime, push it into the static heat value.
        if (currentHeat != s_heatUnits)
        {
            HeatUnits = currentHeat; // will clamp and invoke events
        }

        // Poll every frame in case heat is changed from other systems without using the static API.
        if (s_heatUnits != _lastObservedHeat)
        {
            _lastObservedHeat = s_heatUnits;
            UpdateStageTargets(Stage);
            UpdateOvercharged(_lastObservedHeat);
        }
    }
    

    private void HandleStageChanged(int newStage)
    {
        UpdateStageTargets(newStage);
    }

    private void UpdateStageTargets(int activeStage)
    {
        if (stageTargets == null) return;
        int cappedStage = Mathf.Clamp(activeStage, 0, StageCount - 1);
        for (int i = 0; i < StageCount; i++)
        {
            bool shouldBeActive = enableAllUpToStage ? (i <= cappedStage) : (i == cappedStage);
            if (i < stageTargets.Length && stageTargets[i] != null)
            {
                stageTargets[i].SetActive(shouldBeActive);
            }
        }
    }

    private void UpdateOvercharged(int kelvin)
    {
        if (overchargedTarget == null) return;
        bool over = kelvin > OverchargedThreshold;
        overchargedTarget.SetActive(over);
    }

    // quantization removed — heat is not rounded to 1000 increments

    /// <summary>
    /// Add kelvin units (positive or negative). Returns the new quantized heat value.
    /// This is the universal static API.
    /// </summary>
    public static int AddHeat(int deltaKelvin)
    {
        int target = s_heatUnits + deltaKelvin;
        HeatUnits = target; // property will quantize and clamp and invoke events
        return s_heatUnits;
    }

    /// <summary>
    /// Add heat in stages (positive or negative). Each stage = 1000 K.
    /// </summary>
    public static int AddHeatStages(int deltaStages)
    {
        return AddHeat(deltaStages * KelvinPerStage);
    }

    /// <summary>
    /// Set heat explicitly (will be quantized).
    /// </summary>
    public static int SetHeat(int kelvin)
    {
        HeatUnits = kelvin;
        return s_heatUnits;
    }

    /// <summary>
    /// Return current heat units (0..8000).
    /// </summary>
    public static int GetHeatUnits() => s_heatUnits;

    /// <summary>
    /// Return current stage (0..8).
    /// </summary>
    public static int GetStage() => Stage;
}

