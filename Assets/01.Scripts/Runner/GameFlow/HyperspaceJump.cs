using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// The hyperspace jump — the shortcut to the ending once the run is won
    /// in all but distance. While the ship holds Light Speed
    /// (<see cref="RunFeedback.AtLightSpeed"/>, the state the hyperspace sky
    /// follows) with every objective met, a counter runs; at
    /// <see cref="GameSettings.hyperspacePromptDelaySeconds"/> the
    /// <see cref="HyperspacePrompt"/> comes up (HIT HYPERSPACE, LB + RB / Q + E)
    /// and the dash is suppressed — the chord IS the two dash shoulders.
    /// Losing Light Speed hides it and starts the count over. The chord
    /// (<see cref="Engage"/>) brings the end in: the generator forces the
    /// final run-up and its three ramps where the road currently stops
    /// (<see cref="TrackGenerator.ForceEndAhead"/> — never inside road that is
    /// already built), the patrol is held where it is (the chase is over),
    /// and the autopilot lines the ship up on the nearest end ramp until it
    /// leaves the track, where the ordinary win takes over. While
    /// <see cref="Jumping"/> the run CANNOT be lost: the GameManager holds the
    /// countdown, ignores laser hits and catches, shields the hull, refuses
    /// every loss and counts the end of the track as the win.
    /// Hand-placed beside the GameManager in <c>PF_Systems</c>, bound in its
    /// Awake; off (<see cref="GameSettings.hyperspaceJumpEnabled"/>) it never
    /// shows anything.
    /// </summary>
    public class HyperspaceJump : MonoBehaviour
    {
        ShipMotor motor;
        TrackGenerator generator;
        PolicePatrol patrol;
        GameSettings settings;
        IRunState run;
        RunFeedback feedback;
        HyperspacePrompt prompt;

        float targetLateral;

        /// <summary>Seconds held at Light Speed toward the prompt (0 while it is not counting).</summary>
        [ShowInInspector, ReadOnly]
        public float LightSpeedSeconds { get; private set; }

        /// <summary>True while HIT HYPERSPACE is up and the chord is live.</summary>
        [ShowInInspector, ReadOnly]
        public bool PromptUp { get; private set; }

        /// <summary>True from the chord until the run restarts: the end is coming in and the autopilot flies the ship onto a ramp.</summary>
        [ShowInInspector, ReadOnly]
        public bool Jumping { get; private set; }

        /// <summary>The scene's jump (hand-placed beside the GameManager); added only when the scene has none.</summary>
        public static HyperspaceJump Ensure(Component host)
        {
            var jump = host.GetComponent<HyperspaceJump>();
            return jump != null ? jump : host.gameObject.AddComponent<HyperspaceJump>();
        }

        /// <summary>Binds the jump to the run. A null patrol simply has nothing to switch off; a null prompt jumps without a picture.</summary>
        public void Bind(ShipMotor ship, TrackGenerator trackGenerator, PolicePatrol chaser, GameSettings runSettings,
                         IRunState runState, RunFeedback runFeedback, HyperspacePrompt hyperspacePrompt)
        {
            motor = ship;
            generator = trackGenerator;
            patrol = chaser;
            settings = runSettings;
            run = runState;
            feedback = runFeedback;
            prompt = hyperspacePrompt;
            ResetForRun();
        }

        /// <summary>A retry: no count, no prompt, the dash and the duel handed back.</summary>
        public void ResetForRun()
        {
            LightSpeedSeconds = 0f;
            HidePrompt();
            if (prompt != null) prompt.ResetForRun();
            Jumping = false; // the patrol's hold is released by its own Launch
        }

        /// <summary>
        /// The chord: brings the end of the track in and flies the ship onto
        /// the nearest end ramp. Refused unless the prompt is up (the Odin
        /// button forces it, for testing).
        /// </summary>
        [Button("Engage (test)"), EnableIf("@UnityEngine.Application.isPlaying")]
        public void Engage() => Engage(force: true);

        void Engage(bool force)
        {
            if (Jumping || motor == null || generator == null || run == null) return;
            if (!force && !PromptUp) return;
            if (run.IsEnding || run.RunOver || !generator.IsFinite || motor.HasLeftTrackEnd) return;

            HidePrompt();
            Jumping = true;

            // Shielded from this very frame (the GameManager re-asserts it
            // every Update, which may run before or after this one).
            var hull = motor.GetComponent<ShipHealth>();
            if (hull != null) hull.Shielded = true;

            // The chase is over: the patrol is HELD where it is — it stops
            // moving, catching and attacking, and its duel lock lets go of the
            // controls. (Switching the duel off instead would bring back the
            // old proximity arrest — the cruiser alongside arrested the ship.)
            if (patrol != null)
            {
                patrol.AbortEncounter();
                patrol.SetHold(true);
            }

            // The end, where the road stops now (false = the real end is
            // already that close — then the autopilot just flies onto it).
            generator.ForceEndAhead(settings.hyperspaceRunUpMeters);

            // The ramp is chosen once: the one whose centre is nearest, so the
            // line never flips between two ramps as the ship crosses the gap.
            float[] laterals = generator.EndRampLaterals();
            targetLateral = laterals[0];
            foreach (float lateral in laterals)
                if (Mathf.Abs(lateral - motor.LateralOffset) < Mathf.Abs(targetLateral - motor.LateralOffset))
                    targetLateral = lateral;
            FlyToRamp();

            // The fire goes down on the road from here: two burning lines up
            // to the ramp, and on into the air after it (the ship's exit).
            var exit = motor.GetComponent<EscapeVanish>();
            if (exit != null) exit.Ignite();

            HapticsSystem.Instance.Pulse(settings.escapeRumble);
            if (GlitchController.Instance != null) GlitchController.Instance.Pulse(settings.escapeFlashGlitch);
        }

        void Update()
        {
            if (motor == null || run == null || settings == null) return;

            if (Jumping)
            {
                // Re-asserted every frame until the ship is off the end: the
                // autopilot is shared state, and nothing else may drop it.
                if (!motor.HasLeftTrackEnd && !run.RunOver) FlyToRamp();
                return;
            }

            bool counting = settings.hyperspaceJumpEnabled
                            && feedback != null && feedback.AtLightSpeed
                            && run.ObjectivesMet && !run.IsEnding && !run.RunOver
                            && !motor.Paused && !motor.HasLeftTrackEnd
                            && generator != null && generator.IsFinite;
            if (!counting)
            {
                LightSpeedSeconds = 0f;
                HidePrompt();
                return;
            }

            LightSpeedSeconds += Time.deltaTime;
            if (LightSpeedSeconds < settings.hyperspacePromptDelaySeconds) return;

            ShowPrompt();
            if (ChordPressed()) Engage(force: false);
        }

        // Both halves of a chord down, the second landing this frame: the two
        // dash shoulders (the pad's LB + RB — their keys N + M count too) or
        // the two hyperspace keys (Q + E). Through the binding table, so a
        // rebind moves the chord with it.
        static bool ChordPressed() =>
            Chord(GameAction.ShipDashLeft, GameAction.ShipDashRight)
            || Chord(GameAction.ShipHyperspaceLeft, GameAction.ShipHyperspaceRight);

        static bool Chord(GameAction a, GameAction b) =>
            ControlBindings.IsPressed(a) && ControlBindings.IsPressed(b)
            && (ControlBindings.WasPressedThisFrame(a) || ControlBindings.WasPressedThisFrame(b));

        void FlyToRamp()
        {
            motor.Autopilot = true;
            motor.AutopilotLateral = targetLateral;
        }

        void ShowPrompt()
        {
            if (PromptUp) return;
            PromptUp = true;
            motor.DashSuppressed = true;
            if (prompt != null) prompt.SetVisible(true);
        }

        void HidePrompt()
        {
            if (!PromptUp) return;
            PromptUp = false;
            if (motor != null)
            {
                motor.DashSuppressed = false;
                // A shoulder pressed while the prompt was up latched a dash
                // request: it must not fire the moment the gate opens.
                motor.ConsumeDashRequest();
            }
            if (prompt != null) prompt.SetVisible(false);
        }

        void OnDisable()
        {
            HidePrompt();
        }
    }
}
