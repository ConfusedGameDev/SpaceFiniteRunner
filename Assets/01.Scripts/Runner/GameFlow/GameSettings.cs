using Sirenix.OdinInspector;
using UnityEngine;

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
        // --------------------------------------------------------------- flow
        [TitleGroup("Flow")]
        [Tooltip("Open the pre-run point-allocation screen (TuningScreen) instead of flying straight on the Store's bought upgrades. Off in the shipping flow: the Store between missions owns the ship's stats, and the runner is entered mid-mission from the city with no pause for a setup panel. On, the screen applies the store levels on top of its points.")]
        public bool useTuningScreen = false;

        // ---------------------------------------------------------------- win
        [TitleGroup("Win condition")]
        [Tooltip("Light Speed — the speed that wins the run, in km/h.")]
        [PropertyRange(100f, 10000f), SuffixLabel("km/h", true)]
        public float lightSpeedKmh = 6500f;

        [TitleGroup("Win condition")]
        [Tooltip("Seconds to reach Light Speed before the chase is lost.")]
        [PropertyRange(10f, 300f), SuffixLabel("s", true)]
        public float timeLimitSeconds = 60f;

        // ------------------------------------------------------------- patrol
        // The chase tunables (speeds, rubber band, distances) moved to the
        // PatrolDefinition asset on the scene's PolicePatrol object — this
        // section only keeps the run-level rules: on/off, minimap, redeploy.
        [ToggleGroup("patrolEnabled", "Police patrol")]
        [Tooltip("Enable the chasing patrol (the scene object the GameManager references; its chase tunables live on its PatrolDefinition asset).")]
        public bool patrolEnabled = true;

        [ToggleGroup("patrolEnabled")]
        [Tooltip("Gap that puts the patrol icon at the very bottom of the chase minimap.")]
        [PropertyRange(50f, 2000f), SuffixLabel("m", true)]
        public float minimapRangeMeters = 400f;

        // ----------------------------------------------------------- redeploy
        [ToggleGroup("patrolEnabled"), Title("Redeploy")]
        [Tooltip("Keep the chase alive: once the ship is clear by the outer distance below, that patrol drops out and a fresh one cuts in.")]
        public bool patrolRedeploys = true;

        [ToggleGroup("patrolEnabled")]
        [Tooltip("Redeploy band, in meters: X = how far behind the ship the fresh patrol drops in (keep it inside the minimap range so the player sees it arrive), Y = the gap that retires the old one.")]
        [MinMaxSlider(50f, 2000f, true), EnableIf("patrolRedeploys")]
        public Vector2 patrolRedeployBand = new(320f, 700f);

        [ToggleGroup("patrolEnabled")]
        [Tooltip("Fresh patrol's speed as a multiple of the ship's current speed. Above 1 so it closes in until the next boost.")]
        [PropertyRange(1f, 3f), SuffixLabel("x ship speed", true), EnableIf("patrolRedeploys")]
        public float patrolRedeploySpeedFactor = 1.25f;

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

        // --------------------------------------------------------------- dash
        // Per-ship dash stats (power, speed, fill rate, ghost count) live on
        // ShipDefinition — this section only holds the run-level rules.
        [ToggleGroup("dashEnabled", "Lateral dash")]
        [Tooltip("Enable the double-tap lateral dash (bumpers on pad, N/M on keyboard) with its power meter, ghosts and prompts.")]
        public bool dashEnabled = true;

        [ToggleGroup("dashEnabled")]
        [Tooltip("Meter fraction one dash consumes. 0.5 = a full meter holds two dashes.")]
        [PropertyRange(0.1f, 1f)]
        public float dashCost = 0.5f;

        [ToggleGroup("dashEnabled")]
        [Tooltip("Max seconds between two taps of the same bumper/key that still count as a double tap.")]
        [PropertyRange(0.1f, 0.6f), SuffixLabel("s", true)]
        public float dashDoubleTapSeconds = 0.3f;

        [ToggleGroup("dashEnabled")]
        [Tooltip("Glitch-effect burst strength when a dash slams the track edge.")]
        [PropertyRange(0f, 1f)]
        public float dashWallGlitchStrength = 0.7f;

        [ToggleGroup("dashEnabled")]
        [Tooltip("Minimum seconds between two wall-slam feedbacks, so hugging the edge can't spam them.")]
        [PropertyRange(0.1f, 2f), SuffixLabel("s", true)]
        public float dashWallHitCooldownSeconds = 0.5f;

        [ToggleGroup("dashEnabled"), Title("Ghost trail")]
        [Tooltip("Seconds an onion-skin ghost takes to fade out completely.")]
        [PropertyRange(0.05f, 1f), SuffixLabel("s", true)]
        public float dashGhostLifetime = 0.35f;

        [ToggleGroup("dashEnabled")]
        [Tooltip("Starting opacity of a freshly spawned ghost.")]
        [PropertyRange(0f, 1f)]
        public float dashGhostStartAlpha = 0.45f;

        [ToggleGroup("dashEnabled")]
        [Tooltip("Transparent URP material for the onion-skin ghosts (Materials/DashGhost_Mat). Empty = a runtime fallback material is built instead.")]
        public Material dashGhostMaterial;

        [ToggleGroup("dashEnabled"), Title("Barrel roll trails")]
        [Tooltip("Seconds the two wingtip ribbons drawn during an airborne barrel roll linger before they fade out — the spiral's length in time.")]
        [PropertyRange(0.1f, 2f), SuffixLabel("s", true)]
        public float barrelRollTrailSeconds = 0.6f;

        [ToggleGroup("dashEnabled")]
        [Tooltip("Width of a wingtip ribbon at the ship, in metres; it tapers to nothing along its length.")]
        [PropertyRange(0.1f, 4f), SuffixLabel("m", true)]
        public float barrelRollTrailWidth = 0.9f;

        [ToggleGroup("dashEnabled")]
        [Tooltip("Where along the wing each ribbon is emitted, as a fraction of the model's half-width (1 = the wingtip).")]
        [PropertyRange(0.2f, 1.2f)]
        public float barrelRollTrailSpan = 1f;

        [ToggleGroup("dashEnabled")]
        [Tooltip("Ribbon colour at the ship; it fades to transparent along the trail.")]
        public Color barrelRollTrailColor = new(0.45f, 0.9f, 1f, 0.9f);

        [ToggleGroup("dashEnabled")]
        [Tooltip("Vertex-coloured transparent URP material for the ribbons (Materials/BarrelRollTrail_Mat). Empty = a runtime fallback material is built instead.")]
        public Material barrelRollTrailMaterial;

        [ToggleGroup("dashEnabled"), Title("Power meter")]
        [Tooltip("Fill colour of the meter once at least one dash is banked; dimmed while still charging. The bar itself is the DashMeter scene object under the Ship.")]
        public Color dashMeterColor = new(0.35f, 0.9f, 1f);

        [ToggleGroup("dashEnabled"), Title("Encouragement")]
        [Tooltip("Seconds the meter may sit full and unused before the on-screen hint comes back.")]
        [PropertyRange(2f, 60f), SuffixLabel("s", true)]
        public float dashEncourageAfterSeconds = 10f;

        [ToggleGroup("dashEnabled")]
        [Tooltip("Caption of the pulsing on-screen hint between the bumper glyphs / key labels.")]
        public string dashHintText = "DOUBLE-TAP TO DASH";

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

        [ToggleGroup("rainEnabled"), InlineEditor]
        [Tooltip("Override asset pushed onto the scene's RainSystem on boot. Empty = leave that system with the asset it was authored with (the shipped FiniteRunner_Rain from Resources).")]
        public RainSettings rainSettings;

        // -------------------------------------------------------- speed lines
        [ToggleGroup("speedLinesEnabled", "Speed lines")]
        [Tooltip("Manga speed lines over the picture as the ship nears Light Speed. The look and the speed band (a fraction of Light Speed) live on the asset below — this is only the on/off for this scene.")]
        public bool speedLinesEnabled = true;

        [ToggleGroup("speedLinesEnabled"), InlineEditor]
        [Tooltip("Speed lines asset pushed onto the driver on boot. Empty = the shipped FiniteRunner_SpeedLines from Resources.")]
        public SpeedLinesSettings speedLinesSettings;

        [ToggleGroup("speedLinesEnabled")]
        [Tooltip("Burst of lines on a green (1×) orb pickup; blue and purple scale it by their tier (2.5× / 10×, clamped to full). 0 = no burst.")]
        [PropertyRange(0f, 1f)]
        public float boostPulseStrength = 0.4f;

        [ToggleGroup("speedLinesEnabled")]
        [Tooltip("How long a boost burst takes to fade back to the speed-driven level.")]
        [PropertyRange(0.05f, 2f), SuffixLabel("s", true)]
        public float boostPulseSeconds = 0.6f;

        // --------------------------------------------------------- accessors
        /// <summary>How far behind the ship a fresh patrol drops in, in meters (band X).</summary>
        public float PatrolRedeployGap => patrolRedeployBand.x;

        /// <summary>Gap that retires the current patrol for a fresh one; 0 when redeploying is off (band Y).</summary>
        public float PatrolRedeployDistance => patrolRedeploys ? patrolRedeployBand.y : 0f;
    }
}
