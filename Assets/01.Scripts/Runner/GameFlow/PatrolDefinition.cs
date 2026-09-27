using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// Every chase tunable of the police patrol in one designer-facing asset,
    /// assigned to the scene's <see cref="PolicePatrol"/> object. All speeds
    /// are stored in m/s like the rest of the sim (UI converts with ×3.6).
    /// The patrol clones this at init and only ever reads the clone, so the
    /// debug menu can tweak a live chase without touching the asset on disk.
    /// Every stat is an Odin slider with a hand-picked range, same style as
    /// <see cref="ShipDefinition"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "PatrolDefinition", menuName = "FiniteRunner/Patrol Definition")]
    public class PatrolDefinition : ScriptableObject
    {
        [TitleGroup("Chase")]
        [Tooltip("Launch speed and the rubber band's initial floor — the patrol never goes slower than this. Keep it above the ship's launch speed.")]
        [PropertyRange(1f, 600f), SuffixLabel("m/s", true)]
        public float baseSpeed = 97f;

        [TitleGroup("Chase")]
        [Tooltip("How much the rubber band's floor grows per second — the chase tightens the longer the run lasts.")]
        [PropertyRange(0f, 15f), SuffixLabel("m/s per s", true)]
        public float ramp = 0.8f;

        [TitleGroup("Chase")]
        [Tooltip("Rubber band: the patrol chases the ship's current speed times this factor (1.05 = always 5% faster), but never below the floor above.")]
        [PropertyRange(0.5f, 2f), SuffixLabel("x ship speed", true)]
        public float rubberBand = 1.05f;

        [TitleGroup("Chase")]
        [Tooltip("How fast the patrol's speed adapts toward its rubber-band target. Lower = boosts buy more breathing room before the patrol matches them.")]
        [PropertyRange(0.5f, 150f), SuffixLabel("m/s per s", true)]
        public float catchUpAccel = 16.7f;

        [TitleGroup("Chase")]
        [Tooltip("Share of every speed-up the ship collects that the patrol gets at once (0.7 = a +100 km/h orb also gives the patrol +70 km/h). " +
                 "Measured on the ship's actual gain (after its weight), so boosts can't buy the breathing room the rubber band's catch-up accel would otherwise leave. Brakes are never shared. 0 = off.")]
        [PropertyRange(0f, 1.5f), SuffixLabel("x ship boost", true)]
        public float boostShare = 0.7f;

        [TitleGroup("Handling")]
        [Tooltip("Lateral speed the patrol's full steer settles at — the same force-against-drag steering as the ship. Below the ship's, so the player can out-dodge it.")]
        [PropertyRange(0f, 100f), SuffixLabel("m/s", true)]
        public float lateralSpeed = 22f;

        [TitleGroup("Handling")]
        [Tooltip("Lateral drag, 1/s: how fast its steering settles.")]
        [PropertyRange(0.01f, 30f)]
        public float handlingResponse = 6f;

        [TitleGroup("Handling")]
        [Tooltip("Grip at a standstill on a flat sweep (the ship's rule). The driver brakes to the speed this grip holds, so lower = it slows more for flat sweeps.")]
        [PropertyRange(0f, 500f), SuffixLabel("m/s²", true)]
        public float gripBase = 50f;

        [TitleGroup("Handling")]
        [Tooltip("Extra grip per m/s of speed (the ship's rule).")]
        [PropertyRange(0f, 3f), SuffixLabel("m/s² per m/s", true)]
        public float gripPerSpeed = 0.5f;

        [TitleGroup("Handling")]
        [Tooltip("How hard the patrol can brake for a flat sweep ahead.")]
        [PropertyRange(0f, 500f), SuffixLabel("m/s per s", true)]
        public float brakeDecel = 140f;

        [TitleGroup("Handling")]
        [Tooltip("Lateral speed (m/s) past which a slide on a flat sweep starts costing speed (the ship's rule, for the cruiser's body).")]
        [PropertyRange(0f, 30f), SuffixLabel("m/s", true)]
        public float slideThreshold = 4f;

        [TitleGroup("Handling")]
        [Tooltip("Share of its speed a sliding cruiser loses per second past the threshold.")]
        [PropertyRange(0f, 1f)]
        public float slideSpeedLoss = 0.1f;

        [TitleGroup("Handling")]
        [Tooltip("Minimum seconds between two wall hits the cruiser's body reports.")]
        [PropertyRange(0.1f, 2f), SuffixLabel("s", true)]
        public float wallHitCooldownSeconds = 0.5f;

        [TitleGroup("Driver")]
        [Tooltip("How far ahead (in seconds at its speed) the driver looks for a flat sweep to brake for.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("s", true)]
        public float curveLookaheadSeconds = 4f;

        [TitleGroup("Driver")]
        [Tooltip("How far ahead (seconds) it looks for boost orbs it can still reach.")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        public float orbLookaheadSeconds = 2.5f;

        [TitleGroup("Driver")]
        [Tooltip("How strongly a reachable orb pulls its line away from the ship's: 0 = it only ever chases, 1 = it goes straight for the orb. Fades out as the gap closes — up close it wants the ship.")]
        [PropertyRange(0f, 1f)]
        public float orbSeekWeight = 0.6f;

        [TitleGroup("Driver")]
        [Tooltip("Share of an orb's boost the patrol gains when it collects one ITSELF (the orb is used up). Separate from Boost Share, which is its cut of the SHIP's pickups.")]
        [PropertyRange(0f, 1.5f), SuffixLabel("x orb boost", true)]
        public float orbBoostShare = 0.5f;

        [TitleGroup("Driver")]
        [Tooltip("How far ahead (seconds) it decides about a ramp in its line: round it if the sideways travel still fits, otherwise line up and jump it.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("s", true)]
        public float rampLookaheadSeconds = 2.5f;

        [TitleGroup("Distances")]
        [Tooltip("Meters behind the start line the patrol launches from.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m", true)]
        public float startGap = 250f;

        [TitleGroup("Distances"), Title("Redeploy")]
        [Tooltip("Keep the chase alive: once the ship is clear by the band's outer distance, this cruiser drops out and a fresh one cuts in.")]
        public bool redeploys = true;

        [TitleGroup("Distances")]
        [Tooltip("Redeploy band, in meters: X = how far behind the ship the fresh cruiser drops in (keep it inside the minimap range so the player sees it arrive), Y = the gap that retires the old one.")]
        [MinMaxSlider(50f, 2000f, true), EnableIf("redeploys")]
        public Vector2 redeployBand = new(320f, 700f);

        [TitleGroup("Distances")]
        [Tooltip("The fresh cruiser's speed as a multiple of the ship's current speed. Above 1 so it closes in until the next boost; that speed becomes the new rubber-band floor.")]
        [PropertyRange(1f, 3f), SuffixLabel("x ship speed", true), EnableIf("redeploys")]
        public float redeploySpeedFactor = 1.25f;

        /// <summary>How far behind the ship a fresh cruiser drops in, metres (band X).</summary>
        public float RedeployGap => redeployBand.x;

        /// <summary>Gap that retires this cruiser for a fresh one, metres; 0 when redeploying is off (band Y).</summary>
        public float RedeployDistance => redeploys ? redeployBand.y : 0f;

        [TitleGroup("Distances")]
        [Tooltip("Gap inside which the patrol can catch the ship. Inside it the patrol stops gaining and sits on the ship's tail, steering for it. Keep it below the warn distance.")]
        [PropertyRange(0f, 100f), SuffixLabel("m", true)]
        public float catchDistance = 10f;

        [TitleGroup("Distances")]
        [Tooltip("How close across the track counts as ALONGSIDE — the band an attack run has to hold, and the band the ship has to break out of to shake one off. " +
                 "With the duel off it is the old catch width instead: inside the catch distance AND this far across is an arrest.")]
        [FormerlySerializedAs("catchLateral")]
        [PropertyRange(0f, 60f), SuffixLabel("m", true)]
        public float alongsideLateral = 18f;

        [TitleGroup("Distances")]
        [Tooltip("Tailing the ship this long inside the catch distance is an arrest. Suspended for the whole of an attack run, so it only ever punishes a patrol that sits there WITHOUT committing — keep it long.")]
        [PropertyRange(0f, 20f), SuffixLabel("s", true)]
        public float sustainedCatchSeconds = 7f;

        [TitleGroup("Distances")]
        [Tooltip("Gap below which the patrol's proximity line (once per approach) and the proximity rumble kick in.")]
        [PropertyRange(0f, 500f), SuffixLabel("m", true)]
        public float warnDistance = 130f;

        // ------------------------------------------------------------- duel run
        // The duel's run-wide rules (on/off, the clock, the windows, what a ram
        // costs, the look of the bar and sparks) — moved here off GameSettings
        // in refactor Step 4 so the whole duel is tuned on this one asset.
        [TitleGroup("Duel")]
        [Tooltip("The patrol hunts you: it commits to an attack run, pulls onto a flank and shoves you into the wall or off the edge, then breaks off. Off returns the old chase exactly, proximity arrest included.")]
        public bool duelEnabled = true;

        [TitleGroup("Duel")]
        [Tooltip("World timescale during a tug of war. Slow-mo buys PERCEPTION, not advantage: the bar runs on real seconds and the run's countdown keeps full pace, so an exchange still costs you mission time.")]
        [PropertyRange(0.1f, 1f), EnableIf("duelEnabled")]
        public float duelTimeScale = 0.3f;

        [TitleGroup("Duel")]
        [Tooltip("How long the world clock takes to ease into and out of the duel's slow motion.")]
        [PropertyRange(0f, 1f), SuffixLabel("s", true), EnableIf("duelEnabled")]
        public float duelTimeBlendSeconds = 0.15f;

        [TitleGroup("Duel")]
        [Tooltip("DORMANT since the cinematic duel (2026-09-25): the exchange LOCKS the ship's controls outright (see PatrolEncounterIntent.ShipLateralTarget), so no soft assist is added. Kept for the old soft-assist path and the debug row; it does nothing today.")]
        [PropertyRange(0f, 1f), EnableIf("duelEnabled")]
        public float duelAssistStrength = 0.5f;

        [TitleGroup("Duel")]
        [Tooltip("How long the kill prompt stays open after you win the bar. Generous on purpose: missing it costs nothing but the kill.")]
        [PropertyRange(0.2f, 3f), SuffixLabel("s", true), EnableIf("duelEnabled")]
        public float finisherWindowSeconds = 1.5f;

        [TitleGroup("Duel")]
        [Tooltip("The deeper dip the world takes on the kill's connect, before it snaps back to full speed.")]
        [PropertyRange(0f, 0.5f), SuffixLabel("s", true), EnableIf("duelEnabled")]
        public float duelHitStopSeconds = 0.12f;

        [TitleGroup("Duel")]
        [Tooltip("How long a strong boost orb leaves the ship ARMED: an exchange that draws level inside this skips the tug of war and goes straight to the kill prompt.")]
        [PropertyRange(0.5f, 10f), SuffixLabel("s", true), EnableIf("duelEnabled")]
        public float armedWindowSeconds = 3f;

        [TitleGroup("Duel")]
        [Tooltip("Which orb tiers arm the ship, by their name in the orb spawner's tiers. Green would make the window permanent; purple alone would make it a rumour.")]
        [EnableIf("duelEnabled")]
        public System.Collections.Generic.List<string> armingOrbTiers = new() { "Blue", "Purple" };

        [TitleGroup("Duel")]
        [Tooltip("Colour of the sparks streaming off an ARMED ship. The tell is on the ship, not the HUD.")]
        [EnableIf("duelEnabled")]
        public Color armedTellColor = new(0.75f, 0.45f, 1f, 1f);

        [TitleGroup("Duel")]
        [Tooltip("What a rear ram costs the SHIP, as a share of its current forward speed — so the price scales with how fast you arrive.")]
        [PropertyRange(0f, 0.5f), SuffixLabel("x speed", true), EnableIf("duelEnabled")]
        public float ramSpeedCost = 0.08f;

        [TitleGroup("Duel")]
        [Tooltip("Size of the fireball a killed cruiser leaves, as a multiple of the ship's own explosion (GameSettings). It reuses the same textures.")]
        [PropertyRange(0.1f, 3f), SuffixLabel("x ship blast", true), EnableIf("duelEnabled")]
        public float killExplosionScale = 0.8f;

        [TitleGroup("Duel")]
        [Tooltip("Print the attack run's state, gap and gates on screen — the only way to tell 'it decided not to' from 'it is broken'.")]
        [EnableIf("duelEnabled")]
        public bool duelDebugReadout = false;

        [TitleGroup("Duel")]
        [Tooltip("The colour the PATROL pushes the tug-of-war bar with. Your side uses the dash meter's colour.")]
        [EnableIf("duelEnabled")]
        public Color duelBarColor = new(1f, 0.32f, 0.28f, 1f);

        [TitleGroup("Duel")]
        [Tooltip("Tint of the sparks grinding between the hulls in a tug of war (each spark rolls between this and white). The glow between the cars takes it too.")]
        [EnableIf("duelEnabled")]
        public Color duelSparkColor = new(1f, 0.5f, 0.8f, 1f);

        [TitleGroup("Duel")]
        [Tooltip("Size of the contact-spark rig: scales the spark rates, their size and the glow's brightness together. 1 = the authored look.")]
        [PropertyRange(0.25f, 3f), SuffixLabel("x", true), EnableIf("duelEnabled")]
        public float duelSparkScale = 1f;

        [TitleGroup("Duel")]
        [Tooltip("The kill: the cruiser destroyed. (low motor, high motor, seconds)")]
        public Vector3 killRumble = new(1f, 1f, 0.5f);

        [TitleGroup("Duel")]
        [Tooltip("Ramming the cruiser from behind. (low motor, high motor, seconds)")]
        public Vector3 ramRumble = new(0.9f, 0.6f, 0.25f);

        [TitleGroup("Duel")]
        [Tooltip("A lost tug of war: the shove into the wall or off the road. (low motor, high motor, seconds)")]
        public Vector3 shoveRumble = new(1f, 0.8f, 0.5f);

        [TitleGroup("Duel")]
        [Tooltip("A fresh cruiser cutting in behind you. (low motor, high motor, seconds)")]
        public Vector3 redeployRumble = new(0.6f, 0.4f, 0.4f);

        [TitleGroup("Duel")]
        [Tooltip("A slam in the tug of war at the weakest (X) and hardest (Y) push: the low motor rides between them by the slam's strength. (low motor, high motor, seconds) for the weakest; only the low motor changes.")]
        public Vector3 tugSlamRumble = new(0.4f, 0.5f, 0.12f);

        [TitleGroup("Duel")]
        [Tooltip("The low motor of the hardest slam (the weakest is tugSlamRumble.x).")]
        [PropertyRange(0f, 1f)]
        public float tugSlamRumbleMax = 0.9f;

        [TitleGroup("Duel")]
        [Tooltip("How much FASTER than the ship the patrol drives while committing to an attack run. This burst is what closes the gap — and it is the telegraph, so keep it visible.")]
        [PropertyRange(1f, 2f), SuffixLabel("x ship speed", true)]
        public float attackRunOverdrive = 1.15f;

        [TitleGroup("Duel")]
        [Tooltip("The LEAST closing speed an attack run gets, in m/s, whatever the ship is doing. The overdrive is a multiple of the SHIP's speed, so on its own a run against a slow or stopped ship could never reach the flank — the cruiser parked in its standoff for ever. This floor lets it close at least this fast, so a duel can open at any speed.")]
        [PropertyRange(0f, 60f), SuffixLabel("m/s", true)]
        public float minClosingSpeed = 12f;

        [TitleGroup("Duel")]
        [Tooltip("The patrol only starts an attack run once the gap is inside this — keep it just above the standoff, and inside the warn distance. " +
                 "The ordinary rubber band does the APPROACH; the overdrive only has to cover this last stretch. Large values make the run cross so much road " +
                 "that a ramp or loop is almost certain to interrupt it, and the run aborts for nothing.")]
        [PropertyRange(20f, 1500f), SuffixLabel("m", true)]
        public float commitFromDistance = 70f;

        [TitleGroup("Duel")]
        [Tooltip("Minimum seconds between attack runs, counted from the END of the last one. The single number that decides how much of a run is spent duelling.")]
        [PropertyRange(2f, 60f), SuffixLabel("s", true)]
        public float commitIntervalSeconds = 12f;

        [TitleGroup("Duel")]
        [Tooltip("An attack run that has not drawn level within this gives up and goes on cooldown — a ship that keeps boosting away is never chased forever.")]
        [PropertyRange(2f, 60f), SuffixLabel("s", true)]
        public float commitTimeoutSeconds = 15f;

        [TitleGroup("Duel")]
        [Tooltip("The longitudinal gap at which the patrol counts as alongside and the exchange begins.")]
        [PropertyRange(0f, 60f), SuffixLabel("m", true)]
        public float alongsideDistance = 12f;

        [TitleGroup("Duel")]
        [Tooltip("How far to the side of the ship the patrol drives during a run — beside it, not behind it. Keep it well inside the alongside width.")]
        [PropertyRange(0f, 30f), SuffixLabel("m", true)]
        public float flankOffsetMeters = 7f;

        [TitleGroup("Duel")]
        [Tooltip("How long the patrol holds the flank before it shoves. The tug of war takes this hold over later.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float alongsideHoldSeconds = 1.2f;

        [TitleGroup("Duel")]
        [Tooltip("How long the ship has to hold outside the alongside width to break the run off. Short, so a decisive dodge works and a wobble does not.")]
        [PropertyRange(0f, 3f), SuffixLabel("s", true)]
        public float abortGraceSeconds = 0.4f;

        [TitleGroup("Duel")]
        [Tooltip("How fast the patrol drives relative to the ship while backing off after a run — below 1 so it actually opens a gap instead of sitting on the bumper. This is what makes the chase a rhythm rather than a permanent tailgate.")]
        [PropertyRange(0.5f, 1f), SuffixLabel("x ship speed", true)]
        public float breakOffSpeedFactor = 0.85f;

        [TitleGroup("Duel")]
        [Tooltip("Disengage time after a shove or an abort, before the cooldown starts.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float breakOffSeconds = 1f;

        [TitleGroup("Duel")]
        [Tooltip("How long after a run ends before the patrol may commit to another one.")]
        [PropertyRange(0f, 60f), SuffixLabel("s", true)]
        public float attackRunCooldownSeconds = 8f;

        [TitleGroup("Duel")]
        [Tooltip("How far ahead of the SHIP the road has to be clear of ramps, landings, loops, tubes and the final run-up for an attack run to START.")]
        [PropertyRange(50f, 1000f), SuffixLabel("m", true)]
        public float encounterLookaheadMeters = 220f;

        [TitleGroup("Duel")]
        [Tooltip("How far ahead a run ALREADY UNDER WAY looks before it lets go. Much shorter than the starting distance on purpose: judge a running exchange by the starting window and every feature that drifts into range kills a run that just began.")]
        [PropertyRange(20f, 400f), SuffixLabel("m", true)]
        public float encounterAbortMeters = 70f;

        [TitleGroup("Duel")]
        [Tooltip("How far behind the ship the replacement appears after a kill. Inside the minimap's range, so the player watches the next one come rather than being surprised by it. 0 = use the ordinary redeploy gap.")]
        [PropertyRange(0f, 1500f), SuffixLabel("m", true)]
        public float killTeleportGap = 0f;

        [TitleGroup("Duel")]
        [Tooltip("How close the patrol may sit while NOT attacking. Inside this it eases off to the back-off speed instead of driving into you — without it a raised floor parks the cruiser inside the ship between runs.")]
        [PropertyRange(0f, 200f), SuffixLabel("m", true)]
        public float standoffDistance = 45f;

        [TitleGroup("Duel")]
        [Tooltip("How fast the patrol drives the tug-of-war bar toward its own side, in bar widths per REAL second. 0.2 = centre to the edge in 2.5 s if you never press.")]
        [PropertyRange(0f, 1f), SuffixLabel("bar / s", true)]
        public float tugPatrolForce = 0.15f;

        [TitleGroup("Duel")]
        [Tooltip("How much of the bar one press wins back. 0.08 = about six presses to take it from the centre.")]
        [PropertyRange(0.01f, 0.5f), SuffixLabel("bar / press", true)]
        public float tugPressValue = 0.08f;

        [TitleGroup("Duel")]
        [Tooltip("How far sideways the shove is sized to throw the ship — into the wall on a walled stretch, off the road on an open edge. The shove itself deals no damage; whatever it puts you into does.")]
        [PropertyRange(0f, 40f), SuffixLabel("m", true)]
        public float shoveMeters = 10f;

        [TitleGroup("Duel")]
        [Tooltip("How fast the cruiser can change speed DURING a run, overriding the chase's catch-up accel. The cruise rate is deliberately sluggish so boosts buy breathing room, " +
                 "but holding a flank means ±10 m/s corrections inside a second — at the cruise rate it answers three seconds late and sails straight past you. 0 = use the chase rate.")]
        [PropertyRange(0f, 300f), SuffixLabel("m/s per s", true)]
        public float stationAccel = 60f;

        [TitleGroup("Duel")]
        [Tooltip("How much harder each KILL (or outrun) makes the next cruiser, as a fraction. 0.15 = the fifth replacement is 1.75x. " +
                 "It drives two things at once: the attack-run interval is DIVIDED by it (they come sooner) and the tug-of-war push is MULTIPLIED by it (they shove harder). 0 = no escalation.")]
        [PropertyRange(0f, 1f), SuffixLabel("per kill", true)]
        public float tierScalePerKill = 0.15f;

        [TitleGroup("Duel")]
        [Tooltip("Ceiling on the escalation above. Mostly it bounds the ATTACK INTERVAL: without it a long run divides 12 s by a growing number until cruisers arrive on top of each other.")]
        [PropertyRange(1f, 5f), SuffixLabel("x", true)]
        public float tierScaleMax = 2f;

        [TitleGroup("Duel")]
        [Tooltip("Separate, LOWER ceiling on what escalation does to the tug-of-war push. Escalation is allowed to compress your recovery time; it must never make the mash mathematically unwinnable, so this is capped below the interval's ceiling.")]
        [PropertyRange(1f, 3f), SuffixLabel("x push", true)]
        public float tugForceMaxScale = 1.8f;

        [TitleGroup("Duel")]
        [Tooltip("How many rear rams the cruiser soaks up. The pool never kills it — it sets how hard the tug of war pushes: a full pool is full strength, one point left is a pushover. It refills on every fresh patrol.")]
        [PropertyRange(1, 10), SuffixLabel("hits", true)]
        public int damagePoolMax = 3;

        [TitleGroup("Duel")]
        [Tooltip("How far BEHIND the cruiser the ship's nose counts as rear contact. Only reachable once the patrol is ahead of you — brake past it or let it overshoot.")]
        [PropertyRange(0f, 30f), SuffixLabel("m", true)]
        public float ramContactDistance = 8f;

        [TitleGroup("Duel")]
        [Tooltip("How close across the track a rear ram has to line up. Tight on purpose: ramming is an aimed move, not a side-swipe.")]
        [PropertyRange(0f, 20f), SuffixLabel("m", true)]
        public float ramContactLateral = 6f;

        [TitleGroup("Duel")]
        [Tooltip("How much faster than the cruiser the ship has to be arriving for a ram to register. Below it the contact is a harmless nudge — drifting into its bumper is not an attack.")]
        [PropertyRange(0f, 100f), SuffixLabel("m/s closing", true)]
        public float ramClosingSpeedThreshold = 15f;

        [TitleGroup("Duel")]
        [Tooltip("How long a cruiser caught out by the player BRAKING holds its speed and sails past before it drops back behind. The overshoot is the cinematic beat and the way into a rear ram; too long and it drives out of the chase.")]
        [PropertyRange(0f, 6f), SuffixLabel("s", true)]
        public float overshootHoldSeconds = 2.5f;

        [TitleGroup("Duel")]
        [Tooltip("Brake input (0..1) the ship has to hold, with the cruiser in its standoff, for the cruiser to overshoot. 0 = never on the brake input alone.")]
        [PropertyRange(0f, 1f)]
        public float overshootBrakeThreshold = 0.5f;

        [TitleGroup("Duel")]
        [Tooltip("Ship deceleration that also triggers the overshoot — a brake pad slows the ship with no brake input at all. 0 = off.")]
        [PropertyRange(0f, 200f), SuffixLabel("m/s²", true)]
        public float overshootDecelThreshold = 25f;

        [TitleGroup("Duel")]
        [Tooltip("How far beyond the standoff distance still counts as 'in the standoff' for the overshoot. The cruiser oscillates around its station by design, so give it some room.")]
        [PropertyRange(0f, 100f), SuffixLabel("m", true)]
        public float overshootTriggerMargin = 20f;

        [TitleGroup("Duel")]
        [Tooltip("How much of the room between the ship and the far lane edge the tug of war pushes it across when the bar is fully the cruiser's way. The bar IS the ship's position (it opens at the centre, so the cruiser's arrival shoves the ship halfway out at once; presses claw it back). " +
                 "Kept below 1 so the bar never drops the ship off an open edge by itself — that is the SHOVE's job when the bar bottoms out.")]
        [PropertyRange(0f, 0.85f)]
        public float tugPushFraction = 0.8f;

        [TitleGroup("Duel")]
        [Tooltip("How HARD the tug of war shoves the ship sideways: a multiplier on the steering pull that walks the locked ship toward the edge. 1 is the ship's own autopilot pull (a creep); higher moves it decisively. The DISTANCE it can be pushed is the push room above; this is the force.")]
        [PropertyRange(0.25f, 8f), SuffixLabel("x", true)]
        public float tugPushGain = 2.5f;

        [TitleGroup("Duel")]
        [Tooltip("How often the cruiser SLAMS the ship sideways during a tug of war, in REAL seconds — the same clock as the bar, so an exchange always carries the same number of hits whatever the slow-mo. " +
                 "Each slam is presentation: the cruiser lurches into the ship nose-first, the ship is knocked, a burst of sparks jumps out of the seam, the glow flares, the pad rumbles and the camera takes the wall-hit shake. The bar is untouched — it stays a steady push.")]
        [PropertyRange(0.2f, 3f), SuffixLabel("s", true)]
        public float tugSlamIntervalSeconds = 0.7f;

        [TitleGroup("Duel")]
        [Tooltip("How far the cruiser's nose turns INTO the ship at the peak of a slam, on the visual only. Between slams it holds a third of this, so the whole exchange reads as the cruiser leaning on you.")]
        [PropertyRange(0f, 40f), SuffixLabel("°", true)]
        public float tugSlamYawDegrees = 18f;

        [TitleGroup("Duel")]
        [Tooltip("How far the two visuals are knocked sideways by a slam: the cruiser lurches this far INTO the ship and the ship this far AWAY, both snapping back over the hit. Visual only — where the bodies actually are is the bar's business.")]
        [PropertyRange(0f, 3f), SuffixLabel("m", true)]
        public float tugSlamKickMeters = 0.9f;

        [TitleGroup("Duel")]
        [Tooltip("How far the cruiser peels away from the ship once the player has WON the bar and the kill prompt is open — the two separate a little before the button.")]
        [PropertyRange(0f, 15f), SuffixLabel("m", true)]
        public float finisherSeparationMeters = 4f;

        [TitleGroup("Duel")]
        [Tooltip("How long the cruiser brakes HARD after the player misses the kill prompt (wrong shoulder or no press), before it goes on the ordinary cooldown.")]
        [PropertyRange(0f, 5f), SuffixLabel("s", true)]
        public float finisherMissBrakeSeconds = 1.5f;

        [TitleGroup("Duel")]
        [Tooltip("The speed the miss brake stops at, as a share of the ship's — how far back the cruiser visibly drops.")]
        [PropertyRange(0.2f, 1f), SuffixLabel("x ship speed", true)]
        public float finisherMissBrakeSpeedFactor = 0.65f;
    }
}
