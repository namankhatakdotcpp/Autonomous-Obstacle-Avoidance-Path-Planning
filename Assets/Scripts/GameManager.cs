using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("References")]
    public MazeGenerator    mazeGenerator;
    public AStarPathfinder  pathfinder;
    public RobotController  robot;
    public ObstacleManager  obstacleManager;

    [Header("Cameras")]
    public Camera mainCamera;
    public Camera minimapCamera;

    [Header("Isometric Camera")]
    public float camHeight      = 30f;
    public float camAngle       = 55f;
    public float camFollowSpeed = 4f;
    public bool  followRobot    = true;

    [Header("UI")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI collisionText;
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI replanText;
    public Button          startButton;
    public Button          regenerateButton;
    public Slider          mazeSizeSlider;
    public TextMeshProUGUI mazeSizeLabel;
    public GameObject      goalReachedPanel;

    private float elapsedTime = 0f;
    private bool  running     = false;

    // ────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        startButton      ?.onClick.AddListener(StartSimulation);
        regenerateButton ?.onClick.AddListener(RegenerateMaze);
        mazeSizeSlider   ?.onValueChanged.AddListener(OnSizeSliderChanged);

        if (mainCamera == null) mainCamera = Camera.main;

        RegenerateMaze();
        if (goalReachedPanel) goalReachedPanel.SetActive(false);
    }

    void Update()
    {
        if (running)
        {
            elapsedTime += Time.deltaTime;
            UpdateUI();
        }

        if (followRobot && robot != null && robot.IsMoving())
            SmoothFollowRobot();
    }

    // ────────────────────────────────────────────────────────
    public void RegenerateMaze()
    {
        running = false;
        if (goalReachedPanel) goalReachedPanel.SetActive(false);

        // 1. Build maze
        mazeGenerator.GenerateMaze();

        // 2. Spawn obstacles
        obstacleManager?.SpawnObstacles();

        // 3. Place robot INSIDE the maze at the start cell
        //    Must happen AFTER GenerateMaze() so StartWorldPos is valid
        robot.PlaceAtStart(mazeGenerator.StartWorldPos);

        // 4. Aim cameras at the maze
        CenterCameraOnMaze();
        SetupMinimapCamera();

        if (statusText) statusText.text = "Maze ready — press Start!";
        elapsedTime = 0f;
    }

    public void StartSimulation()
    {
        if (goalReachedPanel) goalReachedPanel.SetActive(false);
        elapsedTime = 0f;
        running     = true;

        robot.StartNavigation(mazeGenerator.StartWorldPos, mazeGenerator.GoalWorldPos);

        if (statusText) statusText.text = "Navigating…";
    }

    public void OnGoalReached()
    {
        running = false;
        if (statusText) statusText.text = "Goal reached!";
        if (goalReachedPanel) goalReachedPanel.SetActive(true);
        StartCoroutine(AutoReset(5f));
    }

    private IEnumerator AutoReset(float delay)
    {
        yield return new WaitForSeconds(delay);
        RegenerateMaze();
    }

    // ── UI ───────────────────────────────────────────────────
    private void UpdateUI()
    {
        if (timeText)      timeText.text      = $"Time: {elapsedTime:F1}s";
        if (collisionText) collisionText.text = $"Collisions: {robot.CollisionCount}";
        if (replanText)    replanText.text    = $"Replans: {robot.ReplanCount}";
    }

    private void OnSizeSliderChanged(float val)
    {
        int size = Mathf.RoundToInt(val);
        if (size % 2 == 0) size++;
        mazeGenerator.width  = size;
        mazeGenerator.height = size;
        if (mazeSizeLabel) mazeSizeLabel.text = $"Maze: {size}×{size}";
    }

    // ── cameras ──────────────────────────────────────────────
    private void CenterCameraOnMaze()
    {
        if (mainCamera == null) return;

        float mx = mazeGenerator.GridWidth  * mazeGenerator.cellSize * 0.5f;
        float mz = mazeGenerator.GridHeight * mazeGenerator.cellSize * 0.5f;

        float rad    = camAngle * Mathf.Deg2Rad;
        float dist   = camHeight / Mathf.Sin(rad);
        float offset = dist * Mathf.Cos(rad);

        mainCamera.transform.position = new Vector3(mx, camHeight, mz - offset);
        mainCamera.transform.LookAt(new Vector3(mx, 0f, mz));
    }

    private void SmoothFollowRobot()
    {
        if (mainCamera == null || robot == null) return;

        float mx = mazeGenerator.GridWidth  * mazeGenerator.cellSize * 0.5f;
        float mz = mazeGenerator.GridHeight * mazeGenerator.cellSize * 0.5f;

        Vector3 follow = Vector3.Lerp(new Vector3(mx, 0, mz), robot.transform.position, 0.4f);

        float rad    = camAngle * Mathf.Deg2Rad;
        float dist   = camHeight / Mathf.Sin(rad);
        float offset = dist * Mathf.Cos(rad);

        Vector3 desired = new Vector3(follow.x, camHeight, follow.z - offset);
        mainCamera.transform.position = Vector3.Lerp(mainCamera.transform.position, desired, camFollowSpeed * Time.deltaTime);
        mainCamera.transform.LookAt(new Vector3(follow.x, 0f, follow.z));
    }

    private void SetupMinimapCamera()
    {
        if (minimapCamera == null) return;

        float mx = mazeGenerator.GridWidth  * mazeGenerator.cellSize * 0.5f;
        float mz = mazeGenerator.GridHeight * mazeGenerator.cellSize * 0.5f;

        minimapCamera.orthographic      = true;
        minimapCamera.clearFlags        = CameraClearFlags.SolidColor;
        minimapCamera.backgroundColor   = new Color(0.05f, 0.05f, 0.1f);
        minimapCamera.transform.position = new Vector3(mx, 80f, mz);
        minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        minimapCamera.orthographicSize   = Mathf.Max(
            mazeGenerator.GridWidth  * mazeGenerator.cellSize * 0.55f,
            mazeGenerator.GridHeight * mazeGenerator.cellSize * 0.55f);
    }
}