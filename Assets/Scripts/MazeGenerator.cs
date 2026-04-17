using System.Collections.Generic;
using UnityEngine;

public class MazeGenerator : MonoBehaviour
{
    [Header("Maze Settings")]
    public int   width      = 21;
    public int   height     = 21;
    public float cellSize   = 3f;
    public float wallHeight = 4f;

    [Header("Prefabs")]
    public GameObject wallPrefab;
    public GameObject floorPrefab;
    public GameObject goalPrefab;
    public GameObject startMarkerPrefab;

    [Header("Materials")]
    public Material wallMaterial;
    public Material floorMaterial;

    // Permanent maze structure (true = wall)
    private bool[,] grid;
    // Temporary overlay — set by ObstacleManager each frame (true = treat as wall)
    private bool[,] tempWalls;

    private List<GameObject> mazeObjects = new List<GameObject>();

    public Vector3 StartWorldPos { get; private set; }
    public Vector3 GoalWorldPos  { get; private set; }

    public int GridWidth  => width;
    public int GridHeight => height;

    // ── public entry point ──────────────────────────────────
    public void GenerateMaze()
    {
        ClearMaze();

        width  = Mathf.Max(5, width  % 2 == 0 ? width  + 1 : width);
        height = Mathf.Max(5, height % 2 == 0 ? height + 1 : height);

        grid      = new bool[width, height];
        tempWalls = new bool[width, height];

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = true;

        CarvePassage(1, 1);
        grid[1, 1]                       = false;
        grid[width - 2, height - 2]      = false;

        // 🔍 DEBUG: Count walkable cells
        int walkableCount = 0;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (!grid[x, y]) walkableCount++;
        Debug.Log($"[Maze] Walkable cells: {walkableCount} / {width * height}");

        // 🔍 VALIDATE: Ensure start and goal are in same connected region
        var visited = new HashSet<int>();
        if (!FloodFill(1, 1, visited))
            Debug.LogError("[Maze] Start is isolated!");
        
        int startKey = 1 * width + 1;
        int goalKey = (width - 2) * width + (height - 2);
        if (!visited.Contains(goalKey))
        {
            Debug.LogError("[Maze] Goal is NOT reachable from start! Maze is broken!");
        }
        else
        {
            Debug.Log($"[Maze] ✅ Connectivity verified: {visited.Count} cells reachable from start");
        }

        BuildMeshObjects();

        StartWorldPos = GridToWorld(1,           1);
        GoalWorldPos  = GridToWorld(width - 2,   height - 2);

        Debug.Log($"[Maze] {width}×{height} | Start={StartWorldPos} | Goal={GoalWorldPos}");
    }

    // ── recursive backtracker ───────────────────────────────
    // RULE: Only carve 2 steps away! (CELL -> WALL -> CELL)
    private void CarvePassage(int cx, int cy)
    {
        grid[cx, cy] = false;  // Mark current cell as path
        int[] dx = {  0,  0,  2, -2 };  // Move in steps of 2
        int[] dy = {  2, -2,  0,  0 };  // "2 away" pattern
        int[] dirs = { 0, 1, 2, 3 };
        
        // Fisher-Yates shuffle for random direction order
        for (int i = dirs.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int t = dirs[i]; dirs[i] = dirs[j]; dirs[j] = t;
        }
        
        foreach (int d in dirs)
        {
            int nx = cx + dx[d], ny = cy + dy[d];  // 2 steps away
            // Only carve if 2-step target is unvisited (still a wall)
            if (nx > 0 && nx < width - 1 && ny > 0 && ny < height - 1 && grid[nx, ny])
            {
                // Carve the WALL between current and target
                grid[cx + dx[d] / 2, cy + dy[d] / 2] = false;  // Middle cell
                // Recursively carve from the new cell
                CarvePassage(nx, ny);
            }
        }
    }

    // ── flood fill: verify connectivity ───────────────────
    /// <summary>
    /// BFS from (x, y) to find all reachable cells.
    /// Returns false if starting cell is a wall, true otherwise.
    /// </summary>
    private bool FloodFill(int startX, int startY, HashSet<int> visited)
    {
        if (grid[startX, startY]) return false;  // Starting cell must be walkable
        
        var queue = new Queue<(int, int)>();
        queue.Enqueue((startX, startY));
        visited.Add(startX * width + startY);

        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];
                int key = nx * width + ny;
                
