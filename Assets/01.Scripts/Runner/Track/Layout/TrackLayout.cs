using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// A whole decided track, as data: the knots the spline is rebuilt from,
    /// every knot's track distance, the sections (loops, tubes), the dangerous
    /// spans (flat sweeps, open straights), the keep-outs the patrol's duel
    /// reads, the end, and every <see cref="TrackPlacement"/>. Captured from
    /// the generator after it decided a track (<see cref="TrackGenerator.CaptureLayout"/>)
    /// and loaded back in its place (<see cref="TrackGenerator"/> when the
    /// level names a <see cref="TrackLayoutAsset"/>), so a saved track plays
    /// exactly as it was generated. Nothing here is built: decoration and
    /// colliders are always derived from it at runtime, inside the stream
    /// window.
    /// </summary>
    [System.Serializable]
    public class TrackLayout
    {
        public const int CurrentVersion = 1;

        public int formatVersion = CurrentVersion;
        public uint seed;
        /// <summary>Full road width, metres.</summary>
        public float width;
        /// <summary>Track length (spline + inserted sections) when captured — a load checks it.</summary>
        public float length;
        public float endZoneStart = -1f;
        public float endDistance = -1f;
        public float endRunUp;

        public List<Knot> knots = new();
        public List<float> knotDistances = new();
        public List<Section> sections = new();
        public List<Span> flatSweeps = new();
        public List<Span> openStretches = new();
        public List<Vector2> keepOuts = new();
        public List<TrackPlacement> placements = new();

        public bool IsValid => knots != null && knots.Count >= 2 && knotDistances != null && knotDistances.Count == knots.Count && endDistance > 0f;

        /// <summary>
        /// One spline knot, in the spline container's space, with its tangent
        /// mode. Re-added in order, AutoSmooth knots recompute their tangents
        /// off the same neighbours they had, and Continuous ones keep theirs.
        /// </summary>
        [System.Serializable]
        public struct Knot
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 tangentIn;
            public Vector3 tangentOut;
            public TangentMode mode;

            public Knot(BezierKnot knot, TangentMode mode)
            {
                position = knot.Position;
                rotation = knot.Rotation;
                tangentIn = knot.TangentIn;
                tangentOut = knot.TangentOut;
                this.mode = mode;
            }

            public BezierKnot ToBezier() => new((float3)position, (float3)tangentIn, (float3)tangentOut, (quaternion)rotation);
        }

        /// <summary>
        /// A loop or a tube: the feature table entry that made it, where it
        /// starts, the random state its definition rolled from (replayed, it
        /// rolls the same section off the same road), and a loop's bridge.
        /// </summary>
        [System.Serializable]
        public struct Section
        {
            public int featureIndex;
            public float start;
            public uint rngState;
            public float splineExtent;
        }

        /// <summary>A stretch of track: a flat sweep (with its outer side) or an open straight.</summary>
        [System.Serializable]
        public struct Span
        {
            public float start;
            public float end;
            public int outerSide;

            public Span(float start, float end, int outerSide = 0)
            {
                this.start = start;
                this.end = end;
                this.outerSide = outerSide;
            }
        }
    }
}
