using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Laser gates: emitter pairs firing a beam across part of the road that
    /// burns the hull and bleeds speed off a ship flying through it. A
    /// ground claimer — it runs before the pickups and claims its stretch, so
    /// no orb or coin sits in a beam. A gate is rolled first (variant and beam
    /// length decide how much track it takes), then tested against the
    /// keep-outs and pushed on if it does not fit: never on or near a ramp,
    /// its landing zone, a loop, a tube or the final run-up (flat sweeps and
    /// open edges are the two toggles). Play mode only: the beams are runtime
    /// objects, and detection is analytic (<see cref="LaserGate"/>).
    /// </summary>
    [CreateAssetMenu(fileName = "Spawner_LaserGates", menuName = "FiniteRunner/Spawners/Laser Gates")]
    public class LaserGateSpawner : TrackSpawner
    {
        [Tooltip("The emitter pair (PF_LaserSystem): LaserA and LaserB, each with a ShootPoint child, carrying a LaserBeam. One instance per beam — three for the triple gate.")]
        [SerializeField] GameObject laserPrefab;

        [Tooltip("Beam size, variant weights, rotor speed and the look. Cloned at play, so the asset is never edited by a run.")]
        [InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        [SerializeField] LaserGateDefinition definition;

        [Tooltip("Clear road kept between a gate and every feature (ramp + its longest landing, loop, tube), flat sweep and the final run-up, either side. Must stay under the settle margin (two segments), which is what guarantees no feature is decided behind an already placed gate.")]
        [PropertyRange(0f, 500f), SuffixLabel("m", true)]
        [SerializeField] float clearance = 150f;

        [Tooltip("Gates may stand on a FLAT sweep (no bank, the outer wall gone, grip tested). Off = the clearance is kept round every flat sweep too — on a track authored with mostly flat sweeps that leaves few spots.")]
        [SerializeField] bool onFlatSweeps = true;

        [Tooltip("Gates may stand where the road has no wall (an open straight, the outer edge of a flat sweep). Off = walled road only.")]
        [SerializeField] bool onOpenEdges = true;

        [System.NonSerialized] LaserGateDefinition runtime;

        public override SpawnPhase Phase => SpawnPhase.ClaimsGround;

        public override bool IsActive(TrackSpawnContext ctx) =>
            base.IsActive(ctx) && Application.isPlaying && laserPrefab != null && runtime != null && runtime.TotalWeight > 0f;

        protected override void OnBegin(TrackSpawnContext ctx)
        {
            if (runtime != null && runtime != definition) DestroyRuntime(runtime);
            runtime = definition != null && Application.isPlaying ? Instantiate(definition) : definition;
        }

        public override void Cleanup()
        {
            if (runtime != null && runtime != definition) DestroyRuntime(runtime);
            runtime = null;
        }

        protected override float Step(TrackSpawnContext ctx, float distance, float limit)
        {
            LaserGateVariant variant = runtime.PickVariant(Rng.NextFloat());
            ctx.Track.GetLateralBand(distance, out float bandMin, out float bandMax);
            float length = (bandMax - bandMin) * Rng.NextFloat(runtime.CoverageMin, runtime.CoverageMax);
            // Only the rotor has depth: its blade sweeps a disc of road.
            float halfDepth = runtime.beamRadius + (variant == LaserGateVariant.Rotor ? length * 0.5f : 0f);

            float blockedUntil = ctx.KeepOutUntil(distance, halfDepth + clearance, onFlatSweeps, onOpenEdges);
            if (blockedUntil >= 0f) return float.IsPositiveInfinity(blockedUntil) ? blockedUntil : Mathf.Max(blockedUntil, distance + 10f);

            CreateGate(ctx, distance, variant, length, halfDepth, bandMin, bandMax);
            return -1f;
        }

        /// <summary>
        /// One gate: a root on the track pose at the flight line's middle, a
        /// laser prefab instance per beam under it, and the <see cref="LaserGate"/>
        /// that owns the beams' track-space segments. The beam's lateral is
        /// rolled so the whole beam (and the emitters' bulk) stays in the lane.
        /// </summary>
        void CreateGate(TrackSpawnContext ctx, float distance, LaserGateVariant variant, float length, float halfDepth, float bandMin, float bandMax)
        {
            float halfAcross = variant == LaserGateVariant.Vertical ? runtime.beamRadius : length * 0.5f;
            float margin = halfAcross + 2f * runtime.emitterScale; // the emitter is ~2 m long at scale 1
            float lo = bandMin + margin, hi = bandMax - margin;
            float lateral = hi > lo ? Rng.NextFloat(lo, hi) : (bandMin + bandMax) * 0.5f;
            float rotorSpeed = Rng.NextFloat(runtime.RotorSpeedMin, runtime.RotorSpeedMax) * (Rng.NextBool() ? 1f : -1f);
            float rotorPhase = Rng.NextFloat(0f, 360f);
            bool wavy = runtime.wavy && Rng.NextFloat() < runtime.waveChance; // no draw while the wave is off

            ctx.Track.GetPoseAtDistance(distance, 0f, out Vector3 pos, out Quaternion rot);
            var root = new GameObject($"LaserGate_{variant}_{distance:00000}");
            root.transform.SetParent(ctx.Parent, false);
            root.transform.SetPositionAndRotation(pos, rot);

            int beamCount = variant == LaserGateVariant.Triple ? 3 : 1;
            var visuals = new List<LaserBeam>(beamCount);
            for (int i = 0; i < beamCount; i++)
            {
                GameObject instance = Instantiate(laserPrefab, root.transform);
                instance.name = $"Beam_{i}";
                instance.transform.localPosition = Vector3.zero; // the emitters are posed in world space by the beam
                instance.transform.localRotation = Quaternion.identity;
                var beam = instance.GetComponent<LaserBeam>();
                if (beam == null) beam = instance.AddComponent<LaserBeam>();
                beam.Configure(runtime, wavy);
                visuals.Add(beam);
            }

            var gate = root.AddComponent<LaserGate>();
            gate.Configure(runtime, variant, distance, lateral, length, rotorSpeed, rotorPhase, visuals, wavy);

            ctx.Register(distance + halfDepth, root); // keyed on its END, like everything that spans track
            ctx.Claim(distance - halfDepth, distance + halfDepth);
        }
    }
}
