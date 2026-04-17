using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class RobotController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed         = 8f;
    public float rotateSpeed       = 12f;
    public float waypointReachDist = 1.2f;

    [Header("Collision Prevention")]
    // Layer 1: how many waypoints ahead to scan for obstacles
    public int   lookAheadWaypoints = 6;
    // Layer 2: distance at which robot FULLY STOPS and waits for replan
    public float hardStopDistance   = 2.0f;
    // Layer 3: distance at which robot starts slowing down
    public float slowdownDistance   = 4.5f;
    public LayerMask obstacleLayer;

    [Header("Replanning")]
    public float replanCooldown = 0.4f;   // shorter = reacts faster

    [Header("VFX")]
    public TrailRenderer   pathTrail;
    public ParticleSystem  reachGoalFX;

    // ── private runtime ──────────────────────────────────────
    private AStarPathfinder pathfinder;
    private MazeGenerator   maze;
    private Rigidbody        rb;

    private List<Vector3> currentPath = new List<Vector3>();
    private int   pathIndex      = 0;
    private bool  isMoving       = false;
    private bool  goalReached    = false;
    private float lastReplanTime = -999f;
    private bool  isHardStopped  = false;   // Layer 3 state

    // ── stats ─────────────────────────────────────────────────
    public int   CollisionCount    { get; private set; }
    public float DistanceTravelled { get; private set; }
    public int   ReplanCount       { get; private set; }
    public float TimeElapsed       { get; private set; }
    private Vector3 lastPos;

    // ═══════════════════════════════════════════════════════════
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX
                       | RigidbodyConstraints.FreezeRotationZ;
        rb.mass                   = 1f;
        rb.interpolation          = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

#if UNITY_6000_0_OR_NEWER
        rb.linearDamping  = 6f;
        rb.angularDamping = 10f;
#else
        rb.drag        = 6f;
        rb.angularDrag = 10f;
