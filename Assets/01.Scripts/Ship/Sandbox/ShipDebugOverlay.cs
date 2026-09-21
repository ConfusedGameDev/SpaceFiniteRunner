using UnityEngine;
using UnityEngine.InputSystem;

namespace ConfusedGameDev.FiniteRunner.Ship.Sandbox
{
    /// <summary>
    /// The sandbox's instrument panel and test bench for a <see cref="HoverShip"/>:
    /// an on-screen readout of what the body is doing (speed, state, substeps
    /// and queries per tick, the surface follower's tracking error, the up's
    /// tilt) plus the two shortcuts every acceptance test needs — jump to a
    /// course station (F1–F10) and force a speed (1 / 2 / 3 = 300 / cruise /
    /// Light Speed, 0 = stop). Debug tooling only: it polls the keyboard
    /// directly, like the menus, and none of it is bindable.
    /// </summary>
    public sealed class ShipDebugOverlay : MonoBehaviour
    {
        [SerializeField] HoverShip ship;
        [SerializeField] ShipSandboxCourse course;
        [SerializeField] float lightSpeed = 1805.6f;

        float worstError;
        int wallHits;
        GUIStyle style;

        static readonly Key[] StationKeys =
        {
            Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10,
        };

        void OnEnable()
        {
            if (ship != null) ship.WallHit += OnWallHit;
        }

        void OnDisable()
        {
            if (ship != null) ship.WallHit -= OnWallHit;
        }

        void OnWallHit(float speed) => wallHits++;

        void Update()
        {
            if (ship == null) return;
            worstError = Mathf.Max(worstError * 0.995f, Mathf.Abs(ship.Body.SurfaceError));

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard[Key.Digit1].wasPressedThisFrame) ship.Body.ForwardSpeed = 300f;
            if (keyboard[Key.Digit2].wasPressedThisFrame) ship.Body.ForwardSpeed = ship.Definition.cruiseSpeed;
            if (keyboard[Key.Digit3].wasPressedThisFrame) ship.Body.ForwardSpeed = lightSpeed;
            if (keyboard[Key.Digit0].wasPressedThisFrame) ship.Body.ForwardSpeed = 0f;

            if (course == null) return;
            for (int i = 0; i < StationKeys.Length && i < course.Stations.Count; i++)
                if (keyboard[StationKeys[i]].wasPressedThisFrame)
                    TeleportTo(i);
        }

        /// <summary>Re-seats the ship at a course station, keeping the speed it had (a launch resets it to the initial impulse).</summary>
        public void TeleportTo(int station)
        {
            if (ship == null || course == null || station < 0 || station >= course.Stations.Count) return;
            ShipSandboxCourse.Station spot = course.Stations[station];
            float speed = ship.Body.ForwardSpeed;
            ship.Launch(spot.Position, spot.Rotation);
            ship.Body.ForwardSpeed = speed;
            worstError = 0f;
        }

        void OnGUI()
        {
            if (ship == null) return;
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
            HoverBody body = ship.Body;

            string text =
                $"<b>{body.ForwardSpeed:F0} m/s</b>  ({body.ForwardSpeed * 3.6f:F0} km/h)   {body.State}\n" +
                $"substeps {body.Substeps}   queries {body.Queries}   air {body.AirTime:F2} s\n" +
                $"surface error {body.SurfaceError:F3} m   (recent worst {worstError:F3})\n" +
                $"up tilt {Vector3.Angle(body.Up, Vector3.up):F1}°   lateral {body.TotalLateralVelocity:F1} m/s   wall hits {wallHits}\n" +
                "1 / 2 / 3 = 300 / cruise / light speed, 0 = stop";
            if (course != null)
                for (int i = 0; i < course.Stations.Count && i < StationKeys.Length; i++)
                    text += (i == 0 ? "\n" : "   ") + $"F{i + 1} {course.Stations[i].Name}";

            GUI.Label(new Rect(16f, 16f, 900f, 260f), text, style);
        }
    }
}
