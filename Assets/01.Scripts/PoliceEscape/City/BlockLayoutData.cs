using System;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// The serializable twin of <see cref="ChunkData"/>, stored on each baked
    /// block's <see cref="CityBlock"/> component inside the city prefab. This
    /// is what lets play mode skip generation entirely: the grid model that
    /// produced the baked GameObjects rides along with them, and the road
    /// graph is rebuilt from it at load — pure array walks, no RNG, no
    /// generator. Enum arrays are stored as bytes (both enums fit), features
    /// as a flat struct list; <see cref="ToChunkData"/> reconstructs an
    /// identical model.
    /// </summary>
    [Serializable]
    public sealed class BlockLayoutData
    {
        public Vector2Int coord;
        public int sizeInCells;
        public byte[] kinds;
        public byte[] connections;
        public byte[] upperConnections;
        public byte[] rampDirection;
        public byte[] rampStep;
        public byte[] rampLength;
        public int[] featureIndex;
        public Vector2[] centerOffset;
        public SerializedFeature[] features;

        [Serializable]
        public struct SerializedFeature
        {
            public byte kind;
            public int pieceIndex;
            public Vector2Int origin;
            public int quarterTurns;
            public Vector2Int footprint;
            public int variant;
        }

        /// <summary>True when the arrays match the declared size — false for a component that was never baked.</summary>
        public bool IsValid =>
            sizeInCells > 0
            && kinds != null && kinds.Length == sizeInCells * sizeInCells
            && connections != null && connections.Length == kinds.Length;

        public static BlockLayoutData From(ChunkData data)
        {
            int count = data.SizeInCells * data.SizeInCells;
            var layout = new BlockLayoutData
            {
                coord = data.Coord,
                sizeInCells = data.SizeInCells,
                kinds = new byte[count],
                connections = new byte[count],
                upperConnections = new byte[count],
                rampDirection = (byte[])data.RawRampDirection.Clone(),
                rampStep = (byte[])data.RawRampStep.Clone(),
                rampLength = (byte[])data.RawRampLength.Clone(),
                featureIndex = (int[])data.RawFeatureIndex.Clone(),
                centerOffset = (Vector2[])data.RawCenterOffset.Clone(),
                features = new SerializedFeature[data.Features.Count],
            };
            for (int i = 0; i < count; i++)
            {
                layout.kinds[i] = (byte)data.RawKinds[i];
                layout.connections[i] = (byte)data.RawConnections[i];
                layout.upperConnections[i] = (byte)data.RawUpperConnections[i];
            }
            for (int i = 0; i < data.Features.Count; i++)
            {
                RoadFeature feature = data.Features[i];
                layout.features[i] = new SerializedFeature
                {
                    kind = (byte)feature.Kind,
                    pieceIndex = feature.PieceIndex,
                    origin = feature.Origin,
                    quarterTurns = feature.QuarterTurns,
                    footprint = feature.Footprint,
                    variant = feature.Variant,
                };
            }
            return layout;
        }

        public ChunkData ToChunkData()
        {
            if (!IsValid) return null;
            var data = new ChunkData(coord, sizeInCells);
            int count = sizeInCells * sizeInCells;
            for (int i = 0; i < count; i++)
            {
                data.RawKinds[i] = (ChunkData.CellKind)kinds[i];
                data.RawConnections[i] = (EdgeMask)connections[i];
                data.RawUpperConnections[i] = (EdgeMask)upperConnections[i];
                data.RawRampDirection[i] = rampDirection[i];
                data.RawRampStep[i] = rampStep[i];
                data.RawRampLength[i] = rampLength[i];
                data.RawFeatureIndex[i] = featureIndex[i];
                data.RawCenterOffset[i] = centerOffset[i];
            }
            if (features != null)
            {
                foreach (SerializedFeature feature in features)
                {
                    data.Features.Add(new RoadFeature(
                        (RoadFeatureKind)feature.kind, feature.pieceIndex, feature.origin,
                        feature.quarterTurns, feature.footprint, feature.variant));
                }
            }
            return data;
        }
    }
}
