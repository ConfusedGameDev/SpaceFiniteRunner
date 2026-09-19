using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Simulation;
namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>
    /// One laser gate on the track: one to three beams, each a SEGMENT in the
    /// gate's own track space (x across the track, y above the flight line,
    /// z along it, the gate's distance being z = 0), drawn by a
    /// <see cref="LaserBeam"/> apiece. <b>Detection is analytic</b>, in two
    /// phases: the gate sits in the <see cref="PickupRegistry"/> as the box
    /// round all its beams, so the body's swept query finds it at any speed
    /// (~36 m per step at Light Speed), and the ship then asks
    /// <see cref="Touches"/> — the closest approach of the stretch it just
    /// flew to each beam, measured in a space squashed by the ship's own half
    /// width and half height. One test serves all four variants: a ship on a
    /// jump clears a ground beam and meets the triple's upper ones, and the
    /// rotor only burns where its blade actually is. The rotor's angle is a
    /// pure function of scaled time, shared by the picture and the test, so a
    /// pause freezes both. A gate is never used up; what a hit MEANS is the
    /// listener's business (<see cref="Hit"/> — the GameManager burns the hull).
    /// The patrol's body finds gates too and ignores them.
    /// </summary>
    public class LaserGate : MonoBehaviour, ITrackPickup
    {
        /// <summary>Seconds a gate stays quiet after burning a ship, so one pass is one hit whatever the listeners do.</summary>
        const float RehitSeconds = 0.5f;

        struct Beam
        {
            public Vector3 a, b;        // gate-local track space; a rotor's are recomputed from its angle
            public LaserBeam visual;
        }

        /// <summary>Raised when the player's ship flies through a beam. Static, like SpeedPad.Collected, so listeners need no per-gate wiring.</summary>
        public static event System.Action<LaserGate, ShipMotor> Hit;

        readonly List<Beam> beams = new();
        LaserGateDefinition definition;
        float trackDistance;
        bool placed;
        bool isRotor;
        Vector3 rotorCentre;
        float rotorHalfLength, rotorSpeed, rotorPhase;
        Vector3 boundsCentre, boundsHalf;
        float lastHitTime = float.NegativeInfinity;
        float waveAmplitude; // 0 = straight beams; else what burns grows by it on the axis the wave swings along

        public LaserGateVariant Variant { get; private set; }

        // ------------------------------------------------------- ITrackPickup
        public float TrackDistance => trackDistance + boundsCentre.z;
        public float TrackLateral => boundsCentre.x;
        public float TrackHeight => boundsCentre.y;
        public Vector3 TrackHalfExtents => boundsHalf;
        public bool Available => placed;

        /// <summary>
        /// Shapes the gate. <paramref name="lateral"/> is the beam's centre
        /// across the track, <paramref name="length"/> its length;
        /// <paramref name="beamVisuals"/> carries one picture per beam (three
        /// for the triple). The root already stands on the track pose at
        /// <paramref name="distance"/>, lateral 0.
        /// </summary>
        public void Configure(LaserGateDefinition def, LaserGateVariant variant, float distance, float lateral,
                              float length, float rotorDegreesPerSecond, float rotorPhaseDegrees,
                              IReadOnlyList<LaserBeam> beamVisuals, bool wavy = false)
        {
            definition = def;
            Variant = variant;
            waveAmplitude = wavy ? def.waveAmplitude : 0f;
            trackDistance = distance;
            beams.Clear();

            float half = length * 0.5f;
            float h = def.beamHeight;
            switch (variant)
            {
                case LaserGateVariant.Vertical:
                    AddBeam(new Vector3(lateral, -1f, 0f), new Vector3(lateral, def.verticalHeight, 0f), beamVisuals, 0);
                    break;
                case LaserGateVariant.Triple:
                    for (int i = 0; i < 3; i++)
                    {
                        float y = h + i * def.tripleSpacing;
                        AddBeam(new Vector3(lateral - half, y, 0f), new Vector3(lateral + half, y, 0f), beamVisuals, i);
                    }
                    break;
                case LaserGateVariant.Rotor:
                    isRotor = true;
                    rotorCentre = new Vector3(lateral, h, 0f);
                    rotorHalfLength = half;
                    rotorSpeed = rotorDegreesPerSecond;
                    rotorPhase = rotorPhaseDegrees;
                    AddBeam(Vector3.zero, Vector3.zero, beamVisuals, 0);
                    break;
                default:
                    AddBeam(new Vector3(lateral - half, h, 0f), new Vector3(lateral + half, h, 0f), beamVisuals, 0);
                    break;
            }

            ComputeBounds();
            UpdateBeams();
            placed = true;
            if (Application.isPlaying && isActiveAndEnabled) PickupRegistry.Register(this);
        }

        void AddBeam(Vector3 a, Vector3 b, IReadOnlyList<LaserBeam> visuals, int index)
        {
            var visual = visuals != null && index < visuals.Count ? visuals[index] : null;
            beams.Add(new Beam { a = a, b = b, visual = visual });
        }

        // The broad phase's box: everything the beams can ever reach (the
        // rotor sweeps a disc), grown by the beam's own thickness.
        void ComputeBounds()
        {
            float r = definition.beamRadius;
            if (isRotor)
            {
                boundsCentre = rotorCentre;
                boundsHalf = new Vector3(rotorHalfLength + r, r + waveAmplitude, rotorHalfLength + r);
                return;
            }

            var bounds = new Bounds(beams[0].a, Vector3.zero);
            foreach (var beam in beams) { bounds.Encapsulate(beam.a); bounds.Encapsulate(beam.b); }
            boundsCentre = bounds.center;
            boundsHalf = bounds.extents + Vector3.one * r + WaveReach;
        }

        // A wavy beam swings along the track's up — across the track when the
        // beam itself stands up (LaserBeam picks the same axis) — so the
        // zigzag's whole envelope burns, not just its axis.
        Vector3 WaveReach => Variant == LaserGateVariant.Vertical
            ? new Vector3(waveAmplitude, 0f, 0f)
            : new Vector3(0f, waveAmplitude, 0f);

        /// <summary>How far the gate reaches along the track either side of its distance (the generator's footprint).</summary>
        public float HalfDepth => boundsHalf.z;

        // One clock for the picture and the test. Scaled time: a pause stops the blade.
        float RotorAngle => (rotorPhase + rotorSpeed * Time.time) * Mathf.Deg2Rad;

        void GetSegment(int index, out Vector3 a, out Vector3 b)
        {
            if (!isRotor) { a = beams[index].a; b = beams[index].b; return; }
            float angle = RotorAngle;
            var arm = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * rotorHalfLength;
            a = rotorCentre - arm;
            b = rotorCentre + arm;
        }

        void Update() => UpdateBeams();

        void UpdateBeams()
        {
            Transform root = transform;
            for (int i = 0; i < beams.Count; i++)
            {
                if (beams[i].visual == null) continue;
                GetSegment(i, out Vector3 a, out Vector3 b);
                beams[i].visual.SetEndpoints(root.TransformPoint(a), root.TransformPoint(b), root.up, root.forward);
            }
        }

        /// <summary>
        /// True when a body that just flew <paramref name="fromDistance"/> →
        /// <paramref name="toDistance"/> at this lateral and height went
        /// through a beam. <paramref name="reach"/> is the body's half width
        /// and half height.
        /// </summary>
        public bool Touches(float fromDistance, float toDistance, float lateral, float height, Vector2 reach)
        {
            if (!placed || Time.time - lastHitTime < RehitSeconds) return false;

            float r = definition.beamRadius;
            // Squash the space so "within the ship's box + the beam" is "within 1".
            Vector3 wave = WaveReach;
            var scale = new Vector3(1f / (reach.x + r + wave.x), 1f / (reach.y + r + wave.y), 1f / (reach.x + r));
            Vector3 p0 = Vector3.Scale(new Vector3(lateral, height, fromDistance - trackDistance), scale);
            Vector3 p1 = Vector3.Scale(new Vector3(lateral, height, toDistance - trackDistance), scale);

            for (int i = 0; i < beams.Count; i++)
            {
                GetSegment(i, out Vector3 a, out Vector3 b);
                if (SegmentDistanceSqr(p0, p1, Vector3.Scale(a, scale), Vector3.Scale(b, scale)) <= 1f) return true;
            }
            return false;
        }

        /// <summary>The ship went through a beam: tell the listeners, once per pass.</summary>
        public void RaiseHit(ShipMotor motor)
        {
            if (motor == null) return;
            lastHitTime = Time.time;
            Hit?.Invoke(this, motor);
        }

        // Closest approach of two segments, squared (Ericson, Real-Time
        // Collision Detection 5.1.9), degenerate segments included — a body
        // at a standstill sweeps a point.
        static float SegmentDistanceSqr(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            const float Epsilon = 1e-8f;

            if (a <= Epsilon && e <= Epsilon) return r.sqrMagnitude;
            if (a <= Epsilon)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= Epsilon)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > Epsilon ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return ((p1 + d1 * s) - (p2 + d2 * t)).sqrMagnitude;
        }

        void OnEnable()
        {
            if (placed && Application.isPlaying) PickupRegistry.Register(this);
        }

        void OnDisable() => PickupRegistry.Unregister(this);
    }
}
