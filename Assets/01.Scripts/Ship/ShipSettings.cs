using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// How the standalone ship reads and rides the WORLD — everything the
    /// track-space ship never needed because the spline answered it. The
    /// ship's own feel (speed, handling, dash, hover, bank) stays on
    /// <see cref="ShipDefinition"/>, untouched, so the store and the debug
    /// persistence keep working; this asset only holds the physics of a body
    /// that knows its level through PhysX queries: how finely a tick is
    /// substepped, how the hover probes attach to and let go of a surface,
    /// what counts as a wall, how a flight is shaped, and how free steering
    /// turns. Play runs on a runtime clone (<see cref="HoverShip"/> takes it
    /// in Awake), so a run rule pushed in by a game never reaches the asset.
    /// </summary>
    [CreateAssetMenu(fileName = "ShipSettings", menuName = "FiniteRunner/Ship Settings")]
    public class ShipSettings : ScriptableObject
    {
        [TitleGroup("Simulation")]
        [Tooltip("Longest distance one substep may cover. A tick is split so no step is longer: 4 m = 5 substeps at cruise (1000 m/s), 10 at Light Speed. Smaller follows tight geometry (a loop, a ramp) more exactly and costs more queries.")]
        [PropertyRange(1f, 12f), SuffixLabel("m", true)]
        public float maxStepMeters = 4f;

        [TitleGroup("Simulation")]
        [Tooltip("Hard cap on substeps per tick, whatever the speed.")]
        [PropertyRange(1, 48)]
        public int maxSubsteps = 24;

        [TitleGroup("World")]
        [Tooltip("Layers the ship rides, lands on and hits. Default + ShipGround flies over any ordinary level as it is; a level that shares its physics scene with colliders the ship must never touch (the runner during the city handoff) narrows this to ShipGround alone.")]
        public LayerMask groundLayers = (1 << 0) | ShipLayers.GroundMask | ShipLayers.SurfaceMask;

        [TitleGroup("World")]
        [Tooltip("Take pickups (needs a ShipPickupSweeper on the ship).")]
        public bool pickupsEnabled = true;

        [TitleGroup("World")]
        [Tooltip("Layers the pickup sweep looks on. A pickup is anything there with an IShipPickup on it, found by its own collider (trigger or not).")]
        public LayerMask pickupLayers = (1 << 0) | ShipLayers.PickupMask;

        [TitleGroup("Surface")]
        [Tooltip("How far below the ride height the probes still find ground. Inside it the ship is held to the surface; past it (for the coyote distance) it lets go.")]
        [PropertyRange(0.5f, 20f), SuffixLabel("m", true)]
        public float attachRange = 6f;

        [TitleGroup("Surface")]
        [Tooltip("Half spacing of the side probes (x) and the nose / tail probes (y), around the centre probe. The plane through them is the ship's up.")]
        public Vector2 probeHalfExtents = new(2f, 5f);

        [TitleGroup("Surface")]
        [Tooltip("Distance the ship keeps flying straight after the probes lose the ground before it counts as a take-off. Bridges seams and small gaps.")]
        [PropertyRange(0f, 40f), SuffixLabel("m", true)]
        public float coyoteMeters = 8f;

        [TitleGroup("Surface")]
        [Tooltip("Extra cosmetic lift of the model above the physical ride height (which is the ship definition's hover height).")]
        [PropertyRange(-5f, 5f), SuffixLabel("m", true)]
        public float visualLift = 0f;

        [TitleGroup("Surface")]
        [Tooltip("Magnetic hover: the ship holds to whatever surface is under its probes, at any angle — loops, tubes and steep banks are plain geometry. Off = world gravity rules: only surfaces within the ground angle hold, anything steeper needs centripetal load, and a bank pulls the ship downhill.")]
        public bool magnetic = true;

        [TitleGroup("Surface")]
        [Tooltip("Centripetal pull the magnet can supply over a CREST, m/s². The ship lets go when speed² × curvature exceeds it. 0 = unlimited (never launches off a crest).")]
        [PropertyRange(0f, 50000f), SuffixLabel("m/s²", true)]
        public float magnetStrength = 0f;

        [TitleGroup("Surface")]
        [Tooltip("World-gravity mode only: steepest surface, from level, that holds the ship on its own.")]
        [PropertyRange(0f, 90f), SuffixLabel("°", true), HideIf(nameof(magnetic))]
        public float maxGroundAngle = 50f;

        [TitleGroup("Walls")]
        [Tooltip("Radius of the hull sweep that finds walls and obstacles, centred on the ship. Must stay below the hover height or it scrapes the floor.")]
        [PropertyRange(0.25f, 5f), SuffixLabel("m", true)]
        public float hullRadius = 2f;

        [TitleGroup("Walls")]
        [Tooltip("A surface tilted up to this far from the ship's up is floor (a ramp, a bank) and is climbed; steeper is a wall.")]
        [PropertyRange(5f, 85f), SuffixLabel("°", true)]
        public float climbAngle = 45f;

        [TitleGroup("Walls")]
        [Tooltip("Seconds before another wall hit can cost speed / fire feedback.")]
        [PropertyRange(0f, 2f), SuffixLabel("s", true)]
        public float wallHitCooldownSeconds = 0.5f;

        [TitleGroup("Flight")]
        [Tooltip("Metres of air per m/s of take-off speed. A flight's gravity is solved so it lands this far on, at the speed it left with — real 9.81 at 1000 m/s would be a 65 km jump.")]
        [PropertyRange(0f, 2f)]
        public float airDistancePerSpeed = 0.6f;

        [TitleGroup("Flight")]
        [Tooltip("Shortest and longest flight, metres, before the ship's jump strength.")]
        [MinMaxSlider(10f, 2000f, true)]
        public Vector2 airDistanceRange = new(80f, 600f);

        [TitleGroup("Flight")]
        [Tooltip("Steering and dash authority while airborne, 0..1.")]
        [PropertyRange(0f, 1f)]
        public float airControlFactor = 0.5f;

        [TitleGroup("Flight")]
        [Tooltip("Gentlest gravity a flight may get, and the gravity of a fall with no ground found below.")]
        [PropertyRange(1f, 200f), SuffixLabel("m/s²", true)]
        public float minAirGravity = 30f;

        [TitleGroup("Flight")]
        [Tooltip("Steepest surface, from the ship's up, it can land on. Anything steeper met in the air is a wall.")]
        [PropertyRange(5f, 85f), SuffixLabel("°", true)]
        public float maxLandAngle = 60f;

        [TitleGroup("Guide")]
        [Tooltip("Take the help of a guide spline when the level has one in reach. Off = always free flight.")]
        public bool useGuide = true;

        [TitleGroup("Guide")]
        [Tooltip("The ship's own share of a guide's assist (multiplied with the guide's). 1 on a full-assist guide = the heading is the line's and the stick strafes, the runner's feel; lower keeps turning in the player's hands and only leans the heading toward the line.")]
        [PropertyRange(0f, 1f), EnableIf(nameof(useGuide))]
        public float guideAssist = 1f;

        [TitleGroup("Guide")]
        [Tooltip("How fast a partial assist eases the heading onto the line, 1/s (scaled by the assist). Full assist locks it.")]
        [PropertyRange(0.5f, 30f), EnableIf(nameof(useGuide))]
        public float guideHeadingResponse = 6f;

        [TitleGroup("Guide")]
        [Tooltip("Metres flown between full projections onto the guide. In between, the ship's place on the line is advanced by what it flew and the line's tangent is turned by its curvature — no query at all. A projection is the expensive part of a guided substep (a spline-backed track costs ~0.04 ms each, ten substeps a tick at Light Speed); 0 = project every substep.")]
        [PropertyRange(0f, 100f), SuffixLabel("m", true), EnableIf(nameof(useGuide))]
        public float guideRefreshMeters = 0f;

        [TitleGroup("Guide")]
        [Tooltip("How often a ship with no guide looks for one.")]
        [PropertyRange(0.1f, 5f), SuffixLabel("s", true), EnableIf(nameof(useGuide))]
        public float guideSearchSeconds = 0.5f;

        [TitleGroup("Dash")]
        [Tooltip("Master switch for the lateral dash and its airborne barrel roll.")]
        public bool dashEnabled = true;

        [TitleGroup("Dash")]
        [Tooltip("Share of the meter one dash spends. The recharge time is the ship definition's.")]
        [PropertyRange(0.05f, 1f)]
        public float dashCost = 0.5f;

        [TitleGroup("Dash")]
        [Tooltip("One press is the whole dash gesture (the player's own CONTROLS toggle can also switch this on).")]
        public bool dashSinglePress = false;

        [TitleGroup("Dash")]
        [Tooltip("Window for the second tap of a double-tap dash.")]
        [PropertyRange(0.1f, 0.6f), SuffixLabel("s", true), DisableIf(nameof(dashSinglePress))]
        public float dashDoubleTapSeconds = 0.3f;

        [TitleGroup("Stall")]
        [Tooltip("Seconds at a standstill with the throttle released before the ship reports itself stopped. Braking to a stop alone never stalls it. The report is only a signal (a game may end its run on it): the ship itself keeps flying.")]
        [PropertyRange(0.25f, 10f), SuffixLabel("s", true)]
        public float stallGraceSeconds = 2f;

        [TitleGroup("Recovery")]
        [Tooltip("Falling off the world and coming back (needs a ShipRecovery on the ship). Off = a lost ship just keeps falling.")]
        public bool recoveryEnabled = true;

        [TitleGroup("Recovery")]
        [Tooltip("Seconds in the air with NO ground anywhere below before the ship counts as lost. A jump always has ground under it.")]
        [PropertyRange(0.1f, 5f), SuffixLabel("s", true)]
        public float groundlessSeconds = 0.75f;

        [TitleGroup("Recovery")]
        [Tooltip("Longest the ship may stay airborne at all, ground below or not.")]
        [PropertyRange(1f, 60f), SuffixLabel("s", true)]
        public float maxAirSeconds = 10f;

        [TitleGroup("Recovery")]
        [Tooltip("Treat anything below the kill height as lost.")]
        public bool useKillHeight;

        [TitleGroup("Recovery")]
        [SuffixLabel("m (world Y)", true), EnableIf(nameof(useKillHeight))]
        public float killHeight = -200f;

        [TitleGroup("Recovery")]
        [Tooltip("Gravity of the tumbling fall.")]
        [PropertyRange(1f, 200f), SuffixLabel("m/s²", true)]
        public float fallGravity = 30f;

        [TitleGroup("Recovery")]
        [PropertyRange(0f, 720f), SuffixLabel("°/s", true)]
        public float fallTumbleDegreesPerSecond = 120f;

        [TitleGroup("Recovery")]
        [Tooltip("How long the fall is watched before the ship is put back.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float fallDurationSeconds = 1.5f;

        [TitleGroup("Recovery")]
        [Tooltip("Blinking standstill before the relaunch.")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        public float respawnWaitSeconds = 3f;

        [TitleGroup("Recovery")]
        [Tooltip("Share of the speed it fell with that the relaunch loses.")]
        [PropertyRange(0f, 1f)]
        public float respawnSpeedPenalty = 0.15f;

        [TitleGroup("Recovery")]
        [Tooltip("Guided respawn: plain road the guide must find ahead of the spot.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m", true)]
        public float respawnClearance = 150f;

        [TitleGroup("Recovery")]
        [Tooltip("Free respawn: how far back along its own trail the ship is put, so it is clear of what it fell off.")]
        [PropertyRange(5f, 500f), SuffixLabel("m", true)]
        public float respawnBackMeters = 60f;

        [TitleGroup("Recovery")]
        [Tooltip("Free respawn: how far along each candidate heading the ship looks for road. It faces the way with the most road ahead, so it never relaunches at the edge it fell off.")]
        [PropertyRange(50f, 2000f), SuffixLabel("m", true)]
        public float respawnLookAheadMeters = 400f;

        [TitleGroup("Recovery")]
        [Tooltip("Free respawn: how far to each side the road's edges are searched for, to put the ship in the middle of it. A side with no edge inside this range is open ground: the ship is not moved that way.")]
        [PropertyRange(10f, 500f), SuffixLabel("m", true)]
        public float respawnCentreSearchMeters = 150f;

        [TitleGroup("Recovery")]
        [Tooltip("Free respawn: distance between the breadcrumbs the ship drops while safely grounded.")]
        [PropertyRange(2f, 100f), SuffixLabel("m", true)]
        public float crumbSpacingMeters = 15f;

        [TitleGroup("Recovery")]
        [Tooltip("Free respawn: a breadcrumb is only dropped while the ship is within this tilt of upright — never halfway up a loop.")]
        [PropertyRange(5f, 90f), SuffixLabel("°", true)]
        public float maxCrumbTilt = 35f;

        [TitleGroup("Reverse")]
        [Tooltip("At a standstill, holding the brake backs the ship up to this speed — the way out of a wall it has nosed into. 0 = no reverse (the runner's rule: the brake only stops).")]
        [PropertyRange(0f, 100f), SuffixLabel("m/s", true)]
        public float reverseSpeed = 25f;

        [TitleGroup("Reverse")]
        [PropertyRange(1f, 200f), SuffixLabel("m/s per s", true)]
        public float reverseAcceleration = 40f;

        [TitleGroup("Feel")]
        [Tooltip("Material of the dash ghosts and of the respawn blink. Empty = a runtime translucent fallback.")]
        public Material ghostMaterial;

        [TitleGroup("Feel")]
        [Tooltip("Blinks per second while the ship waits to relaunch after a fall.")]
        [PropertyRange(1f, 20f), SuffixLabel("Hz", true)]
        public float respawnBlinkRate = 8f;

        [TitleGroup("Feel")]
        [Tooltip("How long a wingtip ribbon of the barrel roll lives.")]
        [PropertyRange(0.05f, 3f), SuffixLabel("s", true)]
        public float barrelRollTrailSeconds = 0.6f;

        [TitleGroup("Feel")]
        [PropertyRange(0.05f, 5f), SuffixLabel("m", true)]
        public float barrelRollTrailWidth = 0.9f;

        [TitleGroup("Feel")]
        [Tooltip("Where the ribbons leave the wing, as a share of the model's half width.")]
        [PropertyRange(0.2f, 2f)]
        public float barrelRollTrailSpan = 1f;

        [TitleGroup("Feel")]
        public Color barrelRollTrailColor = new(0.45f, 0.9f, 1f, 0.9f);

        [TitleGroup("Feel")]
        [Tooltip("Must be URP Particles/Unlit, additive — the plain Unlit ignores the trail's gradient. Empty = a runtime fallback.")]
        public Material barrelRollTrailMaterial;

        [TitleGroup("Free steering")]
        [Tooltip("Fastest the ship can turn with no guide spline, at low speed.")]
        [PropertyRange(5f, 360f), SuffixLabel("°/s", true)]
        public float maxYawRate = 90f;

        [TitleGroup("Free steering")]
        [Tooltip("Share of the ship's grip a full-lock turn may ask for. At speed the turn rate is grip ÷ speed: 1 = exactly what holds, above 1 = full lock slides (brake first).")]
        [PropertyRange(0.1f, 3f)]
        public float turnAuthority = 1f;

        [TitleGroup("Free steering")]
        [Tooltip("Share of the definition's lateral (strafe) speed that steering also gets with no guide. 0 = pure turning, 1 = the runner's full strafe on top of the turn.")]
        [PropertyRange(0f, 1f)]
        public float freeStrafeShare = 0.35f;
    }
}
