using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

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
    public class GameManager : MonoBehaviour
    {
        [Title("Scene references")]
        [SerializeField, Required] ShipMotor motor;
        [SerializeField, Required] TrackGenerator generator;
        [SerializeField] TuningScreen tuningScreen;
        [Tooltip("The scene's police patrol object. Initialized (and its cruiser visual built) here in Awake; deactivated when the patrol is disabled on GameSettings.")]
        [SerializeField] PolicePatrol patrol;

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
        OrbitCameraRig cameraRig;          // null when GameSettings has no camera asset
        CameraMode modeBeforeJump;         // the view a jump forced to Far hands back on landing
        SpeedLines speedLines;             // null when the speed lines are off on GameSettings

        /// <summary>How a run ended — typed, so the save record never has to match a result label.</summary>
        enum RunOutcome { Escaped, Caught, Stalled, TimedOut }

        bool runCounted; // this run's "escape attempted" has been recorded

        // The win wind-down: objectives met, ship still flying until it is back
        // on the track, then the glitch ramps to max and the panel opens.
        Coroutine winRoutine;
        float glitchFadeBeforeWin = -1f; // the GlitchController's fade rate to restore; < 0 = nothing remembered

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

        /// <summary>Entry speed (m/s) a loop at <paramref name="distance"/> demands: floor + ramp × distance, capped. Fixed per loop, so its gate never lies.</summary>
        public float LoopRequiredSpeed(float distance)
        {
            float kmh = Mathf.Min(settings.loopSpeedCapKmh,
                                  settings.loopSpeedFloorKmh + settings.loopSpeedRampKmhPer100m * distance / 100f);
            return kmh / 3.6f;
        }

        public PolicePatrol Patrol => patrol;
        /// <summary>The run's goal speed: the level's first mandatory Reach Speed objective, else the settings' fallback.</summary>
        public float LightSpeedKmh => level != null && level.LightSpeedKmh > 0f ? level.LightSpeedKmh : settings.lightSpeedKmh;
        public RunnerLevelDefinition Level => level;
        /// <summary>Ramps taken off from this run.</summary>
        public int JumpCount => jumpCount;
        public bool IsObjectiveDone(int index) => index >= 0 && index < objectiveDone.Length && objectiveDone[index];
        public bool IsChallengeDone(int index) => index >= 0 && index < challengeDone.Length && challengeDone[index];
        public float TimeLimit => settings.timeLimitSeconds;
        public float TimeRemaining { get; private set; }
        public bool HasWon { get; private set; }
        /// <summary>True from the frame the run ended until <see cref="Restart"/>.</summary>
        public bool RunOver { get; private set; }

        void Awake()
        {
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
            ResetObjectives();

            TimeRemaining = settings.timeLimitSeconds;
            if (motor != null) motor.PadImpulse += OnPadImpulse;
            SpeedPad.Collected += OnPadCollected;

            if (motor != null)
            {
                motor.ConfigureDash(settings);
                motor.WallHit += OnWallHit;
                motor.DashPerformed += OnDashPerformed;
                motor.TookOff += OnTookOff;
                motor.Landed += OnLanded;
                motor.LoopFailed += OnLoopFailed;

                // The DashMeterUI is a scene child of the Ship — it configures
                // itself off the motor in Start, after ConfigureDash above.
                if (settings.dashEnabled)
                {
                    var trail = motor.GetComponent<DashGhostTrail>();
                    if (trail == null) trail = motor.gameObject.AddComponent<DashGhostTrail>();
                    trail.Init(motor, settings);
                    // The airborne dash's wingtip ribbons — same lifetime as the ghosts.
                    var rollTrail = motor.GetComponent<BarrelRollTrail>();
                    if (rollTrail == null) rollTrail = motor.gameObject.AddComponent<BarrelRollTrail>();
                    rollTrail.Init(motor, settings);
                    dashPrompt = DashPromptController.Spawn(motor, settings);
                }
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

            // Above the pause menu's canvas and holding timeScale at 0, so the
            // scene boots to the attract screen with nothing running behind it.
            if (mainMenuOnBoot) MainMenuController.Spawn(motor, tuningScreen);

            // The patrol is a scene object now: wire it up here (its chase
            // tunables live on its PatrolDefinition asset), or park it when
            // the feature is off. A missing reference degrades to a chase-less
            // run instead of breaking the scene.
            if (settings.patrolEnabled && motor != null && patrol != null)
            {
                patrol.Init(motor);
                patrol.SetRedeployRule(settings.PatrolRedeployDistance, settings.PatrolRedeployGap,
                                       settings.patrolRedeploySpeedFactor);
                patrol.Redeployed += OnPatrolRedeployed;
                patrol.Warned += OnPatrolWarned;
                patrol.ProximityRumble = settings.patrolProximityRumble;
                ChaseMinimap.Spawn(motor, patrol, settings.minimapRangeMeters, patrol.Definition.warnDistance);
            }
            else
            {
                if (settings.patrolEnabled && motor != null)
                    Debug.LogError($"{nameof(GameManager)} has no {nameof(PolicePatrol)} scene reference — running without the chase.", this);
                if (patrol != null) patrol.gameObject.SetActive(false);
                patrol = null;
            }

            // The chase camera: the shared Cinemachine rig, attached to the ship
            // root with the ship's own settings asset (Far framing, target-up
            // roll binding). Without an asset the scene keeps its camera as is.
            if (motor != null && settings.cameraSettings != null)
                cameraRig = CameraRigInstaller.Attach(motor, settings.cameraSettings);

            // Weather rides with the camera and needs nothing from the run, so
            // it goes up before the menu — the debug page binds to the live
            // system the same way the patrol tab binds to the patrol. Apply,
            // not spawn: the scene's own RainSystem is the one the designer
            // tuned before play, and switching the weather off has to park it.
            RainSystem.Apply(settings.rainEnabled, settings.rainSettings);

            // Speed lines: the scene's hand-placed SpeedLines object (next to
            // the fog and the rain — tuned before play, never spawned here).
            // Apply finds it and parks it when off. The driver cannot see the
            // camera rig or the ship (FX does not reference Cameras), so it
            // takes the ship root as its focus, a km/h reader and Light Speed
            // as the reference its band is a fraction of; Update pushes the
            // camera mode each frame.
            speedLines = SpeedLines.Apply(settings.speedLinesEnabled, settings.speedLinesSettings);
            if (speedLines != null && motor != null)
                speedLines.SetTarget(motor.transform, () => motor.CurrentSpeed * 3.6f, LightSpeedKmh);

            // After the patrol init, so the debug menu's patrol tab can bind
            // to the live definition clone.
            PauseMenu.Spawn(this, motor);
        }

        void Update()
        {
            // Before the RunOver return: the lines stay up through the death
            // glitch, and the view can still be cycled on the result screen.
            if (speedLines != null && cameraRig != null) speedLines.SetCameraMode((int)cameraRig.Mode);

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
                PlayerStats.SampleShipSpeed(motor.CurrentSpeed * 3.6f);
            }

            // The win latches the moment every objective is met — but the run
            // does not end yet: the ship flies on until it is back on the
            // track, then FinishWin glitches out and raises the panel. From
            // here on nothing can be lost and the clock stands still.
            if (!HasWon && EvaluateObjectives(motor.CurrentSpeed * 3.6f))
            {
                HasWon = true;
                winRoutine = StartCoroutine(FinishWin());
            }
            if (HasWon)
            {
                UpdateLoops();
                return;
            }

            if (patrol != null && patrol.HasCaught)
            {
                HapticsSystem.Instance.Pulse(1f, 0.7f, 1.5f); // long busted rumble
                EndRun(RunOutcome.Caught);
                return;
            }

            if (motor.HasStopped)
            {
                EndRun(RunOutcome.Stalled);
                return;
            }

            UpdateLoops();

            // Time only pressures the player while the ship is flying.
            if (motor.Paused) return;

            TimeRemaining = Mathf.Max(0f, TimeRemaining - Time.deltaTime);
            if (TimeRemaining <= 0f)
                EndRun(RunOutcome.TimedOut);
        }

        void EndRun(RunOutcome outcome)
        {
            RunOver = true;
            motor.Paused = true; // freeze the sim; the hover keeps the ship floating

            // The record: an escape completed (and how long it took — the
            // timer only ran while flying, so this is launch-to-light-speed),
            // or a failed one; the patrol catching up is the runner's arrest.
            PlayerStats.RecordRunEnded(outcome == RunOutcome.Escaped, settings.timeLimitSeconds - TimeRemaining);
            if (outcome == RunOutcome.Caught) PlayerStats.RecordArrest();
            PlayerProfileStore.SaveIfDirty();

            // Straight to the panel, this frame. Both panels freeze the clock
            // and the RPG box types on scaled time, so a story line still up
            // (an orb hype line, a patrol taunt) would sit frozen under the
            // panel — drop it, and its callback with it.
            RpgMessageSystem.Instance.ClearMessages();
            if (HasWon) ShowMissionComplete();
            else ShowGameOver(outcome);
        }

        /// <summary>
        /// The win's wind-down, on unscaled time: wait for the ship to be
        /// back on the track surface (a jump, a loop, a loop fall or a tube
        /// plays out first — and a ramp it is already committed to is taken,
        /// not frozen on), ramp the glitch from wherever it is to max over
        /// <c>winGlitchRampSeconds</c>, hold it <c>winGlitchHoldSeconds</c>,
        /// then end the run — which opens the panel behind the full glitch,
        /// the city handoff's picture. The controller's fade is remembered
        /// and handed back once the panel is up, so the glitch clears behind
        /// the results the way it clears behind the runner's first frames.
        /// </summary>
        IEnumerator FinishWin()
        {
            while (motor != null && (motor.State != ShipState.Grounded || motor.CurrentRamp != null))
                yield return null;

            GlitchController glitch = GlitchController.Instance;
            if (glitch != null)
            {
                glitchFadeBeforeWin = glitch.baseFadePerSecond;
                glitch.baseFadePerSecond = 0f; // healing stops: the ramp must reach and hold max
                float from = glitch.baseIntensity;
                float ramp = Mathf.Max(0.01f, settings.winGlitchRampSeconds);
                for (float t = 0f; t < ramp; t += Time.unscaledDeltaTime)
                {
                    glitch.SetBaseIntensity(Mathf.Lerp(from, 1f, t / ramp));
                    yield return null;
                }
                glitch.SetBaseIntensity(1f);
                glitch.Pulse(1f);
            }

            if (settings.winGlitchHoldSeconds > 0f)
                yield return new WaitForSecondsRealtime(settings.winGlitchHoldSeconds);

            winRoutine = null;
            EndRun(RunOutcome.Escaped);
            RestoreGlitchFade();
        }

        // Hands the GlitchController its fade rate back so a held max decays
        // again (behind the panel, or on a retry). Idempotent.
        void RestoreGlitchFade()
        {
            if (glitchFadeBeforeWin < 0f) return;
            GlitchController glitch = GlitchController.Instance;
            if (glitch != null) glitch.baseFadePerSecond = glitchFadeBeforeWin;
            glitchFadeBeforeWin = -1f;
        }

        /// <summary>
        /// The retry panel on the shared screen: GAME OVER, the reason this
        /// run ended, then RETRY (runs the track again, in place — no load)
        /// and EXIT TO MAIN MENU (under the loading curtain). It is the same
        /// screen the city chase raises, so both games answer death the same
        /// way; the city's has no reason line.
        /// </summary>
        void ShowGameOver(RunOutcome outcome)
        {
            MenuTextId reason = outcome switch
            {
                RunOutcome.Caught => MenuTextId.LoseCaught,
                RunOutcome.TimedOut => MenuTextId.LoseTimeOut,
                _ => MenuTextId.LoseStalled
            };
            GameOverScreen.Show(reason, onRetry: Restart, onGiveUp: LoadingScreen.LoadMainMenu);
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
        string NextSceneAfterMission() => MissionSession.Active ? StoreSettings.SceneName : level.nextSceneName;

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

        // Story beat: hype line every time the rare orb tier is grabbed.
        void OnPadCollected(SpeedPad pad, ShipMotor collector)
        {
            if (collector != motor || RunOver) return;
            PlayerStats.RecordPad(pad.SpeedDelta > 0f); // positive = power-up, negative = slow-down
            if (!string.IsNullOrEmpty(settings.messageOrbTierName) && pad.TierName == settings.messageOrbTierName)
                RpgMessageSystem.Instance.ShowMessage(
                    "PILOT", settings.purpleOrbMessage, settings.messageHoldSeconds, settings.pilotMessageColor);
        }

        // Story beat: the fresh patrol announces itself — a dialogue line, not
        // a floating text, and only when GameSettings asks for it (the minimap
        // and the rumble already show it cutting in).
        void OnPatrolRedeployed(int patrolNumber)
        {
            if (RunOver || !settings.showPatrolAlert) return;
            RpgMessageSystem.Instance.ShowMessage(
                "PATROL", string.Format(settings.patrolInboundMessage, patrolNumber),
                settings.messageHoldSeconds, settings.patrolMessageColor);
        }

        // Story beat: the patrol taunts as it closes in — once per approach,
        // and never queued behind a line already up (a stale gap would lie).
        void OnPatrolWarned(float gap)
        {
            if (RunOver || !settings.showPatrolWarnings || RpgMessageSystem.Instance.IsBusy) return;
            RpgMessageSystem.Instance.ShowMessage(
                "PATROL", string.Format(settings.patrolWarningMessage, Mathf.RoundToInt(gap)),
                settings.messageHoldSeconds, settings.patrolMessageColor);
        }

        // Haptics: a snappy buzz for boosts, a heavier thud for brakes.
        void OnPadImpulse(float rawMagnitude)
        {
            if (rawMagnitude > 0f) HapticsSystem.Instance.Pulse(0.15f, 0.55f, 0.15f);
            else HapticsSystem.Instance.Pulse(0.65f, 0.2f, 0.25f);

            // A burst of speed lines per boost, scaled by the orb's tier
            // (rawMagnitude is tier × powerUpSpeedBoost; a ramp takeoff's
            // boost comes through the same event and gets one too).
            if (rawMagnitude > 0f && speedLines != null)
                speedLines.Pulse(settings.boostPulseStrength * rawMagnitude / Mathf.Max(0.01f, settings.powerUpSpeedBoost),
                                 settings.boostPulseSeconds);
        }

        // Dash feel: a short kick in the hands.
        void OnDashPerformed(int direction)
        {
            HapticsSystem.Instance.Pulse(0.3f, 0.6f, 0.12f);
        }

        // A jump: the camera pulls out to the Far framing for the arc and
        // hands the player's view back on landing (a no-op if it was Far).
        // The cycle is locked meanwhile — ShipMotor.BlockModeCycle.
        void OnTookOff()
        {
            if (!RunOver) jumpCount++; // the Jump X Times goals count takeoffs
            if (cameraRig == null) return;
            modeBeforeJump = cameraRig.Mode;
            cameraRig.SetMode(CameraMode.Far, instant: false);
        }

        /// <summary>
        /// Loop gates and labels: every live loop's gate is tinted against the
        /// ship's speed, and its required-speed number (fixed above the mouth)
        /// is shown once the loop comes inside its definition's label lead and
        /// hidden again once the ship has gone through the gate.
        /// </summary>
        void UpdateLoops()
        {
            float speed = motor.CurrentSpeed;
            float d = motor.DistanceTravelled;
            foreach (var loop in Track.Features.LoopFeature.Active)
            {
                if (loop == null) continue;
                loop.SetGateColor(speed >= loop.RequiredSpeed);

                float gap = loop.StartDistance - d;
                float lead = loop.Definition != null ? loop.Definition.labelLeadMeters : 0f;
                loop.SetLabelVisible(gap >= 0f && gap <= lead);
            }
        }

        // Too slow for the loop: the drop off the top is a hit of corruption
        // and a long rumble; the landing below rides the ordinary Landed path.
        void OnLoopFailed()
        {
            HapticsSystem.Instance.Pulse(0.9f, 0.5f, 0.5f);
            if (GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.loopFallGlitchStrength);
        }

        // Touchdown: a thump in the hands and on the picture, a spray of
        // sparkles at the touchdown point, no speed change.
        void OnLanded()
        {
            HapticsSystem.Instance.Pulse(0.5f, 0.3f, 0.2f);
            CameraShake.Shake(settings.landingShake);
            SparkleVfx.SpawnBurst(motor.transform.position, motor.transform.up,
                                  settings.landingSparkleColor, settings.landingSparkleScale,
                                  settings.landingSparkleCount);
            if (cameraRig != null && modeBeforeJump != CameraMode.Far)
                cameraRig.SetMode(modeBeforeJump, instant: false);
        }

        // Dash into the wall, or a ramp hit from the side: a thud in the
        // hands, a burst of signal corruption and a kick on the picture.
        void OnWallHit(float impactSpeed)
        {
            HapticsSystem.Instance.Pulse(0.8f, 0.4f, 0.2f);
            CameraShake.Shake(settings.wallHitShake);
            if (GlitchController.Instance != null)
                GlitchController.Instance.Pulse(settings.dashWallGlitchStrength);
        }

        void OnDestroy()
        {
            if (motor != null)
            {
                motor.PadImpulse -= OnPadImpulse;
                motor.WallHit -= OnWallHit;
                motor.DashPerformed -= OnDashPerformed;
                motor.TookOff -= OnTookOff;
                motor.Landed -= OnLanded;
                motor.LoopFailed -= OnLoopFailed;
            }
            if (patrol != null)
            {
                patrol.Redeployed -= OnPatrolRedeployed;
                patrol.Warned -= OnPatrolWarned;
            }
            SpeedPad.Collected -= OnPadCollected;
        }

        /// <summary>Resets the run; rebuilds the track (endless runs must — the stretch behind the start was culled).</summary>
        public void Restart()
        {
            // Drop any story line still queued (and its onFinished) so nothing
            // from the old run lands on the new one.
            RpgMessageSystem.Instance.ClearMessages();
            if (dashPrompt != null) dashPrompt.ResetForRun();
            if (speedLines != null) speedLines.ClearPulse(); // the speed term follows the relaunch on its own

            // A retry from the panel: the wind-down is over, but a RETRY pressed
            // while a glitch is still decaying must start on a clean picture.
            if (winRoutine != null) { StopCoroutine(winRoutine); winRoutine = null; }
            RestoreGlitchFade();
            if (GlitchController.Instance != null) GlitchController.Instance.SetBaseIntensity(0f);

            RunOver = false;
            HasWon = false;
            runCounted = false;
            TimeRemaining = settings.timeLimitSeconds;
            ResetObjectives();
            // This run's money counter goes back to $0; what was picked up is
            // already banked in the profile.
            CollectibleManager collectibles = CollectibleManager.Instance;
            if (collectibles != null) collectibles.ResetRun();
            if (generator != null) generator.RegenerateForRun();
            motor.Paused = false; // EndRun froze the sim; the tuning screen re-pauses if present
            motor.Launch();
            if (patrol != null) patrol.Launch();

            // Reopen ship setup so points can be re-allocated; it re-launches on START.
            if (tuningScreen != null) tuningScreen.Show();
        }
    }
}
