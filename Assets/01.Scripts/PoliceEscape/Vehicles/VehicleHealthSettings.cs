using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Every tunable of the damage model in one asset — the NPC cars' and,
    /// in the Player group, the player car's (its health meter, refactor Step
    /// 9.2; the knobs used to be fields on the LevelManager): how hard the player
    /// has to hit a car to hurt it, how a wounded car slows, where the smoke
    /// and fire thresholds sit, and what the death blast does. One asset for
    /// the whole fleet — traffic and police share the same flesh, they only
    /// differ in who is driving. Loaded from Resources so a car never fails
    /// for want of wiring; the sprite lists are filled by
    /// Tools → Police Escape → Create Vehicle Health Settings.
    /// </summary>
    [CreateAssetMenu(menuName = "PoliceEscape/Vehicle Health Settings", fileName = "PoliceEscape_VehicleHealth")]
    public class VehicleHealthSettings : ScriptableObject
    {
        const string ResourcePath = "PoliceEscape_VehicleHealth";

        // -------------------------------------------------------------- damage
        [TitleGroup("Damage")]
        [Tooltip("Contact slower than this is a scrape, not a hit — for every car, the player's included (no pulse, no rumble, no damage).")]
        [PropertyRange(0.5f, 15f), SuffixLabel("m/s", true)]
        public float minImpactSpeed = 3f;

        [TitleGroup("Damage")]
        [Tooltip("Health lost per m/s of relative velocity above the floor. 0.03 ≈ a one-hit kill at 130 km/h, four solid shunts at 40 km/h.")]
        [PropertyRange(0.005f, 0.2f)]
        public float damagePerImpactSpeed = 0.03f;

        // -------------------------------------------------------------- player
        // The player car's health meter (PlayerHealthMeter). Its damage is a
        // 0..1 "corruption" the city shows as glitch; full reboots the level.
        [TitleGroup("Player")]
        [Tooltip("Glitch pulse on any hard impact the player takes (at or above the impact floor).")]
        [PropertyRange(0f, 1f)]
        public float playerCollisionPulse = 0.4f;

        [TitleGroup("Player")]
        [Tooltip("Meter filled per police hit — full reboots the level. Default: three hits and you're out.")]
        [PropertyRange(0.05f, 1f)]
        public float playerPoliceHitDamage = 0.34f;

        [TitleGroup("Player")]
        [Tooltip("How fast the meter heals, per second, while the run is live. 0 = damage stays for the whole run.")]
        [PropertyRange(0f, 0.5f)]
        public float playerHealPerSecond = 0.05f;

        [TitleGroup("Player")]
        [Tooltip("Fraction of a blast's normalized damage (1 = what kills an NPC car outright) that reaches the player's meter — the hero car's plating.")]
        [PropertyRange(0f, 1f)]
        public float playerBlastDamageScale = 0.35f;

        [TitleGroup("Player")]
        [Tooltip("Impact speed at which the crash rumble reaches playerCrashRumbleFull; from the impact floor up to it the rumble scales from playerCrashRumbleLight.")]
        [PropertyRange(5f, 40f), SuffixLabel("m/s", true)]
        public float playerCrashRumbleFullSpeed = 20f;

        [TitleGroup("Player")]
        [Tooltip("The crash rumble at the impact floor — a kerb tap. (low motor, high motor, seconds)")]
        public Vector3 playerCrashRumbleLight = new(0.25f, 0.15f, 0.12f);

        [TitleGroup("Player")]
        [Tooltip("The crash rumble at playerCrashRumbleFullSpeed and above — a wall at speed. (low motor, high motor, seconds)")]
        public Vector3 playerCrashRumbleFull = new(1f, 0.6f, 0.4f);

        [TitleGroup("Player")]
        [Tooltip("A blast reaching the player (a barrel, a dying car). (low motor, high motor, seconds)")]
        public Vector3 playerBlastRumble = new(1f, 0.7f, 0.45f);

        [TitleGroup("Player")]
        [Tooltip("Driving into water. (low motor, high motor, seconds)")]
        public Vector3 playerSplashRumble = new(0.8f, 0.5f, 0.4f);

        // --------------------------------------------------------------- speed
        [TitleGroup("Speed")]
        [Tooltip("Floor under the health-matched speed factor — a nearly dead car still crawls at this fraction of its cruise speed instead of freezing mid-road. Above it, speed tracks health one to one.")]
        [PropertyRange(0.05f, 1f)]
        public float crawlSpeedFactor = 0.3f;

        [TitleGroup("Speed")]
        [Tooltip("How fast the live speed factor slides toward its health target, per second — the 'slowly' in slowing down. Hits bleed speed away rather than snapping it.")]
        [PropertyRange(0.05f, 2f)]
        public float speedEasePerSecond = 0.4f;

        // -------------------------------------------------------------- police
        [TitleGroup("Police")]
        [Tooltip("A cruiser's health bar as a multiple of a civilian's — every bite (shunts and blasts alike) is divided by this. 3 = three ram exchanges where a taxi would take one.")]
        [PropertyRange(1f, 6f), SuffixLabel("x", true)]
        public float policeToughness = 3f;

        [TitleGroup("Police")]
        [Tooltip("A cruiser holds FULL speed until its (normalized) health falls to this, then limps down to the crawl like a civilian — a chase car that bleeds speed on the first hit stops being a threat. 0 = never slows until it is dead.")]
        [PropertyRange(0f, 1f)]
        public float policeLimpHealth = 0.2f;

        // ---------------------------------------------------------- thresholds
        [TitleGroup("Thresholds")]
        [Tooltip("Health at or below which the engine starts blowing light white smoke — the first warning.")]
        [PropertyRange(0f, 1f)]
        public float lightSmokeHealth = 0.5f;

        [TitleGroup("Thresholds")]
        [Tooltip("Health at or below which the white smoke is joined by heavy black smoke — this car is dying.")]
        [PropertyRange(0f, 1f)]
        public float heavySmokeHealth = 0.2f;

        [TitleGroup("Thresholds")]
        [Tooltip("How long a dead car smokes at a standstill before it explodes — the window to get clear (or to lure a cruiser in).")]
        [PropertyRange(0.5f, 15f), SuffixLabel("s", true)]
        public float fuseSeconds = 5f;

        [TitleGroup("Thresholds")]
        [Tooltip("How long the charred, wheel-less wreck stays in the street after the explosion before it is cleaned up.")]
        [PropertyRange(2f, 60f), SuffixLabel("s", true)]
        public float wreckLingerSeconds = 12f;

        // --------------------------------------------------------------- blast
        [TitleGroup("Blast")]
        [Tooltip("A dying car's blast — the same positional rule as the explosive barrel: radius, impulse, lift and normalized damage.")]
        [InlineProperty, HideLabel]
        public BlastProfile blast = BlastProfile.Default;

        [TitleGroup("Blast")]
        [Tooltip("Size of the fireball, in metres. Independent of the blast radius so the look and the damage can be tuned apart.")]
        [PropertyRange(0.5f, 20f), SuffixLabel("m", true)]
        public float explosionScale = 5f;

        [TitleGroup("Blast")]
        [Tooltip("How long a fireball billboard lives.")]
        [PropertyRange(0.1f, 4f), SuffixLabel("s", true)]
        public float explosionLifetime = 0.9f;

        [TitleGroup("Blast")]
        [Tooltip("Billboards in one blast.")]
        [PropertyRange(1, 40)]
        public int explosionParticles = 14;

        // ------------------------------------------------------------- sprites
        [TitleGroup("Sprites")]
        [Tooltip("First-warning smoke billboards — one is picked at random per car. Filled from SmokeAndExplosions/White puff by the builder.")]
        public List<Texture2D> lightSmokeTextures = new();

        [TitleGroup("Sprites")]
        [Tooltip("Dying-car smoke billboards. Filled from SmokeAndExplosions/Black smoke by the builder.")]
        public List<Texture2D> heavySmokeTextures = new();

        [TitleGroup("Sprites")]
        [Tooltip("Fireball sprites for the death blast — one is picked at random, so no two wrecks look alike. Filled from SmokeAndExplosions/Explosion by the builder.")]
        public List<Texture2D> explosionTextures = new();

        /// <summary>
        /// The shipped asset from Resources, or an in-memory default so the
        /// damage model still works without it — the mechanics survive, only
        /// the sprites (and so the VFX) are missing.
        /// </summary>
        public static VehicleHealthSettings Load()
        {
            var asset = Resources.Load<VehicleHealthSettings>(ResourcePath);
            if (asset != null) return asset;
            Debug.LogWarning($"No {nameof(VehicleHealthSettings)} at Resources/{ResourcePath} — " +
                             "cars take damage but burn without smoke or fire. " +
                             "Run Tools > Police Escape > Create Vehicle Health Settings.");
            return CreateInstance<VehicleHealthSettings>();
        }
    }
}
