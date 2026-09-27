namespace ConfusedGameDev.FiniteRunner.Contracts
{
    /// <summary>
    /// What a chaser may READ of the vehicle it hunts: where it is on the
    /// track, how fast, whether it is steady, and the few stats a chase
    /// scales by. The patrol hunts through this and never sees the vehicle's
    /// motor, body or definition. Distances are metres from the track start;
    /// speeds m/s.
    /// </summary>
    public interface IChaseTarget
    {
        /// <summary>Track distance of the simulation (the physics tick's value — what gaps are judged on).</summary>
        float Distance { get; }
        /// <summary>Track distance as rendered this frame (interpolated) — for readouts that must not jitter.</summary>
        float DisplayDistance { get; }
        /// <summary>Lateral offset from the flight line, metres.</summary>
        float Lateral { get; }
        /// <summary>Forward speed, m/s.</summary>
        float Speed { get; }
        /// <summary>The run is frozen (menus, endings).</summary>
        bool Paused { get; }
        /// <summary>On the road and flying normally — not falling, off the track or respawning.</summary>
        bool Steady { get; }
        /// <summary>Left the track off its end (the finish or the void).</summary>
        bool HasLeftTrackEnd { get; }
        /// <summary>Brake input this frame, 0..1.</summary>
        float BrakeInput { get; }
        /// <summary>Carrying a kill (a strong orb armed it): the next exchange skips to the finisher.</summary>
        bool IsArmed { get; }
        /// <summary>Spends the armed window (the kill consumes it).</summary>
        void SpendArmed();
        /// <summary>Lateral drag, 1/s — how a shove in metres becomes a velocity.</summary>
        float HandlingResponse { get; }
        /// <summary>The speed a raw boost of <paramref name="rawMagnitude"/> actually adds to it (its weight's scaling).</summary>
        float ScaleBoost(float rawMagnitude);
        /// <summary>Gravity and tumble of an off-track fall, so a chaser falling beside it falls the same way.</summary>
        float FallGravity { get; }
        float FallTumbleDegreesPerSecond { get; }
        /// <summary>Raised with the raw magnitude of every speed impulse it collects.</summary>
        event System.Action<float> Boosted;
    }

    /// <summary>
    /// What a chaser may DO to the vehicle during an exchange: take its
    /// controls (steer assist, the dash lock, an autopilot line with its pull)
    /// and land the exchange's physical results (a shove, a kick, a speed
    /// loss, a spent dash). The duel acts through this and nothing else.
    /// </summary>
    public interface IControlTakeover
    {
        float SteerAssist { get; set; }
        bool DashLocked { get; set; }
        bool Autopilot { get; set; }
        float AutopilotLateral { get; set; }
        float AutopilotGain { get; set; }
        void ConsumeDashRequest();
        void DrainDashMeter();
        void ApplyImpactSpeedLoss(float share);
        void AddLateralShove(float velocity);
        void VisualKick(float lateralMeters);
    }
}
