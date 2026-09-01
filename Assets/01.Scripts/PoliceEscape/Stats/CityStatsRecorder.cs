using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using ConfusedGameDev.FiniteRunner.SaveData;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Stats
{
    /// <summary>
    /// Feeds the saved <see cref="PlayerProfile"/> from the city chase: every
    /// car the player totals (with its identity, police told apart from
    /// civilians the way <c>LevelManager</c> does — by the <see cref="PoliceCarInput"/>
    /// on it), the player car's top speed, and its jumps. A scene-lifetime
    /// system under ===SYSTEMS=== (placed by <c>SceneSystemsPlacer</c>, created
    /// by <c>CityManager.Awake</c> when missing); it needs nothing wired.
    ///
    /// Deaths and level completion are NOT recorded here — the
    /// <c>LevelManager</c> owns those moments and records them itself.
    ///
    /// <see cref="CarHealth.Died"/> is a static event, and domain reload is
    /// off in this project, so the subscription lives in OnEnable/OnDisable —
    /// never a static initializer, which would stack a handler per play
    /// session. The player's own car has no <see cref="CarHealth"/>, so every
    /// death that arrives is a car the player (or a blast the player set off)
    /// destroyed. Jumps are measured here rather than in <c>AirTimeSlowMo</c>
    /// because that component stands down when its slow-mo is switched off,
    /// and a jump is a jump either way: distance is the horizontal velocity
    /// integrated while no wheel touches the ground (scaled dt, matching the
    /// scaled physics step under slow-mo; immune to the pacman wrap, which
    /// moves the car but keeps its velocity).
    /// </summary>
    public class CityStatsRecorder : MonoBehaviour
    {
        /// <summary>Shorter hops (a kerb, a bump) are not jumps.</summary>
        const float MinJumpAirSeconds = 0.25f;
        const float RetargetSeconds = 1f;

        CarController player;
        float retargetTimer;

        bool wasAirborne;
        float airTime;
        float jumpDistance;

        void OnEnable() => CarHealth.Died += OnCarDied;

        void OnDisable() => CarHealth.Died -= OnCarDied;

        void OnCarDied(CarHealth health)
        {
            if (health == null) return;
            bool police = health.GetComponent<PoliceCarInput>() != null;
            var controller = health.GetComponent<CarController>();
            VehicleIdentity identity = controller != null ? controller.identity : default;
            PlayerStats.RecordTotaledCar(police,
                $"{identity.kind}/{identity.paint}",
                VehicleIdentity.Describe(identity.kind, identity.paint, "UNKNOWN VEHICLE"));
        }

        void Update()
        {
            // The car spawns a beat after play starts and respawns keep the
            // object, so a slow re-find is all the tracking needs.
            retargetTimer -= Time.unscaledDeltaTime;
            if (player == null || retargetTimer <= 0f)
            {
                retargetTimer = RetargetSeconds;
                var found = PatrolManager.FindPlayerCar();
                if (found != player)
                {
                    player = found;
                    ResetJump();
                }
            }
            if (player == null || Time.timeScale <= 0f) return;

            PlayerStats.SampleCarSpeed(player.SpeedKmh);
            TrackJump();
        }

        void TrackJump()
        {
            bool airborne = !player.IsGrounded;
            if (airborne)
            {
                float dt = Time.deltaTime;
                Vector3 flat = player.Velocity;
                flat.y = 0f;
                airTime += dt;
                jumpDistance += flat.magnitude * dt;
            }
            else if (wasAirborne)
            {
                if (airTime >= MinJumpAirSeconds) PlayerStats.RecordJump(jumpDistance, airTime);
                ResetJump();
            }
            wasAirborne = airborne;
        }

        void ResetJump()
        {
            wasAirborne = false;
            airTime = 0f;
            jumpDistance = 0f;
        }
    }
}
