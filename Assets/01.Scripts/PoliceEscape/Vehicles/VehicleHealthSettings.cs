using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Every tunable of the NPC damage model in one asset: how hard the player
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
        [Tooltip("Contact slower than this is a scrape, not a hit — same floor the LevelManager uses for the player's own damage.")]
        [PropertyRange(0.5f, 15f), SuffixLabel("m/s", true)]
        public float minImpactSpeed = 3f;

        [TitleGroup("Damage")]
        [Tooltip("Health lost per m/s of relative velocity above the floor. 0.03 ≈ a one-hit kill at 130 km/h, four solid shunts at 40 km/h.")]
        [PropertyRange(0.005f, 0.2f)]
        public float damagePerImpactSpeed = 0.03f;

        // --------------------------------------------------------------- speed
        [TitleGroup("Speed")]
        [Tooltip("Floor under the health-matched speed factor — a nearly dead car still crawls at this fraction of its cruise speed instead of freezing mid-road. Above it, speed tracks health one to one.")]
        [PropertyRange(0.05f, 1f)]
        public float crawlSpeedFactor = 0.3f;

        [TitleGroup("Speed")]
        [Tooltip("How fast the live speed factor slides toward its health target, per second — the 'slowly' in slowing down. Hits bleed speed away rather than snapping it.")]
        [PropertyRange(0.05f, 2f)]
        public float speedEasePerSecond = 0.4f;

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
        [Tooltip("Everything inside this radius is caught by the death blast — same positional rule as the explosive barrel.")]
        [PropertyRange(1f, 30f), SuffixLabel("m", true)]
        public float blastRadius = 7f;

        [TitleGroup("Blast")]
        [Tooltip("Impulse handed to a body at the centre of the blast, falling off to nothing at the radius.")]
        [PropertyRange(0f, 5000f)]
        public float blastForce = 900f;

        [TitleGroup("Blast")]
        [Tooltip("Metres the blast's origin is sunk below itself when throwing bodies — the lower it sits, the more the blast lifts rather than shoves.")]
        [PropertyRange(0f, 5f), SuffixLabel("m", true)]
        public float blastUpModifier = 1.2f;

        [TitleGroup("Blast")]
        [Tooltip("Normalized damage dealt to every IDamageable caught in the blast — 1 is a full NPC health bar (an outright kill). The player's receiver scales it down by their plating before it hits the corruption meter.")]
        [PropertyRange(0f, 1f)]
        public float blastDamage = 1f;

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
