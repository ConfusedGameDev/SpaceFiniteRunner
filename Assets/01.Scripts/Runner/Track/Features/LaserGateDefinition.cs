using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>The four shapes a <see cref="LaserGate"/> comes in. Serialized nowhere, but keep it append-only anyway.</summary>
    public enum LaserGateVariant { Horizontal, Vertical, Triple, Rotor }

    /// <summary>
    /// Tunables of the laser gates: a pair of emitters (the
    /// <c>PF_LaserSystem</c> prefab) firing a beam between their shoot points
    /// that burns the hull of a ship flying through it. A gate never spans the
    /// track — a beam covers <see cref="coverageBand"/> of the lane width, so
    /// the way past is always to steer round it (the ship only leaves the
    /// ground at ramps). Four variants, drawn by weight: one horizontal beam
    /// on the flight line, one vertical beam, three horizontal beams stacked
    /// one above another, and a flat ROTOR — a horizontal beam on the flight
    /// line turning about the track's up like a helicopter blade. Not a
    /// <see cref="TrackFeatureDefinition"/>: gates are streamed on a cursor of
    /// their own, denser than the features and off every feature's ground.
    /// Play runs on a runtime clone, never the asset.
    /// </summary>
    [CreateAssetMenu(fileName = "LaserGate_Definition", menuName = "FiniteRunner/Laser Gate Definition")]
    public class LaserGateDefinition : ScriptableObject
    {
        [TitleGroup("Beam")]
        [Tooltip("Beam length as a fraction of the lane width (min, max), rolled per gate. Small on purpose: a gate is steered round, never jumped.")]
        [MinMaxSlider(0.05f, 0.6f, true)]
        public Vector2 coverageBand = new(0.2f, 0.3f);

        [TitleGroup("Beam")]
        [Tooltip("Half thickness of the beam that burns, metres. The ship's own half width and half height are added on.")]
        [PropertyRange(0.1f, 4f), SuffixLabel("m", true)]
        public float beamRadius = 0.8f;

        [TitleGroup("Beam")]
        [Tooltip("Height of a horizontal beam's axis above the flight line, metres. 0 = dead on the ship.")]
        [PropertyRange(-1f, 5f), SuffixLabel("m", true)]
        public float beamHeight = 0f;

        [TitleGroup("Variants")]
        [Tooltip("Relative weight of the single horizontal beam. 0 = never.")]
        [PropertyRange(0f, 10f)]
        public float horizontalWeight = 4f;

        [TitleGroup("Variants")]
        [Tooltip("Relative weight of the single vertical beam. 0 = never.")]
        [PropertyRange(0f, 10f)]
        public float verticalWeight = 2f;

        [TitleGroup("Variants")]
        [Tooltip("Relative weight of three horizontal beams one above another. 0 = never.")]
        [PropertyRange(0f, 10f)]
        public float tripleWeight = 2f;

        [TitleGroup("Variants")]
        [Tooltip("Relative weight of the flat rotor. 0 = never.")]
        [PropertyRange(0f, 10f)]
        public float rotorWeight = 2f;

        [TitleGroup("Variants")]
        [Tooltip("Triple: metres between one beam and the one above it. The upper two catch a ship in the air.")]
        [PropertyRange(2f, 20f), SuffixLabel("m", true)]
        public float tripleSpacing = 5f;

        [TitleGroup("Variants")]
        [Tooltip("Vertical: how far above the flight line the beam reaches, metres. It starts at the road.")]
        [PropertyRange(5f, 60f), SuffixLabel("m", true)]
        public float verticalHeight = 20f;

        [TitleGroup("Variants")]
        [Tooltip("Rotor: turn rate (min, max), degrees per second, rolled per gate; the direction is a coin toss.")]
        [MinMaxSlider(10f, 360f, true), SuffixLabel("°/s", true)]
        public Vector2 rotorSpeedBand = new(40f, 90f);

        [TitleGroup("Emitters")]
        [Tooltip("Scale of the emitter models. They are authored about 1.2 m across, which is a speck on a 100 m road.")]
        [PropertyRange(0.5f, 20f)]
        public float emitterScale = 5f;

        [TitleGroup("Emitters")]
        [Tooltip("How fast the emitters roll about their barrel (their local Z) while firing, degrees per second. B rolls the other way.")]
        [PropertyRange(0f, 1440f), SuffixLabel("°/s", true)]
        public float emitterSpinDegPerSec = 360f;

        [TitleGroup("Emitters")]
        [Tooltip("Gap kept between the visible road and the lowest point an emitter can reach as it spins. A horizontal, triple or rotor gate is DRAWN lifted by whatever this takes (the burn stays on the flight line, where the ship's hit box is — the ship's model rides that much above it anyway); a vertical beam rises out of the road with its bottom emitter hidden.")]
        [PropertyRange(0f, 5f), SuffixLabel("m", true)]
        public float emitterRoadClearance = 0.5f;

        [TitleGroup("Look")]
        [Tooltip("Additive material of the beam (URP Particles/Unlit, vertex colour). Empty = one is built in code.")]
        public Material beamMaterial;

        [TitleGroup("Look")]
        [Tooltip("Colour of the wide outer glow.")]
        [ColorUsage(true, true)]
        public Color beamColor = new(4f, 0.12f, 0.08f, 0.85f);

        [TitleGroup("Look")]
        [Tooltip("Colour of the thin hot core.")]
        [ColorUsage(true, true)]
        public Color coreColor = new(4f, 0.9f, 0.7f, 1f);

        [TitleGroup("Look")]
        [Tooltip("Drawn width of the glow as a multiple of the burning thickness (the core is a third of it).")]
        [PropertyRange(0.5f, 6f)]
        public float glowWidthFactor = 2.5f;

        [TitleGroup("Look")]
        [Tooltip("How much the beam's width flickers, as a fraction of itself.")]
        [PropertyRange(0f, 0.8f)]
        public float flickerAmount = 0.25f;

        [TitleGroup("Look")]
        [Tooltip("Flickers per second.")]
        [PropertyRange(1f, 60f), SuffixLabel("Hz", true)]
        public float flickerFrequency = 22f;

        // --------------------------------------------------------------- wave
        [ToggleGroup("wavy", "Wave")]
        [Tooltip("Beams may zigzag: a TRIANGLE wave (a sine's rhythm with sharp corners) running along the beam. The wave swings along the track's up — across the track on the vertical gate — and what burns grows by the amplitude on that axis, so the picture never lies.")]
        public bool wavy;

        [ToggleGroup("wavy")]
        [Tooltip("Share of the gates that get the wave. 1 = every gate.")]
        [PropertyRange(0f, 1f)]
        public float waveChance = 1f;

        [ToggleGroup("wavy")]
        [Tooltip("How far the zigzag swings either side of the beam's axis, metres.")]
        [PropertyRange(0.1f, 6f), SuffixLabel("m", true)]
        public float waveAmplitude = 1.5f;

        [ToggleGroup("wavy")]
        [Tooltip("Length of one full zig + zag along the beam, metres.")]
        [PropertyRange(1f, 30f), SuffixLabel("m", true)]
        public float waveLength = 6f;

        [ToggleGroup("wavy")]
        [Tooltip("How fast the wave runs from A to B, wavelengths per second. Negative runs it back; 0 = a frozen zigzag.")]
        [PropertyRange(-10f, 10f)]
        public float waveSpeed = 2f;

        [ToggleGroup("wavy")]
        [Tooltip("Stretch at each muzzle over which the swing grows from nothing, metres, so the beam still leaves and enters the shoot points.")]
        [PropertyRange(0f, 10f), SuffixLabel("m", true)]
        public float waveTaper = 2f;

        // Paired values are read through these, never as .x / .y.
        public float CoverageMin => Mathf.Min(coverageBand.x, coverageBand.y);
        public float CoverageMax => Mathf.Max(coverageBand.x, coverageBand.y);
        public float RotorSpeedMin => Mathf.Min(rotorSpeedBand.x, rotorSpeedBand.y);
        public float RotorSpeedMax => Mathf.Max(rotorSpeedBand.x, rotorSpeedBand.y);

        /// <summary>Sum of the variant weights; 0 = no gate can be drawn.</summary>
        public float TotalWeight =>
            Mathf.Max(0f, horizontalWeight) + Mathf.Max(0f, verticalWeight)
            + Mathf.Max(0f, tripleWeight) + Mathf.Max(0f, rotorWeight);

        /// <summary>The variant a roll in 0..1 lands on, by weight.</summary>
        public LaserGateVariant PickVariant(float roll01)
        {
            float pick = Mathf.Clamp01(roll01) * TotalWeight;
            if ((pick -= Mathf.Max(0f, horizontalWeight)) < 0f) return LaserGateVariant.Horizontal;
            if ((pick -= Mathf.Max(0f, verticalWeight)) < 0f) return LaserGateVariant.Vertical;
            if ((pick -= Mathf.Max(0f, tripleWeight)) < 0f) return LaserGateVariant.Triple;
            if (rotorWeight > 0f) return LaserGateVariant.Rotor;
            // The roll fell off the end of a table whose last weights are 0.
            if (tripleWeight > 0f) return LaserGateVariant.Triple;
            if (verticalWeight > 0f) return LaserGateVariant.Vertical;
            return LaserGateVariant.Horizontal;
        }
    }
}
