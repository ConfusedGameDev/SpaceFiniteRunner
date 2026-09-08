using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track.Layout
{
    /// <summary>What a piece of authored track is. Serialized — append only.</summary>
    public enum TrackPieceKind { Straight = 0, Loop = 1, Tube = 2, Ramp = 3 }

    /// <summary>What an authored pickup is. Serialized — append only.</summary>
    public enum TrackItemKind { Pad = 0, CoinRow = 1 }

    /// <summary>
    /// One piece of an authored track, in the order the builder lays them. A
    /// <b>Straight</b> is one knot: the builder state at its end (absolute
    /// heading, grade and bank, degrees) and the chord that reaches it —
    /// <see cref="pinned"/> when it is a feature's spot (an explicit-tangent
    /// knot). A <b>Loop</b> stands on the previous knot and continues the
    /// spline from its exit; a <b>Tube</b> overlays the spline laid after it;
    /// a <b>Ramp</b> sits on the road at the previous knot and is spawned per
    /// lap. Feature pieces name the generator's feature-table entry they came
    /// from, which supplies the prefab, colour and definition clone.
    /// </summary>
    [System.Serializable]
    public class TrackPiece
    {
        [EnumToggleButtons, HideLabel]
        public TrackPieceKind kind = TrackPieceKind.Straight;

        [ReadOnly, SuffixLabel("m", true)]
        [Tooltip("Track distance where the piece starts. Recorded by the builder; rebuilt on Build.")]
        public float distance;

        // ---- Straight: one knot ------------------------------------------
        [ShowIf("kind", TrackPieceKind.Straight)]
        [Tooltip("Chord from the previous knot to this one, metres.")]
        [PropertyRange(50f, 1000f), SuffixLabel("m", true)]
        public float chord = 360f;

        [ShowIf("kind", TrackPieceKind.Straight)]
        [Tooltip("Heading of the segment ending at this knot, degrees about world up (0 = the start direction).")]
        [PropertyRange(-360f, 360f), SuffixLabel("°", true)]
        public float heading;

        [ShowIf("kind", TrackPieceKind.Straight)]
        [Tooltip("Grade of the segment ending at this knot, degrees (up positive).")]
        [PropertyRange(-30f, 30f), SuffixLabel("°", true)]
        public float pitch;

        [ShowIf("kind", TrackPieceKind.Straight)]
        [Tooltip("Bank at this knot, degrees, right edge up positive.")]
        [PropertyRange(-89f, 89f), SuffixLabel("°", true)]
        public float bank;

        [ShowIf("kind", TrackPieceKind.Straight)]
        [Tooltip("An explicit-tangent knot (a feature's spot): its pose is fixed whatever lands next.")]
        public bool pinned;

        // ---- Features: the table entry ------------------------------------
        [HideIf("kind", TrackPieceKind.Straight)]
        [Tooltip("Name of the generator's feature-table entry (prefab, colour, definition).")]
        public string entryName;

        // ---- Loop ------------------------------------------------------------
        [ShowIf("kind", TrackPieceKind.Loop)]
        [PropertyRange(40f, 250f), SuffixLabel("m", true)]
        public float radius = 100f;

        [ShowIf("kind", TrackPieceKind.Loop)]
        [Tooltip("Sideways displacement of the exit, metres (signed).")]
        [PropertyRange(-600f, 600f), SuffixLabel("m", true)]
        public float lateralDrift;

        [ShowIf("kind", TrackPieceKind.Loop)]
        [Tooltip("Forward displacement of the exit, metres.")]
        [PropertyRange(0f, 1000f), SuffixLabel("m", true)]
        public float forwardCarry;

        [ShowIf("kind", TrackPieceKind.Loop)]
        [Tooltip("Heading change of the exit, degrees (signed).")]
        [PropertyRange(-60f, 60f), SuffixLabel("°", true)]
        public float exitYaw;

        [ShowIf("kind", TrackPieceKind.Loop)]
        [PropertyRange(1, 3)]
        public int turns = 1;

        [ShowIf("kind", TrackPieceKind.Loop)]
        [Tooltip("Entry speed the loop demands on lap 1, km/h. GameSettings adds loopSpeedPerLapKmh per completed lap.")]
        [PropertyRange(200f, 10000f), SuffixLabel("km/h", true)]
        public float requiredKmh = 1200f;

        // ---- Tube ------------------------------------------------------------
        [ShowIf("kind", TrackPieceKind.Tube)]
        [PropertyRange(200f, 8000f), SuffixLabel("m", true)]
        public float length = 2000f;

        [ShowIf("kind", TrackPieceKind.Tube)]
        [PropertyRange(20f, 150f), SuffixLabel("m", true)]
        public float tubeRadius = 60f;

        [ShowIf("kind", TrackPieceKind.Tube)]
        [PropertyRange(1f, 180f), SuffixLabel("°", true)]
        public float bandDegrees = 180f;

        [ShowIf("kind", TrackPieceKind.Tube)]
        [PropertyRange(-180f, 180f), SuffixLabel("°", true)]
        public float centreDegrees;

        [ShowIf("kind", TrackPieceKind.Tube)]
        [PropertyRange(0f, 500f), SuffixLabel("m", true)]
        public float curlLength = 150f;

        [ShowIf("kind", TrackPieceKind.Tube)]
        [PropertyRange(0f, 2000f), SuffixLabel("m", true)]
        public float returnLength = 500f;

        [ShowIf("kind", TrackPieceKind.Tube)]
        [PropertyRange(0.1f, 10f)]
        public float steeringFactor = 3f;

        // ---- Ramp (its size and angle come from the entry's Jump definition) ----
        [ShowIf("kind", TrackPieceKind.Ramp)]
        [Tooltip("Metres across the track from the centre line, right positive. Length, width and angle are the entry's Jump definition.")]
        [PropertyRange(-60f, 60f), SuffixLabel("m", true)]
        public float lateral;

        /// <summary>List label: distance and what the piece is.</summary>
        public string Label => kind switch
        {
            TrackPieceKind.Straight => $"{distance:0} m  Straight {chord:0} m  h{heading:0}° g{pitch:0}° b{bank:0}°{(pinned ? " ●" : "")}",
            TrackPieceKind.Loop => $"{distance:0} m  Loop R{radius:0} ×{turns}  drift {lateralDrift:0}  carry {forwardCarry:0}  yaw {exitYaw:0}°  {requiredKmh:0} km/h",
            TrackPieceKind.Tube => $"{distance:0} m  Tube {length:0} m  R{tubeRadius:0}  ±{bandDegrees:0}°",
            TrackPieceKind.Ramp => $"{distance:0} m  Ramp  lat {lateral:0}",
            _ => $"{distance:0} m  {kind}",
        };
    }

    /// <summary>
    /// One authored pickup: a pad / orb from the generator's pad table (by
    /// entry name — the table supplies the definition, boost multiplier,
    /// colour and sway) or a row of coins. Placed at a track distance and a
    /// lateral, and respawned on every lap.
    /// </summary>
    [System.Serializable]
    public class TrackItem
    {
        [EnumToggleButtons, HideLabel]
        public TrackItemKind kind = TrackItemKind.Pad;

        [Tooltip("Track distance of the item (a coin row's first coin), metres.")]
        [SuffixLabel("m", true)]
        public float distance;

        [Tooltip("Metres across the track from the centre line, right positive.")]
        [PropertyRange(-200f, 200f), SuffixLabel("m", true)]
        public float lateral;

        [ShowIf("kind", TrackItemKind.Pad)]
        [Tooltip("Name of the generator's pad-table entry (Green, Blue, Purple, Brake…).")]
        public string entryName = "Green";

        [ShowIf("kind", TrackItemKind.Pad)]
        public PadLane lane = PadLane.Ground;

        [ShowIf("kind", TrackItemKind.CoinRow)]
        [PropertyRange(1, 10)]
        public int count = 3;

        [ShowIf("kind", TrackItemKind.CoinRow)]
        [PropertyRange(5f, 100f), SuffixLabel("m", true)]
        public float step = 30f;

        [ShowIf("kind", TrackItemKind.CoinRow)]
        [Tooltip("Money per coin.")]
        [PropertyRange(1, 500)]
        public int value = 10;

        public string Label => kind == TrackItemKind.Pad
            ? $"{distance:0} m  {entryName}  lat {lateral:0}{(lane == PadLane.Air ? "  (air)" : "")}"
            : $"{distance:0} m  {count} coins × {value}  lat {lateral:0}";
    }

    /// <summary>
    /// An authored runner track: the pieces the builder lays in order and the
    /// pickups on them, plus the knobs the GENERATE button seeds them with.
    /// The generator runs on it in two ways — <b>Generate</b> replays the
    /// procedural builder while RECORDING every knot, feature and pickup into
    /// these lists and then closes the loop; <b>Build</b> replays the lists
    /// deterministically (no rng) into knots and sections, which is what the
    /// runner scene does on its first frame under the glitch. Items and
    /// road-bound features are streamed around the ship per lap, so every
    /// lap plays the same gauntlet. One layout per runner level
    /// (<c>RunnerLevelDefinition.trackLayout</c>); the Track's generator holds
    /// the default. Never mutated at play.
    /// </summary>
    [CreateAssetMenu(fileName = "TrackLayout", menuName = "FiniteRunner/Track Layout")]
    public class TrackLayout : ScriptableObject
    {
        [TitleGroup("Generate")]
        [Tooltip("0 = a different circuit every Generate; any other value repeats it.")]
        public int seed;

        [TitleGroup("Generate")]
        [Tooltip("Length the Generate button fills to before closing the loop, km. The closing leg adds a little.")]
        [PropertyRange(5f, 60f), SuffixLabel("km", true)]
        public float targetLengthKm = 20f;

        [TitleGroup("Generate")]
        [Tooltip("Plain road reserved at the end for steering back to the start, km. Larger closes more gently.")]
        [PropertyRange(1f, 10f), SuffixLabel("km", true)]
        public float closingDistanceKm = 4f;

        [TitleGroup("Generate")]
        [Tooltip("Close the spline into a circuit (laps). Off = an open authored track that ends.")]
        public bool closed = true;

        [TitleGroup("Generate")]
        [Tooltip("Full width of the track, metres — the steering clamp, pad bounds and road meshes.")]
        [PropertyRange(10f, 120f), SuffixLabel("m", true)]
        public float trackWidth = 120f;

        [TitleGroup("Generate")]
        [Tooltip("Chance a straight knot starts a banked sweep: 100% = never.")]
        [PropertyRange(0f, 100f), SuffixLabel("%", true)]
        public float straightness = 60f;

        [TitleGroup("Generate")]
        [Tooltip("Jump strength the ramp landing zones are sized for (the Store raises the ship's; the circuit is fixed at authoring, so size for the strongest ship you expect).")]
        [PropertyRange(1f, 3f)]
        public float jumpStrengthAllowance = 1.5f;

        [TitleGroup("Generate")]
        [Tooltip("Road shape (elevation, sweeps, banking) for Generate. Empty = the generator's own asset.")]
        [InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        public TrackShapeSettings shape;

        [TitleGroup("Circuit")]
        [ReadOnly, SuffixLabel("m", true)]
        [Tooltip("Track length of one lap as last built (closing curve included).")]
        public float length;

        [TitleGroup("Circuit")]
        [ReadOnly]
        public int knotCount;

        [TitleGroup("Pieces")]
        [ListDrawerSettings(ShowIndexLabels = true, ListElementLabelName = nameof(TrackPiece.Label))]
        public List<TrackPiece> pieces = new();

        [TitleGroup("Items")]
        [ListDrawerSettings(ShowIndexLabels = true, ListElementLabelName = nameof(TrackItem.Label))]
        public List<TrackItem> items = new();

        /// <summary>True once Generate (or a hand edit) has given it pieces to build.</summary>
        public bool IsAuthored => pieces != null && pieces.Count > 0;

        public float TargetLength => targetLengthKm * 1000f;
        public float ClosingDistance => closingDistanceKm * 1000f;

        /// <summary>Drops every piece and item (Generate starts here).</summary>
        public void Clear()
        {
            pieces.Clear();
            items.Clear();
            length = 0f;
            knotCount = 0;
        }
    }
}
