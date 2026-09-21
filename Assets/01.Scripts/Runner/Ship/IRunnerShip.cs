using System;

using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// A ship flying the RUNNER: everything <see cref="IShip"/> says, plus
    /// what only a level with a track can answer — where the ship is in track
    /// coordinates, the ramp or loop it is committed to, the loop verdict, and
    /// the post-win autopilot. The track-space <see cref="ShipMotor"/>
    /// implements it natively; the standalone ship will answer it through the
    /// runner's bridge, off the guide's projection of its world pose. Runner
    /// code that needs track coordinates (the streamer, the loop gates, the
    /// patrol, the ghost trail) asks for this; everything else asks for
    /// <see cref="IShip"/> and works in any level.
    /// </summary>
    public interface IRunnerShip : IShip
    {
        /// <summary>Distance from the track start, metres — the rendered value.</summary>
        float DistanceTravelled { get; }
        /// <summary>Across the track, right positive, metres.</summary>
        float LateralOffset { get; }
        /// <summary>Height above the flight line, metres.</summary>
        float AirHeight { get; }
        TrackManager Track { get; }
        JumpRamp CurrentRamp { get; }
        LoopFeature CurrentLoop { get; }
        /// <summary>The post-win lockdown: hands-off flying that can neither slide nor fall.</summary>
        bool Autopilot { get; set; }

        /// <summary>A loop was entered: true = fast enough, it will be a pass.</summary>
        event Action<bool> LoopEntered;
        event Action LoopFailed;
    }
}
