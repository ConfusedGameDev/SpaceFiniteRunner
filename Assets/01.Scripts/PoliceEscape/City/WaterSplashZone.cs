using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Haptics;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// The sea's one answer to anything that drives in. A water block bakes
    /// this onto its splash trigger (the water column below the surface);
    /// <see cref="CityWrap"/> calls it by hand when a pacman wrap would land
    /// in open water. One entry point on purpose — <see cref="Splash(Vehicles.CarController, float)"/>
    /// — so every way of ending up in the sea has the same consequence:
    ///
    /// The PLAYER takes <c>splashDamage</c> on the corruption meter through
    /// <see cref="LevelManager.ApplyDamage"/> (so a splash fills the same
    /// meter and trips the same reboot as a barrel or a police shunt) and is
    /// put back on the nearest road by their <see cref="Vehicles.CarRespawner"/>.
    /// There are no barrier walls: the shore is a real drop, and the respawn
    /// is what keeps the car from touring the sea floor. A short cooldown
    /// keeps a trigger re-entry (or the wrap refusal and the trigger firing on
    /// the same fall) from charging twice.
    ///
    /// AI cars DIE — a cruiser that follows the player off a pier is killed
    /// through its <see cref="Vehicles.CarHealth"/> (fuse, explosion, wreck,
    /// exactly like a barrel kill), and the <see cref="AI.PatrolManager"/>'s
    /// next maintenance tick cuts a replacement in at its spawn band. That is
    /// the same bait-tactic logic as the explosive barrels: the sea is a
    /// weapon that costs the player a corruption bite to use. A car with no
    /// health component (none of the fleets, but a stray test rig) is simply
    /// destroyed.
    ///
    /// Baked into the city prefab, so the tuning field is serialized — a plain
    /// private field would deserialize as zero and make the sea free.
    /// </summary>
    public class WaterSplashZone : MonoBehaviour
    {
        /// <summary>Seconds after a player splash during which further splashes are ignored.</summary>
        const float PlayerCooldown = 1f;

        /// <summary>Charge used when no baked zone can answer (a water block baked without colliders).</summary>
        public const float DefaultDamage = 0.3f;

        [SerializeField, HideInInspector] float splashDamage = DefaultDamage;

        static float lastPlayerSplash = float.NegativeInfinity;

        /// <summary>Corruption this zone charges the player.</summary>
        public float SplashDamage => splashDamage;

        /// <summary>Arms a freshly built trigger object. Called by <see cref="CityBlockBuilder"/>, never by hand.</summary>
        public static void Configure(GameObject trigger, float damage)
        {
            trigger.AddComponent<WaterSplashZone>().splashDamage = damage;
        }

        void OnTriggerEnter(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (body == null) return;
            var car = body.GetComponent<Vehicles.CarController>();
            if (car == null) return;
            Splash(car, splashDamage);
        }

        /// <summary>This zone's splash, for callers that already resolved the block (the wrap refusal).</summary>
        public void Splash(Vehicles.CarController car) => Splash(car, splashDamage);

        /// <summary>Drown a car: the player is damaged and respawned, an NPC is killed.</summary>
        public static void Splash(Vehicles.CarController car, float damage)
        {
            if (car == null) return;
            if (car.GetComponent<Vehicles.CarInput>() != null) SplashPlayer(car, damage);
            else SplashNpc(car);
        }

        static void SplashPlayer(Vehicles.CarController car, float damage)
        {
            if (Time.time - lastPlayerSplash < PlayerCooldown) return;
            lastPlayerSplash = Time.time;

            if (HapticsSystem.Instance != null) HapticsSystem.Instance.Pulse(0.8f, 0.5f, 0.4f);

            var level = Object.FindAnyObjectByType<LevelManager>();
            if (level != null) level.ApplyDamage(damage, "splash");
            else if (GlitchController.Instance != null) GlitchController.Instance.Pulse(1f);

            var respawner = car.GetComponent<Vehicles.CarRespawner>();
            if (respawner != null)
            {
                respawner.Respawn();
                return;
            }

            // No respawner (a bare test rig): put it back on the road by hand.
            var city = Object.FindAnyObjectByType<CityManager>();
            Rigidbody body = car.Body != null ? car.Body : car.GetComponent<Rigidbody>();
            if (city == null || body == null || !city.TryFindNearestRoadCell(car.transform.position, out Vector3 center, out _)) return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Vehicles.CarFactory.Teleport(body, center + Vector3.up * 0.5f, Quaternion.Euler(0f, car.transform.eulerAngles.y, 0f));
        }

        static void SplashNpc(Vehicles.CarController car)
        {
            var health = car.GetComponent<Vehicles.CarHealth>();
            if (health != null)
            {
                if (!health.IsDead) health.ApplyDamage(float.MaxValue);
                return;
            }
            Destroy(car.gameObject);
        }
    }
}
