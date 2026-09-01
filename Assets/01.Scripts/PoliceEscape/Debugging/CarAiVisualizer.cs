using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.Debugging;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Debugging
{
    /// <summary>
    /// Draws what one AI car is thinking: the route it queued, the point it is
    /// really steering at, and the collision-prevention fan that decides
    /// whether it may keep going. The focused car is drawn in full; every
    /// other AI car gets a dimmed route, so a pile-up two streets away is
    /// still visible without picking each car apart.
    ///
    /// <b>The steer aim is drawn apart from the waypoint on purpose.</b> The
    /// drivers do not aim at the cell centre — they aim beside it, in the
    /// right-hand lane — and almost every "car orbits a junction forever" bug
    /// is the gap between those two points. A path overlay that only drew the
    /// waypoints would hide exactly the thing worth seeing.
    ///
    /// Probes come from the driver's own record of the casts it made
    /// (<see cref="AiProbe"/>), never from re-casting here: a re-cast shows
    /// what the rays would say now, which is a different question from what
    /// the car decided on.
    /// </summary>
    [DisallowMultipleComponent]
    public class CarAiVisualizer : DebugVisualizer
    {
        /// <summary>Which car gets the full treatment.</summary>
        public enum FocusMode
        {
            /// <summary>Whatever is selected in the editor hierarchy (its car, or a parent of it).</summary>
            SceneSelection,

            /// <summary>The AI car nearest the player — the one about to matter.</summary>
            NearestToPlayer,

            /// <summary>Every AI car in full detail. Loud, but honest.</summary>
            All,
        }

        [TitleGroup("Focus")]
        [Tooltip("How the focused car is chosen. Scene selection follows what you click in the hierarchy while playing.")]
        public FocusMode focusMode = FocusMode.SceneSelection;

        [TitleGroup("Focus")]
        [Tooltip("Draw a dimmed route for every car that is not focused, so the rest of the fleet stays visible.")]
        public bool dimUnfocused = true;

        [TitleGroup("Focus")]
        [Tooltip("Unfocused cars farther than this from the focus (or the player) are skipped entirely.")]
        [PropertyRange(20f, 1000f)]
        public float unfocusedRange = 200f;

        [TitleGroup("Draw")]
        [Tooltip("Metres the route is lifted off the road, so it does not z-fight the asphalt.")]
        [PropertyRange(0f, 3f)]
        public float lift = 0.6f;

        [TitleGroup("Draw")]
        [Tooltip("Draw the police line-of-sight ray (the test that starts and ends a chase).")]
        public bool showSightLine = true;

        [TitleGroup("Draw")]
        [Tooltip("Scene-view text over each drawn car: state, obstacle verdict, speed. Editor only.")]
        public bool showLabels = true;

        static readonly Color ProbeClearColor = new(0.35f, 1f, 0.4f, 0.7f);
        static readonly Color ProbeTouchColor = new(1f, 0.85f, 0.2f, 0.9f);
        static readonly Color ProbeVehicleColor = new(1f, 0.5f, 0f, 1f);
        static readonly Color ProbeWallColor = new(1f, 0.15f, 0.1f, 1f);
        static readonly Color WhiskerColor = new(0.6f, 0.8f, 1f, 0.7f);
        static readonly Color AimColor = new(1f, 1f, 1f, 0.95f);
        static readonly Color OffRoadColor = new(1f, 0.4f, 0f, 1f);
        static readonly Color ReverseColor = new(1f, 0f, 0.5f, 1f);

        const float DriverScanInterval = 0.5f;

        readonly List<MonoBehaviour> drivers = new();
        readonly List<Vector3> route = new();
        float nextDriverScan;

#if UNITY_EDITOR
        readonly List<(Vector3 position, string text, Color color)> labels = new();
#endif

        [TitleGroup("Focus"), ShowInInspector, ReadOnly]
        [Tooltip("The car currently drawn in full.")]
        public string Focused { get; private set; } = "-";

        protected override bool ChannelEnabled => DebugManager.ShowCarPaths || DebugManager.ShowCollisionProbes;

        protected override void Rebuild()
        {
#if UNITY_EDITOR
            labels.Clear();
#endif
            CollectDrivers();
            if (drivers.Count == 0)
            {
                Focused = "-";
                return;
            }

            MonoBehaviour focused = PickFocused();
            Focused = focused != null ? focused.gameObject.name : "-";
            Vector3 center = focused != null ? focused.transform.position : FallbackCenter();

            foreach (MonoBehaviour behaviour in drivers)
            {
                bool full = focusMode == FocusMode.All || behaviour == focused;
                if (!full)
                {
                    if (!dimUnfocused) continue;
                    if (Vector3.Distance(behaviour.transform.position, center) > unfocusedRange) continue;
                }
                Draw((IAiDebugDriver)behaviour, behaviour.transform, full);
            }
        }

        void Draw(IAiDebugDriver driver, Transform car, bool full)
        {
            Color color = driver.StateColor;
            Vector3 up = Vector3.up * lift;

            if (DebugManager.ShowCarPaths)
            {
                // The route as the car will drive it: from where it is, through
                // every queued waypoint, arrow on the last leg.
                IReadOnlyList<Vector3> waypoints = driver.Waypoints;
                route.Clear();
                route.Add(car.position);
                for (int i = 0; i < waypoints.Count; i++) route.Add(waypoints[i]);

                Color routeColor = full ? color : Dim(color, 0.35f);
                for (int i = 1; i < route.Count; i++)
                {
                    Vector3 from = route[i - 1] + up;
                    Vector3 to = route[i] + up;
                    if (i == route.Count - 1) Lines.Arrow(from, to, routeColor, 1.2f);
                    else Lines.Line(from, to, routeColor);
                    if (full) Lines.Diamond(to, i == 1 ? 1.4f : 0.9f, routeColor);
                }

                if (full && waypoints.Count > 0)
                {
                    // The lane-offset point actually steered at, and the gap
                    // between it and the waypoint it came from — where orbiting
                    // bugs live.
                    Vector3 aim = driver.SteerAim + up;
                    Lines.Cross(aim, 0.9f, AimColor);
                    Lines.Line(car.position + up, aim, AimColor);
                    Lines.Line(waypoints[0] + up, aim, Dim(AimColor, 0.4f));
                }

                if (driver.OffRoad) Lines.Circle(car.position + up, 2.6f, OffRoadColor, 12);
                if (driver.Reversing) Lines.Circle(car.position + up, 3.2f, ReverseColor, 12);
            }

            if (!full) return;

            if (DebugManager.ShowCollisionProbes) DrawProbes(driver);
            if (showSightLine && DebugManager.ShowPerception) DrawSightLine(driver);

#if UNITY_EDITOR
            if (showLabels)
            {
                string obstacle = driver.Obstacle == ObstacleKind.None ? "clear" : driver.Obstacle.ToString().ToLowerInvariant();
                string stuck = driver.StuckTime > 0.05f ? $"  stuck {driver.StuckTime:0.0}s" : string.Empty;
                // The lane rule's direction of travel — a car whose letter
                // flips without a junction is breaking the no-U-turn rule.
                string heading = driver.TravelDirection >= 0 ? $" >{"NESW"[driver.TravelDirection]}" : string.Empty;
                // Which car this is, off the controller's identity — an
                // "unknown vehicle" here is a spawn path that forgot to stamp it.
                var controller = car.GetComponent<CarController>();
                string identity = controller != null ? $"\n{controller.identity}" : string.Empty;
                labels.Add((car.position + Vector3.up * 3f, $"{driver.StateLabel}{heading}  [{obstacle}]{stuck}{identity}", color));
            }
#endif
        }

        /// <summary>The avoidance fan as it was cast: green reached its full length, yellow touched something harmless, orange/red is what stopped the car.</summary>
        void DrawProbes(IAiDebugDriver driver)
        {
            IReadOnlyList<AiProbe> probes = driver.Probes;
            for (int i = 0; i < probes.Count; i++)
            {
                AiProbe probe = probes[i];
                Color color = probe.Verdict switch
                {
                    ObstacleKind.Wall => ProbeWallColor,
                    ObstacleKind.Vehicle => ProbeVehicleColor,
                    _ => probe.Hit ? ProbeTouchColor : probe.Role == AiProbeRole.Whisker ? WhiskerColor : ProbeClearColor,
                };
                Vector3 end = probe.End;
                Lines.Line(probe.Origin, end, color);
                if (probe.Hit) Lines.Cross(probe.HitPoint, 0.35f, color);
                // Full reach tick, so a short ray reads as "stopped early" and
                // not as "this probe is short".
                if (probe.Hit) Lines.Line(end, probe.Origin + probe.Direction.normalized * probe.Length, Dim(color, 0.25f));
            }
        }

        void DrawSightLine(IAiDebugDriver driver)
        {
            if (!driver.TryGetSightLine(out Vector3 from, out Vector3 to, out bool clear)) return;
            Color color = clear ? new Color(0.4f, 1f, 0.4f, 0.9f) : new Color(0.5f, 0.15f, 0.15f, 0.6f);
            if (clear)
            {
                Lines.Line(from, to, color);
                Lines.Cross(to, 0.6f, color);
                return;
            }
            // Blocked view drawn dashed: the police can't see through it, and
            // neither should the line.
            const int dashes = 8;
            for (int i = 0; i < dashes; i++)
            {
                float a = i / (float)dashes;
                float b = (i + 0.5f) / dashes;
                Lines.Line(Vector3.Lerp(from, to, a), Vector3.Lerp(from, to, b), color);
            }
        }

        /// <summary>
        /// Every live AI driver — police and civilians alike, since both
        /// implement the debug contract. Re-scanned on an interval rather than
        /// per frame: the overlay redraws every frame (probes are a per-frame
        /// decision) but the fleet only changes when the manager spawns or
        /// despawns a car, and two scene-wide scans a frame is a real cost.
        /// </summary>
        void CollectDrivers()
        {
            if (Time.unscaledTime < nextDriverScan)
            {
                drivers.RemoveAll(driver => driver == null);
                return;
            }
            nextDriverScan = Time.unscaledTime + DriverScanInterval;
            drivers.Clear();
            foreach (var police in FindObjectsByType<PoliceCarInput>(FindObjectsSortMode.None)) drivers.Add(police);
            foreach (var traffic in FindObjectsByType<TrafficCarInput>(FindObjectsSortMode.None)) drivers.Add(traffic);
        }

        MonoBehaviour PickFocused()
        {
            switch (focusMode)
            {
                case FocusMode.SceneSelection:
                    return SelectedDriver();

                case FocusMode.NearestToPlayer:
                {
                    Vector3 center = FallbackCenter();
                    MonoBehaviour best = null;
                    float bestDistance = float.MaxValue;
                    foreach (MonoBehaviour driver in drivers)
                    {
                        float distance = Vector3.SqrMagnitude(driver.transform.position - center);
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        best = driver;
                    }
                    return best;
                }

                default:
                    return null; // All: everyone is focused, nobody is "the" focus
            }
        }

        /// <summary>The driver on (or under) the hierarchy selection — a car is usually selected by its root, the driver may sit anywhere in it.</summary>
        MonoBehaviour SelectedDriver()
        {
#if UNITY_EDITOR
            GameObject selection = UnityEditor.Selection.activeGameObject;
            if (selection == null) return null;
            foreach (MonoBehaviour driver in drivers)
                if (driver.transform == selection.transform || driver.transform.IsChildOf(selection.transform))
                    return driver;
#endif
            return null;
        }

        Vector3 FallbackCenter()
        {
            CarController player = PatrolManager.FindPlayerCar();
            if (player != null) return player.transform.position;
            Camera camera = Camera.main;
            return camera != null ? camera.transform.position : transform.position;
        }

        static Color Dim(Color color, float alpha) => new(color.r, color.g, color.b, color.a * alpha);

#if UNITY_EDITOR
        // Labels are the one part that cannot be a GL line, so they ride the
        // Scene view's gizmo pass — the lines themselves still show in the Game
        // view without any Gizmos toggle.
        void OnDrawGizmos()
        {
            if (!showLabels || labels.Count == 0 || !DebugManager.IsDebug) return;
            var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
            foreach ((Vector3 position, string text, Color color) in labels)
            {
                style.normal.textColor = color;
                UnityEditor.Handles.Label(position, text, style);
            }
        }
#endif
    }
}
