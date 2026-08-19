using Sirenix.OdinInspector;
using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Every knob of a chase run — win condition, timer, police behaviour,
    /// power-up strength and story messages — in one designer-facing asset.
    /// The GameManager owns no tunables of its own: it draws this asset inline
    /// in its inspector, so balancing happens here and survives scene changes.
    /// Everything is a slider (Odin) with an explicit range, and values that
    /// only make sense as a pair (catch/warn, redeploy in/out) are single
    /// min-max sliders so they can never be set the wrong way round.
    /// </summary>
    [CreateAssetMenu(fileName = "GameSettings", menuName = "FiniteRunner/Game Settings")]
    public class GameSettings : ScriptableObject
    {
        // ---------------------------------------------------------------- win
        [TitleGroup("Win condition")]
        [Tooltip("Light Speed — the speed that wins the run, in km/h.")]
        [PropertyRange(100f, 10000f), SuffixLabel("km/h", true)]
        public float lightSpeedKmh = 4000f;

        [TitleGroup("Win condition")]
        [Tooltip("Seconds to reach Light Speed before the chase is lost.")]
        [PropertyRange(10f, 300f), SuffixLabel("s", true)]
        public float timeLimitSeconds = 60f;

        // ------------------------------------------------------------- patrol
        [ToggleGroup("patrolEnabled", "Police patrol")]
        [Tooltip("Spawn the chasing patrol at runtime (no scene object needed).")]
        public bool patrolEnabled = true;

        [ToggleGroup("patrolEnabled")]
        [Tooltip("Patrol launch speed, and the rubber band's minimum — the patrol never goes slower than this. Keep it above the ship's launch speed.")]
        [PropertyRange(100f, 2000f), SuffixLabel("km/h", true)]
        public float patrolSpeedKmh = 350f;

        [ToggleGroup("patrolEnabled")]
        [Tooltip("How much the rubber band's minimum speed grows per second — the chase tightens the longer the run lasts.")]
        [PropertyRange(0f, 50f), SuffixLabel("km/h per s", true)]
        public float patrolRampKmhPerSecond = 3f;

        [ToggleGroup("patrolEnabled")]
        [Tooltip("Rubber band: the patrol chases the ship's current speed times this factor (1.05 = always 5% faster), but never below the minimum above.")]
        [PropertyRange(0.5f, 2f), SuffixLabel("x ship speed", true)]
        public float patrolRubberBandFactor = 1.05f;

        [ToggleGroup("patrolEnabled")]
        [Tooltip("How fast the patrol's speed adapts toward its rubber-band target. Lower = boosts buy more breathing room before the patrol matches them.")]
        [PropertyRange(1f, 500f), SuffixLabel("km/h per s", true)]
        public float patrolCatchUpKmhPerSecond = 60f;

        [ToggleGroup("patrolEnabled")]
        [Tooltip("Meters behind the start line the patrol launches from.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m", true)]
        public float patrolStartGap = 250f;

        [ToggleGroup("patrolEnabled")]
        [Tooltip("The danger band, in meters: X = gap that counts as caught (run over), Y = gap below which 'PATROL x M' warnings and rumble kick in.")]
        [MinMaxSlider(0f, 500f, true)]
        public Vector2 patrolDangerBand = new(10f, 130f);

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

        // ----------------------------------------------------------- power-up
        [TitleGroup("Power-ups")]
        [Tooltip("Base speed gain of a power-up orb in m/s (x3.6 for km/h). Each orb tier multiplies this — green 1x, blue 2.5x, purple 10x (tiers live on the TrackGenerator).")]
        [PropertyRange(0f, 100f), SuffixLabel("m/s", true)]
        public float powerUpSpeedBoost = 15f;

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

        [ToggleGroup("dashEnabled"), Title("Power meter")]
        [Tooltip("Fill colour of the meter once at least one dash is banked; dimmed while still charging. The bar itself is the DashMeter scene object under the Ship.")]
        public Color dashMeterColor = new(0.35f, 0.9f, 1f);

        [ToggleGroup("dashEnabled"), Title("Encouragement")]
        [Tooltip("Seconds the meter may sit full and unused before the narrator nags again.")]
        [PropertyRange(2f, 60f), SuffixLabel("s", true)]
        public float dashEncourageAfterSeconds = 10f;

        [ToggleGroup("dashEnabled"), MultiLineProperty(2), LabelText("First-full line")]
        public string dashFirstFullMessage = "Side boosters charged! Double-tap the bumpers to dash — thread the orbs, dodge the pads!";

        [ToggleGroup("dashEnabled"), MultiLineProperty(2), LabelText("Reminder line")]
        public string dashReminderMessage = "Those side boosters won't fire themselves — double-tap and DASH!";

        [ToggleGroup("dashEnabled")]
        [Tooltip("Caption of the pulsing on-screen hint between the bumper glyphs / key labels.")]
        public string dashHintText = "DOUBLE-TAP TO DASH";

        [ToggleGroup("dashEnabled")]
        public Color dashMessageColor = new(1f, 0.85f, 0.4f);

        // ------------------------------------------------------- floating text
        [TitleGroup("Floating text offsets")]
        [Tooltip("How far ahead of the ship the '+N' boost popups spawn.")]
        [PropertyRange(0f, 500f), SuffixLabel("m", true)]
        public float boostTextLeadMeters = 60f;

        [TitleGroup("Floating text offsets")]
        [Tooltip("How far ahead of the ship the 'PATROL x M' alerts spawn.")]
        [PropertyRange(0f, 500f), SuffixLabel("m", true)]
        public float patrolAlertLeadMeters = 180f;

        // ------------------------------------------------------------ messages
        [TitleGroup("Story messages")]
        [Tooltip("Seconds an RPG message stays on screen after it finishes typing.")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        public float messageHoldSeconds = 2.5f;

        [TitleGroup("Story messages")]
        [Tooltip("Orb tier (by name, see TrackGenerator.orbTiers) whose pickup triggers the pilot's hype line. Empty = no orb message.")]
        public string messageOrbTierName = "Purple";

        [TitleGroup("Story messages"), MultiLineProperty(2), LabelText("Orb line")]
        public string purpleOrbMessage = "A purple charge! The engines are SCREAMING — hold on to something!";

        [TitleGroup("Story messages"), MultiLineProperty(2), LabelText("Win line")]
        public string winMessage = "LIGHT SPEED! Not even the whole patrol fleet can touch us now!";

        [TitleGroup("Story messages"), MultiLineProperty(2), LabelText("Lose line")]
        public string loseMessage = "Gotcha, hotshot. Party's over — powering you down.";

        [TitleGroup("Story messages")]
        public Color pilotMessageColor = new(0.35f, 0.9f, 1f);

        [TitleGroup("Story messages")]
        public Color patrolMessageColor = new(1f, 0.4f, 0.35f);

        // --------------------------------------------------------- accessors
        /// <summary>Gap in meters at which the patrol catches the ship (min-max band X).</summary>
        public float PatrolCatchDistance => patrolDangerBand.x;

        /// <summary>Gap in meters below which warnings and proximity rumble start (band Y).</summary>
        public float PatrolWarnDistance => patrolDangerBand.y;

        /// <summary>How far behind the ship a fresh patrol drops in, in meters (band X).</summary>
        public float PatrolRedeployGap => patrolRedeployBand.x;

        /// <summary>Gap that retires the current patrol for a fresh one; 0 when redeploying is off (band Y).</summary>
        public float PatrolRedeployDistance => patrolRedeploys ? patrolRedeployBand.y : 0f;
    }
}
