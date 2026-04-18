using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class RobotController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed         = 6.5f;
    public float rotateSpeed       = 1.2f;   // rad/s
    public float waypointReachDist = 0.3f;

    [Header("Collision Zones")]
    public float hardStopDist = 0.4f;
    public float avoidDist    = 3.5f;
    public LayerMask obstacleLayer;

    [Header("Planner Tuning")]
    public float lookaheadDist   = 0.8f;
    public float backupDuration  = 0.8f;

    [Header("VFX")]
    public TrailRenderer  pathTrail;
    public ParticleSystem reachGoalFX;

    // dependencies (wired in NavigationCoordinator or in Awake)
    private Rigidbody        rb;
    private MazeGenerator    maze;
    public NavigationCoordinator nav;
    private LocalPlanner     local;

    // current plan
    private List<Vector3> currentPath = new List<Vector3>();
    private int  pathIndex  = 0;
    private bool isMoving   = false;
    private bool goalReached = false;

    // stats
    public int   CollisionCount    { get; private set; }
    public float DistanceTravelled { get; private set; }
    public int   ReplanCount       { get; private set; }
    public float TimeElapsed       { get; private set; }
    public LocalPlanner.State CurrentState => local != null ? local.CurrentState : LocalPlanner.State.Cruise;
    public float CurrentSpeed => local != null ? local.CurrentSpeed : 0f;
    public string CurrentDecisionReason => local != null ? local.DecisionReason : "Idle";

    private Vector3 lastPos;
    private bool hasReceivedInitialPath = false;
    
    // collision cooldown
    private float lastCollisionTime = 0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.mass = 1f;
        rb.useGravity = false;  // 🔥 No gravity in maze
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.sleepThreshold = 0f;
#if UNITY_6000_0_OR_NEWER
        rb.linearDamping  = 0f;
        rb.angularDamping = 0f;
#else
        rb.drag        = 0f;
        rb.angularDrag = 0f;
#endif
        maze = FindObjectOfType<MazeGenerator>();
        nav  = FindObjectOfType<NavigationCoordinator>();
        if (obstacleLayer == 0)
            obstacleLayer = LayerMask.GetMask("Obstacle");
        local = new LocalPlanner(rb, transform, obstacleLayer, moveSpeed, rotateSpeed)
        {
            lookaheadDist = lookaheadDist,
            hardStopDist = hardStopDist,
            avoidDist = avoidDist,
            backupDuration = backupDuration,
            turnStopAngle = 20f
        };
        SetupTrail();
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

    // ── lifecycle ────────────────────────────────────────────
    public void PlaceAtStart(Vector3 startPos)
    {
        rb.isKinematic = true;
        rb.position = startPos;
        transform.position = startPos;
        rb.isKinematic = false;
        rb.Sleep();
        isMoving = false;
        goalReached = false;
        local?.Reset();
    }

    public void StartNavigation(Vector3 start, Vector3 goal)
    {
        rb.isKinematic = true;
        rb.position = start;
        transform.position = start;
        rb.isKinematic = false;
        rb.Sleep();

        goalReached = false;
        CollisionCount = 0;
        DistanceTravelled = 0f;
        ReplanCount = 0;
        TimeElapsed = 0f;
        lastPos = start;
        lastCollisionTime = 0f;  // Reset collision cooldown
        hasReceivedInitialPath = false;
        local?.Reset();

        if (pathTrail != null) { pathTrail.Clear(); pathTrail.emitting = true; }

        nav.RequestInitialPath(start, goal, OnPathResult);
    }

    // ── path plumbing — called by NavigationCoordinator ─────
    public void OnPathResult(List<Vector3> path)
    {
        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("[Robot] Planner returned empty path — stopping.");
            isMoving = false;
            rb.Sleep();
            return;
        }
        currentPath = path;
        pathIndex = 0;
        isMoving = true;
        if (hasReceivedInitialPath)
            ReplanCount++;
        else
            hasReceivedInitialPath = true;
    }

    public List<Vector3> GetCurrentPath() => currentPath;
    public int GetPathIndex() => pathIndex;
    public bool IsMoving() => isMoving;
    public bool IsGoalReached() => goalReached;

    // ── main loop ───────────────────────────────────────────
    void FixedUpdate()
    {
        if (!isMoving || goalReached || currentPath.Count == 0) return;

        LocalPlanner.MotionCommand command = local.Tick(currentPath, ref pathIndex, waypointReachDist);
        ApplyMotionCommand(command);

        // Ask coordinator for a replan if local detected trouble
        if (local.NeedsReplan)
            nav.RequestReplan(rb.position, maze.GoalWorldPos, OnPathResult);

        // Have we reached the final waypoint?
        if (pathIndex >= currentPath.Count - 1 &&
            Vector3.Distance(rb.position, currentPath[currentPath.Count - 1]) < waypointReachDist)
        {
            CheckGoal();
        }

        // Stats
        TimeElapsed += Time.fixedDeltaTime;
        DistanceTravelled += Vector3.Distance(rb.position, lastPos);
        lastPos = rb.position;
    }

    private void CheckGoal()
    {
        float threshold = maze != null ? maze.cellSize * 1.2f : 3.6f;
        if (Vector3.Distance(rb.position, maze.GoalWorldPos) < threshold)
        {
            goalReached = true;
            isMoving = false;
            rb.Sleep();
            if (reachGoalFX != null) reachGoalFX.Play();
            if (pathTrail != null) pathTrail.emitting = false;
            GameManager.Instance?.SetGoalReached();
        }
        else
        {
            // Got to end of waypoints but we're not at the goal — force a replan
            nav.RequestReplan(rb.position, maze.GoalWorldPos, OnPathResult);
        }
    }

    private void OnCollisionEnter(Collision col)
    {
        bool countable = col.gameObject.CompareTag("Obstacle") || col.gameObject.CompareTag("Wall");
        if (!countable) return;
        if (Time.time - lastCollisionTime < 0.5f) return;

        lastCollisionTime = Time.time;
        CollisionCount++;
        Debug.Log($"[COLLISION] Hit {col.gameObject.name} | Count:{CollisionCount}");
        GameManager.Instance?.AddCollision();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Goal"))
        {
            Debug.Log("[GOAL] Trigger entered!");
            GameManager.Instance?.SetGoalReached();
        }
    }

    private void ApplyMotionCommand(LocalPlanner.MotionCommand command)
    {
        float maxDegreesDelta = rotateSpeed * Mathf.Rad2Deg * Time.fixedDeltaTime;
        Quaternion nextRotation = Quaternion.RotateTowards(rb.rotation, command.TargetRotation, maxDegreesDelta);
        rb.MoveRotation(nextRotation);

        Vector3 forward = nextRotation * Vector3.forward;
        Vector3 move = forward * command.SignedSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);
    }
}
