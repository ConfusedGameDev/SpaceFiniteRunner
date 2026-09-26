using UnityEngine;

using ConfusedGameDev.FiniteRunner.HUD;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Simulation;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// The timed boost: press Boost (<see cref="GameAction.ShipBoost"/>, A /
    /// Space) as the ship crosses a boost orb and its boost is multiplied by
    /// how close the press was. The rules it enforces:
    /// <list type="bullet">
    /// <item><b>Taking an orb without a press is unchanged</b>: the plain
    /// boost, the plain warp and rumble.</item>
    /// <item><b>Graded in TIME, not metres</b>: the offset is the orb's
    /// distance from the ship over the ship's speed, so the window feels the
    /// same at cruise and at Light Speed. Within
    /// <see cref="GameSettings.boostQtePerfectSeconds"/> of the crossing is
    /// PERFECT (the top of <see cref="GameSettings.boostQteMultiplierBand"/>);
    /// out to <see cref="GameSettings.boostQteWindowSeconds"/> either side it
    /// falls to the bottom of the band; beyond is a miss.</item>
    /// <item><b>Early presses are banked, late presses top up.</b> A press
    /// before the orb is taken is applied by <see cref="SpeedPad.Collect"/> as
    /// ONE multiplied impulse (so speed lines, the "+N", the audio and the
    /// patrol's share all scale with it); a press after is a second impulse of
    /// the missing share.</item>
    /// <item><b>One press per orb.</b> A press while the prompt is up but
    /// outside the window is a miss and locks the orb, so mashing never wins;
    /// the window closing unpressed is a miss too. An orb the ship steers past
    /// drops its prompt without a verdict.</item>
    /// <item><b>Feedback scales with the grade</b> through
    /// <see cref="FeedbackScale"/>: set only around a graded impulse, 0
    /// otherwise, read by the warp (<c>PadEffects</c>) and the rumble
    /// (<c>GameManager.OnPadImpulse</c>) inside the impulse's event.</item>
    /// <item><b>A belongs to the boost while the prompt is up</b>: the
    /// dialogue box stops reading it (<see cref="MenuNavigator.DialogueAdvanceSuppressed"/>).</item>
    /// </list>
    /// Spawned by the GameManager; the picture is <see cref="BoostQtePrompt"/>.
    /// </summary>
    /// <summary>How a boost-orb press landed. Not serialized.</summary>
    public enum BoostQteResult
    {
        /// <summary>Pressed before the window opened.</summary>
        TooFast,
        /// <summary>Pressed after the window closed, or never pressed on an orb that was taken.</summary>
        TooLate,
        /// <summary>Inside the window, outside the perfect band.</summary>
        Sweet,
        /// <summary>Inside the perfect band.</summary>
        Perfect
    }

    /// <summary>
    /// One verdict of the boost QTE: the result, the multiplier it gave (1 on
    /// a miss), the colour the prompt shows, and whether the player pressed at
    /// all — an orb taken unpressed is a TooLate the prompt shows in red but
    /// the HUD's label stays quiet about.
    /// </summary>
    public readonly struct BoostQteVerdict
    {
        public readonly BoostQteResult Result;
        public readonly float Multiplier;
        public readonly Color Color;
        public readonly bool Pressed;

        public BoostQteVerdict(BoostQteResult result, float multiplier, Color color, bool pressed)
        {
            Result = result;
            Multiplier = multiplier;
            Color = color;
            Pressed = pressed;
        }

        public bool IsMiss => Result is BoostQteResult.TooFast or BoostQteResult.TooLate;
    }

    public class BoostQte : MonoBehaviour
    {
        /// <summary>The live controller, or null when the QTE is off. Cleared on disable (domain reload is off).</summary>
        public static BoostQte Instance { get; private set; }

        /// <summary>
        /// 0..1 strength of the boost impulse being raised right now: 0 for a
        /// plain one, 1 for a perfect press. Valid only inside a
        /// <c>PadImpulse</c> handler.
        /// </summary>
        public static float FeedbackScale { get; private set; }

        /// <summary>The warp kick's peak scale for the impulse being raised: 1 plain, up to <see cref="GameSettings.boostQteWarpAtPerfect"/>.</summary>
        public static float WarpScale =>
            Instance != null && Instance.settings != null ? 1f + FeedbackScale * (Instance.settings.boostQteWarpAtPerfect - 1f) : 1f;

        /// <summary>Raised on every verdict of the player's press: the orb and the verdict (the HUD's result label).</summary>
        public static event System.Action<SpeedPad, BoostQteVerdict> Graded;

        ShipMotor motor;
        GameSettings settings;
        BoostQtePrompt prompt;

        // The orb the prompt is on, and what has happened to it.
        SpeedPad target;
        Transform targetRing;
        Vector3 lastRingPosition;
        float lastRingSize;
        bool pressed;          // one press per orb
        float bankedMultiplier; // an early press, waiting for the collect (1 = none)
        bool collected;

        public static BoostQte Spawn(ShipMotor motor, GameSettings settings)
        {
            var qte = FindFirstObjectByType<BoostQte>();
            if (qte == null) qte = new GameObject("BoostQte").AddComponent<BoostQte>();
            qte.motor = motor;
            qte.settings = settings;
            if (qte.prompt == null) qte.prompt = BoostQtePrompt.Spawn();
            return qte;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            FeedbackScale = 0f;
            Graded = null;
        }

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            FeedbackScale = 0f;
            MenuNavigator.DialogueAdvanceSuppressed = false;
            Clear();
        }

        bool Enabled => settings != null && settings.boostQte && motor != null;

        void Update()
        {
            if (!Enabled || Time.timeScale <= 0f)
            {
                MenuNavigator.DialogueAdvanceSuppressed = false;
                return;
            }

            if (target != null && !collected && !target.Available)
            {
                prompt.Drop(); // gone without the player taking it (the patrol took it)
                Clear();
            }
            if (target == null) Retarget();

            if (target != null)
            {
                float dt = SecondsToCrossing(target);
                UpdateRing();

                if (!pressed && CanAct && ControlBindings.WasPressedThisFrame(GameAction.ShipBoost))
                    Press(dt);

                if (!pressed && dt < -settings.boostQteWindowSeconds)
                {
                    // The window closed. Taken unpressed = a miss; never taken = no verdict.
                    if (collected) Verdict(1f, BoostQteResult.TooLate, false);
                    else prompt.Drop();
                    Clear();
                }
                else if (pressed && (collected || dt < -settings.boostQteWindowSeconds))
                {
                    Clear(); // verdict shown; the prompt holds it on its own
                }
                else if (!pressed)
                {
                    float show = Mathf.Max(0.01f, settings.boostQteShowSeconds);
                    prompt.Track(lastRingPosition, lastRingSize, 1f - Mathf.Clamp01(dt / show));
                }
                else
                {
                    prompt.Follow(lastRingPosition, lastRingSize);
                }
            }

            MenuNavigator.DialogueAdvanceSuppressed = prompt != null && prompt.Waiting;
        }

        // The player can't boost while falling, respawning, or with the patrol holding the controls.
        bool CanAct => motor.State != ShipState.OffTrack && motor.State != ShipState.Respawning && !motor.DashLocked;

        // The nearest boost orb ahead inside the show window.
        void Retarget()
        {
            Clear();
            float show = settings.boostQteShowSeconds;
            SpeedPad best = null;
            float bestDt = float.MaxValue;
            var all = PickupRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is not SpeedPad pad || !pad.IsBoostOrb || !pad.Available) continue;
                float dt = SecondsToCrossing(pad);
                if (dt <= 0f || dt > show || dt >= bestDt) continue;
                best = pad;
                bestDt = dt;
            }
            if (best == null || !CanAct) return;

            target = best;
            targetRing = best.transform.Find("Indicator");
            bankedMultiplier = 1f;
            UpdateRing();
        }

        void Clear()
        {
            target = null;
            targetRing = null;
            pressed = false;
            collected = false;
            bankedMultiplier = 1f;
        }

        // Seconds until the ship crosses the orb's centre (negative after it).
        float SecondsToCrossing(SpeedPad pad) =>
            (pad.TrackDistance - motor.DistanceTravelled) / Mathf.Max(1f, motor.CurrentSpeed);

        // The ring's centre and width in the world, remembered for after the orb is gone.
        void UpdateRing()
        {
            if (target == null || !target.gameObject.activeInHierarchy) return;
            Transform ring = targetRing != null ? targetRing : target.transform;
            var filter = ring.GetComponentInChildren<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Bounds b = filter.sharedMesh.bounds;
                Transform t = filter.transform;
                lastRingPosition = t.TransformPoint(b.center);
                lastRingSize = b.size.x * Mathf.Abs(t.lossyScale.x) * settings.boostQteGlyphSize;
            }
            else
            {
                lastRingPosition = ring.position;
                lastRingSize = Mathf.Abs(ring.lossyScale.x) * settings.boostQteGlyphSize;
            }
        }

        // Grade a press at `dt` seconds from the crossing.
        void Press(float dt)
        {
            pressed = true;
            float offset = Mathf.Abs(dt);
            float window = Mathf.Max(0.01f, settings.boostQteWindowSeconds);
            if (offset > window)
            {
                Verdict(1f, dt > 0f ? BoostQteResult.TooFast : BoostQteResult.TooLate);
                return;
            }

            bool perfect = offset <= settings.boostQtePerfectSeconds;
            float falloff = perfect ? 0f : Mathf.Clamp01(settings.boostQteFalloff.Evaluate(offset / window));
            float multiplier = Mathf.Lerp(settings.BoostQteMaxMultiplier, settings.BoostQteMinMultiplier, falloff);

            if (!collected)
            {
                bankedMultiplier = multiplier; // SpeedPad.Collect applies it
                Verdict(multiplier, perfect ? BoostQteResult.Perfect : BoostQteResult.Sweet);
                return;
            }

            // Late: the orb already gave its plain boost — top it up.
            FeedbackScale = Strength(multiplier);
            motor.AddSpeedImpulse(target.SpeedDelta * (multiplier - 1f));
            FeedbackScale = 0f;
            Verdict(multiplier, perfect ? BoostQteResult.Perfect : BoostQteResult.Sweet);
        }

        /// <summary>
        /// Called by <see cref="SpeedPad.Collect"/> for a boost orb, before its
        /// impulse: returns the multiplier an early press banked (1 = none) and
        /// sets <see cref="FeedbackScale"/> for the impulse's event.
        /// </summary>
        public float OnOrbCollected(SpeedPad pad)
        {
            FeedbackScale = 0f;
            if (!Enabled || pad != target) return 1f;
            collected = true;
            UpdateRing();
            float multiplier = pressed ? bankedMultiplier : 1f;
            FeedbackScale = Strength(multiplier);
            return multiplier;
        }

        /// <summary>Called by <see cref="SpeedPad.Collect"/> after the impulse and its events.</summary>
        public static void EndImpulse() => FeedbackScale = 0f;

        // Multiplier → 0..1 over (1, band top], so even the window's edge reads a little stronger than no press.
        float Strength(float multiplier) =>
            Mathf.Clamp01((multiplier - 1f) / Mathf.Max(0.01f, settings.BoostQteMaxMultiplier - 1f));

        void Verdict(float multiplier, BoostQteResult result, bool pressedButton = true)
        {
            bool perfect = result == BoostQteResult.Perfect;
            Color color;
            if (result is BoostQteResult.TooFast or BoostQteResult.TooLate) color = settings.boostQteMissColor;
            else if (perfect) color = settings.boostQtePerfectColor;
            else
            {
                // Red → yellow → green across the band.
                float band = Mathf.Max(0.01f, settings.BoostQteMaxMultiplier - settings.BoostQteMinMultiplier);
                float g = Mathf.Clamp01((multiplier - settings.BoostQteMinMultiplier) / band);
                color = g < 0.5f ? Color.Lerp(settings.boostQteMissColor, settings.boostQteMidColor, g * 2f)
                                 : Color.Lerp(settings.boostQteMidColor, settings.boostQtePerfectColor, (g - 0.5f) * 2f);
            }
            prompt.Follow(lastRingPosition, lastRingSize);
            prompt.Resolve(color, perfect);
            Graded?.Invoke(target, new BoostQteVerdict(result, multiplier, color, pressedButton));
        }
    }
}
