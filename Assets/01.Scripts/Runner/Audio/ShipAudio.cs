using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.UI;

namespace ConfusedGameDev.FiniteRunner.Audio
{
    /// <summary>
    /// The ship's own sound: a speed-driven engine loop, the power-up pickup,
    /// the jump takeoff, the lateral dash and the barrel roll. Rides the ship
    /// like <see cref="LoopSlowMo"/> —
    /// added by <c>GameManager.Awake</c> through <see cref="Ensure"/> +
    /// <see cref="Configure"/>, reading the run's <see cref="GameSettings"/>
    /// and its <see cref="RunnerSfxSettings"/> live (no clone). Every source
    /// sits on <see cref="GameAudio.Fx"/>, so the Paused / Loading / Cinema
    /// snapshots duck them for free and the player's SFX slider scales them;
    /// nothing here touches an exposed mixer parameter.
    ///
    /// Hooks: <see cref="SpeedPad.Collected"/> for pickups — NOT
    /// <see cref="ShipMotor.PadImpulse"/>, which a ramp takeoff also raises
    /// and would double up with the jump — and <see cref="ShipMotor.TookOff"/>
    /// for jumps (tubes and loops never fire it). An airborne dash raises
    /// <see cref="ShipMotor.DashPerformed"/> AND
    /// <see cref="ShipMotor.BarrelRollStarted"/>, so the dash clip is only
    /// played on the track and the roll clip in the air. The engine is a fraction of
    /// Light Speed, smoothed, gated on the sim (it fades out when
    /// <c>motor.Paused</c> freezes the run at either ending and back on a
    /// relaunch — no manager hook), and follows the world clock so the loop
    /// slow-mo drags it down with the picture.
    /// </summary>
    [RequireComponent(typeof(ShipMotor))]
    [DisallowMultipleComponent]
    public class ShipAudio : MonoBehaviour
    {
        ShipMotor motor;
        GameSettings settings;
        RunnerSfxSettings sfx;
        float lightSpeedKmh = 1f;

        AudioSource engine;
        AudioSource pickups;
        AudioSource jumps;
        AudioSource dashes;
        float smoothedSpeed; // 0..1 fraction of Light Speed, smoothed
        float gate;          // 0..1 the sim-freeze fade on the engine

        /// <summary>Add the component to a ship that has none yet — the GameManager.Awake hook.</summary>
        public static ShipAudio Ensure(ShipMotor motor) =>
            motor.GetComponent<ShipAudio>() ?? motor.gameObject.AddComponent<ShipAudio>();

        /// <summary>
        /// The run's settings asset (read live, never cloned) and the speed the
        /// engine's band is a fraction of. An empty <c>sfxSettings</c> slot
        /// falls back to the shipped asset from Resources.
        /// </summary>
        public void Configure(GameSettings runSettings, float lightSpeedKmh)
        {
            settings = runSettings;
            sfx = runSettings != null && runSettings.sfxSettings != null ? runSettings.sfxSettings : RunnerSfxSettings.Load();
            this.lightSpeedKmh = Mathf.Max(1f, lightSpeedKmh);
        }

        void Awake()
        {
            motor = GetComponent<ShipMotor>();
            engine = MakeSource("Engine", loop: true);
            engine.priority = 0; // never the voice the engine steals
            pickups = MakeSource("Pickups", loop: false);
            jumps = MakeSource("Jumps", loop: false);
            dashes = MakeSource("Dashes", loop: false);
        }

        // 2D on purpose: these are the player's own ship, always at the
        // camera — a spatialised source would swing with the chase framing.
        AudioSource MakeSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.volume = loop ? 0f : 1f;
            source.outputAudioMixerGroup = GameAudio.Fx;
            return source;
        }

        void OnEnable()
        {
            if (motor != null)
            {
                motor.TookOff += OnTookOff;
                motor.DashPerformed += OnDashPerformed;
                motor.BarrelRollStarted += OnBarrelRollStarted;
            }
            SpeedPad.Collected += OnPadCollected; // static: paired below — domain reload is off
        }

