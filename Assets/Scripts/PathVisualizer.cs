using System.Collections.Generic;
using UnityEngine;

// Attach to a GameObject alongside a LineRenderer component.
// This draws the planned A* path as a glowing line above the maze floor.
[RequireComponent(typeof(LineRenderer))]
public class PathVisualizer : MonoBehaviour
{
    [Header("Visual Settings")]
    public float lineHeight = 0.3f;
    public float lineWidth = 0.15f;
    public Color pathColor = new Color(0f, 1f, 1f, 0.85f);
    public Color goalColor = new Color(1f, 0.5f, 0f, 1f);

    [Header("Goal Marker")]
    public GameObject goalMarker;
    public float goalPulseSpeed = 2f;
    public float goalPulseScale = 0.3f;

    private LineRenderer lr;
    private RobotController robot;
    private AStarPathfinder pathfinder;
    private List<Vector3> lastPath = new List<Vector3>();
    private Vector3 goalBaseScale;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        robot = FindObjectOfType<RobotController>();

        SetupLineRenderer();

        if (goalMarker != null)
            goalBaseScale = goalMarker.transform.localScale;
    }

    void SetupLineRenderer()
    {
        lr.startWidth = lineWidth;
        lr.endWidth   = lineWidth * 0.5f;
        lr.useWorldSpace = true;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = pathColor;
        lr.endColor   = goalColor;
        lr.numCapVertices = 4;
        lr.sortingOrder = 10;
    }

    void Update()
    {
        // Grab current path from pathfinder debug
        if (robot == null) return;

        // Pulse goal marker
        if (goalMarker != null)
        {
            float pulse = 1f + Mathf.Sin(Time.time * goalPulseSpeed) * goalPulseScale;
            goalMarker.transform.localScale = goalBaseScale * pulse;
            goalMarker.transform.Rotate(Vector3.up, 60f * Time.deltaTime);
        }
    }

    public void DrawPath(List<Vector3> path)
    {
        if (path == null || path.Count == 0)
        {
            lr.positionCount = 0;
            return;
        }

        lr.positionCount = path.Count;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 p = path[i];
            p.y = lineHeight;
            lr.SetPosition(i, p);
        }
    }

    public void SetGoalPosition(Vector3 pos)
    {
        if (goalMarker != null)
            goalMarker.transform.position = pos + Vector3.up * 0.5f;
    }

    public void ClearPath()
    {
        lr.positionCount = 0;
    }
}