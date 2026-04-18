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

    [Header("UI — Core")]
    public TextMeshProUGUI collisionsText;
    public TextMeshProUGUI statusText;
    public GameObject successPanel;

    [Header("UI — Legacy Scene Refs")]
    public TextMeshProUGUI collisionText;
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI replanText;
    public TextMeshProUGUI scoreText;

    [Header("UI — Extended Dashboard (optional — wire in Inspector)")]
    public TextMeshProUGUI speedText;    // live robot speed
    public TextMeshProUGUI distText;     // distance remaining to goal
    public TextMeshProUGUI wpText;       // waypoint progress  X / Y

    [Header("UI — Controls")]
    public Button          startButton;
    public Button          regenerateButton;
    public Slider          mazeSizeSlider;
    public TextMeshProUGUI mazeSizeLabel;
    public GameObject      goalReachedPanel;

    private float elapsedTime = 0f;
    private bool  running     = false;
    private int   collisionCount = 0;
    private bool  goalReached = false;

    // ────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        if (collisionsText == null) collisionsText = collisionText;
        if (successPanel == null) successPanel = goalReachedPanel;

        EnsureSuccessPanel();

        if (successPanel != null)
            successPanel.SetActive(false);

        UpdateCollisionUI();
        if (statusText != null && string.IsNullOrWhiteSpace(statusText.text))
            statusText.text = "Running...";
    }

    void Start()
    {
        startButton      ?.onClick.AddListener(StartGame);
        regenerateButton ?.onClick.AddListener(RegenerateMaze);
        mazeSizeSlider   ?.onValueChanged.AddListener(OnSizeSliderChanged);

        if (mainCamera == null) mainCamera = Camera.main;

        RegenerateMaze();
    }

    void Update()
    {
        if (running)
        {
            elapsedTime += Time.deltaTime;
            if (timeText)
                timeText.text = $"Time: {elapsedTime:F2}s";
            UpdateUI();

            // Fail on timeout (2 minutes)
            if (elapsedTime > 120f)
            {
                running = false;
                if (statusText)     statusText.text = "❌ TIMEOUT";
                if (successPanel) successPanel.SetActive(true);
                StartCoroutine(AutoReset(5f));
            }
        }

        if (followRobot && robot != null && robot.IsMoving())
            SmoothFollowRobot();
    }

    // ────────────────────────────────────────────────────────
    public void RegenerateMaze()
    {
        running = false;
        goalReached = false;
        collisionCount = 0;
        if (successPanel) successPanel.SetActive(false);
        UpdateCollisionUI();

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

    public void StartGame()
    {
        elapsedTime = 0f;
        running = true;
    }

    public void StartSimulation()
    {
        if (successPanel) successPanel.SetActive(false);
        elapsedTime = 0f;
        goalReached = false;
        running     = true;

        robot.StartNavigation(mazeGenerator.StartWorldPos, mazeGenerator.GoalWorldPos);

        if (statusText) statusText.text = "Running...";
    }

    public void OnGoalReached()
    {
        SetGoalReached();
        StartCoroutine(AutoReset(5f));
    }

    public void SetGoalReached()
    {
        running = false;
        if (statusText) statusText.text = "Goal Reached!";
        if (successPanel) successPanel.SetActive(true);
    }

    public void AddCollision()
    {
        collisionCount++;
        UpdateCollisionUI();

        if (collisionCount > 20 && running)
        {
            running = false;
            if (statusText) statusText.text = "❌ FAILED (Too many collisions)";
            if (successPanel) successPanel.SetActive(true);
            StartCoroutine(AutoReset(5f));
        }
    }

    public void RegisterCollision() => AddCollision();

    /// <summary>
    /// Calculate score: Time + (Collisions * 5) + (Replans * 2)
    /// Lower = better
    /// </summary>
    public float GetScore()
    {
        if (robot == null) return elapsedTime;
        // Lower is better: time + collision penalty + replan penalty
        return elapsedTime + (collisionCount * 5f) + (robot.ReplanCount * 2f);
    }

    public int GetCollisionCount() => collisionCount;

    private IEnumerator AutoReset(float delay)
    {
        yield return new WaitForSeconds(delay);
        RegenerateMaze();
    }

    // ── UI ───────────────────────────────────────────────────
    private void UpdateUI()
    {
        UpdateCollisionUI();
        if (replanText)    replanText.text    = $"Replans: {robot?.ReplanCount ?? 0}";
        if (scoreText)     scoreText.text     = $"Score: {GetScore():F0}";

        if (statusText && robot != null && running && !goalReached)
            statusText.text = $"State: {robot.CurrentState} | {robot.CurrentDecisionReason}";

        // Extended dashboard — only populated if the TextMeshPro fields are wired
        if (speedText && robot != null)
            speedText.text = $"Speed: {robot.CurrentSpeed:F1} m/s";

        if (distText && robot != null && mazeGenerator != null)
        {
            float dist = Vector3.Distance(robot.transform.position, mazeGenerator.GoalWorldPos);
            distText.text = $"Goal: {dist:F1} m";
        }

        if (wpText && robot != null)
        {
            int total = robot.GetCurrentPath()?.Count ?? 0;
            wpText.text = $"WP: {robot.GetPathIndex()} / {total}";
        }
    }

    private void UpdateCollisionUI()
    {
        if (collisionsText != null)
            collisionsText.text = "Collisions: " + collisionCount;

        if (collisionText != null && collisionText != collisionsText)
            collisionText.text = "Collisions: " + collisionCount;
    }

    private void EnsureSuccessPanel()
    {
        if (successPanel != null) return;

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        GameObject panelObject = new GameObject("SuccessPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(canvas.transform, false);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(360f, 140f);
        panelRect.anchoredPosition = Vector2.zero;

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.08f, 0.12f, 0.08f, 0.9f);

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(panelRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(20f, 20f);
        textRect.offsetMax = new Vector2(-20f, -20f);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = "🎉 Goal Reached!";
        text.fontSize = 32f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;

        successPanel = panelObject;
        goalReachedPanel = panelObject;
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
