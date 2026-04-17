# Smart Robot Navigation - Autonomous Path Planning & Obstacle Avoidance

A Unity-based autonomous navigation system featuring advanced pathfinding algorithms, dynamic maze generation, and real-time obstacle avoidance. The robot intelligently navigates complex environments using A* pathfinding with multi-layer collision prevention.

## 🎯 Features

- **Advanced A* Pathfinding**: Efficient grid-based pathfinding algorithm for finding optimal paths
- **Dynamic Maze Generation**: Procedurally generated mazes using recursive backtracking algorithm
- **Real-time Obstacle Avoidance**: Multi-layer collision detection and dynamic path replanning
- **Smooth Robot Movement**: Interpolated rotation and velocity-based movement system
- **Path Visualization**: Visual debugging tools to display computed paths in the scene
- **Performance Metrics**: Real-time tracking of collisions, replans, distance traveled, and elapsed time
- **Trail Effect**: Visual trail showing robot movement history
- **Isometric Camera**: Smooth camera following with adjustable isometric projection

## 🏗️ Architecture

### Core Components

#### **AStarPathfinder.cs**
- Implements A* pathfinding algorithm with open/closed sets
- Converts between world and grid coordinates
- Supports dynamic obstacle avoidance
- Handles pathfinding failures gracefully

#### **MazeGenerator.cs**
- Generates random mazes using recursive backtracking (depth-first search)
- Creates procedural passageways and walls
- Manages grid walkability for pathfinding
- Supports temporary obstacle overlays from dynamic objects
- Provides coordinate conversion utilities (world ↔ grid)

#### **RobotController.cs**
- Autonomous robot movement and navigation
- Three-layer collision prevention system:
  - **Layer 1**: Predictive look-ahead replan (checks upcoming waypoints)
  - **Layer 2**: Speed modulation based on obstacle proximity
  - **Layer 3**: Emergency full stop with immediate replan
- Path smoothing via corner detection
- Movement statistics tracking
- Rigidbody-based physics integration

#### **ObstacleManager.cs**
- Manages dynamic obstacles in the scene
- Marks obstacle areas as temporary walls in the pathfinding grid
- Enables dynamic replanning around moving objects

#### **GameManager.cs**
- Central game orchestration and state management
- UI management and event coordination
- Camera control and maze regeneration
- Performance metric display
- Button input handling

#### **PathVisualizer.cs**
- Renders computed paths as visual debug lines
- Displays waypoints and path segments in the scene editor

## 🛠️ Technical Details

### Pathfinding Algorithm
The A* algorithm uses:
- **Open List**: Priority queue of nodes sorted by f-score (g + h)
- **Closed Set**: HashSet for O(1) visited node lookup
- **Heuristic**: Manhattan distance for grid-based movement
- **Cost**: Uniform 1.0 per cell movement

### Grid System
- 21×21 maze grid (configurable, always odd dimensions)
- Cell size: 3.0 units (configurable)
- Walls marked as `true`, paths marked as `false`
- Supports temporary wall overlays for obstacle avoidance

### Collision Prevention
- **Raycast Detection**: 8-directional obstacle detection around robot
- **Sphere Overlap**: Detects nearby obstacles within hard-stop distance
- **Linecast Prediction**: Checks upcoming waypoints for blockages
- **Adaptive Replanning**: Recalculates path every 0.4 seconds when obstacles detected

### Movement System
- Smooth acceleration/deceleration with damping
- Quaternion-based rotation interpolation
- Y-axis velocity preserved for physics interaction
- Configurable waypoint reach distance for waypoint consumption

## 🎮 How to Use

1. **Open the Project**: Load in Unity 2022 LTS or newer
2. **Play the Scene**: Press Play button in editor
3. **Regenerate Maze**: Click "Regenerate" button to create new maze
4. **Start Navigation**: Click "Start" button to begin autonomous navigation
5. **Adjust Maze Size**: Use slider to change maze dimensions (5-99)
6. **Monitor Stats**: Watch real-time performance metrics in UI

## 📊 UI Elements

- **Start Button**: Initiates robot navigation from start to goal
- **Regenerate Button**: Creates new random maze layout
- **Maze Size Slider**: Adjusts maze dimensions (minimum 5, maximum 99)
- **Status Text**: Current navigation state
- **Collision Counter**: Number of collisions with walls/obstacles
- **Time Display**: Elapsed navigation time
- **Replan Counter**: Number of path recalculations
- **Goal Reached Panel**: Displays when robot reaches goal, auto-resets after 5 seconds

## ⚙️ Configuration

All parameters are exposed in the Inspector for easy tuning:

### Robot Movement
- `moveSpeed`: Forward velocity (default: 8 m/s)
- `rotateSpeed`: Rotation responsiveness (default: 12)
- `waypointReachDist`: Distance to consume waypoint (default: 1.2)

### Collision Prevention
- `lookAheadWaypoints`: How many waypoints to scan ahead (default: 6)
- `hardStopDistance`: Full stop trigger distance (default: 2.0)
- `slowdownDistance`: Start slowing at this distance (default: 4.5)
- `replanCooldown`: Minimum time between replans (default: 0.4s)

### Maze Generation
- `width`/`height`: Grid dimensions (default: 21)
- `cellSize`: Physical size per grid cell (default: 3.0)
- `wallHeight`: Visual height of wall meshes (default: 4.0)

## 🐛 Debugging

Enable console logging to see detailed pathfinding and navigation messages:

```
🎮 START SIMULATION CALLED
🔍 A* Pathfinding: start=(1,1), goal=(19,19)
✅ Neighbors from (1,1): count=3
✅ A* PATH FOUND: 37 waypoints
✅ PATH FOUND: 37 waypoints (after smoothing)
🚀 Robot ready to move: isMoving=True
```

Watch the console for:
- Pathfinding success/failure
- Collision detections
- Replanning triggers
- Navigation completion

## 📁 Project Structure

```
Assets/
├── Scripts/
│   ├── AStarPathfinder.cs
│   ├── GameManager.cs
│   ├── MazeGenerator.cs
│   ├── ObstacleManager.cs
│   ├── PathVisualizer.cs
│   └── RobotController.cs
├── Scenes/
│   └── SampleScene/
├── Prefabs/
├── Materials/
└── UI/
```

## 🎓 Learning Resources

This project demonstrates:
- **A* Pathfinding**: Efficient shortest-path algorithms in games
- **Procedural Generation**: Creating dynamic game content
- **Physics Integration**: Rigidbody movement and collision detection
- **Real-time Planning**: Dynamic path recalculation
- **UI/UX Design**: Game state management and feedback
- **Performance Optimization**: Efficient grid operations and spatial queries

## 🚀 Future Enhancements

- RRT* (Rapidly-exploring Random Trees) pathfinding option
- Multi-robot coordination
- Theta* for any-angle pathfinding
- Velocity obstacles for smooth collision avoidance
- Procedural terrain generation
- Multiple goal sequencing

## 📝 License

Open source - feel free to use and modify

## 👤 Author

Created as an autonomous navigation demonstration project

---

**Status**: ✅ Fully Functional - Robot successfully navigates mazes with real-time obstacle avoidance
