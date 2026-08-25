using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>What a collision-prevention probe concluded. Shared by both drivers — they run the same three-ray + verdict logic.</summary>
    public enum ObstacleKind
    {
        None,
        Vehicle,
        Wall,
    }

    /// <summary>Which probe of the avoidance fan this is: the centre ray, one of the two fender rays, or the junction yield whisker.</summary>
    public enum AiProbeRole
    {
        Forward,
        Fender,
        Whisker,
    }

    /// <summary>
    /// One cast of the collision-prevention system, recorded as it happened:
    /// where it started, how far it reached, what it touched and what the
    /// driver concluded from it. Recorded, not re-derived — a visualizer that
    /// re-casts the rays itself shows what the rays WOULD do this frame, not
    /// what the car actually decided on, and the two differ exactly when
    /// something is wrong.
    /// </summary>
    public readonly struct AiProbe
    {
        public readonly AiProbeRole Role;
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;
        public readonly float Length;
        public readonly bool Hit;
        public readonly Vector3 HitPoint;
        public readonly ObstacleKind Verdict;

        public AiProbe(AiProbeRole role, Vector3 origin, Vector3 direction, float length, bool hit, Vector3 hitPoint, ObstacleKind verdict)
        {
            Role = role;
            Origin = origin;
            Direction = direction;
            Length = length;
            Hit = hit;
            HitPoint = hitPoint;
            Verdict = verdict;
        }

        /// <summary>Where the probe visually ends: the contact point when it hit, its full reach otherwise.</summary>
        public Vector3 End => Hit ? HitPoint : Origin + Direction.normalized * Length;
    }

    /// <summary>
    /// The per-car probe record a driver fills while it decides. Reused every
    /// frame (no allocation) and only written when the debug overlay is on, so
    /// a shipped fleet pays nothing for it.
    /// </summary>
    public class AiProbeLog
    {
        readonly List<AiProbe> probes = new();

        public IReadOnlyList<AiProbe> Probes => probes;

        public void Begin() => probes.Clear();

        public void Add(AiProbeRole role, Vector3 origin, Vector3 direction, float length, bool hit, Vector3 hitPoint, ObstacleKind verdict) =>
            probes.Add(new AiProbe(role, origin, direction, length, hit, hitPoint, verdict));
    }

    /// <summary>
    /// What an AI driver exposes to the debug overlay: the plan it is
    /// following, the point it is really steering at (not the waypoint — the
    /// lane-offset target beside it, which is where orbiting bugs hide), its
    /// avoidance probes and, for the police, the sight line that starts a
    /// chase. Both drivers implement it, so one visualizer covers the whole
    /// fleet without knowing which kind of car it is looking at.
    /// </summary>
    public interface IAiDebugDriver
    {
        /// <summary>Short state name for the on-screen label ("CHASE", "WANDER", "FLEE" …).</summary>
        string StateLabel { get; }

        /// <summary>Colour the whole overlay for this car takes — state at a glance across a busy street.</summary>
        Color StateColor { get; }

        /// <summary>Queued route, nearest first. Positions, not nodes: the plan lives in world space.</summary>
        IReadOnlyList<Vector3> Waypoints { get; }

        /// <summary>The waypoint just popped — the anchor the lane offset is measured from.</summary>
        Vector3 PreviousWaypoint { get; }

        /// <summary>The point actually steered at this frame, lane offset included.</summary>
        Vector3 SteerAim { get; }

        /// <summary>Off the road graph and creeping back onto it.</summary>
        bool OffRoad { get; }

        /// <summary>Backing out of a wedge right now.</summary>
        bool Reversing { get; }

        /// <summary>Seconds spent standing still against something — the countdown to a reverse.</summary>
        float StuckTime { get; }

        /// <summary>This frame's avoidance verdict.</summary>
        ObstacleKind Obstacle { get; }

        /// <summary>This frame's probes, in the order they were cast. Empty when the overlay is off.</summary>
        IReadOnlyList<AiProbe> Probes { get; }

        /// <summary>The perception ray, when the driver has one (police only): eye, target, and whether the view was clear.</summary>
        bool TryGetSightLine(out Vector3 from, out Vector3 to, out bool clear);
    }
}
