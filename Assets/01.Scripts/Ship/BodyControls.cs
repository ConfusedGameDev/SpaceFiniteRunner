namespace ConfusedGameDev.FiniteRunner.Simulation
{
    /// <summary>
    /// What a driver (the player's input, the patrol's AI) asks of a simulated
    /// body for one step. It lives in the Ship assembly — below the runner —
    /// because every body speaks it: the runner's track-space
    /// <c>TrackBody</c> and the standalone ship's <c>HoverBody</c> are driven
    /// by the very same three numbers, so a driver never knows which one it
    /// is steering. The namespace is the one it was born in, so no caller changed.
    /// </summary>
    public struct BodyControls
    {
        /// <summary>-1 (full left) .. +1 (full right).</summary>
        public float steer;

        /// <summary>0 (released) .. 1 (full throttle).</summary>
        public float throttle;

        /// <summary>0 (released) .. 1 (full brake).</summary>
        public float brake;
    }
}
