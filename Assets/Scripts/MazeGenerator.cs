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

        // Initialize: all cells are walls
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = true;

        // Carve passages
        CarvePassage(1, 1);
        grid[1, 1]                       = false;
        grid[width - 2, height - 2]      = false;

        // After carving, mark ALL carved cells as paths - both odd and walls between them
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // If this cell was carved (set to false), keep it
                // If not carved and it's between two carved cells, mark it as walkable
                if (!grid[x, y]) continue; // Already a path
                
                // Check if this cell is adjacent to paths in both directions (corridor)
                bool isWall = (x % 2 == 0 && y % 2 == 1) || (x % 2 == 1 && y % 2 == 0);
                if (isWall)
                {
                    bool hasPathNeighbor = false;
                    if (x > 0 && !grid[x-1, y]) hasPathNeighbor = true;
                    if (x < width-1 && !grid[x+1, y]) hasPathNeighbor = true;
                    if (y > 0 && !grid[x, y-1]) hasPathNeighbor = true;
                    if (y < height-1 && !grid[x, y+1]) hasPathNeighbor = true;
                    if (hasPathNeighbor) grid[x, y] = false; // Mark wall as path
                }
            }
        }

        // Debug: show walkability
        int walkableCells = 0;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (!grid[x, y]) walkableCells++;

        Debug.Log($"[Maze] Grid: {walkableCells} walkable cells out of {width * height}");
        Debug.Log($"[Maze] (1,1) walkable={IsWalkable(1, 1)}, ({width - 2},{height - 2}) walkable={IsWalkable(width - 2, height - 2)}");

        BuildMeshObjects();

        StartWorldPos = GridToWorld(1,           1);
        GoalWorldPos  = GridToWorld(width - 2,   height - 2);

        Debug.Log($"[Maze] {width}×{height} | Start={StartWorldPos} | Goal={GoalWorldPos}");
    }

    // ── recursive backtracker ───────────────────────────────
    private void CarvePassage(int cx, int cy)
    {
        grid[cx, cy] = false;
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
            if (nx > 0 && nx < width - 1 && ny > 0 && ny < height - 1 && grid[nx, ny])
            {
                grid[cx + dx[d] / 2, cy + dy[d] / 2] = false;
                CarvePassage(nx, ny);
            }
        }
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
    public Vector3 GridToWorld(int gx, int gy, float yOffset = 0.5f)
        => new Vector3(gx * cellSize + cellSize * 0.5f, yOffset, gy * cellSize + cellSize * 0.5f);

    public Vector2Int WorldToGrid(Vector3 w)
        => new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt(w.x / cellSize), 0, width  - 1),
            Mathf.Clamp(Mathf.FloorToInt(w.z / cellSize), 0, height - 1));

    // ── walkability (combines permanent + temporary) ────────
    /// <summary>
    /// Returns true if the cell is passable.
    /// A* calls this — it sees both permanent walls AND obstacle-padded cells.
    /// </summary>
    public bool IsWalkable(int gx, int gy)
    {
        if (gx < 0 || gx >= width || gy < 0 || gy >= height) return false;
        return !grid[gx, gy] && !tempWalls[gx, gy];
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