using System.Collections.Generic;
using System.Text;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Ship;
namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Proves the physical track against the mathematical one, using the ship
    /// that is known to be right. While the track-space ship flies, every
    /// frame its WORLD position is pushed through <see cref="TrackGuide"/> and
    /// the answer is compared with the track coordinates it actually has
    /// (distance, lateral — modulo the circumference round a full tube), and a
    /// ray is dropped from it onto the streamed colliders, which must lie
    /// exactly the builder's sink below the flight line it rides. Worst errors
    /// are kept per ship state, so a loop, a tube and a ramp each answer for
    /// themselves. Debug tooling: add it beside the guide and the builder,
    /// read <see cref="Report"/>.
    /// </summary>
    public sealed class TrackGuideValidator : MonoBehaviour
    {
        [SerializeField] ShipMotor motor;
        [SerializeField] TrackGuide guide;
        [SerializeField] TrackColliderBuilder colliders;

        struct Worst
        {
            public float distance, lateral, surface, surfaceAt;
            public int frames, misses, noGround;
        }

        readonly Dictionary<ShipState, Worst> worst = new();
        float hint = float.NaN;
        int rampFrames, rampNoGround;
        float rampWorstSurface, rampWorstAt;

        public void Bind(ShipMotor motor, TrackGuide guide, TrackColliderBuilder colliders)
        {
            this.motor = motor;
            this.guide = guide;
            this.colliders = colliders;
        }

        public string Report
        {
            get
            {
                var text = new StringBuilder();
                foreach (KeyValuePair<ShipState, Worst> pair in worst)
                    text.AppendLine($"{pair.Key}: {pair.Value.frames} frames, worst distance error {pair.Value.distance:F3} m, lateral {pair.Value.lateral:F3} m, " +
                                    $"surface {pair.Value.surface:F3} m (at d = {pair.Value.surfaceAt:F0}), projection misses {pair.Value.misses}, no collider under the ship {pair.Value.noGround}");
                text.AppendLine($"on a ramp's slope: {rampFrames} frames, worst surface {rampWorstSurface:F3} m (at d = {rampWorstAt:F0}), no collider under the ship {rampNoGround}" +
                                (colliders != null ? $"; ramp wedges built {colliders.RampsBuilt}, alive {colliders.RampCount}" : ""));
                return text.ToString();
            }
        }

        // LateUpdate: the motor has posed the ship for this frame, from the very coordinates it reports.
        void LateUpdate()
        {
            if (motor == null || guide == null || motor.Paused) return;
            ShipState state = motor.State;
            // Out of play the ship is flown in world space, off the track on purpose.
            if (state == ShipState.OffTrack || state == ShipState.Falling || state == ShipState.Respawning) { hint = float.NaN; return; }

            worst.TryGetValue(state, out Worst w);
            w.frames++;

            Transform ship = motor.transform;
            // A real ship projects every few metres with its last answer as the hint. This runs once a
            // FRAME — a hundred metres apart at speed — so it starts a little behind the truth instead,
            // which is the same search; what is judged is where it converges.
            hint = Mathf.Max(0f, motor.DistanceTravelled - 15f);
            if (guide.TryProject(ship.position, ref hint, out GuideSample sample))
            {
                w.distance = Mathf.Max(w.distance, Mathf.Abs(sample.distance - motor.DistanceTravelled));
                float across = sample.lateral - motor.LateralOffset;
                if (motor.Track != null && motor.Track.SectionAt(motor.DistanceTravelled) is TubeSection { Unbounded: true } tube)
                    across = Mathf.Repeat(across + tube.Circumference * 0.5f, tube.Circumference) - tube.Circumference * 0.5f;
                w.lateral = Mathf.Max(w.lateral, Mathf.Abs(across));
            }
            else
            {
                w.misses++;
                hint = float.NaN;
            }

            // On the road (or a ramp's slope) the colliders must lie exactly the sink below the ship's root.
            if (colliders != null && state != ShipState.Airborne && motor.DistanceTravelled < colliders.BuiltDistance)
            {
                Vector3 up = ship.up;
                bool onRamp = motor.CurrentRamp != null;
                if (onRamp) rampFrames++;
                if (Physics.Raycast(ship.position + up, -up, out RaycastHit hit, colliders.SurfaceSink + 6f, ShipLayers.GroundMask, QueryTriggerInteraction.Ignore))
                {
                    float off = Mathf.Abs(hit.distance - 1f - colliders.SurfaceSink);
                    if (onRamp) { if (off > rampWorstSurface) { rampWorstSurface = off; rampWorstAt = motor.DistanceTravelled; } }
                    else if (off > w.surface) { w.surface = off; w.surfaceAt = motor.DistanceTravelled; }
                }
                else if (onRamp) rampNoGround++;
                else w.noGround++;
            }
            worst[state] = w;
        }
    }
}