        void OnDisable()
        {
            if (motor != null)
            {
                motor.TookOff -= OnTookOff;
                motor.DashPerformed -= OnDashPerformed;
                motor.BarrelRollStarted -= OnBarrelRollStarted;
            }
            SpeedPad.Collected -= OnPadCollected;
            if (engine != null) engine.Stop();
            gate = 0f;
        }

        // ------------------------------------------------------------ one-shots

        void OnPadCollected(SpeedPad pad, ShipMotor collector)
        {
            if (collector != motor || sfx == null || pickups == null) return;

            if (pad.SpeedDelta > 0f)
            {
                if (sfx.powerUpClip == null) return;
                // Tier = how many base boosts this orb is worth (green 1, blue
                // 2.5, purple 10); log10 puts them at 0 / 0.4 / 1 of the band.
                float baseBoost = settings != null ? settings.powerUpSpeedBoost : pad.SpeedDelta;
                float tier = pad.SpeedDelta / Mathf.Max(0.01f, baseBoost);
                float t = Mathf.Clamp01(Mathf.Log10(Mathf.Max(1f, tier)));
                pickups.pitch = Mathf.Lerp(sfx.PowerUpPitchMin, sfx.PowerUpPitchMax, t);
                pickups.PlayOneShot(sfx.powerUpClip, sfx.powerUpVolume);
            }
            else if (pad.SpeedDelta < 0f && sfx.brakeClip != null)
            {
                pickups.pitch = 1f;
                pickups.PlayOneShot(sfx.brakeClip, sfx.brakeVolume);
            }
        }

        void OnTookOff()
        {
            if (sfx == null || sfx.jumpClip == null || jumps == null) return;
            jumps.PlayOneShot(sfx.jumpClip, sfx.jumpVolume);
        }

        void OnDashPerformed(int direction)
        {
            if (sfx == null || sfx.dashClip == null || dashes == null) return;
            // An airborne dash is a barrel roll: BarrelRollStarted follows this
            // event the same frame (unless a roll is already spinning), and
            // the roll clip speaks for it.
            if (motor.State == ShipState.Airborne && !motor.IsBarrelRolling) return;
            dashes.PlayOneShot(sfx.dashClip, sfx.dashVolume);
        }

        void OnBarrelRollStarted(int direction)
        {
            if (sfx == null || sfx.barrelRollClip == null || dashes == null) return;
            dashes.PlayOneShot(sfx.barrelRollClip, sfx.barrelRollVolume);
        }

        // -------------------------------------------------------------- engine

        void Update()
        {
            if (engine == null || sfx == null || motor == null) return;

            if (engine.clip != sfx.engineClip)
            {
                engine.Stop();
                engine.clip = sfx.engineClip;
            }
            if (engine.clip == null) return;

            // Real time throughout: the loop slow-mo and the frozen result
            // panels must not stall the smoothing or the fade.
            float dt = Time.unscaledDeltaTime;
            bool live = !motor.Paused && !motor.HasStopped;
            float speed01 = Mathf.Clamp01(motor.CurrentSpeed * 3.6f / lightSpeedKmh); // clamped: the ship may pass Light Speed after the win latch
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed01, 1f - Mathf.Exp(-sfx.engineResponse * dt));
            gate = Mathf.MoveTowards(gate, live ? 1f : 0f, dt / Mathf.Max(0.01f, sfx.engineFadeSeconds));

            if (gate <= 0f)
            {
                if (engine.isPlaying) engine.Stop(); // nothing plays inaudibly under a result panel
                return;
            }
            if (!engine.isPlaying) engine.Play();

            engine.volume = Mathf.Lerp(sfx.EngineVolumeIdle, sfx.EngineVolumeFull, smoothedSpeed) * gate;
            float pitch = Mathf.Lerp(sfx.EnginePitchIdle, sfx.EnginePitchFull, smoothedSpeed);
            // A stopped clock (the pause menu) leaves the pitch alone — the
            // Paused snapshot hides the loop; only a running slow-mo bends it.
            if (sfx.engineFollowsClock && Time.timeScale > 0f) pitch *= Time.timeScale;
            engine.pitch = pitch;
        }
    }
}
