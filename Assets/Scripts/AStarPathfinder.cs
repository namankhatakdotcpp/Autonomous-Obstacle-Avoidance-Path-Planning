using System.Collections.Generic;
using UnityEngine;

public class AStarPathfinder : MonoBehaviour
{
    private MazeGenerator maze;

    [Header("Pathfinding")]
    public bool allowDiagonalMovement = false; // keep false for maze corridors
    public float diagonalMoveCost = 1.4142135f;

    void Awake()
    {
        maze = FindObjectOfType<MazeGenerator>();
    }

    // World-space entry point
    public List<Vector3> FindPath(Vector3 startWorld, Vector3 goalWorld)
    {
        Vector2Int start = maze.WorldToGrid(startWorld);
        Vector2Int goal  = maze.WorldToGrid(goalWorld);
        return FindPath(start, goal);
    }

    // Grid-space core — uses a simple List<Node> sorted by f (fine for maze sizes ≤ 41×41)
    public List<Vector3> FindPath(Vector2Int start, Vector2Int goal)
    {
        // Clamp to walkable cell
        if (!maze.IsWalkable(start.x, start.y)) start = NearestWalkable(start);
        if (!maze.IsWalkable(goal.x,  goal.y))  goal  = NearestWalkable(goal);

        Debug.Log($"🔍 A* Pathfinding: start={start}, goal={goal}");
        Debug.Log($"   Start walkable={maze.IsWalkable(start.x, start.y)}, Goal walkable={maze.IsWalkable(goal.x, goal.y)}");

        // Debug neighbors from start
        var neighbors = GetNeighbors(start.x, start.y);
        Debug.Log($"✅ Neighbors from {start}: count={neighbors.Count}");
        foreach (var (nx, ny, cost) in neighbors)
            Debug.Log($"   → ({nx},{ny}) cost={cost}");

        var openList   = new List<Vector2Int>();
        var gScore     = new Dictionary<int, float>();
        var cameFrom   = new Dictionary<int, Vector2Int>();
        var inOpenList = new HashSet<int>();
        var closedSet  = new HashSet<int>();

        int startKey = Pack(start.x, start.y);
        openList.Add(start);
        gScore[startKey] = 0f;
        inOpenList.Add(startKey);

        int safety = 50000;

        while (openList.Count > 0 && safety-- > 0)
        {
            // Find node with lowest f-score
            int bestIdx = 0;
            float bestF = Heuristic(openList[0].x, openList[0].y, goal.x, goal.y) + gScore[Pack(openList[0].x, openList[0].y)];
            
            for (int i = 1; i < openList.Count; i++)
            {
                int key = Pack(openList[i].x, openList[i].y);
                float f = Heuristic(openList[i].x, openList[i].y, goal.x, goal.y) + gScore[key];
                if (f < bestF)
                {
                    bestIdx = i;
                    bestF = f;
                }
            }

            Vector2Int current = openList[bestIdx];
            int currentKey = Pack(current.x, current.y);
            openList.RemoveAt(bestIdx);
            inOpenList.Remove(currentKey);
            closedSet.Add(currentKey);

            if (current.x == goal.x && current.y == goal.y)
            {
                var path = ReconstructPath(cameFrom, goal, start);
                Debug.Log($"✅ A* PATH FOUND: {path.Count} waypoints after {50000 - safety} iterations");
                return path;
            }

            foreach (var (nx, ny, cost) in GetNeighbors(current.x, current.y))
            {
                Vector2Int neighbor = new Vector2Int(nx, ny);
                int neighborKey = Pack(nx, ny);

                if (closedSet.Contains(neighborKey))
                    continue;

                float newG = gScore[currentKey] + cost;

                // If we haven't seen this neighbor, or found a better path
                if (!gScore.ContainsKey(neighborKey) || newG < gScore[neighborKey])
                {
                    gScore[neighborKey] = newG;
                    cameFrom[neighborKey] = current;

                    if (!inOpenList.Contains(neighborKey))
                    {
                        openList.Add(neighbor);
                        inOpenList.Add(neighborKey);
                    }
                }
            }
        }

        Debug.LogError($"❌ A* NO PATH FOUND - openList empty after {50000 - safety} iterations");
        return new List<Vector3>(); // no path
    }

    // -------------------------------------------------------
    private Node NewNode(int x, int y, float g, Vector2Int goal)
    {
        return new Node
        {
            x = x, y = y,
            g = g,
            h = Heuristic(x, y, goal.x, goal.y)
        };
    }

    private int Pack(int x, int y) => x * maze.GridWidth + y;

    private float Heuristic(int x1, int y1, int x2, int y2)
    {
        int dx = Mathf.Abs(x1 - x2);
        int dy = Mathf.Abs(y1 - y2);
        if (allowDiagonalMovement)
        {
            float diag = Mathf.Min(dx, dy);
            return diag * diagonalMoveCost + (dx + dy - 2f * diag);
        }
        return dx + dy;
    }

    private List<(int, int, float)> GetNeighbors(int x, int y)
    {
        var result = new List<(int, int, float)>();

        Vector2Int[] directions = {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        foreach (var dir in directions)
        {
            int nx = x + dir.x;
            int ny = y + dir.y;

            bool walkable = maze.IsWalkable(nx, ny);
            Debug.Log($"  Neighbor ({nx},{ny}): walkable={walkable}");
            
            if (!walkable) continue;

            result.Add((nx, ny, 1f));
        }

        return result;
    }

    private List<Vector3> ReconstructPath(Dictionary<int, Vector2Int> cameFrom, Vector2Int goal, Vector2Int start)
    {
        var path = new List<Vector3>();
        Vector2Int current = goal;

        while (current != start)
        {
            path.Add(maze.GridToWorld(current.x, current.y));
            int key = Pack(current.x, current.y);
            
            if (!cameFrom.ContainsKey(key))
                break; // Safety check

            current = cameFrom[key];
        }

        path.Add(maze.GridToWorld(start.x, start.y));
        path.Reverse();
        return path;
    }

    // Find nearest open cell to a blocked coordinate
    private Vector2Int NearestWalkable(Vector2Int pos)
    {
        for (int r = 1; r < 5; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    if (maze.IsWalkable(pos.x + dx, pos.y + dy))
                        return new Vector2Int(pos.x + dx, pos.y + dy);
        return pos;
    }

    // Gizmo debug
    private List<Vector3> debugPath = new List<Vector3>();
    public void SetDebugPath(List<Vector3> path) => debugPath = path;

    void OnDrawGizmos()
    {
        if (debugPath == null || debugPath.Count < 2) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < debugPath.Count - 1; i++)
            Gizmos.DrawLine(debugPath[i] + Vector3.up * 0.5f, debugPath[i + 1] + Vector3.up * 0.5f);
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(debugPath[debugPath.Count - 1] + Vector3.up * 0.5f, 0.4f);
    }

    private class Node
    {
        public int x, y;
        public float g, h;
        public float f => g + h;
        public Node parent;
    }
}