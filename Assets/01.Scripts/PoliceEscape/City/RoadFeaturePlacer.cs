using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Second pass over a freshly generated <see cref="ChunkData"/>: turns
    /// parts of the plain cell network into road <i>features</i>. Pure data,
    /// deterministic (own hash salt), no GameObjects.
    ///
    /// 1. <b>Overpasses</b> — at arterial crossings, a straight run becomes
    ///    ramp(s) → elevated deck cells → ramp(s) down. The deck crosses over
    ///    the perpendicular street (that cell keeps its ground road, reduced to
    ///    the perpendicular pair); ground under the other deck cells becomes
    ///    Reserved (pillar). Ramp cells stay ground nodes that climb.
    /// 2. <b>Forks</b> — a straight side street forks right after its
    ///    arterial junction: the road-split piece (one entrance, two exits)
    ///    sits on the seam between the street and an empty twin row, and both
    ///    branches rejoin the far arterial. Seam cells keep ordinary grid nodes
    ///    whose centres are shifted onto the seam.
    /// 3. <b>Templates</b> — multi-cell pieces (roundabout) are stamped
    ///    wherever their per-cell mask pattern matches, in any of four
    ///    rotations; cells the template needs empty become Reserved.
    ///
    /// Invariants the graph relies on: a cell never carries two roads on the
    /// same axis, ramp cells are plain straights, decks never sit over
    /// T-junctions, and every touched cell is at least one cell away from the
    /// chunk border (a Reserved arterial cell at the border would contradict
    /// what the neighbouring chunk computes for it).
    /// </summary>
    public static class RoadFeaturePlacer
    {
        const int SaltFeatures = 606;
        // Own stream for curved avenues, so tuning curve chances never
        // reshuffles the overpass/fork/template rolls.
        const int SaltCurves = 1013;
        const int BorderMargin = 1;
        // Junctions of a curve keep two cells of margin: their T masks are
        // real road masks the neighbours' contract must never see move.
        const int CurveJunctionMargin = 2;

        public static void Place(CityGenerationSettings settings, in BlockKnobs knobs, ChunkData data, int blockSeed)
        {
            if (settings == null || !knobs.PlaceFeatures) return;
            // Feature stream runs on the BLOCK seed: rerolling one block moves
            // its overpasses and forks without touching any neighbour.
            var rng = new System.Random(DeterministicHash.Combine(blockSeed, SaltFeatures, data.Coord.x, data.Coord.y));
            if (settings.HasOverpassPieces) PlaceOverpasses(settings, knobs.OverpassChance, data, rng);
            if (settings.HasForkPieces) PlaceForks(settings, knobs.ForkChance, data, rng);
            PlaceTemplates(settings, data, rng);
        }

        // ---------------------------------------------------------------- curves

        /// <summary>
        /// Curved avenues. Logically a curve is a monotone, 4-connected
        /// "staircase" of connector cells between two arterial junctions — so
        /// ChunkData, the socket masks and the border contract are untouched —
        /// while a seeded Bézier fitted between the junctions pulls every
        /// chain cell's centre offset onto the smooth line. The graph bakes
        /// those centres, so AI drives the curve with no driver changes; the
        /// builder chord-stamps road-straight pieces along the same line.
        /// Cells the ribbon sweeps but the chain doesn't occupy become
        /// Reserved so buildings never overlap the asphalt. Runs BEFORE the
        /// other features (the Curve flag makes chain cells feature cells, so
        /// overpasses/forks keep off) and strictly inside the border margin.
        /// </summary>
        public static void PlaceCurves(in BlockKnobs knobs, ChunkData data, int blockSeed)
        {
            if (knobs.MaxCurves <= 0 || knobs.CurveChance <= 0f) return;
            var rng = new System.Random(DeterministicHash.Combine(blockSeed, SaltCurves, data.Coord.x, data.Coord.y));
            for (int attempt = 0; attempt < knobs.MaxCurves; attempt++)
            {
                if (rng.NextDouble() >= knobs.CurveChance) continue;
                TryPlaceCurve(data, rng);
            }
        }

        static void TryPlaceCurve(ChunkData data, System.Random rng)
        {
            // Junction candidates: plain arterial straights, two cells inside the border.
            var straights = new List<Vector2Int>();
            for (int y = CurveJunctionMargin; y < data.SizeInCells - CurveJunctionMargin; y++)
            for (int x = CurveJunctionMargin; x < data.SizeInCells - CurveJunctionMargin; x++)
            {
                if (data.GetKind(x, y) != ChunkData.CellKind.Arterial || data.IsFeatureCell(x, y)) continue;
                EdgeMask mask = data.GetConnections(x, y);
                if (mask.ConnectionCount() == 2 && mask.RotateCw(2) == mask) straights.Add(new Vector2Int(x, y));
            }
            if (straights.Count < 2) return;

            Vector2Int entry = straights[rng.Next(straights.Count)];
            var exits = new List<Vector2Int>();
            foreach (Vector2Int c in straights)
            {
                // A real sweep needs room on both axes; same-line exits can't curve.
                if (Mathf.Abs(c.x - entry.x) >= 4 && Mathf.Abs(c.y - entry.y) >= 4) exits.Add(c);
            }
            if (exits.Count == 0) return;
            Vector2Int exit = exits[rng.Next(exits.Count)];

            bool entryVertical = IsVerticalStreet(data, entry);
            bool exitVertical = IsVerticalStreet(data, exit);
            int sx = exit.x > entry.x ? 1 : -1;
            int sy = exit.y > entry.y ? 1 : -1;

            // Seeded Bézier between the junction centres; the control point is
            // bowed off the midpoint but clamped into the AB box so the curve
            // stays monotone — which is what makes the staircase walk sound.
            Vector2 a = Center(entry), b = Center(exit);
            Vector2 dir = (b - a).normalized;
            var perp = new Vector2(-dir.y, dir.x);
            float bow = (0.15f + (float)rng.NextDouble() * 0.25f) * (b - a).magnitude * (rng.Next(2) == 0 ? 1f : -1f);
            Vector2 control = (a + b) * 0.5f + perp * bow;
            control.x = Mathf.Clamp(control.x, Mathf.Min(a.x, b.x) + 0.5f, Mathf.Max(a.x, b.x) - 0.5f);
            control.y = Mathf.Clamp(control.y, Mathf.Min(a.y, b.y) + 0.5f, Mathf.Max(a.y, b.y) - 0.5f);

            const int SampleCount = 128;
            var samples = new Vector2[SampleCount + 1];
            for (int i = 0; i <= SampleCount; i++)
            {
                float t = i / (float)SampleCount;
                float u = 1f - t;
                samples[i] = u * u * a + 2f * u * t * control + t * t * b;
            }

            // Greedy staircase toward the exit: of the two monotone steps, take
            // the cell that hugs the curve. First step must leave the entry
            // street sideways; last step must enter the exit street sideways.
            var chain = new List<Vector2Int> { entry };
            Vector2Int current = entry;
            Vector2Int lastStep = Vector2Int.zero;
            while (current != exit)
            {
                bool canX = current.x != exit.x;
                bool canY = current.y != exit.y;
                if (current == entry)
                {
                    canX &= entryVertical;
                    canY &= !entryVertical;
                }
                Vector2Int stepX = new(sx, 0), stepY = new(0, sy);
                // Never step ONTO the exit along its own street — that would be
                // a merge, not a junction. Pruning here lets the walk route the
                // other axis first instead of failing at the end.
                if (current + stepX == exit && !exitVertical) canX = false;
                if (current + stepY == exit && exitVertical) canY = false;
                Vector2Int step;
                if (canX && canY)
                    step = DistanceToCurve(Center(current + stepX), samples) <= DistanceToCurve(Center(current + stepY), samples)
                        ? stepX : stepY;
                else if (canX) step = stepX;
                else if (canY) step = stepY;
                else return;
                current += step;
                lastStep = step;
                chain.Add(current);
                if (chain.Count > data.SizeInCells * 4) return; // cannot happen on a monotone walk; belt and braces
            }
            // Arriving along the exit street would need a merge, not a junction.
            if (exitVertical != (lastStep.y == 0)) return;

            // Chain interior must be free, inside the margin, and touch no road
            // but its own neighbours — a curve brushing another street would
            // create a junction the masks don't describe.
            var chainCells = new HashSet<Vector2Int>(chain);
            for (int i = 1; i < chain.Count - 1; i++)
            {
                Vector2Int c = chain[i];
                if (!InsideMargin(data, c.x, c.y) || data.GetKind(c.x, c.y) != ChunkData.CellKind.Empty) return;
                for (int d = 0; d < 4; d++)
                {
                    Vector2Int n = c + EdgeMaskUtility.Offset(d);
                    if (!chainCells.Contains(n) && data.InBounds(n.x, n.y) && data.IsRoad(n.x, n.y)) return;
                }
            }

            // Ribbon clearance: cells the chord-stamped ribbon sweeps. Foreign
            // road cells that close reject the curve; empty ones get Reserved.
            const float ClipRadius = 0.8f;
            var clipped = new List<Vector2Int>();
            Vector2Int min = Vector2Int.Min(entry, exit) - Vector2Int.one;
            Vector2Int max = Vector2Int.Max(entry, exit) + Vector2Int.one;
            for (int y = Mathf.Max(min.y, 0); y <= Mathf.Min(max.y, data.SizeInCells - 1); y++)
            for (int x = Mathf.Max(min.x, 0); x <= Mathf.Min(max.x, data.SizeInCells - 1); x++)
            {
                var c = new Vector2Int(x, y);
                if (chainCells.Contains(c)) continue;
                float distance = DistanceToCurve(Center(c), samples);
                if (distance >= ClipRadius) continue;
                if (data.IsRoad(x, y) || data.IsFeatureCell(x, y)) return;
                if (!InsideMargin(data, x, y)) return;
                clipped.Add(c);
            }

            // ---- everything fits: apply.
            int featureIndex = data.Features.Count;
            int firstDir = DirectionOf(chain[1] - chain[0]);
            data.Features.Add(new RoadFeature(RoadFeatureKind.Curve, -1, entry, firstDir, exit, 0));

            // Junctions become ordinary Ts (stamped as normal pieces — no
            // featureIndex), flagged Curve so later features keep off them.
            data.SetConnections(entry.x, entry.y, data.GetConnections(entry.x, entry.y) | EdgeMaskUtility.DirectionBit(firstDir));
            data.SetConnections(exit.x, exit.y, data.GetConnections(exit.x, exit.y) | EdgeMaskUtility.DirectionBit(DirectionOf(chain[^2] - exit)));
            data.AddFlags(entry.x, entry.y, ChunkData.CellFlags.Curve);
            data.AddFlags(exit.x, exit.y, ChunkData.CellFlags.Curve);

            for (int i = 1; i < chain.Count - 1; i++)
            {
                Vector2Int c = chain[i];
                data.SetKind(c.x, c.y, ChunkData.CellKind.Connector);
                data.SetConnections(c.x, c.y,
                    EdgeMaskUtility.DirectionBit(DirectionOf(chain[i - 1] - c)) |
                    EdgeMaskUtility.DirectionBit(DirectionOf(chain[i + 1] - c)));
                Vector2 offset = NearestCurvePoint(Center(c), samples) - Center(c);
                data.SetCenterOffset(c.x, c.y, new Vector2(
                    Mathf.Clamp(offset.x, -0.45f, 0.45f),
                    Mathf.Clamp(offset.y, -0.45f, 0.45f)));
                data.AddFlags(c.x, c.y, ChunkData.CellFlags.Curve);
                data.SetFeatureIndex(c.x, c.y, featureIndex);
            }
            foreach (Vector2Int c in clipped)
            {
                data.SetKind(c.x, c.y, ChunkData.CellKind.Reserved);
                data.SetFeatureIndex(c.x, c.y, featureIndex);
            }
        }

        static Vector2 Center(Vector2Int cell) => new(cell.x + 0.5f, cell.y + 0.5f);

        static bool IsVerticalStreet(ChunkData data, Vector2Int c) =>
            (data.GetConnections(c.x, c.y) & EdgeMaskUtility.DirectionBit(0)) != 0;

        static int DirectionOf(Vector2Int step)
        {
            for (int dir = 0; dir < 4; dir++)
                if (EdgeMaskUtility.Offset(dir) == step) return dir;
            return 0;
        }

        static float DistanceToCurve(Vector2 point, Vector2[] samples)
        {
            float best = float.MaxValue;
            foreach (Vector2 s in samples) best = Mathf.Min(best, (s - point).sqrMagnitude);
            return Mathf.Sqrt(best);
        }

        static Vector2 NearestCurvePoint(Vector2 point, Vector2[] samples)
        {
            float best = float.MaxValue;
            Vector2 nearest = point;
            foreach (Vector2 s in samples)
            {
                float d = (s - point).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = s;
                }
            }
            return nearest;
        }

        // --------------------------------------------------------------- bridge

        /// <summary>
        /// Turn a connector-only block's single carved line into a bridge:
        /// flat border cells (the arterial contract with the axis
        /// neighbours), a ramp run climbing from each end, and an elevated
        /// deck spanning everything between. Ground under the deck becomes
        /// Reserved — a connector block has no crossing streets, the
        /// perpendicular arterials were suppressed by the layout. Runs AFTER
        /// ResolveConnections, mirroring ApplyOverpass: ramp cells keep their
        /// ground masks (the graph links ramp → deck through the upper
        /// level), deck cells carry only upper connections.
        /// </summary>
        public static void PlaceBridge(CityGenerationSettings settings, ChunkData data, int axis, int line)
        {
            if (settings == null || line < 0) return;
            if (!settings.HasOverpassPieces)
            {
                Debug.LogWarning("RoadFeaturePlacer: connector block needs Ramp and Deck pieces in the city piece list - leaving its road at ground level.");
                return;
            }

            int rampLength = Mathf.Max(1, settings.rampLengthInCells);
            int n = data.SizeInCells;
            int deckStart = 1 + rampLength;
            int deckEnd = n - 2 - rampLength;                    // inclusive
            if (deckEnd < deckStart)
            {
                Debug.LogWarning($"RoadFeaturePlacer: block size {n} is too small for a bridge with ramp length {rampLength} - leaving its road at ground level.");
                return;
            }

            int dirUp = axis == 0 ? 1 : 0;                       // uphill toward +axis: East for a row bridge, North for a column one
            EdgeMask axisPair = EdgeMaskUtility.DirectionBit(dirUp) | EdgeMaskUtility.DirectionBit(dirUp + 2);
            Vector2Int Cell(int along) => axis == 0 ? new Vector2Int(along, line) : new Vector2Int(line, along);

            for (int i = 1; i <= rampLength; i++)
            {
                Vector2Int c = Cell(i);
                data.SetRamp(c.x, c.y, dirUp, i - 1, rampLength);                       // climbing toward the deck
            }
            for (int i = deckStart; i <= deckEnd; i++)
            {
                Vector2Int c = Cell(i);
                data.SetUpperConnections(c.x, c.y, axisPair);
                data.SetKind(c.x, c.y, ChunkData.CellKind.Reserved);                    // nothing crosses underneath
                data.SetConnections(c.x, c.y, EdgeMask.None);
            }
            for (int i = deckEnd + 1; i <= n - 2; i++)
            {
                Vector2Int c = Cell(i);
                data.SetRamp(c.x, c.y, dirUp + 2, n - 2 - i, rampLength);               // climbing back toward the deck
            }
        }

        // --------------------------------------------------------------- forks

        /// <summary>
        /// Y-splits. A straight side street leaving a through road via a
        /// T-junction gets the road-split treatment: the junction moves onto
        /// the seam between the street's row and an empty twin row (T piece +
        /// half straights), an optional stem runs along that seam, the split
        /// piece turns it into two parallel branches, and both branches
        /// continue to the far through road where the twin gets its own T.
        /// In the graph the stem/split cells of both rows exist with their
        /// centres pulled onto the seam, so every edge stays axis-aligned.
        /// </summary>
        static void PlaceForks(CityGenerationSettings settings, float forkChance, ChunkData data, System.Random rng)
        {
            var candidates = new List<(int side, int stem)>();
            for (int y = BorderMargin; y < data.SizeInCells - BorderMargin; y++)
            for (int x = BorderMargin; x < data.SizeInCells - BorderMargin; x++)
            {
                if (!data.IsRoad(x, y) || data.IsFeatureCell(x, y)) continue;
                EdgeMask mask = data.GetConnections(x, y);
                if (mask.ConnectionCount() != 3) continue;
                int dir = StemDirection(mask);
                if (rng.NextDouble() >= forkChance) continue;

                candidates.Clear();
                for (int side = -1; side <= 1; side += 2)
                for (int stem = settings.ForkStemMin; stem <= settings.ForkStemMax; stem++)
                {
                    if (ForkFits(data, x, y, dir, side, stem))
                        candidates.Add((side, stem));
                }
                if (candidates.Count == 0) continue;

                var pick = candidates[rng.Next(candidates.Count)];
                ApplyFork(data, x, y, dir, pick.side, pick.stem);
            }
        }

        /// <summary>The one direction of a T mask whose opposite edge is open — where the side street leaves the through road.</summary>
        static int StemDirection(EdgeMask tee)
        {
            for (int dir = 0; dir < 4; dir++)
            {
                bool has = (tee & EdgeMaskUtility.DirectionBit(dir)) != 0;
                bool hasOpposite = (tee & EdgeMaskUtility.DirectionBit(dir + 2)) != 0;
                if (has && !hasOpposite) return dir;
            }
            return 0;
        }

        /// <summary>
        /// Junction J (a T whose stem points <paramref name="dir"/>), its twin
        /// on the through road (a plain straight), <paramref name="stem"/> + 1
        /// cells of plain straight side street with empty twins (stem cells
        /// and the split cell), then straights with empty twins until a
        /// terminating T on the far through road whose twin is again a plain
        /// straight. Everything inside the margin and not yet a feature.
        /// </summary>
        static bool ForkFits(ChunkData data, int jx, int jy, int dir, int side, int stem)
        {
            Vector2Int f = EdgeMaskUtility.Offset(dir);
            Vector2Int p = EdgeMaskUtility.Offset(dir + 1) * side;
            EdgeMask axisPair = EdgeMaskUtility.DirectionBit(dir) | EdgeMaskUtility.DirectionBit(dir + 2);
            EdgeMask perpPair = EdgeMask.All & ~axisPair;
            var junction = new Vector2Int(jx, jy);

            if (data.GetConnections(jx, jy) != (perpPair | EdgeMaskUtility.DirectionBit(dir))) return false;
            if (!IsPlainRoad(data, junction + p, perpPair)) return false;

            for (int i = 1; i <= stem + 1; i++)
            {
                Vector2Int c = junction + f * i;
                if (!IsPlainRoad(data, c, axisPair) || !IsFreeTwin(data, c + p)) return false;
            }

            for (int i = stem + 2; ; i++)
            {
                Vector2Int c = junction + f * i;
                Vector2Int t = c + p;
                if (!InsideMargin(data, c.x, c.y) || !InsideMargin(data, t.x, t.y)) return false;
                if (!data.IsRoad(c.x, c.y) || data.IsFeatureCell(c.x, c.y)) return false;
                EdgeMask m = data.GetConnections(c.x, c.y);
                if (m == axisPair)
                {
                    if (!IsFreeTwin(data, t)) return false;
                    continue;
                }
                // Terminating T: the branch rejoins the far through road; the twin must be able to take a T as well.
                return m == (perpPair | EdgeMaskUtility.DirectionBit(dir + 2)) && IsPlainRoad(data, t, perpPair);
            }
        }

        static bool IsPlainRoad(ChunkData data, Vector2Int c, EdgeMask mask) =>
            InsideMargin(data, c.x, c.y) && data.IsRoad(c.x, c.y) && !data.IsFeatureCell(c.x, c.y) && data.GetConnections(c.x, c.y) == mask;

        static bool IsFreeTwin(ChunkData data, Vector2Int c) =>
            InsideMargin(data, c.x, c.y) && data.IsBuildable(c.x, c.y) && !data.IsFeatureCell(c.x, c.y);

        static void ApplyFork(ChunkData data, int jx, int jy, int dir, int side, int stem)
        {
            Vector2Int f = EdgeMaskUtility.Offset(dir);
            Vector2Int p = EdgeMaskUtility.Offset(dir + 1) * side;
            EdgeMask axisPair = EdgeMaskUtility.DirectionBit(dir) | EdgeMaskUtility.DirectionBit(dir + 2);
            EdgeMask perpPair = EdgeMask.All & ~axisPair;
            var junction = new Vector2Int(jx, jy);
            var toTwin = new Vector2(p.x, p.y) * 0.5f;

            int featureIndex = data.Features.Count;
            data.Features.Add(new RoadFeature(RoadFeatureKind.Fork, -1, junction, dir, new Vector2Int(stem, 0), side));

            // Seam junction: both through-road cells link into the stem; visuals are the feature's.
            Vector2Int twinJunction = junction + p;
            data.SetConnections(twinJunction.x, twinJunction.y, perpPair | EdgeMaskUtility.DirectionBit(dir));
            data.SetFeatureIndex(jx, jy, featureIndex);
            data.SetFeatureIndex(twinJunction.x, twinJunction.y, featureIndex);

            // Stem + split cells: both rows are road, both nodes sit on the seam.
            for (int i = 1; i <= stem + 1; i++)
            {
                Vector2Int c = junction + f * i;
                Vector2Int t = c + p;
                data.SetCenterOffset(c.x, c.y, toTwin);
                data.SetFeatureIndex(c.x, c.y, featureIndex);
                data.SetKind(t.x, t.y, ChunkData.CellKind.Connector);
                data.SetConnections(t.x, t.y, axisPair);
                data.SetCenterOffset(t.x, t.y, -toTwin);
                data.SetFeatureIndex(t.x, t.y, featureIndex);
            }

            // Branches: the twin row becomes an ordinary street up to its own T on the far through road.
            for (int i = stem + 2; ; i++)
            {
                Vector2Int c = junction + f * i;
                Vector2Int t = c + p;
                if (data.GetConnections(c.x, c.y) == axisPair)
                {
                    data.SetKind(t.x, t.y, ChunkData.CellKind.Connector);
                    data.SetConnections(t.x, t.y, axisPair);
                    continue;
                }
                data.SetConnections(t.x, t.y, perpPair | EdgeMaskUtility.DirectionBit(dir + 2));
                break;
            }
        }

        // ---------------------------------------------------------- overpasses

        static void PlaceOverpasses(CityGenerationSettings settings, float overpassChance, ChunkData data, System.Random rng)
        {
            int rampLength = Mathf.Max(1, settings.rampLengthInCells);
            int deckMin = settings.OverpassDeckMin;
            int deckMax = settings.OverpassDeckMax;
            var candidates = new List<(int axisDir, int deckCells, int crossingOffset)>();

            for (int y = BorderMargin; y < data.SizeInCells - BorderMargin; y++)
            for (int x = BorderMargin; x < data.SizeInCells - BorderMargin; x++)
            {
                if (!data.IsRoad(x, y) || data.GetConnections(x, y) != EdgeMask.All || data.IsFeatureCell(x, y)) continue;
                if (rng.NextDouble() >= overpassChance) continue;

                // Every (axis, deck length, crossing position within the deck) that fits.
                candidates.Clear();
                for (int axisDir = 0; axisDir < 2; axisDir++)           // 0 = along N/S, 1 = along E/W
                for (int deck = deckMin; deck <= deckMax; deck++)
                for (int offset = 0; offset < deck; offset++)
                {
                    if (RunFits(data, x, y, axisDir, rampLength, deck, offset))
                        candidates.Add((axisDir, deck, offset));
                }
                if (candidates.Count == 0) continue;

                var pick = candidates[rng.Next(candidates.Count)];
                ApplyOverpass(data, x, y, pick.axisDir, rampLength, pick.deckCells, pick.crossingOffset);
            }
        }

        /// <summary>
        /// A run along <paramref name="axisDir"/> through crossing (cx, cy):
        /// rampLength cells, then deck cells (the crossing sits at
        /// crossingOffset inside them), then rampLength cells. Ramp cells must
        /// be plain straights on the axis; deck cells straights or full
        /// crossroads; nothing may already be a feature; all inside the margin.
        /// </summary>
        static bool RunFits(ChunkData data, int cx, int cy, int axisDir, int rampLength, int deckCells, int crossingOffset)
        {
            Vector2Int step = EdgeMaskUtility.Offset(axisDir);
            EdgeMask axisPair = EdgeMaskUtility.DirectionBit(axisDir) | EdgeMaskUtility.DirectionBit(axisDir + 2);
            int total = rampLength * 2 + deckCells;
            int startIndex = -(rampLength + crossingOffset); // run index 0 relative to the crossing

            for (int i = 0; i < total; i++)
            {
                int x = cx + step.x * (startIndex + i);
                int y = cy + step.y * (startIndex + i);
                if (!InsideMargin(data, x, y) || !data.IsRoad(x, y) || data.IsFeatureCell(x, y)) return false;

                EdgeMask mask = data.GetConnections(x, y);
                bool isRamp = i < rampLength || i >= rampLength + deckCells;
                if (isRamp ? mask != axisPair : (mask != axisPair && mask != EdgeMask.All)) return false;
            }

            // No ramp-to-ramp valleys: the cells just beyond both feet must not be ramps.
            int beforeX = cx + step.x * (startIndex - 1), beforeY = cy + step.y * (startIndex - 1);
            int afterX = cx + step.x * (startIndex + total), afterY = cy + step.y * (startIndex + total);
            if (data.InBounds(beforeX, beforeY) && data.IsRamp(beforeX, beforeY)) return false;
            if (data.InBounds(afterX, afterY) && data.IsRamp(afterX, afterY)) return false;
            return true;
        }

        static void ApplyOverpass(ChunkData data, int cx, int cy, int axisDir, int rampLength, int deckCells, int crossingOffset)
        {
            Vector2Int step = EdgeMaskUtility.Offset(axisDir);
            EdgeMask axisPair = EdgeMaskUtility.DirectionBit(axisDir) | EdgeMaskUtility.DirectionBit(axisDir + 2);
            EdgeMask perpendicularPair = EdgeMask.All & ~axisPair;
            int total = rampLength * 2 + deckCells;
            int startIndex = -(rampLength + crossingOffset);

            for (int i = 0; i < total; i++)
            {
                int x = cx + step.x * (startIndex + i);
                int y = cy + step.y * (startIndex + i);

                if (i < rampLength)
                {
                    data.SetRamp(x, y, axisDir, i, rampLength);                       // climbing along +axis
                }
                else if (i >= rampLength + deckCells)
                {
                    data.SetRamp(x, y, axisDir + 2, total - 1 - i, rampLength);       // climbing back toward the deck
                }
                else
                {
                    data.SetUpperConnections(x, y, axisPair);
                    if (data.GetConnections(x, y) == EdgeMask.All)
                        data.SetConnections(x, y, perpendicularPair);                 // the street passes underneath
                    else
                    {
                        data.SetKind(x, y, ChunkData.CellKind.Reserved);              // pillar ground
                        data.SetConnections(x, y, EdgeMask.None);
                    }
                }
            }
        }

        // ----------------------------------------------------------- templates

        static void PlaceTemplates(CityGenerationSettings settings, ChunkData data, System.Random rng)
        {
            if (settings.roadPieces == null) return;
            var seenTemplates = new List<Template>();

            for (int pieceIndex = 0; pieceIndex < settings.roadPieces.Count; pieceIndex++)
            {
                RoadPieceDefinition piece = settings.roadPieces[pieceIndex];
                if (piece == null || piece.prefab == null || !piece.IsMultiCell || piece.placeChance <= 0f) continue;

                // Symmetric templates repeat under rotation — match each distinct shape once,
                // otherwise the place chance would be rolled several times per spot.
                seenTemplates.Clear();
                Template template = Template.FromDefinition(piece);
                for (int turns = 0; turns < 4; turns++)
                {
                    if (turns > 0) template = template.RotatedCw();
                    if (seenTemplates.Exists(t => t.SameAs(template))) continue;
                    seenTemplates.Add(template);
                    ScanTemplate(data, rng, pieceIndex, piece.placeChance, template, turns);
                }
            }
        }

        static void ScanTemplate(ChunkData data, System.Random rng, int pieceIndex, float chance, Template template, int turns)
        {
            for (int y = BorderMargin; y + template.Height <= data.SizeInCells - BorderMargin; y++)
            for (int x = BorderMargin; x + template.Width <= data.SizeInCells - BorderMargin; x++)
            {
                if (!Matches(data, x, y, template)) continue;
                if (rng.NextDouble() >= chance) continue;

                int featureIndex = data.Features.Count;
                data.Features.Add(new RoadFeature(pieceIndex, new Vector2Int(x, y), turns, new Vector2Int(template.Width, template.Height)));
                for (int v = 0; v < template.Height; v++)
                for (int u = 0; u < template.Width; u++)
                {
                    data.SetFeatureIndex(x + u, y + v, featureIndex);
                    if (template.Mask(u, v) == EdgeMask.None)
                        data.SetKind(x + u, y + v, ChunkData.CellKind.Reserved);
                }
            }
        }

        static bool Matches(ChunkData data, int x, int y, Template template)
        {
            for (int v = 0; v < template.Height; v++)
            for (int u = 0; u < template.Width; u++)
            {
                int cx = x + u, cy = y + v;
                if (data.IsFeatureCell(cx, cy)) return false;
                EdgeMask wanted = template.Mask(u, v);
                if (wanted == EdgeMask.None)
                {
                    if (!data.IsBuildable(cx, cy)) return false;
                }
                else if (!data.IsRoad(cx, cy) || data.GetConnections(cx, cy) != wanted)
                    return false;
            }
            return true;
        }

        static bool InsideMargin(ChunkData data, int x, int y) =>
            x >= BorderMargin && y >= BorderMargin && x < data.SizeInCells - BorderMargin && y < data.SizeInCells - BorderMargin;

        /// <summary>A rotated copy of a piece's cell-mask grid. Rotation follows <see cref="EdgeMaskUtility.RotateCw"/>: (u, v) → (v, w-1-u).</summary>
        readonly struct Template
        {
            public readonly int Width, Height;
            readonly EdgeMask[] masks;

            Template(int width, int height, EdgeMask[] masks)
            {
                Width = width;
                Height = height;
                this.masks = masks;
            }

            public static Template FromDefinition(RoadPieceDefinition piece)
            {
                int w = Mathf.Max(1, piece.footprintInCells.x), h = Mathf.Max(1, piece.footprintInCells.y);
                var masks = new EdgeMask[w * h];
                for (int v = 0; v < h; v++)
                for (int u = 0; u < w; u++)
                    masks[v * w + u] = piece.CellMask(u, v);
                return new Template(w, h, masks);
            }

            public EdgeMask Mask(int u, int v) => masks[v * Width + u];

            public Template RotatedCw()
            {
                var rotated = new EdgeMask[Width * Height];
                int newWidth = Height, newHeight = Width;
                for (int v = 0; v < Height; v++)
                for (int u = 0; u < Width; u++)
                {
                    int nu = v, nv = Width - 1 - u;
                    rotated[nv * newWidth + nu] = Mask(u, v).RotateCw(1);
                }
                return new Template(newWidth, newHeight, rotated);
            }

            public bool SameAs(Template other)
            {
                if (other.Width != Width || other.Height != Height) return false;
                for (int i = 0; i < masks.Length; i++)
                    if (masks[i] != other.masks[i]) return false;
                return true;
            }
        }
    }
}
