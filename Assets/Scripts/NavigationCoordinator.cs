using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central authority for path requests. Enforces:
///   - minimum interval between replans (throttle)
///   - continuous background path validation (every ~200ms)
///   - dynamic-first, static-fallback strategy:
///       1. Try A* with obstacles-as-walls (IsWalkableDynamic)
///       2. If no path exists, fall back to static A* (robot will stop/wait via LocalPlanner)
///   - path smoothing
///
/// Callers hand in a callback; the result is delivered async (same frame or next frame).
/// </summary>
public class NavigationCoordinator : MonoBehaviour
{
    public MazeGenerator maze;
    public AStarPathfinder pathfinder;
    public RobotController robot;

    [Header("Replan Throttling")]
    [Tooltip("Minimum seconds between two replan requests")]
    public float minReplanInterval = 0.25f;

    [Tooltip("Validate the active path this often (seconds) — triggers replan if invalid")]
    public float validationInterval = 0.2f;

    [Header("Smoothing")]
    public bool smoothPaths = true;
    public float colinearityDot = 0.985f;

    private float lastReplanTime = -999f;
    private float lastValidationTime = 0f;
    private bool  planInFlight = false;

    void Awake()
    {
        pathfinder = FindObjectOfType<AStarPathfinder>();
        maze       = FindObjectOfType<MazeGenerator>();
        robot      = FindObjectOfType<RobotController>();
    }

    void Update()
    {
        // Continuous validation: if the active path has been invalidated by an obstacle,
        // request a fresh one even if the local planner hasn't raised the flag yet.
        if (robot != null && robot.IsMoving() && !robot.IsGoalReached())
        {
            if (Time.time - lastValidationTime > validationInterval)
            {
                lastValidationTime = Time.time;
                var path = robot.GetCurrentPath();
                int idx  = robot.GetPathIndex();
                if (path != null && path.Count > 0 && !pathfinder.IsPathValidDynamic(path, idx))
                {
                    RequestReplan(robot.transform.position, maze.GoalWorldPos, robot.OnPathResult);
                }
            }
        }
    }

    // ── public API ──────────────────────────────────────────
    public void RequestInitialPath(Vector3 start, Vector3 goal, Action<List<Vector3>> callback)
    {
        // Initial path: ignore dynamic obstacles so we always hand back SOMETHING.
        var raw = pathfinder.FindPathStatic(start, goal);
        callback?.Invoke(smoothPaths ? Smooth(raw) : raw);
        pathfinder.SetDebugPath(raw);
        lastReplanTime = Time.time;
    }

    public void RequestReplan(Vector3 start, Vector3 goal, Action<List<Vector3>> callback)
    {
        if (planInFlight) return;
        if (Time.time - lastReplanTime < minReplanInterval) return;

        lastReplanTime = Time.time;
        StartCoroutine(DoReplan(start, goal, callback));
    }

    private IEnumerator DoReplan(Vector3 start, Vector3 goal, Action<List<Vector3>> callback)
    {
        planInFlight = true;

        // 1st attempt: dynamic walkability (obstacle-aware)
        List<Vector3> path = pathfinder.FindPathDynamic(start, goal);

        // If no dynamic path exists, the obstacles temporarily block ALL routes.
        // Fall back to static path — the LocalPlanner will keep the robot stopped
        // near the obstacle until the obstacle moves and the next validation cycle
        // succeeds with a clean dynamic path.
        if (path == null || path.Count == 0)
        {
            path = pathfinder.FindPathStatic(start, goal);
        }

        if (smoothPaths) path = Smooth(path);
        pathfinder.SetDebugPath(path);

        // Spread the work out — never block multiple frames even on huge mazes
        yield return null;
        callback?.Invoke(path);
        planInFlight = false;
    }

    // ── path smoothing: remove collinear waypoints ──────────
    private List<Vector3> Smooth(List<Vector3> path)
    {
        if (path == null || path.Count < 3) return path ?? new List<Vector3>();
        var smooth = new List<Vector3> { path[0] };
        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector3 a = path[i] - path[i - 1]; a.y = 0f;
            Vector3 b = path[i + 1] - path[i]; b.y = 0f;
            if (a.sqrMagnitude < 1e-4f || b.sqrMagnitude < 1e-4f) continue;
            if (Vector3.Dot(a.normalized, b.normalized) < colinearityDot)
                smooth.Add(path[i]);
        }
        smooth.Add(path[path.Count - 1]);
        return smooth;
    }
}
