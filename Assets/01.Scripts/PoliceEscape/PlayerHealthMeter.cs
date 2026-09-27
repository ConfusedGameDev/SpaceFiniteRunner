using System;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using ConfusedGameDev.FiniteRunner.Store;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>
    /// The player car's health in the city: the "corruption" meter police
    /// shunts, blasts and splashes fill, which reboots the level when full.
    /// It used to BE the glitch's base intensity — the health lived in a post
    /// effect, healed at the effect's fade rate and was read back off the
    /// shader's driver by the speedometer (refactor Step 9.2 took it out). Now
    /// the meter owns the number, heals it at
    /// <see cref="VehicleHealthSettings.playerHealPerSecond"/>, and only SHOWS
    /// it: a glitch floor at the damage level plus a pulse per hit. Every
    /// player damage knob (impact floor, collision pulse, police hit, crash
    /// rumble, blast plating, splash and blast rumbles) lives on
    /// <see cref="VehicleHealthSettings"/>, beside the NPC damage model.
    /// Hand-placed beside the <see cref="LevelManager"/>, which gates it
    /// (<see cref="Gate"/>) and answers <see cref="Depleted"/> with the reboot.
    /// </summary>
    public class PlayerHealthMeter : MonoBehaviour
    {
        VehicleHealthSettings settings;
        CarController player;
        float retargetTimer;

        /// <summary>Damage taken, 0 = pristine, 1 = the level reboots.</summary>
        public float Damage { get; private set; }
        /// <summary>What is left of the car, 0..1 — the speedometer's life ring.</summary>
        public float Health => 1f - Damage;
        /// <summary>What last filled the meter ("police hit", "blast", "splash") — a police hit makes the reboot an arrest.</summary>
        public string LastDamageReason { get; private set; }

        /// <summary>Damage and healing apply only while this returns true (the level flow closes it while resetting, completing or frozen by a cinema). Null = always open.</summary>
        public Func<bool> Gate;

        /// <summary>The meter reached full: raised once per fill.</summary>
        public event Action Depleted;

        bool Open => Gate == null || Gate();

        VehicleHealthSettings Settings => settings != null ? settings : settings = VehicleHealthSettings.Load();

        /// <summary>The scene's meter (hand-placed beside the LevelManager); added only when the scene has none.</summary>
        public static PlayerHealthMeter Ensure(Component host)
        {
            var meter = host.GetComponent<PlayerHealthMeter>();
            return meter != null ? meter : host.gameObject.AddComponent<PlayerHealthMeter>();
        }

        /// <summary>A fresh car for a retry: no damage, no reason.</summary>
        public void ResetForRun()
        {
            Damage = 0f;
            LastDamageReason = null;
            ShowOnGlitch();
        }

        void OnDisable()
        {
            if (GlitchController.Instance != null) GlitchController.Instance.ClearFloor(this);
        }

        void Update()
        {
            RefreshPlayer(Time.deltaTime);
            if (Damage > 0f && Open)
                Damage = Mathf.MoveTowards(Damage, 0f, Settings.playerHealPerSecond * Time.unscaledDeltaTime);
            ShowOnGlitch();
        }

        void ShowOnGlitch()
        {
            var glitch = GlitchController.Instance;
            if (glitch == null) return;
            if (Damage > 0f) glitch.SetFloor(this, Damage);
            else glitch.ClearFloor(this);
        }

        /// <summary>
        /// Damage from any source. One entry point, so police shunts, blasts
        /// and splashes pay the same Store resistance and trip the same reboot
        /// at full. Returns false when the hit was swallowed (the gate is
        /// closed), so a caller knows not to expect a reaction.
        /// </summary>
        public bool ApplyDamage(float amount, string reason)
        {
            if (!Open || amount <= 0f) return false;
            LastDamageReason = reason;

            // Resistance: the Store's car upgrade divides every hit the player takes.
            amount /= Mathf.Max(0.01f, StoreUpgrades.Multiplier(StoreSectionKind.Car, UpgradeIds.CarResistance));

            bool wasFull = Damage >= 0.999f;
            Damage = Mathf.Clamp01(Damage + amount);
            // Never quieter than the hit is heavy: a barrel to the face should
            // white out the feed even if the collision pulse is tuned gentle.
            if (GlitchController.Instance != null) GlitchController.Instance.Pulse(Mathf.Max(Settings.playerCollisionPulse, amount));
            ShowOnGlitch();
            Debug.Log($"[Level] {reason} — corruption {Damage:F2}", this);
            if (!wasFull && Damage >= 0.999f) Depleted?.Invoke();
            return true;
        }

        /// <summary>A blast reached the player: the plating scales it, the pad takes the blast rumble.</summary>
        public void ApplyBlast(float amount)
        {
            if (amount <= 0f) return;
            if (HapticsSystem.Instance != null) HapticsSystem.Instance.Pulse(Settings.playerBlastRumble);
            ApplyDamage(amount * Settings.playerBlastDamageScale, "blast");
        }

        /// <summary>The player drove into water: the splash rumble, then the zone's damage.</summary>
        public void ApplySplash(float damage)
        {
            if (HapticsSystem.Instance != null) HapticsSystem.Instance.Pulse(Settings.playerSplashRumble);
            ApplyDamage(damage, "splash");
        }

        // Player and car come and go at runtime (the spawner), so re-find it on
        // a slow tick; the impact sensor is bolted on when we first see a car.
        void RefreshPlayer(float dt)
        {
            retargetTimer -= dt;
            if (player != null && retargetTimer > 0f) return;
            retargetTimer = 1f;
            player = PlayerCars.Current;
            if (player != null && player.GetComponent<PlayerImpactSensor>() == null)
                player.gameObject.AddComponent<PlayerImpactSensor>().Impacted += OnPlayerImpact;
        }

        /// <summary>
        /// Player impact, relayed by the sensor: hard hits rumble the pad and
        /// pulse the glitch, police hits also fill the meter. The rumble scales
        /// with impact speed (a kerb tap is a tick, a wall at speed is a slam)
        /// and fires before the glitch, so a scene without one still shakes
        /// the pad.
        /// </summary>
        void OnPlayerImpact(Collision collision)
        {
            if (!Open) return;
            VehicleHealthSettings s = Settings;
            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed < s.minImpactSpeed) return;

            RumbleCrash(impactSpeed);

            var glitch = GlitchController.Instance;
            if (glitch == null) return;
            glitch.Pulse(s.playerCollisionPulse);

            bool policeHit = collision.rigidbody != null
                && collision.rigidbody.GetComponent<PoliceCarInput>() != null;
            if (policeHit) ApplyDamage(s.playerPoliceHitDamage, "police hit");
        }

        // Crash rumble: from the light rumble at the scrape floor to the full
        // one at crashRumbleFullSpeed. Overlapping pulses keep the strongest,
        // so a pile-up never stacks into a buzz.
        void RumbleCrash(float impactSpeed)
        {
            var haptics = HapticsSystem.Instance;
            if (haptics == null) return;
            VehicleHealthSettings s = Settings;
            float t = Mathf.InverseLerp(s.minImpactSpeed, Mathf.Max(s.minImpactSpeed + 0.01f, s.playerCrashRumbleFullSpeed), impactSpeed);
            haptics.Pulse(Vector3.Lerp(s.playerCrashRumbleLight, s.playerCrashRumbleFull, t));
        }
    }

    /// <summary>
    /// Tiny relay the health meter bolts onto the player car at runtime:
    /// collision callbacks only land on the rigidbody's own object, and the
    /// car is a prefab the level shouldn't own — so this forwards them out.
    /// </summary>
    public class PlayerImpactSensor : MonoBehaviour
    {
        public event Action<Collision> Impacted;

        void OnCollisionEnter(Collision collision) => Impacted?.Invoke(collision);
    }
}
