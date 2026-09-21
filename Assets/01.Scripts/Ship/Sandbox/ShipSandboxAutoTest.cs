using System.Collections;
using System.Text;
using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Simulation;
namespace ConfusedGameDev.FiniteRunner.Ship.Sandbox
{
    /// <summary>
    /// Flies the sandbox course by script and prints the numbers the
    /// standalone ship is accepted on, so "does it hold a loop at Light
    /// Speed" is a log line instead of an opinion: the speed model's timings,
    /// the surface follower's worst tracking error over every feature, jump
    /// lengths, seams that must not read as walls, and the cost of a tick.
    /// It drives the ship through <see cref="HoverShip.ControlOverride"/> —
    /// the same seam an autopilot uses — so the simulation under test is the
    /// one the player gets. Off by default; tick <see cref="runOnStart"/> or
    /// press the inspector button in play mode. Every line is prefixed
    /// <c>[ShipTest]</c>.
    /// </summary>
    public sealed class ShipSandboxAutoTest : MonoBehaviour
    {
        [SerializeField] HoverShip ship;
        [SerializeField] ShipSandboxCourse course;
        [SerializeField] bool runOnStart;
        [SerializeField] float lightSpeed = 1805.6f;
        [Tooltip("Log where and why every take-off happened.")]
        [SerializeField] bool logTakeOffs = true;

        readonly StringBuilder report = new();
        int wallHits, takeOffs, landings;
        Vector3 takeOffPoint;
        float lastJump;
        float worstError, worstTick, maxTilt, travelled;
        double tickTotal;
        float pinnedSpeed = -1f;
        int tickCount;

        public bool Running { get; private set; }
        public string Report => report.ToString();

        IEnumerator Start()
        {
            yield return null; // the course builds in Awake, the ship launches in Start
            if (runOnStart) Run();
        }

        void OnEnable()
        {
            if (ship == null) return;
            ship.WallHit += OnWallHit;
            ship.TookOff += OnTookOff;
            ship.Landed += OnLanded;
        }

        void OnDisable()
        {
            if (ship == null) return;
            ship.WallHit -= OnWallHit;
            ship.TookOff -= OnTookOff;
            ship.Landed -= OnLanded;
        }

        void OnWallHit(float speed) => wallHits++;
        void OnTookOff()
        {
            takeOffs++;
            takeOffPoint = ship.Body.Position;
            if (logTakeOffs)
                Debug.Log($"[ShipTest] take-off: {ship.Body.LastTakeOffReason} at {ship.Body.Position:F1}, up {ship.Body.Up:F2}, " +
                          $"forward {ship.Body.Forward:F2}, after {travelled:F0} m, lateral {ship.Body.TotalLateralVelocity:F1} m/s");
        }
        void OnLanded()
        {
            landings++;
            lastJump = Vector3.ProjectOnPlane(ship.Body.Position - takeOffPoint, Vector3.up).magnitude;
        }

        [Button, DisableInEditorMode]
        public void Run()
        {
            if (Running || ship == null || course == null) return;
            StartCoroutine(RunAll());
        }

        /// <summary>One pass over one feature — for chasing a single failure without the whole run.</summary>
        public void RunOne(string station, float speed, float metres)
        {
            if (Running || ship == null || course == null) return;
            StartCoroutine(One(station, speed, metres));
        }

        /// <summary>Only the dash / barrel roll / stall checks.</summary>
        public void RunFeel()
        {
            if (Running || ship == null || course == null) return;
            StartCoroutine(FeelOnly());
        }

        IEnumerator FeelOnly()
        {
            Running = true;
            report.Clear();
            yield return Feel();
            ship.ControlOverride = null;
            Running = false;
        }

        IEnumerator One(string station, float speed, float metres)
        {
            Running = true;
            report.Clear();
            yield return Feature(station, speed, metres, station);
            ship.ControlOverride = null;
            Running = false;
        }

        void FixedUpdate()
        {
            if (!Running || ship == null) return;
            worstError = Mathf.Max(worstError, Mathf.Abs(ship.Body.SurfaceError));
            // A feature pass is flown at ONE speed: coast drag and wall scrapes are put back every tick (the scrapes are still counted).
            if (pinnedSpeed >= 0f) ship.Body.ForwardSpeed = pinnedSpeed;
            travelled += ship.CurrentSpeed * Time.fixedDeltaTime;
            // The first ticks pay for JIT and collider cooking; the budget is judged on the rest.
            if (++tickCount > 50)
            {
                worstTick = Mathf.Max(worstTick, (float)ship.LastTickMilliseconds);
                tickTotal += ship.LastTickMilliseconds;
            }
            maxTilt = Mathf.Max(maxTilt, Vector3.Angle(ship.Body.Up, Vector3.up));
        }

