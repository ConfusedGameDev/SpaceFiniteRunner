using ConfusedGameDev.FiniteRunner.GameFlow;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Slow motion for the patrol duel: while the patrol has the ship in a tug
    /// of war (or, later, the finisher prompt) the world clock eases down to
    /// <see cref="GameSettings.duelTimeScale"/> and eases back when the
    /// exchange ends.
    ///
    /// What slow-mo buys here is PERCEPTION, not advantage. The bar is
    /// integrated against real seconds (see <see cref="PatrolEncounter"/>), so
    /// the mash is no easier for it, and the run's countdown keeps full pace,
    /// so an exchange still costs real mission time — which is what makes
    /// declining one, by braking off the flank, a genuine option when the
    /// clock is short.
    ///
    /// Clock ownership follows <see cref="LoopSlowMo"/> exactly, because they
    /// are two owners of one global: ENTER only when the clock reads exactly 1,
    /// remember what was written, and CANCEL silently (restoring the fixed step
    /// alone) the moment it reads anything else. That is also what settles the
    /// two of them against each other without either knowing the other exists —
    /// whoever gets the clock at 1 keeps it, and the other stands down instead
    /// of fighting. The common case is ruled out anyway: the duel is forbidden
    /// on loops, which is the only thing LoopSlowMo runs for.
    /// </summary>
    [RequireComponent(typeof(ShipMotor))]
    [DisallowMultipleComponent]
    public class DuelSlowMo : MonoBehaviour
    {
        ShipMotor motor;
        GameSettings settings;
        PolicePatrol patrol;
        float baseFixedDelta;
        float blend;            // 0 normal clock .. 1 full slow-mo, unscaled seconds
        bool owning;            // we wrote the clock last, and it still reads our value
        float appliedScale = 1f;

        /// <summary>True while the duel owns the clock.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>0..1 how deep into the duel's slow motion the clock is.</summary>
        public static float Blend { get; private set; }

        static float hitStopLeft;

        /// <summary>
        /// A brief deeper dip — the kill's connect. It is the TRANSITION back
        /// to full speed, not an effect of its own: the exchange ends on the
        /// same frame, so the clock punches down and then releases all the way
        /// out, which is what makes the snap back to Light Speed land.
        /// Unscaled, so the dip does not stretch itself.
        /// </summary>
        public static void RequestHitStop(float seconds) =>
            hitStopLeft = Mathf.Max(hitStopLeft, seconds);

        /// <summary>Add the component to a ship that has none yet — the GameManager.Awake hook.</summary>
        public static DuelSlowMo Ensure(ShipMotor motor) =>
            motor.GetComponent<DuelSlowMo>() ?? motor.gameObject.AddComponent<DuelSlowMo>();

        /// <summary>The run's settings asset (read live) and the patrol whose exchange drives the window.</summary>
        public void Configure(GameSettings runSettings, PolicePatrol runPatrol)
        {
            settings = runSettings;
            patrol = runPatrol;
        }

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            baseFixedDelta = Time.fixedDeltaTime;
        }

        void Update()
        {
            if (settings == null || !settings.patrolDuelEnabled || motor == null || patrol == null)
            {
                hitStopLeft = 0f;
                Drop();
                Publish();
                return;
            }

            if (hitStopLeft > 0f) hitStopLeft -= Time.unscaledDeltaTime;

            // A paused sim (a menu, the run over) is never an exchange.
            // The hit-stop holds the window open past the end of the exchange,
            // which is the point: the kill ends the contest and the dip is what
            // carries the player out of it.
            bool inExchange = !motor.Paused && (patrol.InExchange || hitStopLeft > 0f);

            // Someone else took the clock — a menu, or the loop's own slow-mo
            // got there first. It is theirs now.
            if (owning && !Mathf.Approximately(Time.timeScale, appliedScale))
                Cancel();

            if (!owning)
            {
                bool clockFree = Mathf.Approximately(Time.timeScale, 1f);
                if (inExchange && clockFree)
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

            float seconds = Mathf.Max(settings.duelTimeBlendSeconds, 0f);
            float target = inExchange ? 1f : 0f;
            blend = seconds > 0f ? Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime / seconds) : target;

            if (!inExchange && blend <= 0f)
            {
                Release();
                Publish();
                return;
            }

            float resting = Mathf.Clamp(settings.duelTimeScale, 0.05f, 1f);
            // The dip goes UNDER the exchange's own scale and ignores the
            // blend, so the connect is felt as a hit rather than a fade.
            if (hitStopLeft > 0f) resting = Mathf.Clamp(resting * 0.25f, 0.02f, 1f);
            Apply(Mathf.Lerp(1f, resting, Mathf.SmoothStep(0f, 1f, blend)));
            Publish();
        }

        void OnDisable()
        {
            Drop();
            Publish();
        }

        void Apply(float scale)
        {
            appliedScale = scale;
            Time.timeScale = scale;
            Time.fixedDeltaTime = baseFixedDelta * scale;
        }

        /// <summary>The exchange is over and the clock is still ours: hand it back at exactly 1.</summary>
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
