using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Track;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The ARMED window: for a few seconds after the ship takes a strong boost
    /// orb it can kill a patrol outright, skipping the tug of war and going
    /// straight to the finisher prompt (D19). It is the mechanical expression of
    /// "grab a big orb while hunted and you get to use it as a weapon" — the
    /// reason a blue or purple orb is worth reaching for even when a cruiser is
    /// already on your flank.
    ///
    /// The ship had no power-up state of any kind before this: pickups were
    /// instantaneous speed impulses and nothing outlived the frame. So this is
    /// modelled on the one timed state that does exist, <see cref="ShipHealth"/>'s
    /// invulnerability blink — a float ticked down in the FIXED tick (never a
    /// coroutine, so <see cref="ShipMotor.Paused"/> and a menu's timeScale freeze
    /// it exactly like every other runner timer) and read as a bool.
    ///
    /// It rides the ship like <see cref="RespawnBlink"/> and
    /// <see cref="DuelSlowMo"/> do: <see cref="Ensure"/> + <see cref="Configure"/>
    /// from <c>GameManager.Awake</c>, gating itself on
    /// <see cref="GameSettings.patrolDuelEnabled"/> so the duel's master switch
    /// turns the whole idea off.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShipArmed : MonoBehaviour
    {
        ShipMotor motor;
        GameSettings settings;
        float armedLeft;
        float sparkleTimer;

        /// <summary>Seconds of the window still to run; 0 when the ship is not armed.</summary>
        public float ArmedLeft => armedLeft;

        /// <summary>The ship is carrying a kill: the next exchange goes straight to the finisher.</summary>
        public bool IsArmed => armedLeft > 0f;

        /// <summary>
        /// Spends the window. The kill consumes it, so one orb buys one
        /// finisher and not every exchange inside the three seconds.
        /// </summary>
        public void Spend() => armedLeft = 0f;

        /// <summary>Add the component to a ship that has none yet — the GameManager.Awake hook.</summary>
        public static ShipArmed Ensure(ShipMotor ship) =>
            ship.GetComponent<ShipArmed>() ?? ship.gameObject.AddComponent<ShipArmed>();

        /// <summary>The run's settings asset, read live like every other duel knob.</summary>
        public void Configure(GameSettings runSettings)
        {
            settings = runSettings;
            armedLeft = 0f;
        }

        void Awake() => motor = GetComponent<ShipMotor>();

        // Subscribed here rather than in a static initializer: domain reload is
        // off, so a static subscription would survive play sessions and stack up.
        void OnEnable() => SpeedPad.Collected += OnPadCollected;
        void OnDisable() => SpeedPad.Collected -= OnPadCollected;

        /// <summary>
        /// A strong orb arms the ship. Which tiers count is authored
        /// (<see cref="GameSettings.armingOrbTiers"/>, Blue and Purple by
        /// default): green is 46 % of spawns and would make the window
        /// permanent, purple alone is 3 % and would make it a rumour (D10).
        /// A brake pad is a negative delta and never arms anything.
        /// </summary>
        void OnPadCollected(SpeedPad pad, IShip collector)
        {
            if (motor == null || settings == null || pad == null) return;
            if (!settings.patrolDuelEnabled || !motor.Is(collector)) return;
            if (!pad.IsBoostOrb || !Arms(pad.TierName)) return;

            armedLeft = Mathf.Max(armedLeft, settings.armedWindowSeconds);
            sparkleTimer = 0f;
            Spark(2f); // one bright burst on the pickup, then the trail below
        }

        bool Arms(string tierName)
        {
            List<string> tiers = settings.armingOrbTiers;
            if (string.IsNullOrEmpty(tierName) || tiers == null) return false;
            for (int i = 0; i < tiers.Count; i++)
                if (string.Equals(tiers[i], tierName, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // The window is a run timer, so it lives in the fixed tick and stops
        // dead with the sim — a player reading a pause menu is not spending it.
        void FixedUpdate()
        {
            if (armedLeft <= 0f || motor == null || motor.Paused) return;
            armedLeft = Mathf.Max(0f, armedLeft - Time.fixedDeltaTime);
        }

        // The tell (R5.4): sparks streaming off the ship for as long as the
        // window lasts. Deliberately not a HUD icon — the player is watching the
        // road and the cruiser, and this is a large enough reward that it has to
        // be visible where their eyes already are. Unscaled, so the duel's slow
        // motion does not thin it out.
        void Update()
        {
            if (armedLeft <= 0f || motor == null || motor.Paused) return;
            sparkleTimer -= Time.unscaledDeltaTime;
            if (sparkleTimer > 0f) return;
            sparkleTimer = SparkleInterval;
            Spark(1f);
        }

        const float SparkleInterval = 0.09f;

        void Spark(float scale)
        {
            if (settings == null) return;
            SparkleVfx.SpawnBurst(transform.position, transform.up,
                                  settings.armedTellColor, 4f * scale, (int)(10f * scale));
        }
    }
}
