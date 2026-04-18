using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grid-aligned local planner for maze navigation.
/// Behavior is intentionally discrete:
/// - target the current waypoint instead of a far lookahead
/// - snap desired movement to the maze axes
/// - stop, rotate, then move
/// - request a replan only when the path is blocked in front
/// </summary>
public class LocalPlanner
{
    public enum State { Cruise, Avoid, Backup }

    public struct MotionCommand
    {
        public Quaternion TargetRotation;
        public float SignedSpeed;

        public static MotionCommand Stop(Quaternion rotation)
        {
            return new MotionCommand
            {
                TargetRotation = rotation,
                SignedSpeed = 0f
            };
        }
    }

    private struct ObstacleContext
    {
        public bool frontBlocked;
        public bool leftBlocked;
        public bool rightBlocked;
        public float frontDistance;
        public float leftDistance;
        public float rightDistance;
    }

    public float lookaheadDist = 0.8f;
    public float avoidDist = 3.5f;
    public float hardStopDist = 0.4f;
    public float backupDuration = 0.8f;
    public float turnStopAngle = 20f;
    public float replanCooldown = 0.5f;

    public State CurrentState { get; private set; } = State.Cruise;
    public float CurrentSpeed { get; private set; }
    public string DecisionReason { get; private set; } = "Idle";
    public bool NeedsReplan { get; private set; }
    public float NearestObstacleDist { get; private set; } = float.MaxValue;
    public Vector3 CurrentLookaheadPoint { get; private set; }
    public float CurrentCurvature { get; private set; }

    private readonly Rigidbody rb;
    private readonly Transform tf;
    private readonly LayerMask obstacleLayer;
    private readonly float moveSpeed;

    private float backupTimer;
    private float blockedTimer;
    private float lastReplanTime;
    private float lastLogTime;

    public LocalPlanner(Rigidbody rb, Transform tf, LayerMask obstacleLayer,
                        float moveSpeed, float rotateSpeed)
    {
        this.rb = rb;
        this.tf = tf;
        this.obstacleLayer = obstacleLayer;
        this.moveSpeed = moveSpeed;
        lastReplanTime = -999f;
    }

    public void Reset()
    {
        CurrentState = State.Cruise;
        CurrentSpeed = 0f;
        DecisionReason = "Ready";
        NeedsReplan = false;
        NearestObstacleDist = float.MaxValue;
        CurrentLookaheadPoint = rb.position;
        CurrentCurvature = 0f;

        backupTimer = 0f;
        blockedTimer = 0f;
        lastReplanTime = -999f;
        lastLogTime = 0f;
    }

    public MotionCommand Tick(List<Vector3> path, ref int pathIndex, float reachDist)
    {
        NeedsReplan = false;

        if (path == null || path.Count == 0)
        {
            CurrentState = State.Cruise;
            CurrentSpeed = 0f;
            DecisionReason = "No path";
            CurrentCurvature = 0f;
            NearestObstacleDist = float.MaxValue;
            return MotionCommand.Stop(rb.rotation);
        }

        if (CurrentState == State.Backup)
            return TickBackup();

        AdvanceWaypoint(path, ref pathIndex, reachDist);

        Vector3 target = GetTargetPoint(path, pathIndex);
        CurrentLookaheadPoint = target;
        CurrentCurvature = 0f;

        Vector3 snappedDir = GetAxisAlignedDirection(target);
        ObstacleContext context = SenseObstacles();
        NearestObstacleDist = Mathf.Min(context.frontDistance, Mathf.Min(context.leftDistance, context.rightDistance));

        float angle = Vector3.Angle(tf.forward, snappedDir);

        if (context.frontDistance < hardStopDist)
        {
            CurrentState = State.Backup;
            CurrentSpeed = 0f;
            DecisionReason = "Too close -> backup";
            backupTimer = 0f;
            return TickBackup();
        }

        if (angle > turnStopAngle)
        {
            CurrentState = State.Cruise;
            CurrentSpeed = 0f;
            DecisionReason = "Stop then turn";
            blockedTimer = 0f;
            LogState("turn");
            return new MotionCommand
            {
                TargetRotation = Quaternion.LookRotation(snappedDir, Vector3.up),
                SignedSpeed = 0f
            };
        }

        if (context.frontBlocked)
        {
            CurrentState = State.Avoid;
            CurrentSpeed = 0f;
            DecisionReason = "Blocked ahead";
            blockedTimer += Time.fixedDeltaTime;

            if (blockedTimer >= 0.2f && Time.time - lastReplanTime >= replanCooldown)
            {
                NeedsReplan = true;
                lastReplanTime = Time.time;
            }

            LogState("blocked");
            return new MotionCommand
            {
                TargetRotation = Quaternion.LookRotation(snappedDir, Vector3.up),
                SignedSpeed = 0f
            };
        }

        blockedTimer = 0f;
        CurrentState = State.Cruise;
        CurrentSpeed = moveSpeed;
        DecisionReason = "Following grid path";

        LogState("move");
        return new MotionCommand
        {
            TargetRotation = Quaternion.LookRotation(snappedDir, Vector3.up),
            SignedSpeed = moveSpeed
        };
    }

