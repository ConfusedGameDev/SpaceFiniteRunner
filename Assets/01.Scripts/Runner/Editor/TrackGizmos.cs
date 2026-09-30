using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Draws the runner's track in the Scene view (TrackAuthoringPRD M3), read
    /// straight off the live <see cref="TrackManager"/> and the generator's
    /// decided <see cref="TrackPlacement"/>s — so it shows whatever the
    /// generator holds: a Generate Track bake, a Preview Saved Track, the run
    /// in play — and never a GameObject, so a 70 km track costs no more than
    /// its stream window.
    /// <list type="bullet">
    /// <item>The whole centre line, always, coarsely.</item>
    /// <item>Round the Scene camera (<see cref="TrackGizmoSettings.DetailRadius"/>):
    /// the lane edges, the road's outer lip with its walls — RED where an
    /// edge is open (a drop), ORANGE along a flat sweep (grip tested), CYAN
    /// inside a loop or a tube, MAGENTA through the final run-up — a tick
    /// across the road every 500 m, the section and end marks, and an icon
    /// per placement in its spawner's colour.</item>
    /// </list>
    /// Everything is sampled every <see cref="Step"/> metres once per track
    /// (cached on the track's revision, length and record count); a repaint
    /// only culls and draws.
    /// </summary>
    public static class TrackGizmos
    {
        const float Step = 20f;
        const int CoarseEvery = 10;      // centre-line samples per coarse point (200 m)
        const float TickEvery = 500f;
        const float WallHeight = 8f;
        const float Lift = 0.3f;          // lines sit just above the visible road

        static readonly Color CentreColor = new(1f, 1f, 1f, 0.35f);
        static readonly Color LaneColor = new(0.35f, 0.75f, 1f, 0.9f);
        static readonly Color WallColor = new(1f, 1f, 1f, 0.85f);
        static readonly Color OpenColor = new(1f, 0.15f, 0.15f, 1f);
        static readonly Color FlatColor = new(1f, 0.55f, 0.05f, 1f);
        static readonly Color SectionColor = new(0.2f, 1f, 1f, 1f);
        static readonly Color EndZoneColor = new(1f, 0.25f, 0.9f, 1f);
        static readonly Color TickColor = new(1f, 1f, 1f, 0.5f);

        [System.Flags]
        enum Flag : byte { None = 0, OpenLeft = 1, OpenRight = 2, Flat = 4, Section = 8, EndZone = 16 }

        sealed class Mark
        {
            public TrackPlacementKind kind;
            public float distance;
            public Vector3 position;
            public Vector3 up;
            public Vector3 right;
            public Color color;
            public float size;
            public Vector3[] outline; // ramps: the footprint's four corners; gates: the beam's two ends
            public string label;
        }

        sealed class Cache
        {
            public int revision = -1;
            public float length = -1f;
            public int placementCount = -1;
            public float endDistance;
            public float roadY;
            public readonly List<float> distance = new();
            public readonly List<Vector3> centre = new();
            public readonly List<Vector3> laneLeft = new(), laneRight = new();
            public readonly List<Vector3> lipLeft = new(), lipRight = new();
            public readonly List<Vector3> up = new();
            public readonly List<Flag> flags = new();
            public Vector3[] coarse = new Vector3[0];
            public readonly List<Mark> marks = new();
        }

        static readonly Dictionary<TrackGenerator, Cache> caches = new();

        // Reused per repaint: one line batch per colour.
        static readonly List<Vector3> lane = new(), wall = new(), open = new(), flat = new(), section = new(), endZone = new(), ticks = new();

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        static void Draw(TrackGenerator generator, GizmoType gizmoType)
        {
            if (!TrackGizmoSettings.Show) return;
            SceneView view = SceneView.currentDrawingSceneView;
            if (view == null || view.camera == null) return;
            TrackManager track = generator.Track;
            if (track == null || track.Spline == null || track.Length < Step * 2f) return;

            Cache cache = GetCache(generator, track);
            Vector3 eye = view.camera.transform.position;
            float radius = Mathf.Max(200f, TrackGizmoSettings.DetailRadius);
            float radiusSqr = radius * radius;
            float labelSqr = radiusSqr / 9f;

            var zTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            Handles.color = CentreColor;
            Handles.DrawPolyLine(cache.coarse);

            lane.Clear(); wall.Clear(); open.Clear(); flat.Clear(); section.Clear(); endZone.Clear(); ticks.Clear();
            int count = cache.centre.Count;
            for (int i = 0; i < count - 1; i++)
            {
                if ((cache.centre[i] - eye).sqrMagnitude > radiusSqr) continue;
                Flag f = cache.flags[i];

                List<Vector3> laneBatch = (f & Flag.EndZone) != 0 ? endZone : (f & Flag.Section) != 0 ? section : (f & Flag.Flat) != 0 ? flat : lane;
                AddSegment(laneBatch, cache.laneLeft[i], cache.laneLeft[i + 1]);
                AddSegment(laneBatch, cache.laneRight[i], cache.laneRight[i + 1]);

                AddEdge(cache, i, cache.lipLeft, (f & Flag.OpenLeft) != 0);
                AddEdge(cache, i, cache.lipRight, (f & Flag.OpenRight) != 0);

                // A tick across the whole road every 500 m.
                if (Mathf.FloorToInt(cache.distance[i] / TickEvery) != Mathf.FloorToInt(cache.distance[i + 1] / TickEvery))
                {
                    AddSegment(ticks, cache.lipLeft[i + 1], cache.lipRight[i + 1]);
                    if (TrackGizmoSettings.Labels && (cache.centre[i + 1] - eye).sqrMagnitude < labelSqr)
                        Label(cache.lipRight[i + 1] + cache.up[i + 1] * 3f, $"{Mathf.Round(cache.distance[i + 1] / TickEvery) * TickEvery / 1000f:0.0} km", TickColor);
                }
            }
            DrawBatch(lane, LaneColor);
            DrawBatch(wall, WallColor);
            DrawBatch(open, OpenColor);
            DrawBatch(flat, FlatColor);
            DrawBatch(section, SectionColor);
            DrawBatch(endZone, EndZoneColor);
            DrawBatch(ticks, TickColor);

            DrawMarksAcross(track, cache, eye, radiusSqr, labelSqr);
            if (TrackGizmoSettings.Placements) DrawPlacements(cache, eye, radiusSqr, labelSqr);

            Handles.zTest = zTest;
        }

        static void AddEdge(Cache cache, int i, List<Vector3> lip, bool isOpen)
        {
            if (isOpen)
            {
                AddSegment(open, lip[i], lip[i + 1]);
                return;
            }
            AddSegment(wall, lip[i], lip[i + 1]);
            AddSegment(wall, lip[i] + cache.up[i] * WallHeight, lip[i + 1] + cache.up[i + 1] * WallHeight);
        }

        static void AddSegment(List<Vector3> batch, Vector3 a, Vector3 b)
        {
            batch.Add(a);
            batch.Add(b);
        }

        static void DrawBatch(List<Vector3> batch, Color color)
        {
            if (batch.Count == 0) return;
            Handles.color = color;
            Handles.DrawLines(batch.ToArray());
        }

        static readonly GUIStyle labelStyle = new() { fontSize = 11, fontStyle = FontStyle.Bold };

        static void Label(Vector3 position, string text, Color color)
        {
            labelStyle.normal.textColor = color;
            Handles.Label(position, text, labelStyle);
        }

        // Lines across the road: every section's mouth and exit, the final
        // run-up's start and the end of the road.
        static void DrawMarksAcross(TrackManager track, Cache cache, Vector3 eye, float radiusSqr, float labelSqr)
        {
            foreach (TrackSection s in track.Sections)
            {
                string name = s is LoopSection ? "LOOP" : s is TubeSection ? "TUBE" : "SECTION";
                Across(track, cache, s.StartDistance, SectionColor, name, eye, radiusSqr, labelSqr);
                Across(track, cache, s.EndDistance, SectionColor, name + " END", eye, radiusSqr, labelSqr);
            }
            if (track.EndZoneStart >= 0f) Across(track, cache, track.EndZoneStart, EndZoneColor, "RUN-UP", eye, radiusSqr, labelSqr);
            if (track.HasEnd) Across(track, cache, track.EndDistance, EndZoneColor, "END", eye, radiusSqr, labelSqr);
        }

        static void Across(TrackManager track, Cache cache, float distance, Color color, string label, Vector3 eye, float radiusSqr, float labelSqr)
        {
            track.GetFrameAtDistance(distance, out Vector3 centre, out _, out Vector3 up, out Vector3 right);
            if ((centre - eye).sqrMagnitude > radiusSqr) return;
            Vector3 surface = centre + up * (cache.roadY + Lift);
            float half = track.RoadHalfWidth;
            Handles.color = color;
            Handles.DrawAAPolyLine(4f, surface - right * half, surface + right * half);
            if (TrackGizmoSettings.Labels && (centre - eye).sqrMagnitude < labelSqr) Label(surface + up * 12f, label, color);
        }

        static void DrawPlacements(Cache cache, Vector3 eye, float radiusSqr, float labelSqr)
        {
            foreach (Mark m in cache.marks)
            {
                if ((m.position - eye).sqrMagnitude > radiusSqr) continue;
                Handles.color = m.color;
                switch (m.kind)
                {
                    case TrackPlacementKind.Ramp:
                    case TrackPlacementKind.EndRamp:
                        Handles.DrawAAPolyLine(3f, m.outline[0], m.outline[1], m.outline[2], m.outline[3], m.outline[0]);
                        break;
                    case TrackPlacementKind.LaserGate:
                        Handles.DrawAAPolyLine(5f, m.outline[0], m.outline[1]);
                        if (m.size > 0f) Handles.DrawWireDisc(m.position, m.up, m.size); // a rotor's swept disc
                        break;
                    case TrackPlacementKind.RepairOrb:
                        Handles.DrawWireDisc(m.position, m.up, m.size);
                        Handles.DrawWireDisc(m.position, m.right, m.size);
                        break;
                    case TrackPlacementKind.Collectible:
                        Handles.DrawWireCube(m.position, Vector3.one * m.size);
                        break;
                    case TrackPlacementKind.Loop:
                        Handles.DrawWireDisc(m.position, m.right, m.size);
                        break;
                    default: // orbs, brake pads, anything a spawner adds
                        Handles.SphereHandleCap(0, m.position, Quaternion.identity, m.size, EventType.Repaint);
                        break;
                }
                if (m.label != null && TrackGizmoSettings.Labels && (m.position - eye).sqrMagnitude < labelSqr)
                    Label(m.position + m.up * (m.size + 4f), m.label, m.color);
            }
        }

        // -------------------------------------------------------------- cache
        static Cache GetCache(TrackGenerator generator, TrackManager track)
        {
            if (!caches.TryGetValue(generator, out Cache cache))
            {
                cache = new Cache();
                caches[generator] = cache;
            }
            int placementCount = generator.Placements.Count;
            if (cache.revision == track.Revision && Mathf.Approximately(cache.length, track.Length)
                && cache.placementCount == placementCount && Mathf.Approximately(cache.endDistance, track.EndDistance))
                return cache;

            cache.revision = track.Revision;
            cache.length = track.Length;
            cache.placementCount = placementCount;
            cache.endDistance = track.EndDistance;
            cache.roadY = generator.Decorator != null ? generator.Decorator.RoadYOffset : -1f;
            SampleRoad(track, cache);
            ResolvePlacements(generator, track, cache);
            return cache;
        }

        static void SampleRoad(TrackManager track, Cache cache)
        {
            cache.distance.Clear(); cache.centre.Clear(); cache.up.Clear(); cache.flags.Clear();
            cache.laneLeft.Clear(); cache.laneRight.Clear(); cache.lipLeft.Clear(); cache.lipRight.Clear();
            float surface = cache.roadY + Lift;
            float end = track.Length;

            for (float d = 0f; ; d += Step)
            {
                d = Mathf.Min(d, end);
                track.GetFrameAtDistance(d, out Vector3 centre, out _, out Vector3 up, out Vector3 right);
                track.GetLateralBand(d, out float laneMin, out float laneMax);
                track.GetRoadBand(d, out float roadMin, out float roadMax);
                float rise = track.HasShoulders(d) ? track.ShoulderRise : 0f;

                Vector3 floor = centre + up * surface;
                cache.distance.Add(d);
                cache.centre.Add(floor);
                cache.up.Add(up);
                cache.laneLeft.Add(floor + right * laneMin);
                cache.laneRight.Add(floor + right * laneMax);
                cache.lipLeft.Add(floor + right * roadMin + up * rise);
                cache.lipRight.Add(floor + right * roadMax + up * rise);

                Flag f = Flag.None;
                if (track.IsEdgeOpen(d, -1)) f |= Flag.OpenLeft;
                if (track.IsEdgeOpen(d, 1)) f |= Flag.OpenRight;
                if (track.FlatSweepAt(d) != null) f |= Flag.Flat;
                if (track.SectionAt(d) != null) f |= Flag.Section;
                if (track.EndZoneStart >= 0f && d >= track.EndZoneStart) f |= Flag.EndZone;
                cache.flags.Add(f);

                if (d >= end) break;
            }

            var coarse = new List<Vector3>();
            for (int i = 0; i < cache.centre.Count; i += CoarseEvery) coarse.Add(cache.centre[i]);
            coarse.Add(cache.centre[cache.centre.Count - 1]);
            cache.coarse = coarse.ToArray();
        }

        static void ResolvePlacements(TrackGenerator generator, TrackManager track, Cache cache)
        {
            cache.marks.Clear();
            var orbs = generator.GetSpawner<SpeedOrbSpawner>();
            var table = generator.FeatureTable;

            foreach (TrackPlacement p in generator.Placements)
            {
                track.GetFrameAtDistance(p.distance, out Vector3 centre, out Vector3 forward, out Vector3 up, out Vector3 right);
                var m = new Mark
                {
                    kind = p.kind,
                    distance = p.distance,
                    up = up,
                    right = right,
                    position = centre + right * p.lateral + up * p.height,
                    color = SpawnerColor(generator, p.kind),
                    size = 5f,
                };

                switch (p.kind)
                {
                    case TrackPlacementKind.SpeedOrb:
                        if (orbs != null && orbs.Tiers != null && p.variant >= 0 && p.variant < orbs.Tiers.Length)
                            m.color = orbs.Tiers[p.variant].color;
                        m.size = 6f;
                        break;
                    case TrackPlacementKind.RepairOrb:
                        m.size = 7.5f;
                        m.position += up * 6f;
                        break;
                    case TrackPlacementKind.Collectible:
                        m.color = new Color(1f, 0.8f, 0.2f);
                        m.size = 3f;
                        m.position += up * 4f;
                        break;
                    case TrackPlacementKind.LaserGate:
                    {
                        float half = p.data.x * 0.5f;
                        Vector3 beam = centre + right * p.lateral + up * 3f;
                        m.position = beam;
                        m.outline = new[] { beam - right * half, beam + right * half };
                        m.size = p.variant == (int)LaserGateVariant.Rotor ? half : 0f;
                        m.label = ((LaserGateVariant)p.variant).ToString().ToUpperInvariant();
                        break;
                    }
                    case TrackPlacementKind.Ramp:
                    {
                        var entry = table != null && p.variant >= 0 && p.variant < table.Length ? table[p.variant] : null;
                        var def = entry?.definition as JumpDefinition;
                        float length = def != null ? def.length : 60f;
                        float half = track.HalfWidth * (def != null ? Mathf.Clamp01(def.widthFraction) : 0.25f);
                        if (entry != null) m.color = entry.color;
                        m.outline = Footprint(track, cache, p.distance, length, p.lateral, half);
                        m.label = "RAMP";
                        break;
                    }
                    case TrackPlacementKind.EndRamp:
                        m.color = EndZoneColor;
                        m.outline = Footprint(track, cache, p.distance, generator.EndRampLength, p.lateral, generator.EndRampHalfWidth);
                        m.label = p.variant == 1 ? "END RAMPS" : null;
                        break;
                    case TrackPlacementKind.Pickup:
                    {
                        var catalog = generator.Catalog;
                        var entry = catalog != null && p.variant >= 0 && p.variant < catalog.entries.Count ? catalog.entries[p.variant] : null;
                        if (entry != null) { m.color = entry.color; m.label = entry.displayName; }
                        m.size = 6f;
                        break;
                    }
                    case TrackPlacementKind.CustomPrefab:
                    {
                        var prefabs = generator.LoadedTrack != null ? generator.LoadedTrack.customPrefabs : null;
                        m.label = prefabs != null && p.variant >= 0 && p.variant < prefabs.Count && prefabs[p.variant] != null ? prefabs[p.variant].name : "PREFAB";
                        m.color = new Color(0.8f, 0.8f, 1f);
                        m.size = 4f;
                        break;
                    }
                    case TrackPlacementKind.Loop:
                        m.color = SectionColor;
                        m.size = track.SectionAt(p.distance) is LoopSection loop ? loop.Radius : 50f;
                        m.position = centre + up * m.size;
                        m.label = "LOOP";
                        break;
                }
                cache.marks.Add(m);
            }
        }

        static Vector3[] Footprint(TrackManager track, Cache cache, float start, float length, float lateral, float half)
        {
            float surface = cache.roadY + Lift;
            track.GetFrameAtDistance(start, out Vector3 a, out _, out Vector3 upA, out Vector3 rightA);
            track.GetFrameAtDistance(start + length, out Vector3 b, out _, out Vector3 upB, out Vector3 rightB);
            a += upA * surface;
            b += upB * surface;
            return new[]
            {
                a + rightA * (lateral - half), a + rightA * (lateral + half),
                b + rightB * (lateral + half), b + rightB * (lateral - half),
            };
        }

        static Color SpawnerColor(TrackGenerator generator, TrackPlacementKind kind)
        {
            foreach (TrackSpawner spawner in generator.Spawners)
                if (spawner != null && spawner.Kind == kind) return spawner.color;
            return kind == TrackPlacementKind.RepairOrb ? new Color(1f, 0.2f, 0.2f)
                 : kind == TrackPlacementKind.LaserGate ? new Color(1f, 0.1f, 0.1f)
                 : Color.white;
        }
    }
}
