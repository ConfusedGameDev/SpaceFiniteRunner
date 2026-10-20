using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Contracts;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.UI;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The held boost: while Boost (<see cref="GameAction.ShipBoost"/>, A /
    /// Space) is held and the meter has charge, the ship takes a steady
    /// <see cref="BoostMeterSettings.potenceKmhPerSecond"/> of extra thrust and
    /// the meter drains. The rules it enforces:
    /// <list type="bullet">
    /// <item><b>Empty is empty.</b> At 0 a press does nothing but rumble
    /// (<see cref="BoostMeterSettings.emptyPressRumble"/>).</item>
    /// <item><b>The refill waits for the hands to leave the button.</b>
    /// Holding or pressing Boost — an empty press too — restarts the
    /// <see cref="BoostMeterSettings.cooldownSeconds"/> countdown; only once
    /// it runs out does the meter refill.</item>
    /// <item><b>The push is quiet.</b> It goes through
    /// <see cref="ShipMotor.AddBoostThrust"/>, not an orb's impulse: no weight
    /// scaling, no <c>PadImpulse</c> — so no per-frame rumble or speed-line
    /// pulse, and the patrol takes no share of it. Above cruise the passive
    /// bleed keeps pulling, and takes the speed back once Boost is let go.</item>
    /// <item><b>Only the player's own ship boosts</b>: never while paused, on
    /// autopilot (the duel, hyperspace, a kill cam), with the dash locked or
    /// suppressed, out of play, off the end of the track, or once the run is
    /// ending. A held button stays held through those, and drains nothing.</item>
    /// <item><b>A belongs to the boost over live gameplay</b>: the dialogue box
    /// stops reading it while the boost can act
    /// (<see cref="MenuNavigator.DialogueAdvanceSuppressed"/>); Enter still
    /// advances a page, and pages hold on their own.</item>
    /// </list>
    /// Hand-placed on <c>PF_Ship</c> beside the <see cref="ShipMotor"/>; every
    /// knob is on its own <see cref="BoostMeterSettings"/> asset. The HUD reads
    /// it through <see cref="IBoostMeter"/>. With no asset it idles.
    /// </summary>
    [RequireComponent(typeof(ShipMotor))]
    [DisallowMultipleComponent]
    public class BoostMeter : MonoBehaviour, IBoostMeter
    {
        [InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        [SerializeField] BoostMeterSettings settings;

        ShipMotor motor;
        IRunState run;
        float cooldownLeft;
        bool suppressingDialogue; // we set DialogueAdvanceSuppressed last, so we must clear it

        /// <summary>The live meter, for the debug page. Null when no ship carries one.</summary>
        public static BoostMeter Live { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Live = null; // domain reload is off

        /// <summary>The asset this meter reads (the debug page edits it).</summary>
        public BoostMeterSettings Settings => settings;

        public bool MeterEnabled => settings != null && settings.meterEnabled;
        public float Fraction { get; private set; } = 1f;
        public bool IsBoosting { get; private set; }
        public bool IsRecovering => !IsBoosting && Fraction < 1f;
        public int EmptyPresses { get; private set; }

        void Awake() => motor = GetComponent<ShipMotor>();

        void OnEnable() => Live = this;

        void OnDisable()
        {
            if (Live == this) Live = null;
            IsBoosting = false;
            ReleaseDialogue();
        }

        /// <summary>A fresh run: full meter, no cooldown.</summary>
        public void ResetMeter()
        {
            Fraction = 1f;
            cooldownLeft = 0f;
            IsBoosting = false;
        }

        // The boost may act: the player is flying their own ship in a live run.
        bool CanAct
        {
            get
            {
                if (motor == null || motor.Paused || Time.timeScale <= 0f) return false;
                if (motor.Autopilot || motor.DashLocked || motor.DashSuppressed) return false;
                if (motor.State is ShipState.OffTrack or ShipState.Respawning or ShipState.Falling) return false;
                if (motor.HasLeftTrackEnd) return false;
                run ??= ContractLookup.Find<IRunState>();
                return run == null || !run.IsEnding;
            }
        }

        void Update()
        {
            if (!MeterEnabled)
            {
                IsBoosting = false;
                ReleaseDialogue();
                return;
            }

            bool canAct = CanAct;
            SetDialogueSuppressed(canAct);
            if (Time.timeScale <= 0f) return; // a menu is open: hold everything where it is

            bool held = ControlBindings.IsPressed(GameAction.ShipBoost);
            bool pressed = ControlBindings.WasPressedThisFrame(GameAction.ShipBoost);
            float dt = Time.deltaTime;

            if (held || pressed) cooldownLeft = settings.cooldownSeconds;
            else cooldownLeft = Mathf.Max(0f, cooldownLeft - dt);

            if (pressed && canAct && Fraction <= 0f)
            {
                EmptyPresses++;
                HapticsSystem.Instance.Pulse(settings.emptyPressRumble);
            }

            IsBoosting = held && canAct && Fraction > 0f;
            if (IsBoosting) Fraction = Mathf.Max(0f, Fraction - settings.drainPerSecond * dt);
            else if (!held && cooldownLeft <= 0f) Fraction = Mathf.Min(1f, Fraction + settings.refillPerSecond * dt);
        }

        void FixedUpdate()
        {
            if (!IsBoosting || motor == null) return;
            motor.AddBoostThrust(settings.potenceKmhPerSecond / BoostMeterSettings.KmhPerMs * Time.fixedDeltaTime);
        }

        void SetDialogueSuppressed(bool suppressed)
        {
            if (suppressed == suppressingDialogue) return;
            suppressingDialogue = suppressed;
            MenuNavigator.DialogueAdvanceSuppressed = suppressed;
        }

        void ReleaseDialogue() => SetDialogueSuppressed(false);
    }
}
