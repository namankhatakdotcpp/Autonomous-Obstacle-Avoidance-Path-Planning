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
            Debug.Log($"   → ({nx},{ny}) cost={cost} walkable={maze.IsWalkable(nx, ny)}");

        var openList   = new List<Node>();
        var closedSet  = new HashSet<int>(); // packed key: x * 1000 + y
        var nodeMap    = new Dictionary<int, Node>();

        var startNode = NewNode(start.x, start.y, 0f, goal);
        openList.Add(startNode);
        nodeMap[Pack(start.x, start.y)] = startNode;

        int safety = 50000;

        while (openList.Count > 0 && safety-- > 0)
        {
            // Pop lowest f
            int bestIdx = 0;
            for (int i = 1; i < openList.Count; i++)
                if (openList[i].f < openList[bestIdx].f) bestIdx = i;

            Node current = openList[bestIdx];
            openList.RemoveAt(bestIdx);

            int key = Pack(current.x, current.y);
            if (closedSet.Contains(key)) continue;
            closedSet.Add(key);

            if (current.x == goal.x && current.y == goal.y) {
                var path = ReconstructPath(current);
                Debug.Log($"✅ A* PATH FOUND: {path.Count} waypoints");
                return path;
            }

            foreach (var (nx, ny, cost) in GetNeighbors(current.x, current.y))
            {
                int nkey = Pack(nx, ny);
                if (closedSet.Contains(nkey)) continue;

                float newG = current.g + cost;

                if (nodeMap.TryGetValue(nkey, out Node existing) && existing.g <= newG)
                    continue;

                var neighbor = NewNode(nx, ny, newG, goal);
                neighbor.parent = current;
                nodeMap[nkey] = neighbor;
                openList.Add(neighbor);
            }
        }

        Debug.LogError($"❌ A* NO PATH FOUND - openList empty or safety limit hit");
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

    private int Pack(int x, int y) => x * 1000 + y;

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

            if (!maze.IsWalkable(nx, ny)) continue;

            result.Add((nx, ny, 1f));
        }

        return result;
    }

    private List<Vector3> ReconstructPath(Node node)
    {
        var path = new List<Vector3>();
        while (node != null)
        {
            path.Add(maze.GridToWorld(node.x, node.y));
            node = node.parent;
        }
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