using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Audio;
using ConfusedGameDev.FiniteRunner.Campaign;
using ConfusedGameDev.FiniteRunner.Cameras;
using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.SaveData;
using ConfusedGameDev.FiniteRunner.Screens;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Store;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// Owns the win/lose conditions of the chase. Win: every mandatory
    /// objective of the <see cref="RunnerLevelDefinition"/> met (Light Speed
    /// is the first Reach Speed one) before the countdown ends. Lose: the
    /// police patrol catches up, the timer runs out, or the ship bleeds down
    /// to a standstill. The level's optional challenges are live from launch
    /// and latch when met. Both endings are a panel, raised the frame the run
    /// ends with no closing line or HUD text in between: a win hands the run's
    /// rows to the Mission Complete panel, which pays the whole mission (city
    /// level + this run); a loss raises the GameOverScreen's retry panel with
    /// the reason it ended.
    /// Wires up the scene's PolicePatrol object (its chase tunables live on
    /// its own PatrolDefinition asset) and restarts it with the run.
    /// The timer only ticks while the ship is actually flying (not while
    /// the tuning screen has the simulation paused).
    /// Holds no tunables itself — every balance knob lives on the
    /// <see cref="GameSettings"/> asset, drawn inline here for the designers.
    /// </summary>
    public class GameManager : MonoBehaviour, Contracts.ITrackRunRules, IRunState
    {
        [Title("Scene references")]
        [SerializeField, Required] ShipMotor motor;
        [SerializeField, Required] TrackGenerator generator;
        [SerializeField] TuningScreen tuningScreen;
        [Tooltip("The scene's police patrol object. Initialized (and its cruiser visual built) here in Awake; deactivated when the patrol is disabled on GameSettings.")]
        [SerializeField] PolicePatrol patrol;
        [Tooltip("The scene's oncoming-traffic system (PF_TrafficSystem under ===SYSTEMS===). Bound here to the level's traffic definition; a level without one leaves it idle.")]
        [SerializeField] Traffic.TrafficSystem traffic;

        [Title("Flow")]
        [Tooltip("Overlay the main menu (attract screen) over this scene on boot — an in-scene testing shortcut. The shipping flow keeps this off: the menu is its own scene (MainMenu.unity, build index 0) and this scene is reached from the city chase.")]
        [SerializeField] bool mainMenuOnBoot;

        [Title("Balance")]
        [Tooltip("Every tunable of the run. Edit it right here — it is the same asset the whole project shares.")]
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        GameSettings settings;

        [Title("Level")]
        [Tooltip("The run's goals: mandatory objectives (all must be met to win — the first Reach Speed is the Light Speed) and optional challenges that multiply the mission payout. Read live. Missing = the default run (reach 6500 km/h).")]
        [SerializeField, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        RunnerLevelDefinition level;

        DashPromptController dashPrompt;
        // The run's presentation, split out in refactor Step 8.5: both are
        // hand-placed on this object (PF_Systems) and bound in Awake.
        RunCameraDirector cameraDirector;  // framing: jumps, loops, falls, the duel dolly, the endings' shot
        RunFeedback feedback;              // feel: rumbles, shakes, glitch pulses, speed lines, story lines
        HyperspaceJump hyperspace;         // the LB+RB / Q+E shortcut to the end ramps, once at Light Speed
        EscapeVanish escapeVanish;         // every win's exit: fire trails, the model gone in a flash
        RunnerMusic music;                 // null when the music is off on GameSettings

        /// <summary>
        /// How a run ended — typed, so the save record never has to match a
        /// result label. Never serialized. MissedRamp = reached the end of the
        /// track with every objective met but not on an end ramp; TooSlow =
        /// reached it with an objective still open, ramp or not; Destroyed =
        /// the hull reached 0 and the ship blew up.
        /// </summary>
        enum RunOutcome { Escaped, Caught, TimedOut, MissedRamp, TooSlow, Destroyed }

        bool runCounted; // this run's "escape attempted" has been recorded
        bool lostSpeedAtEnd; // Light Speed was latched but no longer held at the end of the track

        // Hull and lives. The lives are this SCENE's: the runner is entered
        // once per mission and retried in place, so Awake deals a fresh set
        // and Restart never touches them.
        ShipHealth shipHealth;
        bool isGameOver;           // the failed run in progress took the last life
        long walletAtMissionStart; // what a GAME OVER rolls the wallet back to

        // The win wind-down: the ship has left an end ramp with every
        // objective met and flies on; the glitch ramps to max and the panel opens.
        Coroutine winRoutine;
        // The loss wind-down: MISSION FAILED slams in, then the retry panel.
        Coroutine failRoutine;
        MissionAccomplishedBanner banner; // the MISSION ACCOMPLISHED / MISSION FAILED slam; killed before the panel
        MissionFailedVideo failVideo;     // the loss's reaction clip under the banner; killed with it

        // The run's objective state: latched per entry (speed bleeds after the
        // peak while a jump goal may still be open), reset with the run.
        int jumpCount;
        bool[] objectiveDone = System.Array.Empty<bool>();
        bool[] challengeDone = System.Array.Empty<bool>();

        public float BoostTextLeadMeters => settings.boostTextLeadMeters;

        /// <summary>Base speed gain of a power-up orb in m/s; orb tiers multiply this.</summary>
        public float PowerUpSpeedBoost => settings.powerUpSpeedBoost;

        /// <summary>Height of the air lane above the flight line, metres.</summary>
        public float AirLaneHeight => settings.airLaneHeight;

        /// <summary>m/s → the km/h the run shows and judges; 3.6 (true km/h) with no settings.</summary>
        public float SpeedDisplayMultiplier => settings != null && settings.speedDisplayMultiplier > 0f ? settings.speedDisplayMultiplier : 3.6f;

        /// <summary>Entry speed (m/s) a loop at <paramref name="distance"/> demands: floor + ramp × distance, capped. Fixed per loop, so its gate never lies.</summary>
        public float LoopRequiredSpeed(float distance)
        {
            float kmh = Mathf.Min(settings.loopSpeedCapKmh,
                                  settings.loopSpeedFloorKmh + settings.loopSpeedRampKmhPer100m * distance / 100f);
            return kmh / SpeedDisplayMultiplier;
        }

        public PolicePatrol Patrol => patrol;
        /// <summary>The run's goal speed: the level's first mandatory Reach Speed objective, else the settings' fallback.</summary>
        public float LightSpeedKmh => level != null && level.LightSpeedKmh > 0f ? level.LightSpeedKmh : settings.lightSpeedKmh;
        public RunnerLevelDefinition Level => level;
        /// <summary>Ramps taken off from this run.</summary>
        public int JumpCount => jumpCount;
        public bool IsObjectiveDone(int index) => index >= 0 && index < objectiveDone.Length && objectiveDone[index];
        public bool IsChallengeDone(int index) => index >= 0 && index < challengeDone.Length && challengeDone[index];
        /// <summary>
        /// The run's countdown, seconds: the saved track's own when it sets one
        /// (<see cref="Track.TrackLayoutAsset.timeLimitSeconds"/>), else
        /// <see cref="GameSettings.timeLimitSeconds"/>.
        /// </summary>
        public float TimeLimit
        {
            get
            {
                ResolveRunData();
                var authored = AuthoredTrack as Track.TrackLayoutAsset;
                return authored != null && authored.timeLimitSeconds > 0f ? authored.timeLimitSeconds : settings.timeLimitSeconds;
            }
        }

        /// <summary>
        /// The debug menu's FORCE RUNTIME TRACK: ignore the level's saved track
        /// and generate one, from the next restart — to compare the two. A
        /// runtime switch, never written to an asset; it lasts while this
        /// scene is loaded.
        /// </summary>
        public bool ForceRuntimeTrack { get; set; }

        /// <summary>The saved track this run plays — the SELECT COURSE pick (<see cref="TrackSelection"/>), else the level's — unless forced off; null generates one.</summary>
        public ScriptableObject AuthoredTrack
        {
            get
            {
                ResolveRunData();
                if (ForceRuntimeTrack) return null;
                if (selectedTrack != null) return selectedTrack;
                return level != null ? level.track : null;
            }
        }
        public float TimeRemaining { get; private set; }
        /// <summary>
        /// Latched the frame every mandatory objective is met (reaching Light
        /// Speed ONCE is enough). It is only half the win: the ship must still
        /// leave the track by an end ramp, and until it does the countdown,
        /// and the patrol are both still live.
        /// </summary>
        public bool ObjectivesMet { get; private set; }
        /// <summary>True once the run's Light Speed goal is latched — the HUD's done state. A level whose objectives hold no Reach Speed never sets it (its Light Speed is only a gauge reference).</summary>
        public bool LightSpeedReached
        {
            get
            {
                if (level == null || level.Count == 0) return ObjectivesMet;
                for (int i = 0; i < level.Count; i++)
                    if (level.objectives[i] != null && level.objectives[i].type == RunnerObjectiveType.ReachSpeed)
                        return IsObjectiveDone(i);
                return false;
            }
        }
        /// <summary>Latched the step the ship leaves an end ramp with <see cref="ObjectivesMet"/>: nothing can be lost any more.</summary>
        public bool HasWon { get; private set; }
        /// <summary>True while an ending is playing out — the win's fly-past or the MISSION FAILED banner — before <see cref="RunOver"/>. No pause, no story lines.</summary>
        public bool IsEnding => HasWon || failRoutine != null;
        /// <summary>True from the frame the run ended until <see cref="Restart"/>.</summary>
        public bool RunOver { get; private set; }

        /// <summary>True while walls and brake pads damage the ship and failed runs cost lives (<see cref="GameSettings.hullEnabled"/>).</summary>
        public bool HullEnabled => settings != null && settings.hullEnabled;
        /// <summary>The ship's hull, for the HUD's life bar. Null without a ship.</summary>
        public ShipHealth ShipHealth => shipHealth;

        /// <summary>The hull's fill for views (<see cref="IRunState"/>): 0..1, below 0 without a hull.</summary>
        public float HullFraction => shipHealth != null ? shipHealth.Fraction : -1f;
        /// <summary>Failed runs this mission still forgives — the HUD's ×N. Every failed run takes one; the run that takes the last is GAME OVER. Survives <see cref="Restart"/>.</summary>
        public int LivesLeft { get; private set; }

        /// <summary>
        /// Length of this run's track, metres: the level's own, else the
        /// settings' fallback. PULLED by the <see cref="TrackGenerator"/>, which
        /// builds its first stretch in its own Awake — possibly before this
        /// manager's — so it resolves the run's data first.
        /// </summary>
        public float TrackLengthMeters
        {
            get
            {
                ResolveRunData();
                return level.trackLengthMeters > 0f ? level.trackLengthMeters : settings.trackLengthMeters;
            }
        }

        /// <summary>
        /// The debug menu's TRACK LENGTH row: writes the length this run
        /// resolves to — the level's own when it sets one, else the
        /// GameSettings fallback — on the asset that holds it. Takes effect on
        /// the next Generate (the menu offers the reload).
        /// </summary>
        public void SetTrackLengthFromDebug(float meters)
        {
            ResolveRunData();
            if (level.trackLengthMeters > 0f) { level.trackLengthMeters = meters; DebugAssetEdits.Touch(level); }
            else { settings.trackLengthMeters = meters; DebugAssetEdits.Touch(settings); }
        }

        /// <summary>Length of the straight, featureless run-up to the end ramps, metres.</summary>
        public float EndRunUpMeters { get { ResolveRunData(); return settings.endRunUpMeters; } }
        /// <summary>Gap between two end ramps, metres.</summary>
        public float EndRampGapMeters { get { ResolveRunData(); return settings.endRampGapMeters; } }
        /// <summary>Gap between an outer end ramp and the wall, metres.</summary>
        public float EndRampSideGapMeters { get { ResolveRunData(); return settings.endRampSideGapMeters; } }

        /// <summary>Metres of track left ahead of the ship, 0 once it is past the end. The end is the generator's: the built one once the last knot is down, the authored target until then.</summary>
        public float DistanceRemaining =>
            generator != null && motor != null ? Mathf.Max(0f, generator.EndDistance - motor.DistanceTravelled) : 0f;

        /// <summary>True when this run's track ends (the HUD's distance line is only drawn then).</summary>
        public bool HasTrackEnd => generator != null && generator.IsFinite;

        bool runDataResolved;
        // The SELECT COURSE pick, taken once with the level so a retry in place
        // and a debug reload both keep playing it. Runtime only.
        Track.TrackLayoutAsset selectedTrack;

        /// <summary>
        /// Settles which settings and level this run plays on. Idempotent, and
        /// safe before Awake: both are serialized references and the campaign
        /// session and the course pick are static.
        /// </summary>
        void ResolveRunData()
        {
            if (runDataResolved) return;
            runDataResolved = true;

            // Never run without balance data: a throwaway instance keeps the
            // scene playable (on defaults) instead of throwing every frame.
            if (settings == null)
            {
                Debug.LogError($"{nameof(GameManager)} has no {nameof(GameSettings)} asset assigned — falling back to defaults.", this);
                settings = ScriptableObject.CreateInstance<GameSettings>();
            }
            // A live campaign session names the run to play; the serialized
            // asset is the direct-play (editor) fallback.
            if (MissionSession.Current != null && MissionSession.Current.runnerLevel is RunnerLevelDefinition sessionLevel)
                level = sessionLevel;
            if (level == null)
            {
                Debug.LogError($"{nameof(GameManager)} has no {nameof(RunnerLevelDefinition)} asset assigned — falling back to the default run.", this);
                level = RunnerLevelDefinition.CreateDefault();
            }
            // A course picked on the SELECT COURSE screen plays over the level's
            // own track in direct play. A live mission is authored whole and
            // ignores it.
            if (!MissionSession.Active) selectedTrack = TrackSelection.Current;
        }

        void Awake()
        {
            ResolveRunData();
            ResetObjectives();

            TimeRemaining = TimeLimit;
            SpeedPad.Collected += OnPadCollected;
            LaserGate.Hit += OnLaserHit;

            if (motor != null)
            {
                motor.ConfigureDash(settings);
                motor.FellOff += OnFellOff;
                motor.ReachedTrackEnd += OnReachedTrackEnd;
                motor.Respawned += OnRespawned;
                motor.TookOff += OnTookOff;

                // The loop's slow motion rides on the ship (the clock-owner
                // contract lives there); the knobs are on the settings asset.
                LoopSlowMo.Ensure(motor).Configure(settings);

                // Debug utility: Y boosts the ship (GameSettings.debugBoostChord).
                DebugBoostChord.Ensure(motor).Configure(settings);

                // The duel's armed window rides the ship too: it is a state OF
                // the ship (a strong orb leaves it carrying a kill), and it
                // gates itself on the duel's master switch.
                ShipArmed.Ensure(motor).Configure(patrol);

                // The ship's own components live below the runner and speak
                // ShipSettings: the ship's live clone is the one home of the
                // dash, ghost, fall and respawn rules.
                ShipSettings shipSettings = motor.ShipSettings;

                // The respawn blink rides the ship the same way.
                RespawnBlink.Ensure(motor).Configure(shipSettings);

                // The ship's own sounds (engine loop, pickup, jump) ride the
                // ship the same way, reading the settings live; off = no
                // component at all. The endings need no hook: the engine gates
                // on the motor's pause.
                if (settings.sfxEnabled) ShipAudio.Ensure(motor).Configure(settings, LightSpeedKmh);

                // The DashMeterUI is a scene child of the Ship — it configures
                // itself off the motor in Start, after ConfigureDash above.
                if (shipSettings.dashEnabled)
                {
                    var trail = motor.GetComponent<DashGhostTrail>();
                    if (trail == null) trail = motor.gameObject.AddComponent<DashGhostTrail>();
                    trail.Init(motor, shipSettings);
                    // The airborne dash's wingtip ribbons — same lifetime as the ghosts.
                    var rollTrail = motor.GetComponent<BarrelRollTrail>();
                    if (rollTrail == null) rollTrail = motor.gameObject.AddComponent<BarrelRollTrail>();
                    rollTrail.Init(motor, shipSettings);
                    dashPrompt = DashPromptController.Spawn(motor, settings);
                }

                if (settings.boostQte) BoostQte.Spawn(motor, settings);
            }

            // The ship's run definition: the Store's bought levels multiplied
            // into a fresh clone of the authored asset (plus the armed debug
            // overrides), set here — every Awake precedes ShipMotor.Start's
            // launch. The tuning screen is parked (the COMPONENT — it shares
            // the RaceHUD canvas object with the HUD, so its object must stay
            // active) before its own Start can fire, and forgotten, so Restart
            // never reopens it. Flip GameSettings.useTuningScreen to get the
            // point allocation back (it then applies the store levels on top
            // of its points).
            if (!settings.useTuningScreen)
            {
                if (motor != null) motor.SetDefinition(ShipUpgradeApplier.BuildRunDefinition(motor.Definition));
                if (tuningScreen != null)
                {
                    tuningScreen.Park();
                    tuningScreen = null;
                }
            }

            // The hull rides the ship like the blink (which it drives through
            // the invulnerability after a hit) — after the run definition is
            // set, so the bar fills to the clone's maxHull. Always added: it
            // gates itself on GameSettings.hullEnabled, read live.
            LivesLeft = settings.startingLives;
            // What a GAME OVER hands back: the wallet as the mission began
            // (before the city), or as this scene opened in direct play.
            walletAtMissionStart = MissionSession.Active ? MissionSession.WalletAtStart : PlayerStats.Balance;
            if (motor != null)
            {
                shipHealth = ShipHealth.Ensure(motor);
                shipHealth.Configure(settings, this);
                shipHealth.Damaged += OnHullDamaged;
                shipHealth.Destroyed += OnShipDestroyed;

                // The win's exit rides the ship too (hand-placed on PF_Ship); it
                // hides the model through the hull, which owns the hide.
                escapeVanish = EscapeVanish.Ensure(motor);
                escapeVanish.Configure(settings, motor, shipHealth);
            }

            // Above the pause menu's canvas and holding timeScale at 0, so the
            // scene boots to the attract screen with nothing running behind it.
            if (mainMenuOnBoot) MainMenuController.Spawn(motor, tuningScreen);

            // The patrol is a scene object now: wire it up here (its chase
            // tunables live on its PatrolDefinition asset), or park it when
            // the feature is off. A missing reference degrades to a chase-less
            // run instead of breaking the scene.
            if (settings.patrolEnabled && motor != null && patrol != null)
            {
                // The composition root hands the patrol what it hunts and may take
                // over (the motor, through the chase contracts), the run's rules and
                // the track it drives. The redeploy rule is on its PatrolDefinition.
                patrol.Init(motor, motor, settings, motor.Track, generator);
                patrol.ProximityRumble = settings.patrolProximityRumble;
            }
            else
            {
                if (settings.patrolEnabled && motor != null)
                    Debug.LogError($"{nameof(GameManager)} has no {nameof(PolicePatrol)} scene reference — running without the chase.", this);
                if (patrol != null) patrol.gameObject.SetActive(false);
                patrol = null;
            }

            // Oncoming traffic: the level opts in with a TrafficDefinition; a
            // level without one (or no ship) leaves the system idle.
            if (traffic != null)
            {
                if (motor != null)
                {
                    traffic.Bind(motor, motor, patrol, motor.Track, generator, this, settings, level.traffic);
                    traffic.ShipStruck += OnTrafficStruck;
                }
                else traffic.Unbind();
            }

            // The track map shows the run's progress with or without a chase;
            // the patrol is only its second marker.
            if (motor != null)
                ChaseMinimap.Spawn(motor, patrol, this, settings.minimapRangeMeters,
                                   patrol != null ? patrol.Definition.warnDistance : 0f, generator);

            // The duel: the tug-of-war bar, and the slow motion the exchange
            // runs under. Both no-op without a patrol or with the duel off.
            // The slow-mo rides the ship like the loop's does, so the two share
            // one clock-ownership contract and settle against each other.
            if (motor != null && patrol != null && patrol.Definition != null && patrol.Definition.duelEnabled)
            {
                DuelSlowMo.Ensure(motor).Configure(patrol);
                DuelBarHud.Spawn(motor, patrol);
            }

            // The presentation: the camera director attaches the chase rig and
            // frames the run's set pieces; the feedback plays each event's
            // rumble, shake, glitch and story line and brings up the speed
            // lines (it follows the director's camera mode, so it binds after).
            cameraDirector = RunCameraDirector.Ensure(this);
            cameraDirector.Bind(motor, patrol, settings, this);
            feedback = RunFeedback.Ensure(this);
            feedback.Bind(motor, patrol, settings, this, cameraDirector);

            // The hyperspace jump follows the feedback's Light Speed state, so
            // it binds after it. Its prompt is hand-placed in PF_UI like the
            // dash prompt; off, the prompt is never bound and stays empty.
            hyperspace = HyperspaceJump.Ensure(this);
            HyperspacePrompt hyperspacePrompt = settings.hyperspaceJumpEnabled && motor != null
                ? HyperspacePrompt.Spawn(motor) : null;
            hyperspace.Bind(motor, generator, patrol, settings, this, feedback, hyperspacePrompt);

            // Weather rides with the camera and needs nothing from the run, so
            // it goes up before the menu — the debug page binds to the live
            // system the same way the patrol tab binds to the patrol. Apply,
            // not spawn: the scene's own RainSystem is the one the designer
            // tuned before play, and switching the weather off has to park it.
            RainSystem.Apply(settings.rainEnabled);

            // VHS tape: the scene's hand-placed VhsTape object, found and
            // parked the same way. It needs nothing from the run — the whole
            // picture, glitch included, is played back off the tape.
            VhsTape.Apply(settings.vhsEnabled);

            // PSX look: the console the tape records — same rule, the scene's
            // hand-placed PsxLook object, found and parked.
            PsxLook.Apply(settings.psxEnabled);

            // CRT screen: the tube the console and the tape are shown on —
            // same rule, the scene's hand-placed CrtScreen object, found and
            // parked.
            CrtScreen.Apply(settings.crtEnabled);

            // Music: the scene's hand-placed Music object under ===SYSTEMS===,
            // found and parked the same way — never spawned. Its own Start
            // begins the loop at a random point under a fade-in; this manager
            // only fades it out on the endings and replays it on a retry.
            music = RunnerMusic.Apply(settings.musicEnabled, settings.musicSettings);

            // After the patrol init, so the debug menu's patrol tab can bind
            // to the live definition clone.
            PauseMenu.Spawn(this, motor);
        }

        void Update()
        {
            // D26: an attack run holds the RPG queue. Driven every frame rather
            // than on the run's edges, because every path that can end a run
            // (an abort, a kill's recycle, a fall, a hold) would otherwise need
            // to remember to release it — and one missed path is a text box that
            // never comes back for the rest of the level.
            RpgMessageSystem.QueueHeld = patrol != null && patrol.InAttackRun && !RunOver;

            if (motor == null || RunOver) return;

            // One "escape attempted" per run, recorded on the first frame the
            // ship actually flies — Launch() fires up to three times per run
            // (the motor's Start, Restart, the tuning screen), so it can't count.
            if (!motor.Paused)
            {
                if (!runCounted)
                {
                    runCounted = true;
                    PlayerStats.RecordRunStarted();
                }
                PlayerStats.SampleShipSpeed(motor.CurrentSpeed * SpeedDisplayMultiplier);
            }

            // An ending is playing out (the win's fly-past, the MISSION FAILED
            // banner): nothing is judged any more and the clock stands still.
            if (IsEnding) return;

            // The objectives latch the moment they are all met — Light Speed
            // reached once is reached — but that is only half the win: the
            // ship still has to leave the track by one of its end ramps
            // (OnReachedTrackEnd), and until then everything below is live.
            // Evaluated every frame either way, so a challenge can still latch.
            if (EvaluateObjectives(motor.CurrentSpeed * SpeedDisplayMultiplier)) ObjectivesMet = true;

            // Once the hyperspace jump is pressed nothing can lose the run: the
            // hull is shielded and a catch is never read (the patrol is held).
            bool jumping = hyperspace != null && hyperspace.Jumping;
            if (shipHealth != null) shipHealth.Shielded = jumping;
            // The chord clears the road of traffic and nothing spawns after it (OncomingTrafficPRD.md D5).
            if (traffic != null) traffic.Suspended = jumping;

            if (!jumping && patrol != null && patrol.HasCaught)
            {
                HapticsSystem.Instance.Pulse(settings.bustedRumble);
                BeginFail(RunOutcome.Caught);
                return;
            }

            // Time only pressures the player while the ship is flying — and
            // not once the hyperspace jump has committed it to the ending.
            if (motor.Paused) return;
            if (hyperspace != null && hyperspace.Jumping) return;

            // The countdown rides the scaled clock like everything else, so a
            // LOOP simply plays longer in real time and costs the same mission
            // seconds. The DUEL is the one exception: its slow motion buys
            // perception, not time, so the clock keeps full pace through an
            // exchange — which is what makes declining one, by braking off the
            // flank, a real choice when the countdown is short.
            float countdownStep = DuelSlowMo.IsActive ? Time.unscaledDeltaTime : Time.deltaTime;
            TimeRemaining = Mathf.Max(0f, TimeRemaining - countdownStep);
            if (TimeRemaining <= 0f)
                BeginFail(RunOutcome.TimedOut);
        }

        /// <summary>
        /// The ship ran out of road (it is already off the track and
        /// dropping). With every objective met AND an end ramp under it, that
        /// is the win: the drop becomes the escape flight and the wind-down
        /// starts. Anything else is a loss the fall itself plays out —
        /// objectives still open is TooSlow whether or not a ramp was taken,
        /// the ramps missed is MissedRamp. Without the hyperspace jump the ship
        /// must also still be AT Light Speed on the lip — latched mid-run and
        /// lost again is TooSlow. Raised from the simulation tick, so
        /// the objectives are read once more first: a goal met on this very
        /// step counts.
        /// </summary>
        void OnReachedTrackEnd(bool tookRamp)
        {
            if (RunOver || IsEnding) return;
            if (EvaluateObjectives(motor.CurrentSpeed * SpeedDisplayMultiplier)) ObjectivesMet = true;

            // The hyperspace jump was only offered with every objective met,
            // and pressing it is the win: off the end is the escape whether
            // the autopilot put the ship on a ramp or through a gap.
            if (hyperspace != null && hyperspace.Jumping)
            {
                tookRamp = true;
                ObjectivesMet = true;
            }

            // Light Speed reached mid-run latches the objective, but without
            // the hyperspace jump the ship must still HOLD it at the lip: a run
            // that dropped back under it is too slow to escape.
            lostSpeedAtEnd = ObjectivesMet && !(hyperspace != null && hyperspace.Jumping)
                             && !HoldsSpeedGoals(motor.CurrentSpeed * SpeedDisplayMultiplier);
            if (tookRamp && ObjectivesMet && !lostSpeedAtEnd)
            {
                HasWon = true; // from here on nothing can be lost
                motor.BeginEscape();
                if (escapeVanish != null) escapeVanish.Begin();
                HapticsSystem.Instance.Pulse(settings.escapeRumble);
                winRoutine = StartCoroutine(FinishWin());
                return;
            }

            HapticsSystem.Instance.Pulse(settings.endFailRumble);
            if (GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.fallGlitchStrength);
            BeginFail(ObjectivesMet && !lostSpeedAtEnd ? RunOutcome.MissedRamp : RunOutcome.TooSlow);
        }

        /// <summary>
        /// True when <paramref name="speedKmh"/> is at or above every mandatory
        /// Reach Speed target (the plain Light Speed test for a level with no
        /// objectives) — what the lip asks of a run that did not jump.
        /// </summary>
        bool HoldsSpeedGoals(float speedKmh)
        {
            if (level == null || level.Count == 0) return speedKmh >= LightSpeedKmh;
            for (int i = 0; i < level.Count; i++)
                if (level.objectives[i] != null && level.objectives[i].type == RunnerObjectiveType.ReachSpeed
                    && speedKmh < level.objectives[i].targetSpeedKmh)
                    return false;
            return true;
        }

        // Every loss goes through the MISSION FAILED wind-down, once.
        // Every failed run costs a life, whatever ended it; the one that takes
        // the last is GAME OVER (no retry — see ShowGameOver).
        void BeginFail(RunOutcome outcome)
        {
            if (RunOver || IsEnding) return;
            // After the hyperspace jump the run cannot be lost (every path
            // that could still end it is guarded; this is the net under them).
            if (hyperspace != null && hyperspace.Jumping) return;
            // The run is over: the chase stops where it is — no attack run
            // finishes under the banner, no cruiser drives on past the wreck.
            if (patrol != null)
            {
                patrol.AbortEncounter();
                patrol.SetHold(true);
            }
            if (HullEnabled)
            {
                LivesLeft = Mathf.Max(0, LivesLeft - 1);
                isGameOver = LivesLeft == 0;
            }
            failRoutine = StartCoroutine(FinishFail(outcome));
        }

        void OnShipDestroyed() => BeginFail(RunOutcome.Destroyed);

        // Any hull hit: a scrape gets its own light feedback; a hard hit (brake
        // pad, dash slam, ramp side) already has its own (RunFeedback's wall hit, the pad's).
        void OnHullDamaged(float amount, bool hard)
        {
            if (GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.hullHitGlitchStrength);
            if (hard) return;
            HapticsSystem.Instance.Pulse(settings.hullHitRumble);
            CameraShake.Shake(settings.scrapeShake);
        }

        // 0 hull: the fireball where the ship was, and the ship gone. The sim
        // is already frozen (FinishFail), so nothing moves out from under it.
        void ExplodeShip()
        {
            Vector3 origin = motor.Visual != null ? motor.Visual.position : motor.transform.position;
            if (settings.explosionTextures != null && settings.explosionTextures.Count > 0)
                ExplosionVfx.SpawnFireball(origin, settings.explosionTextures, settings.explosionScale,
                                           settings.explosionLifetime, settings.explosionParticles);
            if (shipHealth != null) shipHealth.SetShipVisible(false);

            HapticsSystem.Instance.Pulse(settings.explosionRumble);
            CameraShake.Shake(settings.explosionShake);
            if (GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.explosionGlitchStrength);
            var shipAudio = motor.GetComponent<ShipAudio>();
            if (shipAudio != null) shipAudio.PlayExplosion();
        }

        /// <summary>
        /// The loss's wind-down, on unscaled time: MISSION FAILED slams in (the
        /// win banner's own animation, in the fail colour), holds
        /// <c>failBannerHoldSeconds</c>, tears away over
        /// <c>failBannerDismissSeconds</c>, then the run ends and the retry
        /// panel opens. A loss ON the track (caught, destroyed, out of time)
        /// freezes the simulation at once; a loss off the END of it keeps the
        /// simulation running under the banner, from a planted camera, so the
        /// ship's fall — and the patrol's, right behind it — plays out.
        /// </summary>
        IEnumerator FinishFail(RunOutcome outcome)
        {
            bool offTheEnd = outcome == RunOutcome.MissedRamp || outcome == RunOutcome.TooSlow;

            RpgMessageSystem.Instance.ClearMessages();
            if (music != null) music.FadeOut(); // at the asset's fade-out time, under the banner

            bool planted = false;
            if (!offTheEnd)
            {
                motor.Paused = true; // freeze the sim; the hover keeps the ship floating
                if (outcome == RunOutcome.Destroyed) ExplodeShip();
            }
            else
            {
                // A loop or fall shot may still be armed; this one replaces it.
                planted = cameraDirector.PlantEndingShot();
            }

            // The last life lost says so: GAME OVER, not MISSION FAILED.
            banner = MissionAccomplishedBanner.Show(settings, isGameOver ? MenuTextId.GameOver : MenuTextId.MissionFailed,
                                                    settings.failBannerColor, settings.failBannerColor);
            // The reaction clip under the banner; the banner holds for all of it.
            failVideo = MissionFailedVideo.Show(settings);
            float hold = Mathf.Max(settings.failBannerHoldSeconds, failVideo != null ? failVideo.Length : 0f);
            if (hold > 0f)
                yield return new WaitForSecondsRealtime(hold);
            float dismiss = Mathf.Max(0.01f, settings.failBannerDismissSeconds);
            if (banner != null) banner.Dismiss(dismiss);
            yield return new WaitForSecondsRealtime(dismiss);

            failRoutine = null;
            if (planted) cameraDirector.ReleaseEndingShot();
            EndRun(outcome);
        }

        void EndRun(RunOutcome outcome)
        {
            RunOver = true;
            motor.Paused = true; // freeze the sim; the hover keeps the ship floating

            // The music is already fading: a win booked it with the glitch in
            // FinishWin, a loss with the banner in FinishFail.

            // The record: an escape completed (and how long it took — the
            // timer only ran while flying and stops at the win, so this is
            // launch to the end ramp's lip), or a failed one; the patrol
            // catching up is the runner's arrest.
            PlayerStats.RecordRunEnded(outcome == RunOutcome.Escaped, TimeLimit - TimeRemaining);
            if (outcome == RunOutcome.Caught) PlayerStats.RecordArrest();
            PlayerProfileStore.SaveIfDirty();

            // Straight to the panel, this frame. Both panels freeze the clock
            // and the RPG box types on scaled time, so a story line still up
            // (an orb hype line, a patrol taunt) would sit frozen under the
            // panel — drop it, and its callback with it.
            RpgMessageSystem.Instance.ClearMessages();
            KillBanner(); // the panel is the win's text from here on
            if (HasWon) ShowMissionComplete();
            else ShowGameOver(outcome);
        }

        /// <summary>
        /// The win's wind-down, on unscaled time, started the step the ship
        /// leaves an end ramp (it is already in its escape flight, off the
        /// track for good): plant the camera at the lip and let the ship fly
        /// on out of the shot for <c>winCameraHoldSeconds</c>, ramp the glitch from
        /// wherever it is to max over
        /// <c>winGlitchRampSeconds</c>, hold it <c>winGlitchHoldSeconds</c>,
        /// then end the run — which opens the panel behind the full glitch,
        /// the city handoff's picture. The controller's fade is remembered
        /// and handed back once the panel is up, so the glitch clears behind
        /// the results the way it clears behind the runner's first frames.
        /// </summary>
        IEnumerator FinishWin()
        {
            // The exclamation mark: MISSION ACCOMPLISHED slams in letter by
            // letter over the shot below, holds through the fly-past, and is
            // torn apart with the picture once the glitch starts to ramp.
            banner = MissionAccomplishedBanner.Show(settings);

            // The escape beat: the picture stops chasing and watches the ship
            // go. The rig's cinematic shot is PLANTED for the ship — a level
            // tripod that holds its ground and only pans to keep the ship in
            // frame — which is the only framing that reads here: at Light Speed
            // the ship covers 1.8 km a second, so a camera frozen dead behind it
            // would lose it in a tenth of one. The sim is still running (nothing
            // pauses until EndRun), so the ship flies on through the shot, and
            // the player's hands come off the camera for it.
            // A loop shot may still be up with its own hold counting down; the
            // director disarms it so it cannot cut back out from under this one.
            // The shot can refuse — the camera asset's Cinematic toggle kills it
            // per vehicle. Without a planted camera there is nothing to hold on,
            // so don't sit the player in front of a locked chase view for two
            // seconds; go straight to the glitch.
            if (settings.winCameraHoldSeconds > 0f && cameraDirector.PlantEndingShot())
                yield return new WaitForSecondsRealtime(settings.winCameraHoldSeconds);

            // The sound washes out with the picture: the fade spans the ramp
            // and the hold, so the music lands silent on the frame EndRun
            // opens the panel.
            if (music != null) music.FadeOut(settings.winGlitchRampSeconds + settings.winGlitchHoldSeconds);
            // The banner tears apart over the same span, so the word is gone on
            // the frame the panel opens (EndRun kills any remainder).
            if (banner != null) banner.Dismiss(settings.winGlitchRampSeconds + settings.winGlitchHoldSeconds);

            GlitchController glitch = GlitchController.Instance;
            if (glitch != null)
            {
                // A hold: the ramp must reach and keep max, whatever the fade.
                float from = glitch.baseIntensity;
                float ramp = Mathf.Max(0.01f, settings.winGlitchRampSeconds);
                for (float t = 0f; t < ramp; t += Time.unscaledDeltaTime)
                {
                    glitch.Hold(this, Mathf.Lerp(from, 1f, t / ramp));
                    yield return null;
                }
                glitch.Hold(this, 1f);
                glitch.Pulse(1f);
            }

            if (settings.winGlitchHoldSeconds > 0f)
                yield return new WaitForSecondsRealtime(settings.winGlitchHoldSeconds);

            winRoutine = null;
            // The debrief is the player's screen again: the shot cuts back to a
            // live chase view and the camera answers to them while they read it.
            cameraDirector.ReleaseEndingShot();
            EndRun(RunOutcome.Escaped);
            ReleaseGlitch();
        }

        // Drops the MISSION ACCOMPLISHED banner this frame, mid-tear or not.
        // Idempotent: the banner may already have torn itself down.
        void KillBanner()
        {
            if (banner != null) banner.Kill();
            banner = null;
            if (failVideo != null) failVideo.Kill();
            failVideo = null;
        }

        // Lets go of the ending's glitch hold so the held max decays again
        // (behind the panel, or on a retry). Idempotent.
        void ReleaseGlitch()
        {
            if (GlitchController.Instance != null) GlitchController.Instance.Release(this);
        }

        /// <summary>
        /// The retry panel on the shared screen, raised once the MISSION
        /// FAILED banner has torn away: MISSION FAILED, the reason this run
        /// ended, then RETRY? — YES runs the track again, in place (no load),
        /// NO goes to the main menu under the loading curtain. It is the same
        /// screen the city chase raises; the city's has no reason line and
        /// keeps its GAME OVER title.
        /// </summary>
        void ShowGameOver(RunOutcome outcome)
        {
            MenuTextId reason = outcome switch
            {
                RunOutcome.Caught => MenuTextId.LoseCaught,
                RunOutcome.TimedOut => MenuTextId.LoseTimeOut,
                RunOutcome.MissedRamp => MenuTextId.LoseMissedRamp,
                // The speed goal is the one the HUD is about; any other goal
                // left open gets the general line.
                RunOutcome.TooSlow => SpeedGoalOpen || lostSpeedAtEnd ?MenuTextId.LoseTooSlow : MenuTextId.LoseObjectivesIncomplete,
                _ => MenuTextId.LoseDestroyed
            };

            // The last life lost: no retry. The mission is forfeited HERE, not
            // on the button — quitting on the screen must not dodge it: the
            // wallet goes back to what it was when the mission began and the
            // city clear is dropped, so the Store's START MISSION replays the
            // city. Any button then leads back to the Store.
            if (isGameOver)
            {
                PlayerStats.ForfeitMission(walletAtMissionStart);
                GameOverScreen.ShowFinal(reason, onContinue: () =>
                {
                    MissionSession.Clear();
                    LoadingScreen.Load(StoreSettings.SceneName);
                });
                return;
            }

            GameOverScreen.Show(reason, onRetry: Restart, onGiveUp: LoadingScreen.LoadMainMenu,
                                titleId: MenuTextId.MissionFailed);
        }

        // True when what kept the run from its objectives is Light Speed itself.
        bool SpeedGoalOpen
        {
            get
            {
                if (level == null || level.Count == 0) return true;
                for (int i = 0; i < level.Count; i++)
                    if (level.objectives[i] != null && level.objectives[i].type == RunnerObjectiveType.ReachSpeed
                        && !IsObjectiveDone(i))
                        return true;
                return false;
            }
        }

        /// <summary>
        /// The Mission Complete panel, raised by <see cref="FinishWin"/> once
        /// the ship is grounded and the glitch has reached max (the panel
        /// freezes the clock). The city level's rows come off the
        /// profile — the only thing that crosses the scene handoff — and the
        /// run's own rows off the live state; the panel adds them up, ranks
        /// them and banks the mission.
        /// </summary>
        void ShowMissionComplete()
        {
            if (!RunOver || !HasWon || MissionCompleteScreen.IsOpen) return;
            MissionCompleteScreen.Show(BuildMissionCompleteData(),
                                       onNext: () => LoadingScreen.Load(NextSceneAfterMission()),
                                       onRetry: Restart,
                                       onExit: LoadingScreen.LoadMainMenu);
        }

        // NEXT MISSION on a campaign mission always returns to the Store, which
        // offers the new frontier; direct play keeps the level's own next scene.
        // A course picked on the SELECT COURSE screen goes back to that screen.
        string NextSceneAfterMission() => MissionSession.Active ? StoreSettings.SceneName
            : selectedTrack != null ? LevelSelectScreen.SceneName : level.nextSceneName;

        MissionCompleteData BuildMissionCompleteData()
        {
            var last = PlayerProfileStore.Profile.lastLevel;
            bool hasCity = last.objectives.Count > 0 || last.baseReward > 0;

            var data = new MissionCompleteData
            {
                missionId = MissionSession.Current != null ? MissionSession.Current.id : "",
                title = hasCity && !string.IsNullOrEmpty(last.levelName) ? last.levelName : level.levelName,
                video = level.completeVideo,
                baseReward = hasCity ? last.baseReward : 0,
                rank = hasCity && last.rank != null && last.rank.IsSet ? last.rank : level.rankTable
            };
            if (hasCity) data.mainObjectives.AddRange(last.objectives);
            for (int i = 0; i < level.Count; i++)
            {
                RunnerObjective step = level.objectives[i];
                data.runObjectives.Add(new ObjectiveResult(step.Summary, step.reward, IsObjectiveDone(i)));
            }
            if (hasCity) data.challenges.AddRange(last.challenges);
            for (int i = 0; i < level.ChallengeCount; i++)
            {
                RunnerOptionalChallenge challenge = level.optionalChallenges[i];
                data.challenges.Add(new ChallengeResult(challenge.Summary, challenge.multiplier, IsChallengeDone(i)));
            }
            return data;
        }

        /// <summary>
        /// Latches every objective and challenge that is met this frame and
        /// answers whether the run is WON: every mandatory objective done. A
        /// level with no objectives falls back to the plain Light Speed test.
        /// </summary>
        bool EvaluateObjectives(float speedKmh)
        {
            if (level.Count == 0) return speedKmh >= LightSpeedKmh;

            bool allDone = true;
            for (int i = 0; i < level.Count && i < objectiveDone.Length; i++)
            {
                if (!objectiveDone[i] && level.objectives[i].Satisfied(speedKmh, jumpCount)) objectiveDone[i] = true;
                allDone &= objectiveDone[i];
            }
            for (int i = 0; i < level.ChallengeCount && i < challengeDone.Length; i++)
            {
                if (!challengeDone[i] && level.optionalChallenges[i].Satisfied(speedKmh, jumpCount))
                {
                    challengeDone[i] = true;
                    PlayerStats.CompleteBonusObjective();
                }
            }
            return allDone;
        }

        void ResetObjectives()
        {
            jumpCount = 0;
            objectiveDone = new bool[level != null ? level.Count : 0];
            challengeDone = new bool[level != null ? level.ChallengeCount : 0];
        }

        // The record: every orb the ship takes (its story line is RunFeedback's).
        void OnPadCollected(SpeedPad pad, IShip collector)
        {
            if (motor == null || !motor.Is(collector) || RunOver || IsEnding) return;
            PlayerStats.RecordPad(pad.SpeedDelta > 0f); // positive = power-up, negative = slow-down
        }

        // A jump: the Jump X Times goals count takeoffs (the Far framing is the director's).
        void OnTookOff()
        {
            if (!RunOver) jumpCount++;
        }

        // Over an open edge: the patrol holds (the clock does not) until the
        // ship is back. The fall's rumble and glitch are RunFeedback's, the
        // planted shot the director's.
        void OnFellOff()
        {
            if (patrol != null) patrol.SetHold(true);
        }

        void OnRespawned()
        {
            // A jump or a loss holds the patrol for good; only the fall's own hold is released.
            if (IsEnding || RunOver || (hyperspace != null && hyperspace.Jumping)) return;
            if (patrol != null) patrol.SetHold(false, settings.respawnMinPatrolGap);
        }

        // Through a laser beam: a fall's worth of hull (ShipHealth — the blink
        // shields it, and then nothing plays), a share of the speed gone at
        // once, and the heaviest rumble short of the explosion's. The hull's
        // own Damaged handler adds the glitch.
        void OnLaserHit(LaserGate gate, Component hit)
        {
            if (hit != motor) return;
            ApplyHazardHit(fromTraffic: false);
        }

        // An oncoming car ran into the ship: the car has already exploded; the
        // ship takes exactly a laser's hit (OncomingTrafficPRD.md D3).
        void OnTrafficStruck() => ApplyHazardHit(fromTraffic: true);

        /// <summary>
        /// The one hit a laser beam and an oncoming car share: hull (the blink
        /// shields it — then nothing at all plays), a slice of speed, smoke,
        /// rumble, shake, the hit sound, and the glitch when there is no hull.
        /// </summary>
        void ApplyHazardHit(bool fromTraffic)
        {
            // A jump the player chose is not lost to a hazard on the way to the ramps.
            if (motor == null || IsEnding || RunOver || (hyperspace != null && hyperspace.Jumping)) return;
            bool hullOn = settings.hullEnabled && shipHealth != null;
            if (hullOn && !(fromTraffic ? shipHealth.ApplyTrafficHit() : shipHealth.ApplyLaserHit())) return;

            motor.ApplyImpactSpeedLoss(settings.laserSpeedLoss);

            // Smoke rides the ship's root (not the banking visual), so the
            // plume never rolls with a barrel roll.
            Vector3 smokeAt = motor.Visual != null
                ? motor.transform.InverseTransformPoint(motor.Visual.position)
                : Vector3.zero;
            SmokeVfx.SpawnTrail(motor.transform, smokeAt, settings.laserSmokeTextures, settings.laserSmokeScale,
                                settings.laserSmokeSeconds, settings.laserSmokeRate, settings.laserSmokeBurst);

            HapticsSystem.Instance.Pulse(settings.laserHitRumble);
            CameraShake.Shake(settings.laserHitShake);
            var shipAudio = motor.GetComponent<ShipAudio>();
            if (shipAudio != null) shipAudio.PlayLaserHit();
            if (!hullOn && GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.hullHitGlitchStrength);
        }

        void OnDestroy()
        {
            if (motor != null)
            {
                motor.FellOff -= OnFellOff;
                motor.ReachedTrackEnd -= OnReachedTrackEnd;
                motor.Respawned -= OnRespawned;
                motor.TookOff -= OnTookOff;
            }
            SpeedPad.Collected -= OnPadCollected;
            LaserGate.Hit -= OnLaserHit;
            if (traffic != null) traffic.ShipStruck -= OnTrafficStruck;
            if (shipHealth != null)
            {
                shipHealth.Damaged -= OnHullDamaged;
                shipHealth.Destroyed -= OnShipDestroyed;
            }
        }

        /// <summary>Resets the run; rebuilds the track (endless runs must — the stretch behind the start was culled).</summary>
        public void Restart()
        {
            // Drop any story line still queued (and its onFinished) so nothing
            // from the old run lands on the new one.
            RpgMessageSystem.Instance.ClearMessages();
            if (dashPrompt != null) dashPrompt.ResetForRun();
            if (feedback != null) feedback.ResetForRun();
            if (hyperspace != null) hyperspace.ResetForRun();
            if (music != null) music.Play(); // every attempt is a fresh play: a new random point under a fade-in

            // A retry from the panel: the wind-down is over, but a RETRY pressed
            // while a glitch is still decaying must start on a clean picture.
            if (winRoutine != null) { StopCoroutine(winRoutine); winRoutine = null; }
            if (failRoutine != null) { StopCoroutine(failRoutine); failRoutine = null; }
            KillBanner(); // a retry mid-beat must not leave the word over the new run
            ReleaseGlitch();
            if (GlitchController.Instance != null) GlitchController.Instance.SetBaseIntensity(0f);
            // A retry from inside a loop must not leave the shot armed — nor one
            // mid-fall, nor the win's planted shot with the player locked out.
            if (cameraDirector != null) cameraDirector.ResetForRun();

            bool wasWon = HasWon;
            RunOver = false;
            HasWon = false;
            ObjectivesMet = false;
            lostSpeedAtEnd = false;
            runCounted = false;
            TimeRemaining = TimeLimit;
            ResetObjectives();
            // A fresh hull and the ship back on screen; the LIVES are the
            // mission's and carry over. A retry after a WIN is another go at a
            // mission already paid: a new set, and the payout is now part of
            // what a GAME OVER hands back.
            if (wasWon)
            {
                LivesLeft = settings.startingLives;
                walletAtMissionStart = PlayerStats.Balance;
            }
            isGameOver = false;
            if (shipHealth != null) shipHealth.ResetForRun();
            // This run's money counter goes back to $0; what was picked up is
            // already banked in the profile.
            CollectibleManager collectibles = CollectibleManager.Instance;
            if (collectibles != null) collectibles.ResetRun();
            if (generator != null) generator.RegenerateForRun();
            motor.Paused = false; // EndRun froze the sim; the tuning screen re-pauses if present
            motor.Launch();
            if (patrol != null) patrol.Launch();
            if (traffic != null) traffic.ResetForRun();

            // Reopen ship setup so points can be re-allocated; it re-launches on START.
            if (tuningScreen != null) tuningScreen.Show();
        }
    }
}
