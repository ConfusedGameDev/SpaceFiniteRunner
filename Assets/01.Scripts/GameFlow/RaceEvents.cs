using System;

namespace HoverRacer
{
    /// <summary>
    /// Static event bus. All systems communicate through here — no direct references needed.
    /// Subscribe in OnEnable/Awake, unsubscribe in OnDisable/OnDestroy.
    /// </summary>
    public static class RaceEvents
    {
        /// <summary>Fired every FixedUpdate while the vehicle is moving. Arg: current speed in m/s.</summary>
        public static event Action<float> OnSpeedChanged;

        /// <summary>Fired once when the vehicle's speed reaches zero.</summary>
        public static event Action OnVehicleStopped;

        /// <summary>
        /// Fired when the vehicle stops after having crossed the finish line.
        /// Arg: signed distance in metres. Negative = stopped before the line, positive = past it, 0 = perfect.
        /// </summary>
        public static event Action<float> OnFinishLineCrossed;

        /// <summary>Fired once the final score (0–100) has been calculated.</summary>
        public static event Action<int> OnScoreCalculated;

        public static void RaiseSpeedChanged(float speed)        => OnSpeedChanged?.Invoke(speed);
        public static void RaiseVehicleStopped()                  => OnVehicleStopped?.Invoke();
        public static void RaiseFinishLineCrossed(float distance) => OnFinishLineCrossed?.Invoke(distance);
        public static void RaiseScoreCalculated(int score)        => OnScoreCalculated?.Invoke(score);
    }
}
