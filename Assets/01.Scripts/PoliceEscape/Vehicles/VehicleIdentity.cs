using System;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// What kind of car a <see cref="CarController"/> is — one value per model
    /// the game can put on the road (the EVP demo cars, the Cyberpunk kit
    /// cars and the Kenney toys), so gameplay and debugging can ask "which
    /// car is this?" without pattern-matching object names.
    /// </summary>
    public enum VehicleKind
    {
        Unknown = 0,
        SportCoupe,
        SportCoupeDrift,
        L200,
        Bus,
        Taxi,
        Quadron,
        Minivan,
        Sedan,
        Van,
        Suv,
        SuvLuxury,
        Hatchback,
        Truck,
        TruckFlat,
        Delivery,
        GarbageTruck,
    }

    /// <summary>The car's paint as a NAMED colour, the value objectives and dialogue can reason about ("the red coupe").</summary>
    public enum VehiclePaint
    {
        Unknown = 0,
        White,
        Black,
        Silver,
        Grey,
        Red,
        Blue,
        Green,
        Yellow,
        Orange,
        Purple,
    }

    /// <summary>
    /// The identity every car carries on its <see cref="CarController"/>: the
    /// kind of vehicle, its named paint, and the actual paint colour as a
    /// swatch for UI or tinting. Prefab cars author it on the prefab; cars
    /// rigged at spawn from a bare model get it stamped from their
    /// <c>TrafficVehicleDefinition</c>. <see cref="IsSet"/> is the one rule
    /// consumers need: an <see cref="VehicleKind.Unknown"/> kind means nobody
    /// ever assigned an identity, so a definition's own may override it.
    /// </summary>
    [Serializable]
    public struct VehicleIdentity
    {
        [Tooltip("Which car this is.")]
        public VehicleKind kind;

        [Tooltip("The paint as a named colour, for gameplay logic.")]
        public VehiclePaint paint;

        [Tooltip("The paint as an actual colour — a swatch for UI, minimap icons or tinting.")]
        public Color color;

        public VehicleIdentity(VehicleKind kind, VehiclePaint paint, Color color)
        {
            this.kind = kind;
            this.paint = paint;
            this.color = color;
        }

        /// <summary>True once a kind has been assigned — an Unknown kind is an unassigned identity, not a kind of its own.</summary>
        public bool IsSet => kind != VehicleKind.Unknown;

        public override string ToString() => IsSet ? $"{paint} {kind}" : "unknown vehicle";

        /// <summary>
        /// Does this car fit a filter? <see cref="VehicleKind.Unknown"/> /
        /// <see cref="VehiclePaint.Unknown"/> in the filter mean "any", so an
        /// all-Unknown filter matches every car, identity or not; a set
        /// filter part must be equalled exactly.
        /// </summary>
        public bool Matches(VehicleKind kindFilter, VehiclePaint paintFilter) =>
            (kindFilter == VehicleKind.Unknown || kind == kindFilter)
            && (paintFilter == VehiclePaint.Unknown || paint == paintFilter);

        /// <summary>"SportCoupe" → "SPORT COUPE": the enum name spaced out, upper-case, for HUD readouts.</summary>
        public static string DisplayName(System.Enum value)
        {
            string name = value.ToString();
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i])) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(name[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// A filter as words for a HUD line or a dialogue clause — "RED TRUCK",
        /// "BUS", "RED", or the given fallback when both parts are "any".
        /// </summary>
        public static string Describe(VehicleKind kindFilter, VehiclePaint paintFilter, string any)
        {
            string paint = paintFilter != VehiclePaint.Unknown ? DisplayName(paintFilter) : null;
            string kind = kindFilter != VehicleKind.Unknown ? DisplayName(kindFilter) : null;
            if (paint == null && kind == null) return any;
            if (paint == null) return kind;
            if (kind == null) return paint;
            return paint + " " + kind;
        }
    }
}
