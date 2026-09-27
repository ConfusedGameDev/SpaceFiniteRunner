using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Audio;
using ConfusedGameDev.FiniteRunner.FX;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// Every knob of a chase run — win condition, timer, police behaviour,
    /// power-up strength, story messages and weather — in one designer-facing
    /// asset.
    /// The GameManager owns no tunables of its own: it draws this asset inline
    /// in its inspector, so balancing happens here and survives scene changes.
    /// Everything is a slider (Odin) with an explicit range, and values that
    /// only make sense as a pair (catch/warn, redeploy in/out) are single
    /// min-max sliders so they can never be set the wrong way round.
    /// </summary>
    [CreateAssetMenu(fileName = "GameSettings", menuName = "FiniteRunner/Game Settings")]
    public class GameSettings : ScriptableObject
    {
        static GameSettings defaults;

        /// <summary>A shared in-memory instance with the class defaults, for code that runs without a GameManager. Never saved, never edited.</summary>
        public static GameSettings Default
        {
            get
            {
                if (defaults == null)
                {
                    defaults = CreateInstance<GameSettings>();
                    defaults.name = "GameSettings (defaults)";
                    defaults.hideFlags = HideFlags.HideAndDontSave;
                }
                return defaults;
            }
        }

        // --------------------------------------------------------------- flow
        [TitleGroup("Flow")]
        [Tooltip("Open the pre-run point-allocation screen (TuningScreen) instead of flying straight on the Store's bought upgrades. Off in the shipping flow: the Store between missions owns the ship's stats, and the runner is entered mid-mission from the city with no pause for a setup panel. On, the screen applies the store levels on top of its points.")]
        public bool useTuningScreen = false;

        // --------------------------------------------------------- simulation
        [TitleGroup("Simulation")]
        [Tooltip("Substeps per physics tick (0.02 s) of the ship's track-space body. More = the same rules integrated in finer slices; 1 is enough for today's scripted rules, the grip and slide forces want 2+.")]
        [PropertyRange(1, 8)]
        public int simSubsteps = 2;

        // ------------------------------------------------------ fall + respawn
        [TitleGroup("Fall and respawn")]
        [Tooltip("An open edge (the outer side of a flat sweep): how far past it the ship may hang, and for how long, before it falls. Counter-steering back inside the time saves it — this is what keeps a dash or a slide over the edge from being an instant death.")]
        [PropertyRange(0f, 10f), SuffixLabel("m", true)]
        public float edgeOverhang = 1f;

        [TitleGroup("Fall and respawn")]
        [Tooltip("Seconds the ship must stay past the overhang before it falls.")]
        [PropertyRange(0f, 2f), SuffixLabel("s", true)]
        public float edgeGraceSeconds = 0.25f;

        [TitleGroup("Fall and respawn")]
        [Tooltip("Seconds the chase camera keeps following the falling ship before it plants itself and just watches (the rig's cinematic shot).")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float fallCameraFollowSeconds = 0.5f;

        [TitleGroup("Fall and respawn")]
        [Tooltip("Glitch-effect burst strength as the ship goes over the edge.")]
        [PropertyRange(0f, 1f)]
        public float fallGlitchStrength = 0.8f;

        [TitleGroup("Fall and respawn")]
        [Tooltip("The least head start over the patrol the ship relaunches with: a patrol closer than this when the wait ends is dropped back to it.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m", true)]
        public float respawnMinPatrolGap = 150f;

        // ---------------------------------------------------------------- win
        [TitleGroup("Win condition")]
        [Tooltip("Light Speed — the speed that wins the run, in km/h.")]
        [PropertyRange(100f, 10000f), SuffixLabel("km/h", true)]
        public float lightSpeedKmh = 6500f;

        [TitleGroup("Win condition")]
        [Tooltip("Seconds to reach Light Speed AND leave the track by one of its end ramps before the chase is lost.")]
        [PropertyRange(10f, 300f), SuffixLabel("s", true)]
        public float timeLimitSeconds = 60f;

        // ---------------------------------------------------------- track end
        // The track is finite: it ends in three ramps side by side over a
        // void. The run is won by leaving one of them with every objective
        // met; anything else that reaches the end falls.
        [TitleGroup("Track end")]
        [Tooltip("Track length, metres, for a level that does not author its own (RunnerLevelDefinition.trackLengthMeters = 0). At the defaults a cruise-only run covers 15 km in the time limit and a run reaching Light Speed near the end of the clock about 40 km.")]
        [PropertyRange(5000f, 100000f), SuffixLabel("m", true)]
        public float trackLengthMeters = 40000f;

        [TitleGroup("Track end")]
        [Tooltip("Length of the final run-up to the end ramps, metres: dead straight, level, unbanked, walled, with no pads, coins or features on it. It is also where a late fall respawns, so keep it well above the respawn clearance.")]
        [PropertyRange(600f, 3000f), SuffixLabel("m", true)]
        public float endRunUpMeters = 1200f;

        [TitleGroup("Track end")]
        [Tooltip("Gap between two end ramps, metres: the drop a ship that misses the ramps goes through. Wider = easier to miss.")]
        [PropertyRange(4f, 40f), SuffixLabel("m", true)]
        public float endRampGapMeters = 10f;

        [TitleGroup("Track end")]
        [Tooltip("Gap between an outer end ramp and the wall, metres. 0 = the outer ramps stand flush against the walls.")]
        [PropertyRange(0f, 20f), SuffixLabel("m", true)]
        public float endRampSideGapMeters = 0f;

        [TitleGroup("Track end")]
        [Tooltip("Gravity on the winning ship once it has left an end ramp, m/s². 0 = it flies on dead straight along the ramp's line.")]
        [PropertyRange(0f, 60f), SuffixLabel("m/s²", true)]
        public float winEscapeGravity = 0f;

        // ------------------------------------------------------------- patrol
        // The chase tunables (speeds, rubber band, distances) moved to the
        // PatrolDefinition asset on the scene's PolicePatrol object — this
        // section only keeps the run-level rules: on/off, minimap, alerts. The
        // redeploy rule and the duel's rules live on the PatrolDefinition too.
        [ToggleGroup("patrolEnabled", "Police patrol")]
        [Tooltip("Enable the chasing patrol (the scene object the GameManager references; its chase tunables live on its PatrolDefinition asset).")]
        public bool patrolEnabled = true;

        [ToggleGroup("patrolEnabled")]
        [Tooltip("Gap beyond which the patrol is off the track map; at this gap its icon hangs a full chase span (ChaseMinimapSettings.chaseSpan) under the ship.")]
        [PropertyRange(50f, 2000f), SuffixLabel("m", true)]
        public float minimapRangeMeters = 400f;

        [ToggleGroup("patrolEnabled"), Title("Alerts")]
        [Tooltip("Announce every fresh patrol with the 'Patrol inbound' story line (RPG dialogue box). Off by default — the minimap and the rumble already show it arriving.")]
        public bool showPatrolAlert = false;

        [ToggleGroup("patrolEnabled")]
        [Tooltip("Speak the patrol's proximity line (RPG dialogue box) once each time it closes inside its warn distance. Off by default — the minimap shows the gap every frame.")]
        public bool showPatrolWarnings = false;

        [ToggleGroup("patrolEnabled")]
        [Tooltip("Gamepad rumble that grows as the patrol closes inside its warn distance.")]
        public bool patrolProximityRumble = true;

        // ----------------------------------------------------------- power-up
        [TitleGroup("Power-ups")]
        [Tooltip("Base speed gain of a power-up orb in m/s (x3.6 for km/h). Each orb tier multiplies this — green 1x, blue 2.5x, purple 10x (tiers live on the TrackGenerator).")]
        [PropertyRange(0f, 100f), SuffixLabel("m/s", true)]
        public float powerUpSpeedBoost = 15f;

        [TitleGroup("Power-ups")]
        [Tooltip("Base boost rumble (low motor, high motor, seconds) — a boost orb taken without a timed press, and every non-orb boost (a ramp takeoff).")]
        public Vector3 boostRumble = new(0.15f, 0.55f, 0.15f);

        // ------------------------------------------------------------- haptics
        // One rumble per run event, beside the event's shake and glitch
        // strengths on this asset (moved here in refactor Step 8.2 — they were
        // literals in the GameManager). Each is (low motor, high motor, seconds).
        [TitleGroup("Haptics")]
        [Tooltip("The arrest (the patrol sat on your tail too long): a long heavy rumble. (low motor, high motor, seconds)")]
        public Vector3 bustedRumble = new(1f, 0.7f, 1.5f);

        [TitleGroup("Haptics")]
        [Tooltip("Leaving the track off an end ramp with the objectives met — the win latching. (low motor, high motor, seconds)")]
        public Vector3 escapeRumble = new(0.7f, 0.9f, 0.6f);

        [TitleGroup("Haptics")]
        [Tooltip("Reaching the end without the win (an objective open, or through a gap into the void). (low motor, high motor, seconds)")]
        public Vector3 endFailRumble = new(1f, 0.5f, 0.8f);

        [TitleGroup("Haptics")]
        [Tooltip("A hull hit whose cause has no rumble of its own. (low motor, high motor, seconds)")]
        public Vector3 hullHitRumble = new(0.5f, 0.3f, 0.15f);

        [TitleGroup("Haptics")]
        [Tooltip("The hull reaching 0 and the ship exploding. (low motor, high motor, seconds)")]
        public Vector3 explosionRumble = new(1f, 1f, 0.8f);

        [TitleGroup("Haptics")]
        [Tooltip("Taking a repair orb: a soft, even pulse — neither a boost\'s kick nor a hit\'s rumble. (low motor, high motor, seconds)")]
        public Vector3 repairRumble = new(0.3f, 0.3f, 0.2f);

        [TitleGroup("Haptics")]
        [Tooltip("A speed LOSS impulse (a brake pad): a heavier thud than a boost. (low motor, high motor, seconds)")]
        public Vector3 brakeRumble = new(0.65f, 0.2f, 0.25f);

        [TitleGroup("Haptics")]
        [Tooltip("A dash: a short kick in the hands. (low motor, high motor, seconds)")]
        public Vector3 dashRumble = new(0.3f, 0.6f, 0.12f);

        [TitleGroup("Haptics")]
        [Tooltip("Too slow for a loop: the drop off its top. (low motor, high motor, seconds)")]
        public Vector3 loopFailRumble = new(0.9f, 0.5f, 0.5f);

        [TitleGroup("Haptics")]
        [Tooltip("Touching down after a jump. (low motor, high motor, seconds)")]
        public Vector3 landingRumble = new(0.5f, 0.3f, 0.2f);

        [TitleGroup("Haptics")]
        [Tooltip("Going over an open edge. (low motor, high motor, seconds)")]
        public Vector3 fallRumble = new(1f, 0.5f, 0.8f);

        [TitleGroup("Haptics")]
        [Tooltip("Losing grip on a flat sweep: a long low rumble rather than the wall\'s sharp knock. (low motor, high motor, seconds)")]
        public Vector3 slideRumble = new(0.6f, 0.2f, 0.5f);

        [TitleGroup("Haptics")]
        [Tooltip("A hard wall hit. (low motor, high motor, seconds)")]
        public Vector3 wallHitRumble = new(0.8f, 0.4f, 0.2f);

        [TitleGroup("Haptics")]
        [Tooltip("Flying through a laser beam. (low motor, high motor, seconds)")]
        public Vector3 laserHitRumble = new(1f, 0.7f, 0.8f);

        [TitleGroup("Haptics")]
        [Tooltip("The MISSION ACCOMPLISHED / FAILED banner slamming in. (low motor, high motor, seconds)")]
        public Vector3 winBannerRumble = new(0.7f, 1f, 0.25f);

        // ----------------------------------------------------------- boost QTE
        [ToggleGroup("boostQte", "Boost QTE")]
        [Tooltip("Timed boost: press Boost (A / Space) as the ship crosses a boost orb and the boost is multiplied by how close the press was. " +
                 "Taking the orb without a press still gives the plain boost. Off = orbs work exactly as before, no prompt.")]
        public bool boostQte = true;

        [ToggleGroup("boostQte")]
        [Tooltip("The prompt appears over the next boost orb once the ship is this many seconds from it at its current speed.")]
        [PropertyRange(0.2f, 3f), SuffixLabel("s", true)]
        public float boostQteShowSeconds = 1f;

        [ToggleGroup("boostQte")]
        [Tooltip("Graded window on EACH side of the crossing. A press this far before or after gets the low end of the multiplier band; further out is a miss.")]
        [PropertyRange(0.05f, 1f), SuffixLabel("s", true)]
        public float boostQteWindowSeconds = 0.15f;

        [ToggleGroup("boostQte")]
        [Tooltip("A press within this many seconds of the crossing is PERFECT — the top of the band and the green flash.")]
        [PropertyRange(0f, 0.2f), SuffixLabel("s", true)]
        public float boostQtePerfectSeconds = 0.02f;

        [ToggleGroup("boostQte")]
        [Tooltip("Boost multiplier: X at the edge of the window, Y for a perfect press.")]
        [MinMaxSlider(1f, 3f, true)]
        public Vector2 boostQteMultiplierBand = new(1.1f, 1.5f);

        [ToggleGroup("boostQte")]
        [Tooltip("Accuracy (0 = perfect, 1 = window edge) → share of the way from the band's top to its bottom.")]
        public AnimationCurve boostQteFalloff = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [ToggleGroup("boostQte")]
        [Tooltip("Warp kick of a perfect press, as a scale of the base orb warp. A graded press blends toward it.")]
        [PropertyRange(1f, 3f), SuffixLabel("x", true)]
        public float boostQteWarpAtPerfect = 1.8f;

        [ToggleGroup("boostQte")]
        [Tooltip("Rumble of a perfect press (low motor, high motor, seconds). A graded press blends from the base boost rumble toward it.")]
        public Vector3 boostQteRumbleAtPerfect = new(0.7f, 1f, 0.4f);

        [ToggleGroup("boostQte")]
        [Tooltip("Prompt glyph size as a share of the orb ring's width.")]
        [PropertyRange(0.1f, 1f)]
        public float boostQteGlyphSize = 0.45f;

        [ToggleGroup("boostQte")]
        [Tooltip("Prompt colour of a miss (pressed outside the window, or never pressed) and of the window's edge.")]
        public Color boostQteMissColor = new(1f, 0.15f, 0.12f);

        [ToggleGroup("boostQte")]
        [Tooltip("Prompt colour halfway between the window's edge and perfect.")]
        public Color boostQteMidColor = new(1f, 0.85f, 0.1f);

        [ToggleGroup("boostQte")]
        [Tooltip("Prompt colour of a perfect press.")]
        public Color boostQtePerfectColor = new(0.2f, 1f, 0.3f);

        // ----------------------------------------------------- track features
        [TitleGroup("Track features")]
        [Tooltip("Height of the air lane above the flight line — where Air-lane pads spawn, reachable only off a jump. Prepared for future power-ups; the tables hold no air entries yet.")]
        [PropertyRange(5f, 150f), SuffixLabel("m", true)]
        public float airLaneHeight = 30f;

        [TitleGroup("Track features")]
        [Tooltip("Camera shake when the ship lands after a jump. Empty = no shake.")]
        public Cameras.CameraShakeSettings landingShake;

        [TitleGroup("Track features")]
        [Tooltip("Camera shake when the ship slams a track edge or a ramp's side. Empty = no shake.")]
        public Cameras.CameraShakeSettings wallHitShake;

        [TitleGroup("Track features")]
        [Tooltip("Share of the forward speed a laser beam takes AT ONCE, on top of its hull damage (and still when the hull is off). The invulnerability blink shields it like the damage.")]
        [PropertyRange(0f, 0.5f)]
        public float laserSpeedLoss = 0.1f;

        [TitleGroup("Track features")]
        [Tooltip("Sprites of the smoke that pours off the hull after a laser hit — one is picked per plume. Empty = no smoke.")]
        public List<Texture2D> laserSmokeTextures = new();

        [TitleGroup("Track features")]
        [Tooltip("Size of the laser-hit smoke puffs.")]
        [PropertyRange(0.5f, 20f), SuffixLabel("m", true)]
        public float laserSmokeScale = 5f;

        [TitleGroup("Track features")]
        [Tooltip("Seconds the hull keeps smoking after a laser hit.")]
        [PropertyRange(0.1f, 6f), SuffixLabel("s", true)]
        public float laserSmokeSeconds = 2f;

        [TitleGroup("Track features")]
        [Tooltip("Puffs a second while the hull smokes.")]
        [PropertyRange(1f, 80f), SuffixLabel("/s", true)]
        public float laserSmokeRate = 22f;

        [TitleGroup("Track features")]
        [Tooltip("Puffs in the burst at the moment of the hit.")]
        [PropertyRange(0, 60)]
        public int laserSmokeBurst = 14;

        [TitleGroup("Track features")]
        [Tooltip("Camera shake when the ship starts sliding on a flat sweep taken too fast. Empty = no shake.")]
        public Cameras.CameraShakeSettings slideShake;

        [TitleGroup("Track features")]
        [Tooltip("Sparkles sprayed at the touchdown point when the ship lands (after a jump or a loop fall). 0 = no sparkles.")]
        [PropertyRange(0, 120)]
        public int landingSparkleCount = 45;

        [TitleGroup("Track features")]
        [Tooltip("Size of the landing burst — scales the sparks' size, speed and spread together.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("m", true)]
        public float landingSparkleScale = 3f;

        [TitleGroup("Track features")]
        [Tooltip("Tint of the landing sparkles (each spark is rolled between this and white).")]
        public Color landingSparkleColor = new(1f, 0.85f, 0.4f);

        // -------------------------------------------------------------- loops
        [TitleGroup("Loops")]
        [Tooltip("Entry speed a loop demands at the start of the run, km/h. A fresh launch (about 900) must fail it; one green orb (+870) must pass it.")]
        [PropertyRange(200f, 5000f), SuffixLabel("km/h", true)]
        public float loopSpeedFloorKmh = 1200f;

        [TitleGroup("Loops")]
        [Tooltip("How much the demand grows per 100 m of track travelled — the run's speed climbs, so a fixed number would be trivial late.")]
        [PropertyRange(0f, 100f), SuffixLabel("km/h per 100 m", true)]
        public float loopSpeedRampKmhPer100m = 18f;

        [TitleGroup("Loops")]
        [Tooltip("Cap on the demand, km/h. Keep it well under Light Speed.")]
        [PropertyRange(200f, 10000f), SuffixLabel("km/h", true)]
        public float loopSpeedCapKmh = 2900f;

        [TitleGroup("Loops")]
        [Tooltip("Glitch pulse strength when the ship drops off the top of a loop.")]
        [PropertyRange(0f, 1f)]
        public float loopFallGlitchStrength = 0.8f;

        [TitleGroup("Loops")]
        [Tooltip("Cut to the chase camera's cinematic side shot for the loop — and through the fall when the ship was too slow, so the shot never cuts mid-drop. The shot itself is authored on the camera settings asset (its Cinematic group).")]
        public bool loopCinematic = true;

        [TitleGroup("Loops")]
        [Tooltip("Real seconds the cinematic shot lingers after the ship is back on the track — the beat that lets the exit register before the chase view cuts back. The clock is easing up meanwhile.")]
        [PropertyRange(0f, 1f), SuffixLabel("s", true)]
        public float loopCinematicHoldSeconds = 0.25f;

        // --------------------------------------------------------- loop slow-mo
        [ToggleGroup("loopSlowMo", "Loop slow-mo")]
        [Tooltip("Slow the world while the ship is inside a loop (and through the fall of a failed one). Ship, patrol and the countdown all ride the same clock, so the loop costs no run time — it only plays longer in real time.")]
        public bool loopSlowMo = true;

        [ToggleGroup("loopSlowMo")]
        [Tooltip("The speed the loop is SHOWN at, km/h: the clock slows by exactly enough that the ship never appears faster than this through the loop, so a loop lasts the same few real seconds whether it was entered at 1300 or at 6000. A 100 m loop is 630 m round — at 650 the loop plays for about 3.5 s.")]
        [PropertyRange(100f, 2000f), SuffixLabel("km/h", true)]
        public float loopApparentSpeedKmh = 650f;

        [ToggleGroup("loopSlowMo")]
        [Tooltip("Slowest the clock gets, whatever the entry speed. 1 is real time.")]
        [PropertyRange(0.02f, 0.5f)]
        public float loopMinTimeScale = 0.08f;

        [ToggleGroup("loopSlowMo")]
        [Tooltip("Fastest the clock stays inside the loop, so even a loop taken right at its floor speed is unmistakably slowed. 1 is real time.")]
        [PropertyRange(0.1f, 1f)]
        public float loopTimeScale = 0.5f;

        [ToggleGroup("loopSlowMo")]
        [Tooltip("Real seconds the clock eases down over on entering the loop.")]
        [PropertyRange(0f, 1f), SuffixLabel("s", true)]
        public float loopSlowMoBlendIn = 0.2f;

        [ToggleGroup("loopSlowMo")]
        [Tooltip("Real seconds the clock eases back to 1 over on leaving it.")]
        [PropertyRange(0f, 1f), SuffixLabel("s", true)]
        public float loopSlowMoBlendOut = 0.35f;

        // ---------------------------------------------------- mission complete
        // The win latches as the ship leaves an end ramp with every objective
        // met: it flies on, the camera plants for the fly-past, the glitch
        // ramps to max, holds, and the Mission Complete panel opens.
        [TitleGroup("Mission complete")]
        [Tooltip("Real seconds the camera holds a planted trackside shot, watching the escaping ship fly on out of it, before the glitch ramps and the debrief opens. The player cannot move the camera for it. 0 = straight from the chase view into the glitch.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float winCameraHoldSeconds = 2f;

        [TitleGroup("Mission complete")]
        [Tooltip("Seconds the glitch takes to ramp from its current level to max once the ship is back on the track after the win.")]
        [PropertyRange(0.1f, 3f), SuffixLabel("s", true)]
        public float winGlitchRampSeconds = 0.8f;

        [TitleGroup("Mission complete")]
        [Tooltip("Seconds the glitch holds at max before the Mission Complete panel opens.")]
        [PropertyRange(0f, 2f), SuffixLabel("s", true)]
        public float winGlitchHoldSeconds = 0.4f;

        [TitleGroup("Mission complete")]
        [Tooltip("Real seconds after the ship is back on the track before the MISSION ACCOMPLISHED banner starts slamming in over the fly-past.")]
        [PropertyRange(0f, 2f), SuffixLabel("s", true)]
        public float winBannerDelaySeconds = 0.2f;

        [TitleGroup("Mission complete")]
        [Tooltip("Real seconds between one banner letter's entrance and the next. The whole word is in after letters × stagger + slam.")]
        [PropertyRange(0.01f, 0.2f), SuffixLabel("s", true)]
        public float winBannerLetterStaggerSeconds = 0.045f;

        [TitleGroup("Mission complete")]
        [Tooltip("Real seconds one banner letter takes to drop from 3× onto the screen and bounce to rest.")]
        [PropertyRange(0.1f, 1f), SuffixLabel("s", true)]
        public float winBannerLetterSlamSeconds = 0.32f;

        [TitleGroup("Mission complete")]
        [Tooltip("Glitch pulse strength on the frame the banner's last letter lands. 0 = none.")]
        [PropertyRange(0f, 1f)]
        public float winBannerGlitchPunch = 0.4f;

        [TitleGroup("Mission complete")]
        [Tooltip("Camera shake on the frame the banner's last letter lands. Empty = no shake.")]
        public Cameras.CameraShakeSettings winBannerShake;

        // ------------------------------------------------------ mission failed
        // Every loss slams the MISSION FAILED banner in (the win banner's own
        // letter timings) before the retry panel opens.
        [TitleGroup("Mission failed")]
        [Tooltip("Real seconds the MISSION FAILED banner stays up before the retry panel opens. A fall off the end of the track plays out under it.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float failBannerHoldSeconds = 2f;

        [TitleGroup("Mission failed")]
        [Tooltip("Real seconds the MISSION FAILED banner takes to tear away before the retry panel opens.")]
        [PropertyRange(0.25f, 2f), SuffixLabel("s", true)]
        public float failBannerDismissSeconds = 0.5f;

        [TitleGroup("Mission failed")]
        [Tooltip("Colour of the MISSION FAILED banner's letters and underline.")]
        public Color failBannerColor = new(1f, 0.22f, 0.25f, 1f);

        // ----------------------------------------------------- hull and lives
        // The ship's hull points live on ShipDefinition (maxHull) — this
        // section holds the run-level rules: what hurts, how much, how many
        // failed runs a mission forgives, and the explosion.
        [ToggleGroup("hullEnabled", "Hull and lives")]
        [Tooltip("Walls and brake pads damage the ship's hull (the HUD's life bar); at 0 it explodes and the run fails. Every failed run costs a life, and the last one lost is GAME OVER: no retry, back to the Store, the mission forfeited. Off = no damage, no bar, no lives — every loss retries for free.")]
        public bool hullEnabled = true;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Runs a mission forgives: every failed run (destroyed, caught, out of time, off the end) takes one, and losing the last is GAME OVER. A fresh set every time the runner is entered.")]
        [PropertyRange(1, 20)]
        public int startingLives = 3;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Hull points a brake pad takes.")]
        [PropertyRange(0f, 200f)]
        public float brakePadDamage = 20f;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Hull points a hard wall hit takes: a dash slammed into the track's edge, or a ramp hit from the side.")]
        [PropertyRange(0f, 200f)]
        public float wallSlamDamage = 25f;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Hull points steering (or sliding) into a wall takes. Still pressed against it when the invulnerability ends, it takes them again.")]
        [PropertyRange(0f, 200f)]
        public float wallScrapeDamage = 10f;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Hull points falling off the track takes, the moment the ship goes over an open edge — on top of the time the fall costs. It ignores the invulnerability blink; taking the last points, the ship blows up in the fall.")]
        [PropertyRange(0f, 200f)]
        public float fallDamage = 30f;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Hull points flying through a laser beam takes — a fall's worth by default. The invulnerability blink shields it like any other hit.")]
        [PropertyRange(0f, 200f)]
        public float laserDamage = 30f;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Camera shake on a laser hit, on top of the heavy rumble. Empty = no shake.")]
        public Cameras.CameraShakeSettings laserHitShake;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Seconds the ship blinks and takes no damage after a hit.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float hitInvulnerabilitySeconds = 1f;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Share of the full hull a repair orb (green cross in a red sphere) gives back, never past the max — the orb is collected even at full hull. Player only — the patrol never takes one.")]
        [PropertyRange(0.05f, 0.5f)]
        public float repairOrbHealFraction = 0.15f;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Camera shake on a wall scrape (the dash slam keeps Wall Hit Shake). Empty = no shake.")]
        public Cameras.CameraShakeSettings scrapeShake;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Glitch-effect burst strength on any hull hit.")]
        [PropertyRange(0f, 1f)]
        public float hullHitGlitchStrength = 0.35f;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Sprites of the ship's explosion — one is picked per fireball puff. Empty = no fireball (the ship still vanishes).")]
        public List<Texture2D> explosionTextures = new();

        [ToggleGroup("hullEnabled")]
        [Tooltip("Size of the explosion's fireball.")]
        [PropertyRange(1f, 60f), SuffixLabel("m", true)]
        public float explosionScale = 14f;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Seconds the fireball lives.")]
        [PropertyRange(0.2f, 5f), SuffixLabel("s", true)]
        public float explosionLifetime = 1.4f;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Puffs in the fireball.")]
        [PropertyRange(1, 80)]
        public int explosionParticles = 24;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Camera shake as the ship explodes. Empty = no shake.")]
        public Cameras.CameraShakeSettings explosionShake;

        [ToggleGroup("hullEnabled")]
        [Tooltip("Glitch-effect burst strength as the ship explodes.")]
        [PropertyRange(0f, 1f)]
        public float explosionGlitchStrength = 1f;

        // --------------------------------------------------------------- dash
        // Per-ship dash stats (power, speed, fill rate, ghost count) live on
        // ShipDefinition and the dash rules (on/off, cost, gesture, ghosts,
        // barrel-roll trails) on the ship's ShipSettings — this section only
        // holds the runner's own dash feedback: meter look, hints, glitch.
        [TitleGroup("Lateral dash")]
        [Tooltip("Glitch-effect burst strength when a dash slams the track edge.")]
        [PropertyRange(0f, 1f)]
        public float dashWallGlitchStrength = 0.7f;

        [TitleGroup("Lateral dash"), Title("Power meter")]
        [Tooltip("Fill colour of the meter once at least one dash is banked; dimmed while still charging. The bar itself is the DashMeter scene object under the Ship.")]
        public Color dashMeterColor = new(0.35f, 0.9f, 1f);

        [TitleGroup("Lateral dash"), Title("Encouragement")]
        [Tooltip("Seconds the meter may sit full and unused before the on-screen hint comes back.")]
        [PropertyRange(2f, 60f), SuffixLabel("s", true)]
        public float dashEncourageAfterSeconds = 10f;

        [TitleGroup("Lateral dash")]
        [Tooltip("Caption of the pulsing on-screen hint between the bumper glyphs / key labels.")]
        public string dashHintText = "DOUBLE-TAP TO DASH";

        [TitleGroup("Lateral dash")]
        [Tooltip("The same caption while the dash is single-press (the ship settings' Dash Single Press, or the player's SINGLE-PRESS DASH setting on the CONTROLS page).")]
        public string dashHintTextSinglePress = "PRESS TO DASH";

        // ------------------------------------------------------- floating text
        [TitleGroup("Floating text offsets")]
        [Tooltip("How far ahead of the ship the '+N' boost popups spawn.")]
        [PropertyRange(0f, 500f), SuffixLabel("m", true)]
        public float boostTextLeadMeters = 60f;

        // ------------------------------------------------------------ messages
        [TitleGroup("Story messages")]
        [Tooltip("Seconds an RPG message stays on screen after it finishes typing.")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        public float messageHoldSeconds = 2.5f;

        [TitleGroup("Story messages")]
        [Tooltip("Spawn-table entry (by name, see the TrackGenerator's Core Settings) whose pickup triggers the pilot's hype line. Empty = no orb message.")]
        public string messageOrbTierName = "Purple";

        [TitleGroup("Story messages"), MultiLineProperty(2), LabelText("Orb line")]
        public string purpleOrbMessage = "A purple charge! The engines are SCREAMING — hold on to something!";

        [TitleGroup("Story messages"), MultiLineProperty(2), LabelText("Patrol inbound line")]
        [Tooltip("Spoken by the patrol when a fresh one cuts in (only while 'Show patrol alert' is on). {0} = the patrol's number.")]
        public string patrolInboundMessage = "Patrol {0} inbound. You can't outrun all of us, hotshot.";

        [TitleGroup("Story messages"), MultiLineProperty(2), LabelText("Patrol warning line")]
        [Tooltip("Spoken by the patrol when it closes inside its warn distance (only while 'Show patrol warnings' is on). {0} = the gap in meters.")]
        public string patrolWarningMessage = "Right on your tail, hotshot — {0} m and closing.";

        [TitleGroup("Story messages")]
        public Color pilotMessageColor = new(0.35f, 0.9f, 1f);

        [TitleGroup("Story messages")]
        public Color patrolMessageColor = new(1f, 0.4f, 0.35f);

        // ------------------------------------------------------------- camera
        [TitleGroup("Camera")]
        [Tooltip("The ship's chase-camera feel (framing, modes, roll binding, FOV kick). The shared Cinemachine rig is attached to the ship with this asset on boot; empty = the scene keeps whatever camera it has.")]
        [InlineEditor]
        public Cameras.OrbitCameraSettings cameraSettings;

        // ------------------------------------------------------------ weather
        [ToggleGroup("rainEnabled", "Weather")]
        [Tooltip("Spawn the rain over the run. The downpour's own knobs live on the RainSettings asset below — this is only the on/off for this scene.")]
        public bool rainEnabled = true;

        // -------------------------------------------------------- speed lines
        [ToggleGroup("speedLinesEnabled", "Speed lines")]
        [Tooltip("Manga speed lines over the picture as the ship nears Light Speed. The look and the speed band (a fraction of Light Speed) live on the asset below — this is only the on/off for this scene.")]
        public bool speedLinesEnabled = true;

        [ToggleGroup("speedLinesEnabled")]
        [Tooltip("Burst of lines on a green (1×) orb pickup; blue and purple scale it by their tier (2.5× / 10×, clamped to full). 0 = no burst.")]
        [PropertyRange(0f, 1f)]
        public float boostPulseStrength = 0.4f;

        [ToggleGroup("speedLinesEnabled")]
        [Tooltip("How long a boost burst takes to fade back to the speed-driven level.")]
        [PropertyRange(0.05f, 2f), SuffixLabel("s", true)]
        public float boostPulseSeconds = 0.6f;

        // ------------------------------------------------------------ VHS tape
        [ToggleGroup("vhsEnabled", "VHS tape")]
        [Tooltip("Play the run back as a worn VHS tape: chroma bleed, row jitter, a crawling tracking band, grain and scanlines over the finished picture. The look lives on the asset below — this is only the on/off for this scene.")]
        public bool vhsEnabled = true;

        // ------------------------------------------------------------ PSX look
        [ToggleGroup("psxEnabled", "PSX look")]
        [Tooltip("Show the run as a PlayStation-1 console would: a 240-row picture with square pixels, vertex wobble and texture swim per polygon-sized block, 15-bit colour under a Bayer dither. The look lives on the asset below — this is only the on/off for this scene.")]
        public bool psxEnabled = true;

        // ---------------------------------------------------------- CRT screen
        [ToggleGroup("crtEnabled", "CRT screen")]
        [Tooltip("Show the run on a curved CRT tube: barrel curvature with rounded corners, phosphor bleed and colour convergence that grow toward the edges, scanlines, an aperture grille and refresh flicker over the finished picture — the last pass, the display the console and the tape play on. The look lives on the asset below — this is only the on/off for this scene; the player has their own dial on the VIDEO settings page.")]
        public bool crtEnabled = true;

        // --------------------------------------------------------------- music
        [ToggleGroup("musicEnabled", "Music")]
        [Tooltip("Play the runner's soundtrack loop over the run: a random start point every play, fade in on launch, fade out on win and lose. The clip, volume and fade times live on the asset below — this is only the on/off for this scene.")]
        public bool musicEnabled = true;

        [ToggleGroup("musicEnabled"), InlineEditor]
        [Tooltip("Music asset pushed onto the scene's RunnerMusic system on boot. Empty = leave the system with the asset it was authored with (the shipped FiniteRunner_Music from Resources).")]
        public MusicSettings musicSettings;

        // ------------------------------------------------------- sound effects
        [ToggleGroup("sfxEnabled", "Sound effects")]
        [Tooltip("The ship's own sounds: the speed-driven engine loop, the power-up pickup and the jump takeoff. Clips and feel live on the asset below — this is only the on/off for this scene.")]
        public bool sfxEnabled = true;

        [ToggleGroup("sfxEnabled"), InlineEditor]
        [Tooltip("Sound effects asset read live by the ship's ShipAudio. Empty = the shipped FiniteRunner_Sfx from Resources.")]
        public RunnerSfxSettings sfxSettings;

        // --------------------------------------------------------- accessors
        /// <summary>Boost multiplier at the edge of the timing window (band X).</summary>
        public float BoostQteMinMultiplier => boostQteMultiplierBand.x;

        /// <summary>Boost multiplier of a perfect press (band Y).</summary>
        public float BoostQteMaxMultiplier => boostQteMultiplierBand.y;
    }
}
