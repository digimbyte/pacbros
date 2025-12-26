using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class WaveCollapseEngine
{
    [Serializable]
    public struct ForcedCell
    {
        public Vector2Int coords;
        public Tile tile;
        public bool skipSpawn;

        // Rotation is 0..3 (90° steps). This is in the same frame used by the TileNeighborEditor.
        [Range(0, 3)] public int rotation;

        // If true, this forced cell is fully collapsed (tile+rotation).
        // If false, the cell is forced to the tile, but WFC will still resolve the rotation.
        public bool lockRotation;
    }

    public struct Config
    {
        public int width;
        public int height;
        public int seed;
        public Tile[] tileSet;
        public Tile[] borderTiles;
        public Tile[] borderTunnelTiles;
        public Tile fallbackTile;
        public List<ForcedCell> forcedCells;
        public bool allowBorderTunnels;
        // -1 = unlimited
        public int maxDoorCount;
        public int maxBorderTunnelCount;
        public int maxPortalCount;
    }

    public struct CellResult
    {
        public Vector2Int coords;
        public Tile tile;
        public bool skipSpawn;
        public int rotation;
    }

    public struct Result
    {
        public int width;
        public int height;
        public CellResult[] cells; // length == width*height
    }

    // Pure algorithm: returns layout data, no scene interaction.
    public static Result Generate(Config cfg)
    {
        var run = new IncrementalRun(cfg);
        run.Initialize();
        while (run.Step()) { }
        return run.BuildResult();
    }

    // Incremental stepping API for the WFC algorithm. This holds mutable state and can be stepped.
    public sealed class IncrementalRun
    {
        private readonly Config cfg;
        private readonly System.Random rng;

        private int width;
        private int height;
        private CellData[] grid;
        private HashSet<Vector2Int> skipSpawn;

        private Tile[] tileSet;
        private Tile[] borderTiles;
        private Tile[] borderTunnelTiles;
        private Tile fallback;

        private int doorCount;
        private int borderTunnelCount;
        private int portalCount;

        private int safeGuard;

        public bool IsInitialized { get; private set; }
        public bool IsComplete { get; private set; }
        public int StepIndex { get; private set; } // number of collapse-steps performed

        public IncrementalRun(Config cfg)
        {
            this.cfg = cfg;
            rng = new System.Random(cfg.seed);
        }

        public void Initialize()
        {
            if (IsInitialized) return;

            width = Math.Max(1, cfg.width);
            height = Math.Max(1, cfg.height);

            grid = new CellData[width * height];
            skipSpawn = new HashSet<Vector2Int>();

            tileSet = cfg.tileSet ?? Array.Empty<Tile>();
            borderTiles = cfg.borderTiles ?? Array.Empty<Tile>();
            borderTunnelTiles = cfg.borderTunnelTiles ?? Array.Empty<Tile>();
            fallback = cfg.fallbackTile;

            // count forced placements first to initialize type counts
            doorCount = 0;
            borderTunnelCount = 0;
            portalCount = 0;
            if (cfg.forcedCells != null)
            {
                foreach (var fc in cfg.forcedCells)
                {
                    if (fc.tile == null) continue;
                    if (fc.tile.tileType == Tile.TileType.Door) doorCount++;
                    if (fc.tile.tileType == Tile.TileType.BorderTunnel) borderTunnelCount++;
                    if (fc.tile.tileType == Tile.TileType.Portal) portalCount++;
                }
            }

            // initialize options
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = x + y * width;
                    bool onBorder = IsBorderCell(x, y, width, height);
                    Tile[] options = onBorder
                        ? (cfg.allowBorderTunnels ? ConcatArrays(borderTiles, borderTunnelTiles) : borderTiles)
                        : tileSet;

                    if (options == null || options.Length == 0)
                    {
                        // Never allow Border tiles on interior cells, even as fallback.
                        options = (fallback != null && (onBorder || fallback.tileType != Tile.TileType.Border))
                            ? new Tile[] { fallback }
                            : tileSet;
                    }

                    // build options as (tile,rotation) pairs
                    var opts = new List<Option>();
                    if (options != null)
                    {
                        foreach (var t in options)
                        {
                            if (t == null) continue;
                            for (int r = 0; r < 4; r++)
                            {
                                opts.Add(new Option { tile = t, rotation = r });
                            }
                        }
                    }

                    // Border tiles are forbidden on interior cells.
                    if (!onBorder)
                    {
                        opts.RemoveAll(o => o.tile != null && o.tile.tileType == Tile.TileType.Border);
                    }

                    // enforce initial type limits (do not allow options that would exceed the max)
                    FilterOptionsByTypeLimits(opts, doorCount, borderTunnelCount, portalCount, cfg.maxDoorCount, cfg.maxBorderTunnelCount, cfg.maxPortalCount);

                    grid[idx] = new CellData
                    {
                        coords = new Vector2Int(x, y),
                        collapsed = false,
                        options = opts.ToArray()
                    };
                }
            }

            // apply forced cells
            if (cfg.forcedCells != null)
            {
                foreach (var fc in cfg.forcedCells)
                {
                    if (fc.coords.x < 0 || fc.coords.x >= width || fc.coords.y < 0 || fc.coords.y >= height)
                        continue;

                    // Border tiles are forbidden on interior cells.
                    if (fc.tile != null && fc.tile.tileType == Tile.TileType.Border && !IsBorderCell(fc.coords.x, fc.coords.y, width, height))
                    {
                        continue;
                    }

                    int idx = fc.coords.x + fc.coords.y * width;
                    if (fc.tile != null)
                    {
                        grid[idx].isForced = true;
                        grid[idx].forcedTile = fc.tile;

                        int rot = ((fc.rotation % 4) + 4) % 4;
                        if (fc.lockRotation)
                        {
                            var sel = new Option { tile = fc.tile, rotation = rot };
                            grid[idx].options = new Option[] { sel };
                            grid[idx].collapsed = true;
                            grid[idx].selected = sel;
                        }
                        else
                        {
                            // Forced tile, rotation resolved by WFC.
                            var list = new List<Option>(4);
                            for (int r = 0; r < 4; r++) list.Add(new Option { tile = fc.tile, rotation = r });
                            grid[idx].options = list.ToArray();
                            grid[idx].collapsed = false;
                            grid[idx].selected = null;
                        }
                    }

                    if (fc.skipSpawn)
                        skipSpawn.Add(fc.coords);
                }
            }

            // Prefill/collapse the outer border ring BEFORE any wave collapse stepping.
            // This matches the "pad borders, then run WFC on the interior" workflow.
            PrefillBorders();

            // Enforce caps explicitly (no fallback/relaxation) then propagate constraints.
            var initQueue = new Queue<int>(grid.Length);
            for (int i = 0; i < grid.Length; i++) initQueue.Enqueue(i);
            ApplyTypeLimitPruning(initQueue);
            PropagateQueue(initQueue);

            safeGuard = 1000;
            IsInitialized = true;
            IsComplete = !HasAnySelectableCell() || safeGuard <= 0;
        }

        // Performs exactly one collapse + local propagation iteration.
        // Returns true if a step was performed.
        public bool Step()
        {
            if (!IsInitialized) Initialize();
            if (IsComplete) return false;
            if (safeGuard-- <= 0)
            {
                IsComplete = true;
                return false;
            }

            // Find lowest *explicit* entropy among cells that still have at least one option that
            // is compatible with *all* of its neighbors' current option-sets.
            // If a cell has 0 valid options, we leave it blank (no fallback).
            int lowest = int.MaxValue;
            var entropies = new int[grid.Length];
            for (int i = 0; i < grid.Length; i++) entropies[i] = int.MaxValue;

            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i].collapsed) continue;
                if (grid[i].options == null || grid[i].options.Length == 0) continue;

                int e = ComputeExplicitEntropy(i);
                entropies[i] = e;
                if (e > 0 && e < lowest) lowest = e;
            }

            if (lowest == int.MaxValue)
            {
                // No selectable cells remain (either complete or contradictions everywhere).
                IsComplete = true;
                return false;
            }

            // Collect candidates with that lowest explicit entropy.
            var candidates = new List<int>();
            for (int i = 0; i < grid.Length; i++)
            {
                if (!grid[i].collapsed && entropies[i] == lowest)
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                IsComplete = true;
                return false;
            }

            // Tie-break: pick randomly among lowest-entropy ties (seeded RNG) to avoid scan-order bias.
            int chosenIdx = candidates[rng.Next(candidates.Count)];

            // Choose from the options that are currently valid against all neighbors.
            var valid = GetValidOptionsForCell(chosenIdx);
            if (valid.Count == 0)
            {
                // Explicit: no fallback. Leave blank.
                grid[chosenIdx].options = Array.Empty<Option>();
                grid[chosenIdx].collapsed = true;
                grid[chosenIdx].selected = null;
                StepIndex++;
                IsComplete = !HasAnySelectableCell() || safeGuard <= 0;
                return true;
            }

            Option sel = valid[rng.Next(valid.Count)];
            grid[chosenIdx].collapsed = true;
            grid[chosenIdx].selected = sel;
            grid[chosenIdx].options = new Option[] { sel };

            if (sel.tile != null)
            {
                if (sel.tile.tileType == Tile.TileType.Door) doorCount++;
                if (sel.tile.tileType == Tile.TileType.BorderTunnel) borderTunnelCount++;
                if (sel.tile.tileType == Tile.TileType.Portal) portalCount++;
            }

            // Explicit pruning (no relaxation) then propagate.
            var q = new Queue<int>();
            q.Enqueue(chosenIdx);
            ApplyTypeLimitPruning(q);
            PropagateQueue(q);

            StepIndex++;
            IsComplete = !HasAnySelectableCell() || safeGuard <= 0;
            return true;
        }

        public Result BuildResult()
        {
            if (!IsInitialized) Initialize();
            var result = new Result { width = width, height = height, cells = new CellResult[width * height] };
            for (int i = 0; i < grid.Length; i++)
            {
                var gd = grid[i];
                result.cells[i] = new CellResult
                {
                    coords = gd.coords,
                    tile = gd.selected?.tile,
                    skipSpawn = skipSpawn.Contains(gd.coords),
                    rotation = gd.selected?.rotation ?? 0
                };
            }
            return result;
        }

        private void PrefillBorders()
        {
            // Collapse the entire outer ring to Border tiles.
            // NOTE: border tunnels are expected to be placed via cfg.forcedCells.

            // Find a default border tile (first non-null TileType.Border)
            Tile borderTile = null;
            for (int i = 0; i < borderTiles.Length; i++)
            {
                var t = borderTiles[i];
                if (t != null && t.tileType == Tile.TileType.Border)
                {
                    borderTile = t;
                    break;
                }
            }

            if (borderTile == null)
            {
                return;
            }

            // Collect border cell indices that are not already forced or already collapsed.
            var borderIndices = new List<int>();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!IsBorderCell(x, y, width, height))
                        continue;

                    int idx = x + y * width;
                    if (grid[idx].isForced || grid[idx].collapsed)
                        continue;

                    borderIndices.Add(idx);
                }
            }

            // Collapse all border cells to the border tile (single option, fixed rotation).
            for (int i = 0; i < borderIndices.Count; i++)
            {
                int idx = borderIndices[i];
                var sel = new Option { tile = borderTile, rotation = 0 };
                grid[idx].options = new Option[] { sel };
                grid[idx].collapsed = true;
                grid[idx].selected = sel;
            }
        }

        private bool HasAnySelectableCell()
        {
            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i].collapsed) continue;
                if (grid[i].options == null || grid[i].options.Length == 0) continue;
                if (ComputeExplicitEntropy(i) > 0) return true;
            }
            return false;
        }

        private int ComputeExplicitEntropy(int index)
        {
            // "True" entropy here means: count of options that have at least one compatible
            // option in each neighbor direction, considering BOTH tiles' neighbor specs and rotations.
            if (grid[index].options == null || grid[index].options.Length == 0) return 0;

            int valid = 0;
            for (int i = 0; i < grid[index].options.Length; i++)
            {
                var opt = grid[index].options[i];
                if (opt.tile == null) continue;

                bool okAll = true;
                foreach (Direction d in Enum.GetValues(typeof(Direction)))
                {
                    if (!TryGetNeighborIndex(index, d, out int nidx)) continue;
                    if (!HasAnyCompatibleNeighbor(opt, d, grid[nidx].options))
                    {
                        okAll = false;
                        break;
                    }
                }

                if (okAll) valid++;
            }
            return valid;
        }

        private List<Option> GetValidOptionsForCell(int index)
        {
            var result = new List<Option>();
            if (grid[index].options == null) return result;

            for (int i = 0; i < grid[index].options.Length; i++)
            {
                var opt = grid[index].options[i];
                if (opt.tile == null) continue;

                bool okAll = true;
                foreach (Direction d in Enum.GetValues(typeof(Direction)))
                {
                    if (!TryGetNeighborIndex(index, d, out int nidx)) continue;
                    if (!HasAnyCompatibleNeighbor(opt, d, grid[nidx].options))
                    {
                        okAll = false;
                        break;
                    }
                }

                if (okAll) result.Add(opt);
            }

            return result;
        }

        private void ApplyTypeLimitPruning(Queue<int> enqueueIfChanged)
        {
            // Explicit: if a max is reached, remove those tile-types from all non-forced, non-collapsed cells.
            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i].collapsed) continue;
                if (grid[i].isForced) continue; // forced tiles reserve their type slot
                if (grid[i].options == null || grid[i].options.Length == 0) continue;

                bool changed = false;
                var list = new List<Option>(grid[i].options);

                if (cfg.maxDoorCount >= 0 && doorCount >= cfg.maxDoorCount)
                {
                    changed |= list.RemoveAll(o => o.tile != null && o.tile.tileType == Tile.TileType.Door) > 0;
                }
                if (cfg.maxBorderTunnelCount >= 0 && borderTunnelCount >= cfg.maxBorderTunnelCount)
                {
                    changed |= list.RemoveAll(o => o.tile != null && o.tile.tileType == Tile.TileType.BorderTunnel) > 0;
                }
                if (cfg.maxPortalCount >= 0 && portalCount >= cfg.maxPortalCount)
                {
                    changed |= list.RemoveAll(o => o.tile != null && o.tile.tileType == Tile.TileType.Portal) > 0;
                }

                if (changed)
                {
                    grid[i].options = list.ToArray();
                    enqueueIfChanged.Enqueue(i);
                }
            }
        }

        private void PropagateQueue(Queue<int> q)
        {
            // Queue-based constraint propagation (explicit, no relaxation).
            int guard = Math.Max(64, width * height * 16);
            while (q.Count > 0 && guard-- > 0)
            {
                int src = q.Dequeue();
                if (src < 0 || src >= grid.Length) continue;
                if (grid[src].options == null || grid[src].options.Length == 0) continue;

                foreach (Direction d in Enum.GetValues(typeof(Direction)))
                {
                    if (!TryGetNeighborIndex(src, d, out int nidx)) continue;

                    // Constrain neighbor against src.
                    Direction dirFromNeighborToSrc = Opposite(d);
                    if (ConstrainCellAgainstNeighbor(nidx, src, dirFromNeighborToSrc))
                    {
                        q.Enqueue(nidx);
                    }
                }
            }
        }

        private bool ConstrainCellAgainstNeighbor(int cellIndex, int neighborIndex, Direction dirFromCellToNeighbor)
        {
            if (grid[cellIndex].collapsed) return false;
            if (grid[cellIndex].options == null || grid[cellIndex].options.Length == 0) return false;
            if (grid[neighborIndex].options == null || grid[neighborIndex].options.Length == 0) return false;

            var original = grid[cellIndex].options;
            var kept = new List<Option>(original.Length);

            for (int i = 0; i < original.Length; i++)
            {
                var opt = original[i];
                if (opt.tile == null) continue;

                bool ok = false;
                for (int j = 0; j < grid[neighborIndex].options.Length; j++)
                {
                    var nopt = grid[neighborIndex].options[j];
                    if (nopt.tile == null) continue;
                    if (AreCompatible(opt, nopt, dirFromCellToNeighbor))
                    {
                        ok = true;
                        break;
                    }
                }

                if (ok) kept.Add(opt);
            }

            if (kept.Count == original.Length) return false;

            grid[cellIndex].options = kept.ToArray();
            // If options become empty, we leave it blank (do not refill).
            return true;
        }

        private bool TryGetNeighborIndex(int index, Direction direction, out int nidx)
        {
            int x = index % width;
            int y = index / width;
            int nx = x, ny = y;
            switch (direction)
            {
                case Direction.Up: ny = y + 1; break;
                case Direction.Right: nx = x + 1; break;
                case Direction.Down: ny = y - 1; break;
                case Direction.Left: nx = x - 1; break;
            }

            if (nx < 0 || nx >= width || ny < 0 || ny >= height)
            {
                nidx = -1;
                return false;
            }

            nidx = nx + ny * width;
            return true;
        }

        private bool HasAnyCompatibleNeighbor(Option opt, Direction dirFromCellToNeighbor, Option[] neighborOptions)
        {
            if (neighborOptions == null || neighborOptions.Length == 0) return false;
            for (int i = 0; i < neighborOptions.Length; i++)
            {
                var nopt = neighborOptions[i];
                if (nopt.tile == null) continue;
                if (AreCompatible(opt, nopt, dirFromCellToNeighbor)) return true;
            }
            return false;
        }
    }

    // ----- helpers & internal types -----
    private class CellData
    {
        public Vector2Int coords;
        public bool collapsed;
        public Option[] options;
        public Option? selected;

        // Forced cells reserve their tile-type count and must not be overwritten by border fill.
        public bool isForced;
        public Tile forcedTile;
    }

    private struct Option
    {
        public Tile tile;
        public int rotation;
    }

    private enum Direction { Up, Right, Down, Left }

    private static bool IsBorderCell(int x, int y, int width, int height)
    {
        return x == 0 || y == 0 || x == width - 1 || y == height - 1;
    }

    private static Direction RotateDirection(Direction d, int rot)
    {
        // rot: 0..3, where +1 means rotate 90 degrees clockwise in grid-space (Up->Right).
        int di = (int)d;
        int r = ((rot % 4) + 4) % 4;
        return (Direction)(((di + r) % 4 + 4) % 4);
    }

    private static Tile.NeighborSpec[] GetNeighborSpecsForOption(Tile tile, int optionRotation, Direction worldDirection)
    {
        if (tile == null) return null;

        // Convert the world direction into the tile's "rotation 0" frame.
        // If the tile is rotated +r, then worldDirection corresponds to baseDirection rotated by -r.
        Direction baseDir = RotateDirection(worldDirection, -optionRotation);
        return baseDir switch
        {
            Direction.Up => tile.upNeighbours,
            Direction.Right => tile.rightNeighbours,
            Direction.Down => tile.downNeighbours,
            Direction.Left => tile.leftNeighbours,
            _ => null
        };
    }

    private static Direction Opposite(Direction dir)
    {
        return dir switch
        {
            Direction.Up => Direction.Down,
            Direction.Down => Direction.Up,
            Direction.Left => Direction.Right,
            Direction.Right => Direction.Left,
            _ => Direction.Up
        };
    }

    private static bool AreCompatible(Option a, Option b, Direction dirFromAToB)
    {
        if (a.tile == null || b.tile == null) return false;

        // Normalize rotations.
        int aRot = ((a.rotation % 4) + 4) % 4;
        int bRot = ((b.rotation % 4) + 4) % 4;

        // Compatibility is explicit and bidirectional:
        // - A must allow B in dirFromAToB, accounting for both rotations.
        // - B must allow A in the opposite direction, accounting for both rotations.
        return Allows(a.tile, aRot, b.tile, bRot, dirFromAToB)
            && Allows(b.tile, bRot, a.tile, aRot, Opposite(dirFromAToB));
    }

    private static bool Allows(Tile srcTile, int srcRot, Tile neighborTile, int neighborRot, Direction dirFromSrcToNeighbor)
    {
        if (srcTile == null || neighborTile == null) return false;

        Tile.NeighborSpec[] specs = GetNeighborSpecsForOption(srcTile, srcRot, dirFromSrcToNeighbor);
        if (specs == null || specs.Length == 0) return false;

        int nRot = ((neighborRot % 4) + 4) % 4;
        for (int i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];
            if (spec.tile == null || spec.tile != neighborTile) continue;
            int requiredNeighborRot = ((spec.rotation + srcRot) % 4 + 4) % 4;
            if (requiredNeighborRot == nRot) return true;
        }
        return false;
    }

    private static void ApplyDirectionConstraint(CellData[] grid, int width, int height, int index, Direction direction, List<Option> options)
    {
        int x = index % width;
        int y = index / width;
        int nx = x, ny = y;
        switch (direction)
        {
            case Direction.Up: ny = y + 1; break;
            case Direction.Right: nx = x + 1; break;
            case Direction.Down: ny = y - 1; break;
            case Direction.Left: nx = x - 1; break;
        }
        if (nx < 0 || nx >= width || ny < 0 || ny >= height) return;
        int nidx = nx + ny * width;
        var neighbor = grid[nidx];
        if (neighbor == null || neighbor.options == null || neighbor.options.Length == 0) return;

        // Build a set of valid neighbor (tile, rotation) pairs for fast lookup
        var validNeighbors = new HashSet<(Tile, int)>();
        foreach (var nopt in neighbor.options)
        {
            if (nopt.tile != null)
                validNeighbors.Add((nopt.tile, nopt.rotation));
        }

        // For each candidate option (tile+rotation) keep it only if at least one
        // neighbor option is compatible based on neighbour specs and rotations.
        int writeIdx = 0;
        for (int i = 0; i < options.Count; i++)
        {
            var opt = options[i];
            if (opt.tile == null)
                continue;

            bool ok = false;

            // IMPORTANT: the allowed neighbor set depends on this option's rotation.
            // We fetch the correct base-direction list, then rotate the *neighbor rotation requirements*
            // into world rotation space by +optionRotation.
            Tile.NeighborSpec[] baseSpecs = GetNeighborSpecsForOption(opt.tile, opt.rotation, direction);
            if (baseSpecs != null)
            {
                for (int j = 0; j < baseSpecs.Length && !ok; j++)
                {
                    var spec = baseSpecs[j];
                    if (spec.tile == null) continue;

                    int requiredNeighborRot = ((spec.rotation + opt.rotation) % 4 + 4) % 4;
                    if (validNeighbors.Contains((spec.tile, requiredNeighborRot)))
                    {
                        ok = true;
                    }
                }
            }

            if (ok)
            {
                options[writeIdx++] = opt;
            }
        }
        options.RemoveRange(writeIdx, options.Count - writeIdx);
    }

    private static void FilterOptionsByTypeLimits(List<Option> options, int currentDoorCount, int currentBorderTunnelCount, int currentPortalCount, int maxDoorCount, int maxBorderTunnelCount, int maxPortalCount)
    {
        if (options == null || options.Count == 0) return;
        if (maxDoorCount >= 0 && currentDoorCount >= maxDoorCount)
        {
            options.RemoveAll(o => o.tile != null && o.tile.tileType == Tile.TileType.Door);
        }
        if (maxBorderTunnelCount >= 0 && currentBorderTunnelCount >= maxBorderTunnelCount)
        {
            options.RemoveAll(o => o.tile != null && o.tile.tileType == Tile.TileType.BorderTunnel);
        }
        if (maxPortalCount >= 0 && currentPortalCount >= maxPortalCount)
        {
            options.RemoveAll(o => o.tile != null && o.tile.tileType == Tile.TileType.Portal);
        }
    }

    private static bool AllCollapsed(CellData[] grid)
    {
        foreach (var c in grid) if (!c.collapsed) return false;
        return true;
    }

    private static bool SequenceEqual(List<Tile> a, Tile[] b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Count != b.Length) return false;
        for (int i = 0; i < b.Length; i++)
        {
            if (!Equals(a[i], b[i])) return false;
        }
        return true;
    }

    private static bool SequenceEqualOptions(List<Option> a, Option[] b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Count != b.Length) return false;
        for (int i = 0; i < b.Length; i++)
        {
            if (!Equals(a[i].tile, b[i].tile) || a[i].rotation != b[i].rotation) return false;
        }
        return true;
    }

    private static Tile[] ConcatArrays(Tile[] a, Tile[] b)
    {
        if ((a == null || a.Length == 0) && (b == null || b.Length == 0)) return Array.Empty<Tile>();
        if (a == null || a.Length == 0) return b.ToArray();
        if (b == null || b.Length == 0) return a.ToArray();
        var list = new List<Tile>(a);
        list.AddRange(b);
        return list.ToArray();
    }
}
