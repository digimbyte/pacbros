You’re trying to use WFC for *local* consistency (tile adjacency), but you also need *global* guarantees (every room/corridor reachable, plus “secret” topology via portals). Pure WFC doesn’t naturally guarantee global connectivity, so you add a **connectivity layer** on top.

Here’s a practical, Unity-friendly way that won’t turn into a PhD.

---

## The right architecture: Macro layout first, WFC second

### 1) Generate a coarse “walkable layout” grid (macro)

Run WFC on a **low-resolution grid** where each cell is one of a small set of high-level modules:

* `Wall`
* `CorridorStraight`, `CorridorCorner`, `CorridorJunction`, `DeadEnd` (optional)
* `RoomSmall/Med/Large` (or `RoomAnchor` cells that later expand)
* `Doorway` (optional)
* `PortalA`, `PortalB` (paired endpoints)
* `SecretRoom` (optional / special rules)

Each macro tile has **sockets** on N/E/S/W like `"WALK"`, `"BLOCK"`, `"DOOR"`, etc.

**Why macro?** Because connectivity checks and repairs are cheap and predictable here. Then you “skin” it with detailed micro-tiles later.

---

## 2) Enforce accessibility with a graph check (including portals)

After macro-WFC produces a candidate:

* Build a graph of walkable macro-cells (edges for N/E/S/W adjacency where both sides are walkable)
* Add **portal edges**: `PortalId=7` connects its two endpoints even if they’re not adjacent
* BFS/DFS from your start cell
* Validate:

  * **All non-secret walkables reachable**
  * Secret rooms: reachable **only if** you include portal edges (or hidden-door edges), depending on design

### Unity-ish C# (grid reachability + portal edges)

```csharp
public struct Cell {
    public bool walkable;
    public bool isSecret;     // secret room area
    public int portalId;      // -1 if none
}

public static HashSet<Vector2Int> Flood(
    Cell[,] grid,
    Vector2Int start,
    Dictionary<int, List<Vector2Int>> portalEndpoints,
    bool includePortals)
{
    int w = grid.GetLength(0), h = grid.GetLength(1);
    var seen = new HashSet<Vector2Int>();
    var q = new Queue<Vector2Int>();

    void Enqueue(Vector2Int p) {
        if (p.x < 0 || p.y < 0 || p.x >= w || p.y >= h) return;
        if (!grid[p.x, p.y].walkable) return;
        if (seen.Add(p)) q.Enqueue(p);
    }

    Enqueue(start);

    while (q.Count > 0) {
        var p = q.Dequeue();

        // cardinal neighbors
        Enqueue(p + Vector2Int.up);
        Enqueue(p + Vector2Int.down);
        Enqueue(p + Vector2Int.left);
        Enqueue(p + Vector2Int.right);

        // portal neighbors (non-local)
        if (includePortals) {
            int pid = grid[p.x, p.y].portalId;
            if (pid >= 0 && portalEndpoints.TryGetValue(pid, out var ends)) {
                foreach (var e in ends) Enqueue(e);
            }
        }
    }

    return seen;
}
```

Validation example:

* `reachableNoPortal = Flood(... includePortals:false)`
* `reachableWithPortal = Flood(... includePortals:true)`
* Require:

  * For every walkable cell where `isSecret == false`, it must be in `reachableNoPortal`
  * For every walkable cell where `isSecret == true`, it must be in `reachableWithPortal` (but *not necessarily* in `reachableNoPortal`)

That gives you “secret rooms exist but aren’t accessible until portal is used” as a hard rule.

---

## 3) If disconnected: repair, don’t pray

You have two sane options:

### Option A: “Repair carve” pass (fast, reliable)

If the macro layout is disconnected:

1. Find connected components of walkables
2. Pick the largest component as “main”
3. For each other component, carve a corridor path through walls to connect it (A* through wall cells with a cost)
4. Lock those carved cells as corridor modules (fixed), then (optionally) re-run a **local** WFC cleanup for visuals

This preserves WFC’s look while guaranteeing playability.

### Option B: “Constraint injection + reroll” (simpler, more brute-force)

If disconnected:

* Add forced corridor “bridge” cells between components (choose a path), mark them **pre-collapsed**, then rerun WFC
* Or just rerun with a new seed up to N tries

For most games: do **A first**, and fall back to **B** if carving would violate design.

---

## 4) Portals and secret rooms: treat them as graph edges, not tiles

**Portals are not just decoration**. They change reachability rules. So:

* When placing portals in macro-WFC, ensure:

  * Endpoint A is in the main reachable space
  * Endpoint B is inside a secret region (or separate pocket)
  * Both endpoints are placed on walkable cells
* Store them as `portalId`, and add that “teleport edge” in your connectivity solver (as above)

If you want “secret portal rooms” that are *optional*:

* Exclude secret cells from the “must be reachable without portals” rule.

If you want “secret but still fair”:

* Require the portal entrance to be reachable, and the secret area reachable *with* portals, but keep the secret area unreachable without portals.

---

## 5) Then do the real WFC: micro-tiles per chunk (visual detail)

Once macro connectivity is correct, generate the *actual tilemap*:

* Each macro cell expands into an `NxN` micro grid (like 6×6 or 8×8)
* Run WFC per chunk with boundary constraints derived from macro sockets

  * If macro says corridor north-south, the micro boundary must expose corridor openings on those edges
  * Rooms have different micro palettes than corridors

This prevents micro-WFC from accidentally “closing” a corridor that macro said must remain open.

---

## What I’d do (strong opinion)

Do **macro-WFC + connectivity repair + micro-WFC skinning**.

Trying to bake global reachability directly into a single-pass tile-level WFC is possible, but it’s complexity-heavy and brittle. The two-layer approach gives you:

* guaranteed accessibility
* controlled room/corridor ratios
* portals handled cleanly as non-local edges
* still looks like WFC

---

If you want, I can sketch the data structures for your Unity ScriptableObjects (tile prototypes with sockets/tags/weights), plus a clean “Generate → Validate → Repair → Skin → Instantiate” pipeline class layout.
