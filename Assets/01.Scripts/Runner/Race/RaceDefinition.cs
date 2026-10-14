using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Race
{
    /// <summary>
    /// One rival pilot: its look, its size for contact and its personality
    /// (RaceModePRD D9). Pace is a share of the run's Light Speed, so a level
    /// that raises the goal raises the field with it.
    /// </summary>
    [Serializable]
    public class RivalEntry
    {
        [Tooltip("Debug / inspector name of this pilot.")]
        [TableColumnWidth(90)]
        public string displayName = "Rival";

        [Tooltip("The rival's model. Instantiated under the pooled shell with its transform reset and every collider stripped. Empty = a code-built placeholder hull in the map colour.")]
        [TableColumnWidth(140)]
        public GameObject prefab;

        [Tooltip("Size of the model.")]
        [PropertyRange(0.1f, 10f), TableColumnWidth(60)]
        public float scale = 1f;

        [Tooltip("Yaw that turns the model's nose down its own +Z.")]
        [PropertyRange(-180f, 180f), SuffixLabel("°", true), TableColumnWidth(70)]
        public float yawOffset = 0f;

        [Tooltip("Height of the model above the flight line, along the track's up.")]
        [PropertyRange(0f, 10f), SuffixLabel("m", true), TableColumnWidth(60)]
        public float hoverHeight = 2f;

        [Tooltip("Half its length along the track, for contact and spacing.")]
        [PropertyRange(0.5f, 20f), SuffixLabel("m", true), TableColumnWidth(60)]
        public float halfLength = 9.5f;

        [Tooltip("Half its width across the track, for contact, spacing and pickups.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("m", true), TableColumnWidth(60)]
        public float halfWidth = 4.5f;

        [Tooltip("Paint over the model's own colour; the alpha is how much of it (0 = the model as authored, 1 = fully this colour). Applied to a per-rival copy of the model's materials.")]
        [TableColumnWidth(60)]
        public Color paintTint = new(1f, 1f, 1f, 0f);

        [Tooltip("Its dot on the minimap (and the placeholder hull's colour).")]
        [TableColumnWidth(60)]
        public Color mapColor = new(1f, 0.55f, 0.1f);

        [Tooltip("Cruise pace as a share of the run's Light Speed. The pack's skill spread lives here.")]
        [PropertyRange(0.2f, 1.5f), TableColumnWidth(70)]
        public float cruiseFraction = 0.75f;

        [Tooltip("How hard it accelerates, as a multiple of the field's thrust. With the pace caps out of reach on most tracks, this is what orders the pack.")]
        [PropertyRange(0.5f, 1.5f), TableColumnWidth(70)]
        public float thrustFactor = 1f;

        [Tooltip("How far it wanders off its grid lane fighting for the line, 0 (holds its lane) to 1 (all over the road).")]
        [PropertyRange(0f, 1f), TableColumnWidth(70)]
        public float aggression = 0.3f;

        [Tooltip("Chance per hazard of misjudging it (late brake for a flat sweep, a shaved open edge, a gap at the end). Read from M3 of the race PRD.")]
        [PropertyRange(0f, 1f), TableColumnWidth(70)]
        public float mistakeRate = 0.05f;

        [Tooltip("Chance it goes for a boost orb it can reach. Rolled once per orb.")]
        [PropertyRange(0f, 1f), TableColumnWidth(70)]
        public float orbAppetite = 0.5f;

        [Tooltip("Rams from behind it takes before it is wrecked.")]
        [PropertyRange(1, 10), TableColumnWidth(60)]
        public int ramHull = 3;

        [Tooltip("Money the player is paid when it is wrecked because of them (a ram, or a shove into a beam, off an edge or into a gap). Its own mistakes pay nothing.")]
        [PropertyRange(0, 5000), SuffixLabel("$", true), TableColumnWidth(70)]
        public int bounty = 250;
    }

    /// <summary>
    /// A race (RaceModePRD.md): the rival pilots a race level puts on the grid
    /// beside the ship, the grid itself, how the pack is paced and how hard the
    /// rivals are simulated. A level opts in through
    /// <c>RunnerLevelDefinition.race</c>; the <see cref="RaceSystem"/> clones
    /// it at bind, so gameplay never writes it and the debug menu edits the
    /// asset then mirrors onto the clone. Every rival tunable lives here, not on
    /// the system.
    /// </summary>
    [CreateAssetMenu(fileName = "RaceDefinition", menuName = "FiniteRunner/Race Definition")]
    public class RaceDefinition : ScriptableObject
    {
        [TitleGroup("Rivals")]
        [Tooltip("The pilots on the grid, front row first. One pooled shell each, built at bind.")]
        [TableList(AlwaysExpanded = true)]
        public List<RivalEntry> rivals = new();

        // ---- grid ----
        [TitleGroup("Grid")]
        [Tooltip("Racers per grid row (the ship included).")]
        [PropertyRange(1, 6)]
        public int gridColumns = 3;

        [TitleGroup("Grid")]
        [Tooltip("Metres between two grid rows along the track.")]
        [PropertyRange(10f, 120f), SuffixLabel("m", true)]
        public float gridRowSpacing = 32f;

        [TitleGroup("Grid")]
        [Tooltip("Metres between two grid lanes across the track.")]
        [PropertyRange(5f, 40f), SuffixLabel("m", true)]
        public float gridLaneSpacing = 14f;

        [TitleGroup("Grid")]
        [Tooltip("The ship's own slot, counted row by row from the front-left (0). -1 = the middle of the back row, so the player starts behind the field.")]
        [PropertyRange(-1, 30)]
        public int playerGridSlot = -1;

        // ---- pace ----
        [TitleGroup("Pace")]
        [Tooltip("Most the rubber band moves a rival's thrust and target pace, as a ± share: a rival ahead of the ship eases off, one behind pushes. Keeps a runaway leader catchable without erasing the skill spread.")]
        [PropertyRange(0f, 0.3f)]
        public float rubberBand = 0.15f;

        [TitleGroup("Pace")]
        [Tooltip("Gap to the ship at which the rubber band is at full strength, metres.")]
        [PropertyRange(100f, 10000f), SuffixLabel("m", true)]
        public float rubberRange = 1500f;

        [TitleGroup("Pace")]
        [Tooltip("A ghost (out of sight) accelerates and cruises at its thrust and target pace × this — the stand-in for the time it would lose to sweeps and hazards.")]
        [PropertyRange(0.5f, 1.2f)]
        public float ghostPaceFactor = 0.94f;

        [TitleGroup("Pace")]
        [Tooltip("Acceleration up to its target pace, as a share of the player's ship's BASE thrust (its definition asset, before Store upgrades). Below 1 a player holding the throttle out-accelerates the field and the orbs decide the race; upgrades make the player faster against it.")]
        [PropertyRange(0.3f, 1.5f)]
        public float thrustShare = 0.8f;

        [TitleGroup("Pace")]
        [Tooltip("Above its target pace (a boost), the speed bleeds back down at this rate, m/s².")]
        [PropertyRange(1f, 300f), SuffixLabel("m/s²", true)]
        public float overPaceBleed = 40f;

        [TitleGroup("Pace")]
        [Tooltip("Share of a boost orb's speed a rival gets when it takes one (used up, like the patrol's).")]
        [PropertyRange(0f, 2f)]
        public float orbBoostShare = 0.4f;

        [TitleGroup("Pace")]
        [Tooltip("Each run every rival's thrust and pace are rolled up to this ± share off its authored values: its form on the day, so the same field finishes in a different order.")]
        [PropertyRange(0f, 0.3f)]
        public float formVariance = 0.08f;

        [TitleGroup("Pace")]
        [Tooltip("How much a rival's thrust swings over the run (± share), each on its own slow wave: surges and fades that make the pack trade places.")]
        [PropertyRange(0f, 0.5f)]
        public float surgeAmount = 0.2f;

        [TitleGroup("Pace")]
        [Tooltip("Seconds per swing of the surge.")]
        [PropertyRange(3f, 60f), SuffixLabel("s", true)]
        public float surgePeriod = 14f;

        [TitleGroup("Pace")]
        [Tooltip("Each rival's reaction at lights out, seconds after the ship moves (rolled per run in this band): the grid breaks up from the first second.")]
        [MinMaxSlider(0f, 2f, true)]
        public Vector2 launchReaction = new(0f, 0.7f);

        [TitleGroup("Pace")]
        [Tooltip("Share of the half road a rival's home lane is rolled across each run (0 = its grid lane for the whole race).")]
        [PropertyRange(0f, 1f)]
        public float laneSpread = 0.65f;

        // ---- lead governor ----
        [ToggleGroup(nameof(leadGovernor), "Lead governor")]
        [Tooltip("A rival AHEAD of the ship can go no faster than the ship's recent speed × a factor (Near right ahead, Far at the rubber range and beyond). Rivals drive clean and lose nothing to beams, walls or braking; without this a leader built on one bad stretch of the player's was never caught.")]
        public bool leadGovernor = true;

        [ToggleGroup(nameof(leadGovernor))]
        [Tooltip("Speed cap, as a share of the ship's recent speed, for a rival just ahead of it: above 1 it can still pull away a little while you are close.")]
        [PropertyRange(0.8f, 1.3f)]
        public float leadCapNear = 1.05f;

        [ToggleGroup(nameof(leadGovernor))]
        [Tooltip("Speed cap, as a share of the ship's recent speed, for a rival the rubber range or more ahead: below 1 a player holding their speed always reels it in.")]
        [PropertyRange(0.6f, 1.1f)]
        public float leadCapFar = 0.9f;

        [ToggleGroup(nameof(leadGovernor))]
        [Tooltip("Seconds the ship's speed is smoothed over for the cap: a laser hit or a fall does not stop the field dead.")]
        [PropertyRange(0.2f, 10f), SuffixLabel("s", true)]
        public float leadSpeedSmoothing = 3f;

        // ---- contact with the ship ----
        [TitleGroup("Contact")]
        [Tooltip("The ship's half length along the track, for contact with rivals.")]
        [PropertyRange(1f, 20f), SuffixLabel("m", true)]
        public float shipHalfLength = 9.6f;

        [TitleGroup("Contact")]
        [Tooltip("The ship's half width across the track, for contact with rivals.")]
        [PropertyRange(1f, 10f), SuffixLabel("m", true)]
        public float shipHalfWidth = 4.75f;

        [TitleGroup("Contact")]
        [Tooltip("Closing speed below which running into a rival is a nudge (it is held clear, nobody pays), m/s.")]
        [PropertyRange(0f, 60f), SuffixLabel("m/s", true)]
        public float ramClosingThreshold = 8f;

        [TitleGroup("Contact")]
        [Tooltip("Share of the ship's speed a ram from behind costs it.")]
        [PropertyRange(0f, 0.5f)]
        public float ramSpeedCost = 0.06f;

        [TitleGroup("Contact")]
        [Tooltip("Share of the closing speed a rammed rival is knocked forward with.")]
        [PropertyRange(0f, 1.5f)]
        public float ramSpeedTransfer = 0.6f;

        [TitleGroup("Contact")]
        [Tooltip("Metres a side-by-side contact throws each hull away from the other (the ship's dash scale).")]
        [PropertyRange(0f, 30f), SuffixLabel("m", true)]
        public float sideShoveMeters = 6f;

        [TitleGroup("Contact")]
        [Tooltip("Metres a rammed rival is knocked sideways (its side picked by where it was hit).")]
        [PropertyRange(0f, 30f), SuffixLabel("m", true)]
        public float ramKnockMeters = 8f;

        [TitleGroup("Contact")]
        [Tooltip("Seconds of travel ahead a rival plans to pass the ship rather than run into it.")]
        [PropertyRange(0f, 4f), SuffixLabel("s", true)]
        public float passLookaheadSeconds = 1.5f;

        [TitleGroup("Contact")]
        [Tooltip("A rival wrecked within this many seconds of its last contact with the ship is the player's kill (and pays its bounty).")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        public float creditWindow = 3f;

        [TitleGroup("Contact")]
        [Tooltip("Colour of the sparks a contact throws.")]
        public Color contactSparkColor = new(1f, 0.75f, 0.3f);

        // ---- handling (the patrol's model) ----
        [TitleGroup("Handling")]
        [Tooltip("Lateral speed full steer settles at, m/s.")]
        [PropertyRange(5f, 120f), SuffixLabel("m/s", true)]
        public float lateralSpeed = 26f;

        [TitleGroup("Handling")]
        [Tooltip("Lateral drag, 1/s: how fast the lateral velocity settles.")]
        [PropertyRange(0.5f, 20f)]
        public float handlingResponse = 6f;

        [TitleGroup("Handling")]
        [Tooltip("Grip on a flat sweep at rest, m/s².")]
        [PropertyRange(0f, 400f), SuffixLabel("m/s²", true)]
        public float gripBase = 130f;

        [TitleGroup("Handling")]
        [Tooltip("Extra grip per m/s of speed.")]
        [PropertyRange(0f, 3f)]
        public float gripPerSpeed = 0.5f;

        [TitleGroup("Handling")]
        [Tooltip("Deceleration at full brake, m/s².")]
        [PropertyRange(10f, 600f), SuffixLabel("m/s²", true)]
        public float brakeDecel = 140f;

        [TitleGroup("Handling")]
        [Tooltip("Outward lateral speed past which a slip is a slide, m/s.")]
        [PropertyRange(0f, 30f), SuffixLabel("m/s", true)]
        public float slideThreshold = 4f;

        [TitleGroup("Handling")]
        [Tooltip("Share of the forward speed a slide scrubs per second.")]
        [PropertyRange(0f, 1f)]
        public float slideSpeedLoss = 0.1f;

        [TitleGroup("Handling")]
        [Tooltip("Seconds of travel ahead a boost orb is considered.")]
        [PropertyRange(0.2f, 6f), SuffixLabel("s", true)]
        public float orbLookaheadSeconds = 2.5f;

        [TitleGroup("Handling")]
        [Tooltip("Seconds of travel ahead a ramp or laser gate is planned for.")]
        [PropertyRange(0.2f, 6f), SuffixLabel("s", true)]
        public float hazardLookaheadSeconds = 2.5f;

        [TitleGroup("Handling")]
        [Tooltip("Metres past a horizontal beam's end (or a rotor blade's tip) a rival treats as burning — the emitter's body plus a margin.")]
        [PropertyRange(0f, 20f), SuffixLabel("m", true)]
        public float gateClearance = 7f;

        [TitleGroup("Handling")]
        [Tooltip("Half width of a VERTICAL beam's sheet, metres either side of it, for gates not built yet. Once one is built its measured width is used when larger (the shipped model is ~10 m).")]
        [PropertyRange(0.5f, 30f), SuffixLabel("m", true)]
        public float verticalBeamHalfWidth = 10.5f;

        [TitleGroup("Handling")]
        [Tooltip("Extra metres a rival keeps between its hull and a beam's zone, on top of the clearances above: steering closes on its line gradually, so the target sits this far inside the free road.")]
        [PropertyRange(0f, 15f), SuffixLabel("m", true)]
        public float gateMargin = 4f;

        [TitleGroup("Handling")]
        [Tooltip("Seconds of travel ahead a flat sweep is braked for.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("s", true)]
        public float curveLookaheadSeconds = 4f;

        [TitleGroup("Handling")]
        [Tooltip("Least lateral gap two rivals side by side keep, metres.")]
        [PropertyRange(2f, 30f), SuffixLabel("m", true)]
        public float rivalSeparation = 10f;

        // ---- mistakes ----
        [TitleGroup("Mistakes")]
        [Tooltip("A rival that misjudges a flat sweep arrives at its grip limit × this (above 1 it slides, and may go off an open edge).")]
        [PropertyRange(1f, 2f)]
        public float mistakeOverspeed = 1.3f;

        [TitleGroup("Mistakes")]
        [Tooltip("A rival that misjudges an open edge holds its line this far past it — long enough and it falls.")]
        [PropertyRange(0f, 10f), SuffixLabel("m", true)]
        public float mistakeEdgeShave = 3f;

        [TitleGroup("Mistakes")]
        [Tooltip("Metres of open-edged road counted as one hazard (one roll of the rival's mistake rate).")]
        [PropertyRange(100f, 3000f), SuffixLabel("m", true)]
        public float edgeHazardLength = 800f;

        // ---- kill cam (D4) ----
        [ToggleGroup(nameof(killCam), "Kill cam")]
        [Tooltip("A rival wrecked near the ship (in view) plays the kill cam: slow motion, a cut to a planted shot of the explosion, the ship on autopilot and untouchable, then back.")]
        public bool killCam = true;

        [ToggleGroup(nameof(killCam))]
        [Tooltip("Farthest a wreck can be from the ship, along the track, and still play it.")]
        [PropertyRange(50f, 3000f), SuffixLabel("m", true)]
        public float killCamRange = 700f;

        [ToggleGroup(nameof(killCam))]
        [Tooltip("The world clock's scale at the bottom of the slow motion.")]
        [PropertyRange(0.05f, 1f)]
        public float killCamTimeScale = 0.2f;

        [ToggleGroup(nameof(killCam))]
        [Tooltip("Real seconds the shot holds at full slow motion.")]
        [PropertyRange(0.2f, 4f), SuffixLabel("s", true)]
        public float killCamSeconds = 1.3f;

        [ToggleGroup(nameof(killCam))]
        [Tooltip("Real seconds the clock eases in and back out.")]
        [MinMaxSlider(0f, 1f, true)]
        public Vector2 killCamBlend = new(0.12f, 0.3f);

        [ToggleGroup(nameof(killCam))]
        [Tooltip("Seconds the ship stays untouchable (blinking) after the cut back.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float killCamGrace = 1f;

        [ToggleGroup(nameof(killCam))]
        [Tooltip("Real seconds after one kill cam before another can play.")]
        [PropertyRange(0f, 30f), SuffixLabel("s", true)]
        public float killCamCooldown = 6f;

        [ToggleGroup(nameof(killCam))]
        [Tooltip("The camera's spot, from the explosion in the track's frame: metres out to its side (away from the ship), up, and ahead along the track.")]
        public Vector3 killCamOffset = new(40f, 14f, 30f);

        // ---- fidelity ----
        [TitleGroup("Fidelity")]
        [Tooltip("The full-sim window round the ship: metres BEHIND it / AHEAD of it (clamped to the built road). Outside it a rival is a ghost. Keep the ahead edge past the fog.")]
        [MinMaxSlider(0f, 5000f, true)]
        public Vector2 fullSimBand = new(400f, 1700f);

        [TitleGroup("Fidelity")]
        [Tooltip("Seconds a finished rival flies on off its ramp before it is parked.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("s", true)]
        public float finishFlightSeconds = 4f;

        // ---- debug ----
        [TitleGroup("Debug")]
        [Tooltip("Prints the live standings and each rival's state, pace and fidelity on screen.")]
        public bool debugReadout = false;

        [TitleGroup("Debug")]
        [Tooltip("Draws every ghost rival (hidden, off the full sim) as a wireframe box in its map colour, where its distance and home lane put it.")]
        public bool debugGhostWireframes = false;

        /// <summary>Metres behind the ship the full-sim window reaches.</summary>
        public float FullSimBehind => fullSimBand.x;
        /// <summary>Metres ahead of the ship the full-sim window reaches (before the built-road clamp).</summary>
        public float FullSimAhead => fullSimBand.y;
        /// <summary>Real seconds the kill cam's clock eases in / back out.</summary>
        public float KillCamBlendIn => killCamBlend.x;
        public float KillCamBlendOut => killCamBlend.y;
        /// <summary>Earliest / latest reaction at lights out, seconds.</summary>
        public float LaunchReactionMin => launchReaction.x;
        public float LaunchReactionMax => launchReaction.y;

        /// <summary>True when the race has at least one rival to put on the grid.</summary>
        public bool HasRivals
        {
            get
            {
                if (rivals == null) return false;
                foreach (var rival in rivals) if (rival != null) return true;
                return false;
            }
        }
    }
}