                if (nx >= 0 && nx < width && ny >= 0 && ny < height && 
                    !grid[nx, ny] && !visited.Contains(key))
                {
                    visited.Add(key);
                    queue.Enqueue((nx, ny));
                }
            }
        }
        return true;
    }

    // ── geometry ────────────────────────────────────────────
    private void BuildMeshObjects()
    {
        Transform p = transform;

        // Floor (Unity Plane = 10×10 units)
        float fcx = width  * cellSize * 0.5f;
        float fcz = height * cellSize * 0.5f;
        var floor = Instantiate(floorPrefab, new Vector3(fcx, 0f, fcz), Quaternion.identity, p);
        floor.transform.localScale = new Vector3(width * cellSize / 10f, 1f, height * cellSize / 10f);
        if (floorMaterial) floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        mazeObjects.Add(floor);

        // Walls
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!grid[x, y]) continue;
                var pos  = new Vector3(x * cellSize + cellSize * 0.5f, wallHeight * 0.5f, y * cellSize + cellSize * 0.5f);
                var wall = Instantiate(wallPrefab, pos, Quaternion.identity, p);
                wall.transform.localScale = new Vector3(cellSize, wallHeight, cellSize);
                if (wallMaterial) wall.GetComponent<Renderer>().sharedMaterial = wallMaterial;
                wall.tag = "Wall";
                mazeObjects.Add(wall);
            }
        }

        // Goal marker
        if (goalPrefab)
        {
            var gp = GridToWorld(width - 2, height - 2); gp.y = 0.05f;
            mazeObjects.Add(Instantiate(goalPrefab, gp, Quaternion.identity, p));
        }

        // Start marker
        if (startMarkerPrefab)
        {
            var sp = GridToWorld(1, 1); sp.y = 0.05f;
            mazeObjects.Add(Instantiate(startMarkerPrefab, sp, Quaternion.identity, p));
        }
    }

    public void ClearMaze()
    {
        foreach (var obj in mazeObjects) if (obj) Destroy(obj);
        mazeObjects.Clear();
    }

    // ── coordinate helpers ──────────────────────────────────
    // ── coordinate helpers ──────────────────────────────────
    public Vector3 GridToWorld(int gx, int gy, float yOffset = 0.5f)
        => new Vector3(gx * cellSize + cellSize * 0.5f, yOffset, gy * cellSize + cellSize * 0.5f);

    public Vector2Int WorldToGrid(Vector3 w)
    {
        int x = Mathf.RoundToInt((w.x - cellSize * 0.5f) / cellSize);
        int y = Mathf.RoundToInt((w.z - cellSize * 0.5f) / cellSize);
        return new Vector2Int(
            Mathf.Clamp(x, 0, width  - 1),
            Mathf.Clamp(y, 0, height - 1));
    }

    // ── walkability (combines permanent + temporary) ────────
    /// <summary>
    /// Returns true if the cell is passable.
    /// A* calls this — it sees both permanent walls AND obstacle-padded cells.
    /// </summary>
    public bool IsWalkable(int gx, int gy)
    {
        if (gx < 0 || gx >= width || gy < 0 || gy >= height) return false;
        
        // 🔥 FIX: Disable tempWalls during pathfinding
        // This ensures A* always finds a path (maze is always solvable)
        // Obstacle avoidance happens in RobotController, not in pathfinding
        return !grid[gx, gy];  // Only check permanent walls
    }

    /// <summary>
    /// Find a connected walkable cell near the target position.
    /// If the target itself is walkable, return it.
    /// Otherwise, search nearby cells in the connected region.
    /// </summary>
    public Vector2Int FindConnectedWalkable(Vector2Int target)
    {
        if (IsWalkable(target.x, target.y))
            return target;
        
        // BFS to find nearest walkable cell connected to target
        var queue = new Queue<Vector2Int>();
        var visited = new HashSet<int>();
        queue.Enqueue(target);
        visited.Add(target.x * width + target.y);

        int[] dx = { 0, 0, 1, -1 };
        int[] dy = { 1, -1, 0, 0 };

        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            
            for (int i = 0; i < 4; i++)
            {
                int nx = pos.x + dx[i];
                int ny = pos.y + dy[i];
                int key = nx * width + ny;
                
                if (nx >= 0 && nx < width && ny >= 0 && ny < height && !visited.Contains(key))
                {
                    visited.Add(key);
                    if (IsWalkable(nx, ny))
                        return new Vector2Int(nx, ny);
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }

        // Fallback (shouldn't happen if maze is connected)
        Debug.LogWarning($"[Maze] No connected walkable cell found near {target}! Returning (1,1)");
        return new Vector2Int(1, 1);
    }

    /// <summary>
    /// Called by ObstacleManager every frame to mark / unmark cells around moving obstacles.
    /// This makes A* treat those cells as walls — so it plans a path that avoids them.
    /// </summary>
    public void SetTemporaryWall(int gx, int gy, bool blocked)
    {
        if (gx < 0 || gx >= width || gy < 0 || gy >= height) return;
        // Never mark the start or goal cell as blocked
        if (gx == 1 && gy == 1)                         return;
        if (gx == width - 2 && gy == height - 2)        return;
        // Never overwrite a permanent wall (it's already blocked)
        if (grid[gx, gy]) return;

        tempWalls[gx, gy] = blocked;
    }
}