#endif

        SetupTrail();
        pathfinder = FindObjectOfType<AStarPathfinder>();
        maze       = FindObjectOfType<MazeGenerator>();
    }

    private void SetupTrail()
    {
        if (pathTrail == null) return;
        pathTrail.startWidth = 0.18f;
        pathTrail.endWidth   = 0f;
        pathTrail.time       = 2f;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.cyan, 0f), new GradientColorKey(Color.cyan, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        pathTrail.colorGradient = g;
    }

    // ═══════════════════════════════════════════════════════════
    public void PlaceAtStart(Vector3 startPos)
    {
        rb.isKinematic     = true;
        rb.position        = startPos;
        transform.position = startPos;
        rb.isKinematic     = false;
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = Vector3.zero;
#else
        rb.velocity = Vector3.zero;
#endif
        rb.angularVelocity = Vector3.zero;
        isMoving           = false;
        goalReached        = false;
        isHardStopped      = false;
    }

    public void StartNavigation(Vector3 start, Vector3 goal)
    {
        rb.isKinematic     = true;
        rb.position        = start;
        transform.position = start;
        rb.isKinematic     = false;
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = Vector3.zero;
#else
        rb.velocity = Vector3.zero;
#endif
        rb.angularVelocity = Vector3.zero;

        goalReached       = false;
        CollisionCount    = 0;
        DistanceTravelled = 0f;
        ReplanCount       = 0;
        TimeElapsed       = 0f;
        lastPos           = start;
        lastReplanTime    = -999f;
        isHardStopped     = false;

        if (pathTrail != null) { pathTrail.Clear(); pathTrail.emitting = true; }

        RequestPath(goal);
        isMoving = currentPath.Count > 0;
    }

    // ═══════════════════════════════════════════════════════════
    void FixedUpdate()
    {
        if (!isMoving || goalReached) return;

        if (currentPath == null || currentPath.Count == 0) return;

        // Simple movement: follow waypoints
        Vector3 target = currentPath[pathIndex];
        Vector3 toTarget = (target - rb.position);
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        if (dist < waypointReachDist)
        {
            pathIndex++;
            if (pathIndex >= currentPath.Count)
            {
                CheckGoal();
                return;
            }
            target = currentPath[pathIndex];
            toTarget = (target - rb.position);
            toTarget.y = 0f;
            dist = toTarget.magnitude;
        }

        if (dist < 0.05f) return;

        Vector3 dir = toTarget / dist;

        // Rotate towards target
        rb.MoveRotation(Quaternion.Slerp(rb.rotation,
            Quaternion.LookRotation(dir), rotateSpeed * Time.fixedDeltaTime));

        // Move
        SetVelocity(dir * moveSpeed);

        // Update stats
        TimeElapsed += Time.fixedDeltaTime;
        DistanceTravelled += Vector3.Distance(rb.position, lastPos);
        lastPos = rb.position;
    }

    // ═══════════════════════════════════════════════════════════
    private void MoveAlongPath(float nearestObstacleDist)
    {
        if (currentPath == null || currentPath.Count == 0) return;

        // Consume waypoints already within reach
        while (pathIndex < currentPath.Count - 1 &&
               Vector3.Distance(rb.position, currentPath[pathIndex]) < waypointReachDist)
        {
            pathIndex++;
        }

        if (pathIndex >= currentPath.Count) { CheckGoal(); return; }

        Vector3 target   = new Vector3(currentPath[pathIndex].x, rb.position.y, currentPath[pathIndex].z);
        Vector3 toTarget = target - rb.position;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;
        if (dist < 0.05f) { pathIndex++; return; }

        Vector3 dir = toTarget / dist;

        // Rotate
        rb.MoveRotation(Quaternion.Slerp(rb.rotation,
            Quaternion.LookRotation(dir), rotateSpeed * Time.fixedDeltaTime));

        // ── LAYER 2: Speed proportional to obstacle proximity ──
        // The closer an obstacle is (between hardStop and slowdown range),
        // the slower we go. This creates a smooth deceleration zone.
        float proximityFactor = Mathf.InverseLerp(hardStopDistance, slowdownDistance, nearestObstacleDist);
        proximityFactor = Mathf.Clamp01(proximityFactor);

        // Also slow down based on turn angle (prevents corner scraping)
        float angleDiff  = Vector3.Angle(transform.forward, dir);
        float angleFactor = Mathf.Max(Mathf.Clamp01(1f - angleDiff / 90f), 0.2f);

        float finalSpeed = moveSpeed * proximityFactor * angleFactor;

        // Minimum speed only when far from any obstacle
        if (nearestObstacleDist > slowdownDistance)
            finalSpeed = Mathf.Max(finalSpeed, moveSpeed * 0.2f);

        SetVelocity(dir * finalSpeed);
    }

    // ─── helpers ────────────────────────────────────────────────

    private void SetVelocity(Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = new Vector3(v.x, rb.linearVelocity.y, v.z);
#else
        rb.velocity = new Vector3(v.x, rb.velocity.y, v.z);
#endif
    }

    /// Returns distance to the nearest obstacle in any direction.
    /// Uses SphereCastAll so it catches obstacles to the sides as well.
    private float NearestObstacleDistance()
    {
        Vector3 origin = rb.position + Vector3.up * 0.4f;

        // Check all 8 directions + forward diagonals
        Vector3[] dirs = {
            transform.forward,
            Quaternion.Euler(0,  45, 0) * transform.forward,
            Quaternion.Euler(0, -45, 0) * transform.forward,
            Quaternion.Euler(0,  90, 0) * transform.forward,
            Quaternion.Euler(0, -90, 0) * transform.forward,
            Quaternion.Euler(0, 135, 0) * transform.forward,
            Quaternion.Euler(0, -135, 0) * transform.forward,
            -transform.forward,
        };

        float nearest = float.MaxValue;
        foreach (var dir in dirs)
        {
            if (Physics.Raycast(origin, dir, out RaycastHit hit, slowdownDistance, obstacleLayer))
                nearest = Mathf.Min(nearest, hit.distance);
        }

        // Also do a sphere overlap for obstacles directly beside us
        Collider[] cols = Physics.OverlapSphere(origin, hardStopDistance * 0.8f, obstacleLayer);
        if (cols.Length > 0)
        {
            foreach (var col in cols)
            {
                float d = Vector3.Distance(origin, col.ClosestPoint(origin));
                nearest = Mathf.Min(nearest, d);
            }
        }

        return nearest;
    }

    /// Layer 1: cast linecasts along upcoming path waypoints.
    /// Returns true if any segment is blocked.
    private bool PathIsBlocked()
    {
        if (currentPath == null || pathIndex >= currentPath.Count) return false;

        Vector3 origin = rb.position + Vector3.up * 0.4f;
        int endIdx = Mathf.Min(pathIndex + lookAheadWaypoints, currentPath.Count - 1);

        for (int i = pathIndex; i <= endIdx; i++)
        {
            Vector3 wp = currentPath[i] + Vector3.up * 0.4f;
            if (Physics.Linecast(origin, wp, obstacleLayer))
                return true;
            origin = wp;
        }
        return false;
    }

    // ─── pathfinding ────────────────────────────────────────────
    private void RequestPath(Vector3 goal)
    {
        if (pathfinder == null || maze == null) {
            Debug.LogError("❌ Pathfinder or Maze is NULL!");
            return;
        }

        var raw = pathfinder.FindPath(rb.position, goal);
        currentPath = SmoothPath(raw);
        pathIndex   = currentPath.Count > 1 ? 1 : 0;

        if (currentPath.Count == 0)
        {
            Debug.LogError("❌ NO PATH FOUND - Possible issues: grid blocking, start/goal not walkable");
            isMoving = false;
        }
        else
        {
            Debug.Log($"✅ PATH FOUND: {currentPath.Count} waypoints");
        }
        pathfinder.SetDebugPath(currentPath);
    }

    private List<Vector3> SmoothPath(List<Vector3> path)
    {
        if (path == null || path.Count < 3) return path ?? new List<Vector3>();
        var smooth = new List<Vector3> { path[0] };
        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector3 a = path[i] - path[i - 1]; a.y = 0f;
            Vector3 b = path[i + 1] - path[i]; b.y = 0f;
            if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f) continue;
            if (Vector3.Dot(a.normalized, b.normalized) < 0.99f)
                smooth.Add(path[i]);
        }
        smooth.Add(path[path.Count - 1]);
        return smooth;
    }

    // ─── goal ───────────────────────────────────────────────────
    private void CheckGoal()
    {
        float threshold = maze != null ? maze.cellSize * 1.2f : 3.6f;
        if (Vector3.Distance(rb.position, maze.GoalWorldPos) < threshold)
        {
            goalReached = true;
            isMoving    = false;
            OnGoalReached();
        }
        else
        {
            RequestPath(maze.GoalWorldPos);
        }
    }

    // ─── collision (last resort counter — should now stay at 0) ─
    private void OnCollisionEnter(Collision col)
    {
        if (!col.gameObject.CompareTag("Wall") && !col.gameObject.CompareTag("Obstacle")) return;

        CollisionCount++;
        Debug.LogWarning($"[Robot] Collision #{CollisionCount} with {col.gameObject.name}");

        SetVelocity(Vector3.zero);
        rb.AddForce(-transform.forward * 3f, ForceMode.Impulse);

        if (Time.time - lastReplanTime > replanCooldown * 0.3f)
        {
            lastReplanTime = Time.time;
            ReplanCount++;
            RequestPath(maze.GoalWorldPos);
        }
    }

    private void OnGoalReached()
    {
        if (reachGoalFX != null) reachGoalFX.Play();
        if (pathTrail   != null) pathTrail.emitting = false;
        GameManager.Instance?.OnGoalReached();
        Debug.Log($"[Robot] DONE | Collisions:{CollisionCount} Replans:{ReplanCount} Time:{TimeElapsed:F1}s");
    }

    public bool IsGoalReached() => goalReached;
    public bool IsMoving()      => isMoving;
}