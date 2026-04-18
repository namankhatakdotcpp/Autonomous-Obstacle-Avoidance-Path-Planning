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

    [Header("Grid Padding (cells marked blocked around each obstacle)")]
    [Tooltip("1 = one-cell buffer in every direction around the obstacle. " +
             "Critical for 1-cell-wide corridors — keeps A* from routing through occupied space.")]
    public int gridPadding = 1;

    private MazeGenerator   maze;
    private List<GameObject>  obstacles   = new List<GameObject>();
    private List<Vector3[]>   patrols     = new List<Vector3[]>();
    private List<int>         patrolIdxs  = new List<int>();
    private List<float>       pauseTimers = new List<float>();

    void Awake() { maze = FindObjectOfType<MazeGenerator>(); }

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
            Rigidbody obstacleBody = obs.GetComponent<Rigidbody>();
            if (obstacleBody != null)
            {
                obstacleBody.isKinematic = true;
                obstacleBody.useGravity = false;
                obstacleBody.constraints = RigidbodyConstraints.FreezeAll;
            }

            obstacles.Add(obs);
            patrols.Add(patrol);
            patrolIdxs.Add(0);
            pauseTimers.Add(0f);
            spawned++;
        }
    }

    void FixedUpdate()
    {
        if (maze == null) return;

        // Wipe all obstacle-blocked cells, advance obstacles, then re-paint.
        // Single pass, no per-obstacle bookkeeping needed.
        maze.ClearAllTemporaryWalls();

        for (int i = 0; i < obstacles.Count; i++)
        {
            if (obstacles[i] == null) continue;

            if (pauseTimers[i] > 0f)
            {
                pauseTimers[i] -= Time.fixedDeltaTime;
                MarkCellsAround(obstacles[i].transform.position);
                continue;
            }

            Vector3 target = patrols[i][patrolIdxs[i]];
            target.y = obstacles[i].transform.position.y;

            obstacles[i].transform.position = Vector3.MoveTowards(
                obstacles[i].transform.position, target, moveSpeed * Time.fixedDeltaTime);

            Vector3 dir = target - obstacles[i].transform.position;
            if (dir.sqrMagnitude > 0.01f)
                obstacles[i].transform.rotation = Quaternion.Slerp(
                    obstacles[i].transform.rotation,
                    Quaternion.LookRotation(dir), 5f * Time.fixedDeltaTime);

            if (Vector3.Distance(obstacles[i].transform.position, target) < 0.1f)
            {
                patrolIdxs[i] = (patrolIdxs[i] + 1) % patrols[i].Length;
                pauseTimers[i] = pauseDuration;
            }

            MarkCellsAround(obstacles[i].transform.position);
        }
    }

    private void MarkCellsAround(Vector3 worldPos)
    {
        Vector2Int c = maze.WorldToGrid(worldPos);
        for (int dx = -gridPadding; dx <= gridPadding; dx++)
            for (int dy = -gridPadding; dy <= gridPadding; dy++)
                maze.SetTemporaryWall(c.x + dx, c.y + dy, true);
    }

    private List<Vector2Int> GetOpenCells()
    {
        var list = new List<Vector2Int>();
        for (int x = 1; x < maze.GridWidth  - 1; x++)
            for (int y = 1; y < maze.GridHeight - 1; y++)
                if (maze.IsWalkableStatic(x, y)) list.Add(new Vector2Int(x, y));
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
            int nx = origin.x + dx[d], ny = origin.y + dy[d];
            if (maze.IsWalkableStatic(nx, ny))
            {
                pts.Add(maze.GridToWorld(nx, ny));
                if (pts.Count >= count + 1) break;
            }
        }
        return pts.Count >= 2 ? pts.ToArray() : System.Array.Empty<Vector3>();
    }

    public void ClearObstacles()
    {
        if (maze != null) maze.ClearAllTemporaryWalls();
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
