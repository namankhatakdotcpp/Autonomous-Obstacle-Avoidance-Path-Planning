using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates the maze and owns the dual-layer grid:
///   - staticGrid[x,y]  : true = permanent wall (never changes after Generate)
///   - dynamicGrid[x,y] : true = temporarily blocked by a moving obstacle
///
/// The global planner (A*) queries IsWalkableStatic().
/// The local planner / replan-check queries IsWalkableDynamic().
/// This separation guarantees A* always finds SOME path on the static maze,
/// while the robot can still react to live obstacles.
/// </summary>
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

    private bool[,] staticGrid;   // permanent walls
    private bool[,] dynamicGrid;  // obstacle overlay (reset every FixedUpdate by ObstacleManager)

    private List<GameObject> mazeObjects = new List<GameObject>();

    public Vector3 StartWorldPos { get; private set; }
    public Vector3 GoalWorldPos  { get; private set; }
    public int GridWidth  => width;
    public int GridHeight => height;

    public void GenerateMaze()
    {
        ClearMaze();

        width  = Mathf.Max(5, width  % 2 == 0 ? width  + 1 : width);
        height = Mathf.Max(5, height % 2 == 0 ? height + 1 : height);

        staticGrid  = new bool[width, height];
        dynamicGrid = new bool[width, height];

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                staticGrid[x, y] = true;

        CarvePassage(1, 1);
        staticGrid[1, 1]                       = false;
        staticGrid[width - 2, height - 2]      = false;

        BuildMeshObjects();

        StartWorldPos = GridToWorld(1,           1);
        GoalWorldPos  = GridToWorld(width - 2,   height - 2);
    }

    // ── recursive backtracker (unchanged) ────────────────────
    private void CarvePassage(int cx, int cy)
    {
        staticGrid[cx, cy] = false;
        int[] dx = {  0,  0,  2, -2 };
        int[] dy = {  2, -2,  0,  0 };
        int[] dirs = { 0, 1, 2, 3 };
        for (int i = dirs.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int t = dirs[i]; dirs[i] = dirs[j]; dirs[j] = t;
        }
        foreach (int d in dirs)
        {
            int nx = cx + dx[d], ny = cy + dy[d];
            if (nx > 0 && nx < width - 1 && ny > 0 && ny < height - 1 && staticGrid[nx, ny])
            {
                staticGrid[cx + dx[d] / 2, cy + dy[d] / 2] = false;
                CarvePassage(nx, ny);
            }
        }
    }

    // ── geometry (unchanged logic, trimmed) ──────────────────
    private void BuildMeshObjects()
    {
        Transform p = transform;
        float fcx = width  * cellSize * 0.5f;
        float fcz = height * cellSize * 0.5f;
        var floor = Instantiate(floorPrefab, new Vector3(fcx, 0f, fcz), Quaternion.identity, p);
        floor.layer = LayerMask.NameToLayer("Default");  // 🔥 FORCE Default layer (not Obstacle)
        floor.transform.localScale = new Vector3(width * cellSize / 10f, 1f, height * cellSize / 10f);
        if (floorMaterial) floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        mazeObjects.Add(floor);

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (!staticGrid[x, y]) continue;
                var pos  = new Vector3(x * cellSize + cellSize * 0.5f, wallHeight * 0.5f, y * cellSize + cellSize * 0.5f);
                var wall = Instantiate(wallPrefab, pos, Quaternion.identity, p);
                wall.transform.localScale = new Vector3(cellSize, wallHeight, cellSize);
                if (wallMaterial) wall.GetComponent<Renderer>().sharedMaterial = wallMaterial;
                wall.tag = "Wall";
                mazeObjects.Add(wall);
            }

        if (goalPrefab)
        {
            var gp = GridToWorld(width - 2, height - 2); gp.y = 0.05f;
            mazeObjects.Add(Instantiate(goalPrefab, gp, Quaternion.identity, p));
        }
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

    // ── coordinate helpers ───────────────────────────────────
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

    // ── walkability: TWO LAYERS ──────────────────────────────
    /// <summary>Static walls only. Global A* uses this — guaranteed to find a path if one exists.</summary>
    public bool IsWalkableStatic(int gx, int gy)
    {
        if (gx < 0 || gx >= width || gy < 0 || gy >= height) return false;
        return !staticGrid[gx, gy];
    }

    /// <summary>Static walls + live obstacle overlay. Used for path validation and obstacle-aware replans.</summary>
    public bool IsWalkableDynamic(int gx, int gy)
    {
        if (gx < 0 || gx >= width || gy < 0 || gy >= height) return false;
        return !staticGrid[gx, gy] && !dynamicGrid[gx, gy];
    }

    /// <summary>Back-compat alias. Defaults to STATIC (safe for existing calls).</summary>
    public bool IsWalkable(int gx, int gy) => IsWalkableStatic(gx, gy);

    /// <summary>Called by ObstacleManager to paint/clear live obstacle cells.</summary>
    public void SetTemporaryWall(int gx, int gy, bool blocked)
    {
        if (gx < 0 || gx >= width || gy < 0 || gy >= height) return;
        // Never overlay on start/goal — robot has to be able to reach them.
        if (gx == 1 && gy == 1)                  return;
        if (gx == width - 2 && gy == height - 2) return;
        if (staticGrid[gx, gy]) return; // permanent wall — don't bother
        dynamicGrid[gx, gy] = blocked;
    }

    /// <summary>Bulk clear — call at the start of each obstacle-manager tick before re-painting.</summary>
    public void ClearAllTemporaryWalls()
    {
        if (dynamicGrid == null) return;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                dynamicGrid[x, y] = false;
    }

    public Vector2Int FindConnectedWalkable(Vector2Int target)
    {
        if (IsWalkableStatic(target.x, target.y)) return target;
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
                int nx = pos.x + dx[i], ny = pos.y + dy[i];
                int key = nx * width + ny;
                if (nx >= 0 && nx < width && ny >= 0 && ny < height && !visited.Contains(key))
                {
                    visited.Add(key);
                    if (IsWalkableStatic(nx, ny)) return new Vector2Int(nx, ny);
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }
        return new Vector2Int(1, 1);
    }
}
