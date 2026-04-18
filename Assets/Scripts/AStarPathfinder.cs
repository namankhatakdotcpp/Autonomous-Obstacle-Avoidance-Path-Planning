using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A* pathfinder with a pluggable walkability predicate.
/// - For GLOBAL planning, call FindPathStatic() — ignores live obstacles.
/// - For AVOIDANCE replanning, call FindPathDynamic() — treats live obstacles as blocked.
///
/// Uses a binary-heap priority queue (O(log n) pop) instead of List-scan.
/// On 41×41 this is ~5x faster and avoids GC churn.
/// </summary>
public class AStarPathfinder : MonoBehaviour
{
    private MazeGenerator maze;

    void Awake() { maze = FindObjectOfType<MazeGenerator>(); }

    public List<Vector3> FindPathStatic(Vector3 startWorld, Vector3 goalWorld)
        => FindPath(startWorld, goalWorld, maze.IsWalkableStatic);

    public List<Vector3> FindPathDynamic(Vector3 startWorld, Vector3 goalWorld)
        => FindPath(startWorld, goalWorld, maze.IsWalkableDynamic);

    public List<Vector3> FindPath(Vector3 startWorld, Vector3 goalWorld, Func<int,int,bool> walkable)
    {
        Vector2Int start = maze.WorldToGrid(startWorld);
        Vector2Int goal  = maze.WorldToGrid(goalWorld);
        if (!walkable(start.x, start.y)) start = maze.FindConnectedWalkable(start);
        if (!walkable(goal.x,  goal.y))  goal  = maze.FindConnectedWalkable(goal);
        return FindPathGrid(start, goal, walkable);
    }

    private List<Vector3> FindPathGrid(Vector2Int start, Vector2Int goal, Func<int,int,bool> walkable)
    {
        int W = maze.GridWidth;
        int startKey = start.x * W + start.y;
        int goalKey  = goal.x  * W + goal.y;

        var gScore   = new Dictionary<int, float> { [startKey] = 0f };
        var cameFrom = new Dictionary<int, int>();
        var open     = new MinHeap();
        var closed   = new HashSet<int>();
        open.Push(startKey, Heuristic(start, goal));

        int safety = 50000;
        while (open.Count > 0 && safety-- > 0)
        {
            int currentKey = open.Pop();
            if (closed.Contains(currentKey)) continue;
            closed.Add(currentKey);

            if (currentKey == goalKey)
                return ReconstructPath(cameFrom, goalKey, startKey, W);

            int cx = currentKey / W;
            int cy = currentKey % W;
            float g = gScore[currentKey];

            // 4-way neighbours — grid-aligned, no diagonals (maze corridors are 1-wide)
            TryNeighbor(cx + 1, cy, currentKey, g, goal, walkable, gScore, cameFrom, closed, open, W);
            TryNeighbor(cx - 1, cy, currentKey, g, goal, walkable, gScore, cameFrom, closed, open, W);
            TryNeighbor(cx, cy + 1, currentKey, g, goal, walkable, gScore, cameFrom, closed, open, W);
            TryNeighbor(cx, cy - 1, currentKey, g, goal, walkable, gScore, cameFrom, closed, open, W);
        }
        return new List<Vector3>(); // no path
    }

    private void TryNeighbor(int nx, int ny, int fromKey, float fromG, Vector2Int goal,
                             Func<int,int,bool> walkable,
                             Dictionary<int,float> gScore, Dictionary<int,int> cameFrom,
                             HashSet<int> closed, MinHeap open, int W)
    {
        if (!walkable(nx, ny)) return;
        int nKey = nx * W + ny;
        if (closed.Contains(nKey)) return;
        float newG = fromG + 1f;
        if (gScore.TryGetValue(nKey, out float existingG) && newG >= existingG) return;
        gScore[nKey]   = newG;
        cameFrom[nKey] = fromKey;
        float f = newG + Heuristic(new Vector2Int(nx, ny), goal);
        open.Push(nKey, f);
    }

    private static float Heuristic(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private List<Vector3> ReconstructPath(Dictionary<int,int> cameFrom, int goalKey, int startKey, int W)
    {
        var path = new List<Vector3>();
        int cur = goalKey;
        int safety = 10000;
        while (cur != startKey && safety-- > 0)
        {
            path.Add(maze.GridToWorld(cur / W, cur % W));
            if (!cameFrom.TryGetValue(cur, out cur)) break;
        }
        path.Add(maze.GridToWorld(startKey / W, startKey % W));
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Fast check: is the given world-space path still valid under DYNAMIC walkability?
    /// Call every ~200ms from the navigation coordinator to decide if a replan is needed.
    /// </summary>
    public bool IsPathValidDynamic(List<Vector3> path, int startFromIndex = 0)
    {
        if (path == null || path.Count == 0) return false;
        for (int i = startFromIndex; i < path.Count; i++)
        {
            var g = maze.WorldToGrid(path[i]);
            if (!maze.IsWalkableDynamic(g.x, g.y)) return false;
        }
        return true;
    }

    // ── gizmo debug ──────────────────────────────────────────
    private List<Vector3> debugPath = new List<Vector3>();
    public void SetDebugPath(List<Vector3> path) => debugPath = path ?? new List<Vector3>();
    void OnDrawGizmos()
    {
        if (debugPath == null || debugPath.Count < 2) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < debugPath.Count - 1; i++)
            Gizmos.DrawLine(debugPath[i] + Vector3.up * 0.5f, debugPath[i + 1] + Vector3.up * 0.5f);
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(debugPath[debugPath.Count - 1] + Vector3.up * 0.5f, 0.4f);
    }

    // ── binary min-heap priority queue ───────────────────────
    private class MinHeap
    {
        private readonly List<(int key, float priority)> data = new List<(int,float)>();
        public int Count => data.Count;
        public void Push(int key, float priority)
        {
            data.Add((key, priority));
            int i = data.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (data[parent].priority <= data[i].priority) break;
                (data[i], data[parent]) = (data[parent], data[i]);
                i = parent;
            }
        }
        public int Pop()
        {
            int top = data[0].key;
            int last = data.Count - 1;
            data[0] = data[last];
            data.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = i * 2 + 2, smallest = i;
                if (l < data.Count && data[l].priority < data[smallest].priority) smallest = l;
                if (r < data.Count && data[r].priority < data[smallest].priority) smallest = r;
                if (smallest == i) break;
                (data[i], data[smallest]) = (data[smallest], data[i]);
                i = smallest;
            }
            return top;
        }
    }
}
