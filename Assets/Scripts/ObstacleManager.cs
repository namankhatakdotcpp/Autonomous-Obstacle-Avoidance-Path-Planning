using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObstacleManager : MonoBehaviour
{
    [Header("Obstacle Settings")]
    public GameObject obstaclePrefab;
    public int   obstacleCount = 5;
    public float moveSpeed     = 1.5f;
    public float pauseDuration = 1.0f;
    public float obstacleSize  = 1.8f;

    [Header("Padding (cells around obstacle that A* treats as blocked)")]
    public int gridPadding = 1;   // 1 = one cell buffer around every obstacle

    private MazeGenerator          maze;
    private AStarPathfinder        pathfinder;
    private List<GameObject>       obstacles      = new List<GameObject>();
    private List<Vector3[]>        patrols        = new List<Vector3[]>();
    private List<int>              patrolIdxs     = new List<int>();
    private List<float>            pauseTimers    = new List<float>();

    // Cells currently marked blocked by obstacles (so we can unmark on move)
    private HashSet<Vector2Int>    markedCells    = new HashSet<Vector2Int>();

    void Awake()
    {
        maze       = FindObjectOfType<MazeGenerator>();
        pathfinder = FindObjectOfType<AStarPathfinder>();
    }

    // ─────────────────────────────────────────────────────────
    public void SpawnObstacles()
    {
        ClearObstacles();

        var openCells = GetOpenCells();
        Shuffle(openCells);

        int spawned = 0;
        foreach (var cell in openCells)
        {
            if (spawned >= obstacleCount) break;

            // Keep clear of start (1,1) and goal (w-2, h-2)
            if (cell.x <= 3 && cell.y <= 3) continue;
            if (cell.x >= maze.GridWidth - 4 && cell.y >= maze.GridHeight - 4) continue;

            Vector3[] patrol = BuildPatrol(cell, 2);
            if (patrol.Length < 2) continue;

            Vector3 pos = maze.GridToWorld(cell.x, cell.y);
            GameObject obs = Instantiate(obstaclePrefab, pos, Quaternion.identity, transform);
            obs.transform.localScale = Vector3.one * obstacleSize;
            obs.tag = "Obstacle";

            obstacles.Add(obs);
            patrols.Add(patrol);
            patrolIdxs.Add(0);
            pauseTimers.Add(0f);
            spawned++;
        }
    }

    // ─────────────────────────────────────────────────────────
    void Update()
    {
        // Unmark all obstacle cells, move, then re-mark at new positions.
        // This keeps the virtual grid accurate for real-time A* replanning.
        UnmarkAllCells();

        for (int i = 0; i < obstacles.Count; i++)
        {
            if (obstacles[i] == null) continue;

            if (pauseTimers[i] > 0f)
            {
                pauseTimers[i] -= Time.deltaTime;
                MarkCellsAround(obstacles[i].transform.position);
                continue;
            }

            Vector3 target = patrols[i][patrolIdxs[i]];
            target.y = obstacles[i].transform.position.y;

            obstacles[i].transform.position = Vector3.MoveTowards(
                obstacles[i].transform.position, target, moveSpeed * Time.deltaTime);

            Vector3 dir = target - obstacles[i].transform.position;
            if (dir.sqrMagnitude > 0.01f)
                obstacles[i].transform.rotation = Quaternion.Slerp(
                    obstacles[i].transform.rotation,
                    Quaternion.LookRotation(dir), 5f * Time.deltaTime);

            if (Vector3.Distance(obstacles[i].transform.position, target) < 0.1f)
            {
                patrolIdxs[i] = (patrolIdxs[i] + 1) % patrols[i].Length;
                pauseTimers[i] = pauseDuration;
            }

            MarkCellsAround(obstacles[i].transform.position);
        }
    }

    // ── grid marking ─────────────────────────────────────────
    private void MarkCellsAround(Vector3 worldPos)
    {
        Vector2Int centre = maze.WorldToGrid(worldPos);

        for (int dx = -gridPadding; dx <= gridPadding; dx++)
        {
            for (int dy = -gridPadding; dy <= gridPadding; dy++)
            {
                int gx = centre.x + dx;
                int gy = centre.y + dy;
                if (gx < 0 || gx >= maze.GridWidth || gy < 0 || gy >= maze.GridHeight) continue;

                var cell = new Vector2Int(gx, gy);
                if (markedCells.Contains(cell)) continue;

                markedCells.Add(cell);
                maze.SetTemporaryWall(gx, gy, true);  // mark as blocked in grid
            }
        }
    }

    private void UnmarkAllCells()
    {
        foreach (var cell in markedCells)
            maze.SetTemporaryWall(cell.x, cell.y, false);
        markedCells.Clear();
    }

    // ── helpers ───────────────────────────────────────────────
    private List<Vector2Int> GetOpenCells()
    {
        var list = new List<Vector2Int>();
        for (int x = 1; x < maze.GridWidth  - 1; x++)
            for (int y = 1; y < maze.GridHeight - 1; y++)
                if (maze.IsWalkable(x, y)) list.Add(new Vector2Int(x, y));
        return list;
    }

    private Vector3[] BuildPatrol(Vector2Int origin, int count)
    {
        var pts = new List<Vector3> { maze.GridToWorld(origin.x, origin.y) };
        int[] dx = { 0, 0, 2, -2 };
        int[] dy = { 2, -2, 0, 0 };
        int[] order = ShuffledRange(4);

        foreach (int d in order)
        {
            int nx = origin.x + dx[d];
            int ny = origin.y + dy[d];
            if (maze.IsWalkable(nx, ny))
            {
                pts.Add(maze.GridToWorld(nx, ny));
                if (pts.Count >= count + 1) break;
            }
        }
        return pts.Count >= 2 ? pts.ToArray() : System.Array.Empty<Vector3>();
    }

    public void ClearObstacles()
    {
        UnmarkAllCells();
        foreach (var obj in obstacles) if (obj != null) Destroy(obj);
        obstacles.Clear(); patrols.Clear(); patrolIdxs.Clear(); pauseTimers.Clear();
    }

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    private int[] ShuffledRange(int n)
    {
        int[] a = new int[n];
        for (int i = 0; i < n; i++) a[i] = i;
        for (int i = n - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = a[i]; a[i] = a[j]; a[j] = tmp;
        }
        return a;
    }
}