    private MotionCommand TickBackup()
    {
        backupTimer += Time.fixedDeltaTime;
        CurrentState = State.Backup;
        CurrentSpeed = moveSpeed * 0.5f;
        DecisionReason = "Backing up";

        if (backupTimer >= backupDuration)
        {
            backupTimer = 0f;
            CurrentState = State.Cruise;
            CurrentSpeed = 0f;
            DecisionReason = "Recovery complete";

            if (Time.time - lastReplanTime >= replanCooldown)
            {
                NeedsReplan = true;
                lastReplanTime = Time.time;
            }

            return MotionCommand.Stop(rb.rotation);
        }

        return new MotionCommand
        {
            TargetRotation = rb.rotation,
            SignedSpeed = -moveSpeed * 0.5f
        };
    }

    private void AdvanceWaypoint(List<Vector3> path, ref int index, float reachDist)
    {
        while (index < path.Count - 1 &&
               HorizontalDistance(rb.position, path[index]) < reachDist)
        {
            index++;
        }
    }

    private Vector3 GetTargetPoint(List<Vector3> path, int index)
    {
        if (index < 0)
            index = 0;

        if (index >= path.Count)
            return Flatten(path[path.Count - 1], rb.position.y);

        return Flatten(path[index], rb.position.y);
    }

    private Vector3 GetAxisAlignedDirection(Vector3 target)
    {
        Vector3 dir = target - tf.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.001f)
            return tf.forward;

        if (Mathf.Abs(dir.x) > Mathf.Abs(dir.z))
            dir = new Vector3(Mathf.Sign(dir.x), 0f, 0f);
        else
            dir = new Vector3(0f, 0f, Mathf.Sign(dir.z));

        return dir.normalized;
    }

    private ObstacleContext SenseObstacles()
    {
        Vector3 origin = tf.position + Vector3.up * 0.3f;
        Vector3 leftDir = Quaternion.AngleAxis(-30f, Vector3.up) * tf.forward;
        Vector3 rightDir = Quaternion.AngleAxis(30f, Vector3.up) * tf.forward;

        bool frontBlocked = Physics.Raycast(origin, tf.forward, out RaycastHit frontHit, avoidDist, obstacleLayer, QueryTriggerInteraction.Ignore);
        bool leftBlocked = Physics.Raycast(origin, leftDir, out RaycastHit leftHit, avoidDist, obstacleLayer, QueryTriggerInteraction.Ignore);
        bool rightBlocked = Physics.Raycast(origin, rightDir, out RaycastHit rightHit, avoidDist, obstacleLayer, QueryTriggerInteraction.Ignore);

        Debug.DrawRay(origin, tf.forward * avoidDist, Color.red);
        Debug.DrawRay(origin, leftDir * avoidDist, Color.yellow);
        Debug.DrawRay(origin, rightDir * avoidDist, Color.green);

        return new ObstacleContext
        {
            frontBlocked = frontBlocked,
            leftBlocked = leftBlocked,
            rightBlocked = rightBlocked,
            frontDistance = frontBlocked ? frontHit.distance : float.MaxValue,
            leftDistance = leftBlocked ? leftHit.distance : float.MaxValue,
            rightDistance = rightBlocked ? rightHit.distance : float.MaxValue
        };
    }

    private void LogState(string mode)
    {
        if (Time.time - lastLogTime <= 0.25f)
            return;

        lastLogTime = Time.time;
        string obsStr = NearestObstacleDist < float.MaxValue
            ? NearestObstacleDist.ToString("F2")
            : "clear";
        Debug.Log($"[LP] {CurrentState,-7} | Mode:{mode} | Spd:{CurrentSpeed:F1} | Obs:{obsStr}m | Reason:{DecisionReason}");
    }

    private static Vector3 Flatten(Vector3 point, float y)
    {
        point.y = y;
        return point;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt((dx * dx) + (dz * dz));
    }
}