        IEnumerator RunAll()
        {
            Running = true;
            report.Clear();
            Line("---- standalone ship acceptance run ----");

            // Speed model: launch → cruise, then full brake.
            Begin("Start", ship.Definition.initialImpulse, throttle: 1f);
            float t0 = Time.time;
            yield return new WaitUntil(() => ship.CurrentSpeed >= ship.Definition.cruiseSpeed - 0.01f || Time.time - t0 > 40f);
            float expected = (ship.Definition.cruiseSpeed - ship.Definition.initialImpulse) / Mathf.Max(ship.Definition.thrust, 0.01f);
            Line($"accelerate to cruise: {Time.time - t0:F2} s (expected {expected:F2})  worst surface error {worstError:F3} m  wall hits {wallHits}");

            ship.ControlOverride = new BodyControls { brake = 1f };
            t0 = Time.time;
            yield return new WaitUntil(() => ship.CurrentSpeed <= 0.01f || Time.time - t0 > 20f);
            Line($"full brake from cruise: {Time.time - t0:F2} s (expected {ship.Definition.cruiseSpeed / Mathf.Max(ship.Definition.brakeDecel, 0.01f):F2})");

            // Over-cruise bleed.
            Begin("Start", lightSpeed, throttle: 1f);
            yield return new WaitForSeconds(2f);
            Line($"bleed above cruise: {(lightSpeed - ship.CurrentSpeed) / 2f:F2} m/s² (expected {ship.Definition.passiveDeceleration:F2})");

            foreach (float speed in new[] { 300f, ship.Definition.cruiseSpeed, lightSpeed })
            {
                yield return Feature("Tiles", speed, 380f, "tile seams");
                yield return Feature("Loop", speed, 1300f, "loop", wantTilt: 150f);
                yield return Feature("Banked sweep", speed, 2850f, "banked sweep");
                yield return Feature("Crest", speed, 650f, "crest");
                yield return Feature("Gap", speed, 440f, "gap");
            }

            foreach (float speed in new[] { 500f, ship.Definition.cruiseSpeed })
            {
                Begin("Ramp", speed, throttle: 0f);
                int before = landings;
                t0 = Time.time;
                yield return new WaitUntil(() => landings > before || Time.time - t0 > 6f);
                float want = Mathf.Clamp(speed * ship.Settings.airDistancePerSpeed, ship.Settings.airDistanceRange.x, ship.Settings.airDistanceRange.y) * ship.Definition.jumpStrength;
                Line($"ramp at {speed:F0} m/s: flew {lastJump:F0} m (authored {want:F0}), air {ship.AirTime:F2} s, {(landings > before ? "landed" : "NEVER LANDED")}");
            }

            // The pipe: once it has fully curled, hold full right steer and count how far round the ship goes.
            Begin("Tube", 300f, throttle: 1f);
            pinnedSpeed = 300f;
            t0 = Time.time;
            float tubeTilt = 0f;
            while (travelled < 1450f && Time.time - t0 < 12f)
            {
                if (travelled > 620f) ship.ControlOverride = new BodyControls { throttle = 1f, steer = 1f };
                tubeTilt = Mathf.Max(tubeTilt, Vector3.Angle(ship.Body.Up, Vector3.up));
                yield return new WaitForFixedUpdate();
            }
            pinnedSpeed = -1f;
            Line($"tube at 300 m/s, full right steer: reached {tubeTilt:F0}° round the pipe, state {ship.State}, worst surface error {worstError:F3} m");

            yield return Feel();

            Begin("End wall", ship.Definition.cruiseSpeed, throttle: 0f);
            yield return new WaitForSeconds(1.5f);
            Line($"end wall at cruise: speed after {ship.CurrentSpeed:F0} m/s, wall hits {wallHits}");

            Line($"tick cost: average {tickTotal / Mathf.Max(1, tickCount - 50):F3} ms, worst {worstTick:F3} ms");
            Line("---- done ----");
            ship.ControlOverride = null;
            Running = false;
        }

