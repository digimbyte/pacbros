using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class WaveCollapseGeneration : MonoBehaviour
{
    [Serializable]
    public struct ForcedCell
    {
        public Vector2Int coords;
        public Tile tile;
        public bool skipSpawn;
    }

    [Header("Grid")]

    [Header("Generation Settings")]
    public bool allowBorderTunnels = true;

    // Result produced by this generator when run as a tool.
    public WaveCollapseEngine.Result lastResult;

    public event Action GenerationCompleted;

    // Generation API: caller constructs a WaveCollapseEngine.Config and passes it in.
    public WaveCollapseEngine.Result Generate(WaveCollapseEngine.Config cfg)
    {
        lastResult = WaveCollapseEngine.Generate(cfg);
        GenerationCompleted?.Invoke();
        return lastResult;
    }
}