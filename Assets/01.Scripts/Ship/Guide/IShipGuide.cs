using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>Where a world point sits relative to a guide, and what the guide says about the road there.</summary>
    public struct GuideSample
    {
        /// <summary>Distance along the guide from its start, metres.</summary>
        public float distance;
        /// <summary>Across the guide, right positive, metres.</summary>
        public float lateral;
        /// <summary>Above the guide's line, along its up, metres.</summary>
        public float height;

        /// <summary>The point ON the line at <see cref="distance"/>.</summary>
        public Vector3 position;
        public Vector3 forward;
        public Vector3 up;
        public Vector3 right;

        /// <summary>Signed turn rate of the line, 1/m, positive to the right.</summary>
        public float curvature;
        /// <summary>The steering lane around the line, metres (min is negative).</summary>
        public float bandMin, bandMax;
        /// <summary>No wall on that side here: past the band is a fall, not a clamp.</summary>
        public bool openLeft, openRight;
        /// <summary>The road does not hold the ship by itself here: taken too fast it slides outward (the runner's flat sweeps).</summary>
        public bool gripTested;
    }

    /// <summary>
    /// An OPTIONAL line through a level that helps a ship along it. A ship
    /// with no guide in reach is fully free; a ship with one gets its heading
    /// eased onto the line (hard-locked at full assist, which is the runner's
    /// feel: the road supplies the heading and the stick strafes), and
    /// everything that thinks in track coordinates — a streamer, a chaser's
    /// gap, respawn points — gets them from <see cref="TryProject"/>. The
    /// guide never moves the ship and never replaces the level's colliders:
    /// the ship still rides whatever surface is under it.
    /// </summary>
    public interface IShipGuide
    {
        /// <summary>Total length of the line, metres.</summary>
        float Length { get; }
        /// <summary>How far from the line a ship is still guided, metres.</summary>
        float CaptureRange { get; }
        /// <summary>The level's say over the assist, 0 (none) .. 1 (the heading is the line's). Multiplied with the ship's own.</summary>
        float Assist { get; }
        /// <summary>The scene the guide lives in — ships only take a guide of their own scene.</summary>
        UnityEngine.SceneManagement.Scene Scene { get; }

        /// <summary>
        /// Projects a world point onto the line. <paramref name="hint"/> is the
        /// distance found last time: the search stays in a window around it
        /// (cheap, and the only way a line that crosses itself — a loop — can
        /// be told apart), and it is updated on success. NaN = search the
        /// whole line (first contact, a landing, a respawn).
        /// </summary>
        bool TryProject(Vector3 worldPosition, ref float hint, out GuideSample sample);

        /// <summary>The line's own frame and road data at a distance (lateral and height are 0).</summary>
        void SampleAt(float distance, out GuideSample sample);

        /// <summary>First distance at or past <paramref name="from"/> where a ship can be put back on the road with <paramref name="clearance"/> metres of plain road ahead.</summary>
        float FindRespawn(float from, float clearance);
    }
}