        /// <summary>What only the ship has: the dash's carry, the airborne barrel roll, the stall rule.</summary>
        IEnumerator Feel()
        {
            // Ground dash: the shove must carry the definition's dash distance, whatever the substep count.
            foreach (float speed in new[] { 300f, lightSpeed })
            {
                Begin("Start", speed, throttle: 0f);
                pinnedSpeed = speed;
                yield return new WaitForSeconds(0.2f);
                Vector3 from = ship.Body.Position, side = ship.Body.Right;
                bool dashed = ship.TryDash(1);
                yield return new WaitForSeconds(1.5f);
                float carried = Vector3.Dot(ship.Body.Position - from, side);
                pinnedSpeed = -1f;
                Line($"dash at {speed:F0} m/s: carried {carried:F2} m (definition {ship.Definition.dashDistance:F2}), {(dashed ? "fired" : "REFUSED")}, meter {ship.DashMeter:F2}");
            }

            // Airborne dash = barrel roll, and it must always finish.
            int rolls = 0;
            void CountRoll(int direction) => rolls++;
            ship.BarrelRollStarted += CountRoll;
            Begin("Ramp", 1000f, throttle: 0f);
            int before = takeOffs;
            float t0 = Time.time;
            yield return new WaitUntil(() => takeOffs > before || Time.time - t0 > 4f);
            bool rolled = ship.TryDash(-1);
            bool sawRolling = ship.IsBarrelRolling;
            yield return new WaitForSeconds(ship.Definition.barrelRollSeconds + 0.3f);
            ship.BarrelRollStarted -= CountRoll;
            float tilt = Quaternion.Angle(ship.Visual.localRotation, Quaternion.identity);
            Line($"barrel roll off the ramp: {(rolled && sawRolling && rolls == 1 ? "rolled" : "DID NOT ROLL")}, " +
                 $"{(ship.IsBarrelRolling ? "STILL ROLLING" : "finished")}, model tilt after {tilt:F0}°");

            // Stall: braking to a stop with the throttle held is fine; releasing it for the grace is the end.
            Begin("Start", 60f, throttle: 1f);
            ship.ControlOverride = new BodyControls { throttle = 1f, brake = 1f };
            yield return new WaitForSeconds(ship.Settings.stallGraceSeconds + 1.5f);
            bool heldOk = !ship.HasStopped && ship.CurrentSpeed <= 0.01f;
            ship.ControlOverride = new BodyControls { brake = 1f };
            yield return new WaitForSeconds(ship.Settings.stallGraceSeconds + 0.5f);
            Line($"stall: brake + throttle held {(heldOk ? "did not stall" : "WRONGLY STALLED OR NEVER STOPPED")}, throttle released {(ship.HasStopped ? "stalled" : "DID NOT STALL")}");
        }

        /// <summary>One pass over a feature: from its station, for <paramref name="metres"/> — just past its end, so the next feature never muddies the numbers.</summary>
        IEnumerator Feature(string station, float speed, float metres, string label, float wantTilt = 0f)
        {
            Begin(station, speed, throttle: 0f);
            pinnedSpeed = speed;
            float t0 = Time.time;
            while (travelled < metres && Time.time - t0 < 20f)
                yield return new WaitForFixedUpdate();
            pinnedSpeed = -1f;
            string tilt = wantTilt > 0f ? $"  max tilt {maxTilt:F0}° ({(maxTilt >= wantTilt ? "went round" : "DID NOT GO ROUND")})" : "";
            Line($"{label} at {speed:F0} m/s: worst surface step {worstError:F3} m, " +
                 $"take-offs {takeOffs}, wall hits {wallHits}, ends {ship.State}{tilt}");
        }

        /// <summary>Seats the ship at a station with a speed and a fixed control input, and zeroes the counters. Feature runs coast (throttle 0): they are short, so the drag is simply accepted and reported.</summary>
        void Begin(string station, float speed, float throttle, float steer = 0f)
        {
            for (int i = 0; i < course.Stations.Count; i++)
            {
                if (course.Stations[i].Name != station) continue;
                ship.Launch(course.Stations[i].Position, course.Stations[i].Rotation);
                break;
            }
            ship.Body.ForwardSpeed = speed;
            ship.ControlOverride = new BodyControls { throttle = throttle, steer = steer };
            wallHits = takeOffs = landings = 0;
            worstError = 0f;
            travelled = 0f;
            maxTilt = 0f;
            lastJump = 0f;
        }

        void Line(string text)
        {
            report.AppendLine(text);
            Debug.Log("[ShipTest] " + text);
        }
    }
}
