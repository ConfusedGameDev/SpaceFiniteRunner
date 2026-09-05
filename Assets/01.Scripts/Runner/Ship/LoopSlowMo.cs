using ConfusedGameDev.FiniteRunner.GameFlow;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Loop slow motion for the ship: while it is inside a loop — Looping,
    /// or Falling off the top of one it was too slow for — the world clock
    /// eases down to whatever scale shows the ship at
    /// <see cref="GameSettings.loopApparentSpeedKmh"/> (the faster the
    /// entry, the deeper the slow-mo, between <see cref="GameSettings.loopMinTimeScale"/>
    /// and <see cref="GameSettings.loopTimeScale"/>) and eases back
    /// to 1 once it is back on the track. The whole runner rides
    /// <c>Time.deltaTime</c> (ship, patrol, countdown), so nothing changes
    /// hands: the loop simply plays longer in real time. Added to the ship
    /// by the GameManager, which also hands it the settings asset — read
    /// live every frame, so the sliders apply mid-loop.
    ///
    /// <c>Time.timeScale</c> has other owners — every menu writes 0 on open
    /// and exactly 1 on close (<c>PauseMenu</c>, <c>GameOverScreen</c>,
    /// <c>MissionCompleteScreen</c>, <c>MainMenuController</c>) and none of
    /// them touch <c>fixedDeltaTime</c> — so this component follows the same
    /// ownership rule as the city's <c>AirTimeSlowMo</c>: it only ENTERS when
    /// the clock reads exactly 1, it remembers the value it last wrote and
    /// CANCELS silently (restoring the fixed step only) the moment the clock
    /// reads anything else, and it never writes the clock again until it
    /// re-enters. A pause mid-loop therefore freezes cleanly, the resume
    /// lands on 1 and, if the ship is still in the loop, slow-mo simply
    /// re-arms. <c>fixedDeltaTime</c> is scaled with the clock and restored
    /// on every exit, including destruction (a scene trip mid-loop).
    /// </summary>
    [RequireComponent(typeof(ShipMotor))]
    [DisallowMultipleComponent]
    public class LoopSlowMo : MonoBehaviour
    {
        ShipMotor motor;
        GameSettings settings;
        float baseFixedDelta;
        float blend;            // 0 normal clock .. 1 full slow-mo, unscaled seconds
        bool owning;            // we wrote the clock last, and it still reads our value
        float appliedScale = 1f;

        /// <summary>True while the loop owns the clock.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>0..1 how deep into slow motion the clock is, for any effect that wants to ride along.</summary>
        public static float Blend { get; private set; }

        /// <summary>Add the component to a ship that has none yet — the GameManager.Awake hook.</summary>
        public static LoopSlowMo Ensure(ShipMotor motor) =>
            motor.GetComponent<LoopSlowMo>() ?? motor.gameObject.AddComponent<LoopSlowMo>();

        /// <summary>The run's settings asset — read live, never cloned (it is the designer's inline-edited asset).</summary>
        public void Configure(GameSettings runSettings) => settings = runSettings;

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            baseFixedDelta = Time.fixedDeltaTime;
        }

        void Update()
        {
            if (settings == null || !settings.loopSlowMo || motor == null)
            {
                Drop();
                Publish();
                return;
            }

            // The fall is the failed loop's second half — one window for both,
            // so the clock never eases up while the ship is still dropping.
            // A paused sim (the run is over) is never a loop.
            bool inLoop = !motor.Paused
                && (motor.State == ShipState.Looping || motor.State == ShipState.Falling);

            // Someone else took the clock (a menu opened, or closed back to
            // 1): it is theirs now. Restore the fixed step and stand down.
            if (owning && !Mathf.Approximately(Time.timeScale, appliedScale))
                Cancel();

            if (!owning)
            {
                bool clockFree = Mathf.Approximately(Time.timeScale, 1f);
                if (inLoop && clockFree)
                {
                    owning = true;
                    blend = 0f;
                }
                else
                {
                    Publish();
                    return;
                }
            }

            float target = inLoop ? 1f : 0f;
            float seconds = inLoop ? settings.loopSlowMoBlendIn : settings.loopSlowMoBlendOut;
            blend = seconds > 0f ? Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime / seconds) : target;

            if (!inLoop && blend <= 0f)
            {
                Release();
                Publish();
                return;
            }

            Apply(Mathf.Lerp(1f, RestingScale(), Mathf.SmoothStep(0f, 1f, blend)));
            Publish();
        }

        void OnDisable()
        {
            Drop();
            Publish();
        }

        // ---------------------------------------------------------------- clock

        /// <summary>
        /// The clock scale the loop settles at — the one that shows the ship
        /// at <see cref="GameSettings.loopApparentSpeedKmh"/>, so the loop
        /// takes the same few real seconds however fast it was entered: a
        /// loop at Light Speed is over in a third of a second real time, and
        /// a flat 0.75 of that is nothing. Capped between the min and max
        /// scales, read live so the sliders apply mid-loop.
        /// </summary>
        float RestingScale()
        {
            float min = Mathf.Clamp(settings.loopMinTimeScale, 0.01f, 1f);
            float max = Mathf.Clamp(settings.loopTimeScale, min, 1f);
            float speedKmh = motor.CurrentSpeed * 3.6f;
            if (speedKmh <= 1f) return max;
            return Mathf.Clamp(settings.loopApparentSpeedKmh / speedKmh, min, max);
        }

        void Apply(float scale)
        {
            appliedScale = scale;
            Time.timeScale = scale;
            Time.fixedDeltaTime = baseFixedDelta * scale;
        }

        /// <summary>The loop is over and the clock is still ours: hand it back at exactly 1.</summary>
        void Release()
        {
            Apply(1f);
            owning = false;
            blend = 0f;
        }

        /// <summary>Another owner has the clock: leave it alone, only the fixed step is ours to restore.</summary>
        void Cancel()
        {
            Time.fixedDeltaTime = baseFixedDelta;
            owning = false;
            blend = 0f;
        }

        /// <summary>Stand down whichever way is right for who holds the clock now — toggled off, disabled, destroyed.</summary>
        void Drop()
        {
            if (!owning) return;
            if (Mathf.Approximately(Time.timeScale, appliedScale)) Release();
            else Cancel();
        }

        void Publish()
        {
            IsActive = owning && blend > 0f;
            Blend = owning ? blend : 0f;
        }
    }
}
