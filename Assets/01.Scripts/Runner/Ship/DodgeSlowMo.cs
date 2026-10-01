using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Contracts;
using ConfusedGameDev.FiniteRunner.Track.Features;
using ConfusedGameDev.FiniteRunner.Traffic;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The clutch dodge: when the ship dashes sideways with an oncoming car or
    /// a laser beam due to hit it within the last
    /// <see cref="DodgeSlowMoSettings.threatWindowSeconds"/>, and the dash
    /// takes it clear, the world clock drops to
    /// <see cref="DodgeSlowMoSettings.timeScale"/> for the length of the dash.
    ///
    /// The verdict is taken the moment the dash fires, from two forecasts
    /// asked of the systems that own the data
    /// (<see cref="TrafficSystem.ForecastContact"/>,
    /// <see cref="LaserGate.ForecastAny"/>): a hit ahead on the line the ship
    /// is on, and none on the line the dash ends on
    /// (<see cref="ShipDefinition.dashDistance"/> over). A hit landing anyway
    /// mid-dash releases the clock at once. The countdown rides the world
    /// clock, so — like the loop — the slow-mo plays longer in real time.
    ///
    /// Hand-placed on <c>PF_Ship</c>, wired to the <see cref="ShipMotor"/>
    /// beside it and to nothing else: no GameManager hook, and every knob is
    /// on its own <see cref="DodgeSlowMoSettings"/> asset. With no asset, no
    /// traffic and no gates it idles.
    ///
    /// Clock ownership follows <see cref="LoopSlowMo"/> exactly: ENTER only
    /// when the clock reads 1, remember what was written, CANCEL silently
    /// (restoring the fixed step alone) the moment it reads anything else. So a
    /// menu, a hit-stop, the loop or the duel always win, and nobody fights.
    /// </summary>
    [RequireComponent(typeof(ShipMotor))]
    [DisallowMultipleComponent]
    public class DodgeSlowMo : MonoBehaviour
    {
        [InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        [SerializeField] DodgeSlowMoSettings settings;

        ShipMotor motor;
        TrafficSystem trafficSource; // the live system we listen to (it binds after we enable)
        float baseFixedDelta;
        float blend;             // 0 normal clock .. 1 full slow-mo, unscaled seconds
        bool owning;             // we wrote the clock last, and it still reads our value
        bool holding;            // the dodge is on: the dash is still running
        bool sawDash;            // the dash we hold for has been seen running (it starts next tick)
        float appliedScale = 1f;
        float readyAt;           // unscaled time the cooldown ends

        /// <summary>True while a dodge owns the clock.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>0..1 how deep into the dodge's slow motion the clock is.</summary>
        public static float Blend { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { IsActive = false; Blend = 0f; } // domain reload is off

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            baseFixedDelta = Time.fixedDeltaTime;
        }

        void OnEnable()
        {
            if (motor != null) motor.DashPerformed += OnDash;
            LaserGate.Hit += OnLaserHit;
        }

        void OnDisable()
        {
            if (motor != null) motor.DashPerformed -= OnDash;
            LaserGate.Hit -= OnLaserHit;
            Listen(null);
            holding = false;
            Drop();
            Publish();
        }

        // ---------------------------------------------------------------- trigger

        void OnDash(int direction)
        {
            if (settings == null || !settings.enabled || motor.Paused) return;
            if (owning || !Mathf.Approximately(Time.timeScale, 1f)) return;
            if (Time.unscaledTime < readyAt) return;

            float speed = motor.CurrentSpeed;
            if (speed * DodgeSlowMoSettings.KmhPerMs < settings.minSpeedKmh) return;

            var body = (ITrafficBody)motor;
            if (!body.TrafficSolid) return;
            float lateral = body.Lateral;
            float dashDistance = motor.Definition != null ? motor.Definition.dashDistance : 0f;
            if (dashDistance <= 0f) return;

            if (!Threatened(body, speed, lateral)) return;
            if (Threatened(body, speed, lateral + direction * dashDistance)) return;

            owning = true;
            holding = true;
            sawDash = false;
            blend = 0f;
            appliedScale = 1f; // the clock reads 1 right now (checked above)
        }

        bool Threatened(ITrafficBody body, float speed, float lateral)
        {
            float horizon = settings.threatWindowSeconds;
            Vector3 reach = body.TrafficReach + new Vector3(settings.extraReach, settings.extraReach, 0f);
            float height = body.TrafficHeight;

            if (settings.dodgeTraffic &&
                TrafficSystem.ForecastContact(body.Distance, speed, lateral, height, reach, horizon) >= 0f)
                return true;

            return settings.dodgeLasers &&
                   LaserGate.ForecastAny(body.Distance, speed, lateral, height, new Vector2(reach.x, reach.y),
                                         horizon, settings.forecastStepSeconds);
        }

        // A hit landed anyway: the dodge failed, hand the clock back now.
        void OnShipStruck() => EndDodge();

        void OnLaserHit(LaserGate gate, Component rider)
        {
            if (rider == motor) EndDodge();
        }

        void EndDodge()
        {
            if (!holding) return;
            holding = false;
            blend = 0f;
            Drop();
            readyAt = Time.unscaledTime + (settings != null ? settings.cooldownSeconds : 0f);
            Publish();
        }

        void Listen(TrafficSystem system)
        {
            if (system == trafficSource) return;
            if (trafficSource != null) trafficSource.ShipStruck -= OnShipStruck;
            trafficSource = system;
            if (trafficSource != null) trafficSource.ShipStruck += OnShipStruck;
        }

        // ---------------------------------------------------------------- clock

        void Update()
        {
            Listen(TrafficSystem.Live);

            if (!owning)
            {
                Publish();
                return;
            }

            // Someone else took the clock (a menu, a hit-stop): it is theirs now.
            if (!Mathf.Approximately(Time.timeScale, appliedScale) || settings == null || !settings.enabled)
            {
                bool wasHolding = holding;
                holding = false;
                Drop();
                if (wasHolding) readyAt = Time.unscaledTime + (settings != null ? settings.cooldownSeconds : 0f);
                Publish();
                return;
            }

            // The dash starts on the ship's next fixed tick: hold until it has
            // been seen running, then until it ends (a wall ends it early).
            if (holding)
            {
                if (motor.IsDashing) sawDash = true;
                else if (sawDash || motor.Paused)
                {
                    holding = false;
                    readyAt = Time.unscaledTime + settings.cooldownSeconds;
                }
            }

            float target = holding ? 1f : 0f;
            float seconds = holding ? settings.blendInSeconds : settings.blendOutSeconds;
            blend = seconds > 0f ? Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime / seconds) : target;

            if (!holding && blend <= 0f)
            {
                Release();
                Publish();
                return;
            }

            Apply(Mathf.Lerp(1f, Mathf.Clamp(settings.timeScale, 0.05f, 1f), Mathf.SmoothStep(0f, 1f, blend)));
            Publish();
        }

        void Apply(float scale)
        {
            appliedScale = scale;
            Time.timeScale = scale;
            Time.fixedDeltaTime = baseFixedDelta * scale;
        }

        /// <summary>The dodge is over and the clock is still ours: hand it back at exactly 1.</summary>
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
