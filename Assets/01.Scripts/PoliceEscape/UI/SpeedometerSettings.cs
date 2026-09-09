using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// Every look/feel knob of the speedometer gauge in one designer-facing
    /// asset: layout, gauge scale/sweep/ticks, needle behavior and the
    /// digital readout. Drawn inline by the Speedometer so it's tuned live
    /// in play mode. Gauge geometry (size, sweep, ticks) is built once at
    /// startup; colors, needle feel and the readout apply live.
    /// </summary>
    [CreateAssetMenu(fileName = "SpeedometerSettings", menuName = "PoliceEscape/Speedometer Settings")]
    public class SpeedometerSettings : ScriptableObject
    {
        // -------------------------------------------------------------- layout
        [TitleGroup("Layout")]
        [Tooltip("Diameter of the gauge, in reference-resolution (1920×1080) pixels.")]
        [PropertyRange(100f, 500f), SuffixLabel("px", true)]
        public float sizePixels = 230f;

        [TitleGroup("Layout")]
        [Tooltip("Gap between the gauge and the bottom-left screen corner.")]
        [PropertyRange(0f, 100f), SuffixLabel("px", true)]
        public float marginPixels = 24f;

        [TitleGroup("Layout")]
        [Tooltip("Width of the ring drawn around the gauge.")]
        [PropertyRange(0f, 20f), SuffixLabel("px", true)]
        public float borderWidth = 6f;

        [TitleGroup("Layout")]
        [Tooltip("Color of the ring around the gauge.")]
        public Color borderColor = new(0.05f, 0.05f, 0.08f, 0.9f);

        [TitleGroup("Layout")]
        [Tooltip("Gauge face color.")]
        public Color backgroundColor = new(0.07f, 0.08f, 0.1f, 0.82f);

        // --------------------------------------------------------------- gauge
        [TitleGroup("Gauge")]
        [Tooltip("Speed at the end of the dial. Not a limit — the needle just pegs there.")]
        [PropertyRange(60f, 400f), SuffixLabel("km/h", true)]
        public float maxSpeedKmh = 220f;

        [TitleGroup("Gauge")]
        [Tooltip("Angular travel of the needle from 0 to max, centered on straight up.")]
        [PropertyRange(120f, 330f), SuffixLabel("°", true)]
        public float sweepDegrees = 270f;

        [TitleGroup("Gauge")]
        [Tooltip("One tick mark per this many km/h.")]
        [PropertyRange(5f, 50f), SuffixLabel("km/h", true)]
        public float tickIntervalKmh = 20f;

        [TitleGroup("Gauge")]
        [Tooltip("Tick mark color below the redline.")]
        public Color tickColor = new(0.8f, 0.85f, 0.9f, 0.9f);

        [TitleGroup("Gauge")]
        [Tooltip("Fraction of the dial where the red zone starts — ticks (and the readout at speed) turn the redline color.")]
        [PropertyRange(0.5f, 1f)]
        public float redlineFraction = 0.8f;

        [TitleGroup("Gauge")]
        public Color redlineColor = new(1f, 0.25f, 0.2f, 1f);

        // -------------------------------------------------------------- needle
        [TitleGroup("Needle")]
        public Color needleColor = new(1f, 0.3f, 0.25f, 1f);

        [TitleGroup("Needle")]
        [Tooltip("Needle length as a fraction of the gauge radius.")]
        [PropertyRange(0.4f, 1f)]
        public float needleLength = 0.78f;

        [TitleGroup("Needle")]
        [Tooltip("Needle thickness.")]
        [PropertyRange(2f, 12f), SuffixLabel("px", true)]
        public float needleWidth = 5f;

        [TitleGroup("Needle")]
        [Tooltip("How quickly the needle chases the real speed. Higher = stiffer.")]
        [PropertyRange(1f, 30f)]
        public float needleSharpness = 12f;

        // ------------------------------------------------------------- readout
        [TitleGroup("Readout")]
        [Tooltip("Show the digital km/h number under the needle hub.")]
        public bool showDigital = true;

        [TitleGroup("Readout")]
        public Color textColor = new(0.92f, 0.95f, 1f, 1f);

        // ----------------------------------------------------------- life ring
        // The player's life is the glitch corruption meter: LevelManager.ApplyDamage
        // raises GlitchController.baseIntensity on every police hit and blast,
        // and full corruption ends the run. The ring wraps the gauge's border
        // and shows what is LEFT, so the risk reads at a glance.
        [ToggleGroup(nameof(lifeRing), "Life Ring")]
        [Tooltip("Draw a segmented ring around the gauge showing the car's remaining life (1 − glitch corruption): green when whole, yellow worn, red when the next hit could end it. Segment count and thickness are built once; colours apply live.")]
        public bool lifeRing = true;

        [ToggleGroup(nameof(lifeRing))]
        [Tooltip("Arc segments around the ring. Fewer = chunkier, each hit visibly knocks segments out.")]
        [PropertyRange(6, 48)]
        public int lifeRingSegments = 24;

        [ToggleGroup(nameof(lifeRing))]
        [Tooltip("Ring thickness, just outside the border.")]
        [PropertyRange(4f, 30f), SuffixLabel("px", true)]
        public float lifeRingThickness = 12f;

        [ToggleGroup(nameof(lifeRing))]
        [Tooltip("Dark gap between neighbouring segments.")]
        [PropertyRange(0f, 8f), SuffixLabel("°", true)]
        public float lifeRingGapDegrees = 2.5f;

        [ToggleGroup(nameof(lifeRing))]
        [Tooltip("Ring colour at full life.")]
        public Color lifeFullColor = new(0.35f, 1f, 0.45f, 1f);

        [ToggleGroup(nameof(lifeRing))]
        [Tooltip("Ring colour at half life.")]
        public Color lifeMidColor = new(1f, 0.85f, 0.2f, 1f);

        [ToggleGroup(nameof(lifeRing))]
        [Tooltip("Ring colour as life runs out.")]
        public Color lifeLowColor = new(1f, 0.2f, 0.15f, 1f);

        [ToggleGroup(nameof(lifeRing))]
        [Tooltip("Life fraction under which the ring blinks — one more police hit (0.34 corruption) and the run ends.")]
        [PropertyRange(0f, 1f)]
        public float lifeLowFraction = 0.35f;

        [ToggleGroup(nameof(lifeRing))]
        [Tooltip("Alpha of a lost segment: the ring keeps its shape so the missing part reads as missing.")]
        [PropertyRange(0f, 0.5f)]
        public float lifeRingEmptyAlpha = 0.12f;

        [ToggleGroup(nameof(lifeRing))]
        [Tooltip("Scale the ring punches to on a hit before settling back. 1 = no punch.")]
        [PropertyRange(1f, 1.6f)]
        public float lifeHitPunch = 1.2f;

        [ToggleGroup(nameof(lifeRing))]
        [Tooltip("Full ring drawn under the segments so the gauge has a rim even at zero life. Alpha 0 = none.")]
        public Color lifeRingTrackColor = new(0f, 0f, 0f, 0.35f);
    }
}
