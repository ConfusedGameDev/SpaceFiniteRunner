using System.Collections.Generic;
using System.Text;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Fills the live CityGenerationSettings' road piece list from the Kenney
    /// City Kit (Roads) FBXs: the five basic sockets, the roundabout and split
    /// templates, and the overpass parts (ramp chain, deck, pillar). Socket
    /// masks are authored best guesses — the Road Kit Showcase scene draws
    /// every piece with its declared sockets so a wrong mask or facing is
    /// obvious and gets fixed on the asset, never on the model. Ramps are
    /// MEASURED off their vertices (surface height at both ends, uphill
    /// side) because the kit ships a half ramp and a high ramp and only the
    /// geometry says which chain climbs from the street to the bridge deck.
    /// Ramp/deck/pillar models get importer colliders so elevated surfaces
    /// are drivable (flat tiles keep riding on the chunk's ground slab).
    /// </summary>
    public static class KenneyRoadSetBuilder
    {
        const string RoadsFolder = "Assets/99.Test/Jorge/InfiniteCity/Roads";
        const string TestFolder = "Assets/99.Test/Jorge/InfiniteCity/Scripts/City/Test";
        const string SettingsPath = TestFolder + "/CityTestSettings.asset";

        /// <summary>Two ramp surfaces count as meeting when their heights differ by less than this (native units; the deck railing alone is 0.02).</summary>
        const float HeightTolerance = 0.04f;

        // road-slant-high climbs the full 0 → 0.5 in one tile; road-slant only 0 → 0.25 (for a low deck the
        // kit has no flat piece for), so the measured chain is normally just the high one.
        static readonly string[] RampCandidates = { "road-slant-high", "road-slant" };
        static readonly string[] ColliderFiles = { "road-slant", "road-slant-high", "road-bridge", "bridge-pillar" };

        const EdgeMask NorthSouth = EdgeMask.North | EdgeMask.South;
        const EdgeMask EastWest = EdgeMask.East | EdgeMask.West;

        [MenuItem("Tools/Police Escape/Create Kenney Road Set")]
        public static void CreateSet()
        {
            CityGenerationSettings settings = LoadSettings();
            if (settings == null) return;

            // Elevated parts need collision; do the importer work first so the
            // prefabs loaded below are the reimported ones.
            foreach (string file in ColliderFiles) EnableImporterCollider(file);

            GameObject straight = LoadRoad("road-straight");
            if (straight == null)
            {
                Debug.LogError($"KenneyRoadSetBuilder: road-straight.fbx not found in {RoadsFolder} — aborting.");
                return;
            }
            float nativeCell = MeasureFootprint(straight).x;

            var pieces = new List<RoadPieceDefinition>();

            // Basic sockets, read off the FBX geometry (asphalt along X for the
            // straight; Unity mirrors the FBX X axis on import, hence end = West,
            // bend = North+East). Verify in the showcase if the kit is ever re-exported.
            AddSingle(pieces, "road-straight", EastWest, 6f);
            AddSingle(pieces, "road-bend", EdgeMask.North | EdgeMask.East, 3f);
            AddSingle(pieces, "road-intersection", EdgeMask.North | EdgeMask.East | EdgeMask.West, 4f);
            AddSingle(pieces, "road-crossroad", EdgeMask.All, 4f);
            AddSingle(pieces, "road-end", EdgeMask.West, 3f);

            // Templates: masks row by row from the south-west corner.
            AddTemplate(pieces, "road-roundabout", new Vector2Int(3, 3), new[]
            {
                EdgeMask.None, NorthSouth,   EdgeMask.None,
                EastWest,      EdgeMask.All, EastWest,
                EdgeMask.None, NorthSouth,   EdgeMask.None,
            }, 0.35f);

            // Fork parts: the Y-split (stem West, exits East at ±half a cell —
            // it lives on the seam between two cells) and the half straight that
            // refills the seam junction's outer half cells.
            GameObject splitPrefab = LoadRoad("road-split");
            if (splitPrefab != null)
                pieces.Add(new RoadPieceDefinition { prefab = splitPrefab, role = RoadPieceRole.Fork, footprintInCells = new Vector2Int(1, 2) });
            GameObject halfPrefab = LoadRoad("road-straight-half");
            if (halfPrefab != null)
                pieces.Add(new RoadPieceDefinition { prefab = halfPrefab, role = RoadPieceRole.HalfStraight, connectionMask = EastWest });

            // Overpass parts.
            GameObject bridge = LoadRoad("road-bridge");
            // The LANE, not the bounds top: the top of a tile is its curb, and
            // measuring that is what put the drivable plane a curb below the
            // asphalt and left a step at the foot of every ramp.
            float deckTop = bridge != null ? MeasureLaneHeight(bridge, MidHeight(bridge)) : 0.5f;
            List<RoadPieceDefinition> chain = BuildRampChain(deckTop, out string rampReport);
            pieces.AddRange(chain);
            if (bridge != null)
            {
                pieces.Add(new RoadPieceDefinition
                {
                    prefab = bridge,
                    role = RoadPieceRole.Deck,
                    connectionMask = NorthSouth,
                    weight = 1f,
                    deckHeight = chain.Count > 0 ? chain[chain.Count - 1].rampEndHeight : deckTop,
                    includesUnderpass = true, // the model carries the E-W street under its N-S deck
                });
            }
            GameObject pillarPrefab = LoadRoad("bridge-pillar");
            if (pillarPrefab != null)
                pieces.Add(new RoadPieceDefinition { prefab = pillarPrefab, role = RoadPieceRole.Pillar });

            // Every flat piece's driving lane. The generator sinks the whole
            // stamped city by this so the asphalt lands on the chunk's ground
            // slab — see CityManager.RoadSurfaceHeight.
            float lane = MeasureLaneHeight(straight);
            foreach (RoadPieceDefinition piece in pieces)
            {
                if (piece.role != RoadPieceRole.Standard || piece.prefab == null) continue;
                float pieceLane = MeasureLaneHeight(piece.prefab);
                if (Mathf.Abs(pieceLane - lane) > HeightTolerance)
                    Debug.LogWarning($"KenneyRoadSetBuilder: '{piece.prefab.name}' lane sits at {pieceLane:0.###}, " +
                                     $"but road-straight's is {lane:0.###}. Mixed lane heights leave steps between tiles.");
                piece.laneHeight = lane;
            }

            settings.roadPieces = pieces;
            settings.pieceNativeSize = nativeCell;
            settings.scaleToCellSize = true;
            // Native chain length, but never steeper than a 2-cell climb by default (road-slant-high
            // alone is 0 → 0.5 in one cell: a 27° kicker — set rampLengthInCells to 1 for that).
            if (chain.Count > 0) settings.rampLengthInCells = Mathf.Clamp(Mathf.Max(chain.Count, 2), 1, 3);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log($"KenneyRoadSetBuilder: {pieces.Count} pieces → {SettingsPath} (native cell {nativeCell:0.##} m, " +
                      $"lane {lane:0.###}, deck lane {deckTop:0.###}).\n{rampReport}" +
                      "Open Tools → Police Escape → Road Kit Showcase Scene to verify sockets and facings, then Recalculate the CityManager.");
        }

        [MenuItem("Tools/Police Escape/Use Box Road Pieces")]
        public static void UseBoxRoadPieces()
        {
            CityGenerationSettings settings = LoadSettings();
            if (settings == null) return;

            (string name, EdgeMask mask)[] boxes =
            {
                ("TestRoad_Straight", NorthSouth),
                ("TestRoad_Corner", EdgeMask.North | EdgeMask.East),
                ("TestRoad_Tee", EdgeMask.North | EdgeMask.East | EdgeMask.West),
                ("TestRoad_Cross", EdgeMask.All),
                ("TestRoad_End", EdgeMask.North),
            };
            var pieces = new List<RoadPieceDefinition>();
            foreach (var (name, mask) in boxes)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{TestFolder}/{name}.prefab");
                if (prefab == null)
                {
                    Debug.LogWarning($"KenneyRoadSetBuilder: {name}.prefab missing — run 'Create City Test Scene' first.");
                    continue;
                }
                pieces.Add(new RoadPieceDefinition { prefab = prefab, connectionMask = mask, weight = 1f });
            }
            if (pieces.Count == 0) return;

            settings.roadPieces = pieces;
            settings.pieceNativeSize = 1f; // test cubes are built on a 1 m footprint
            // The primitive set is authored flat on the drivable plane, so it
            // must not inherit a lane offset from a previously loaded kit.
            foreach (RoadPieceDefinition piece in pieces) piece.laneHeight = 0f;
            settings.scaleToCellSize = true;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("KenneyRoadSetBuilder: settings now use the primitive box pieces (no templates, no overpasses). Recalculate the CityManager.");
        }

        /// <summary>
        /// Unsaved scene with every road piece in a row at native scale, its
        /// declared sockets drawn as arrows (cyan) and ramps' uphill side in
        /// orange. Reads the settings asset live: fix connectionMask /
        /// rotationOffset in the inspector and the arrows and facing update.
        /// </summary>
        [MenuItem("Tools/Police Escape/Road Kit Showcase Scene")]
        public static void OpenShowcase()
        {
            CityGenerationSettings settings = LoadSettings();
            if (settings == null) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var root = new GameObject("RoadKitShowcase");
            const float gap = 1.5f;
            float x = 0f;
            for (int i = 0; i < settings.roadPieces.Count; i++)
            {
                RoadPieceDefinition piece = settings.roadPieces[i];
                if (piece?.prefab == null) continue;
                int width = Mathf.Max(1, piece.footprintInCells.x);

                x += width * 0.5f;
                var holder = new GameObject($"{i:00} {piece.Label}");
                holder.transform.SetParent(root.transform, false);
                holder.transform.position = new Vector3(x, 0f, 0f);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(piece.prefab, holder.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.Euler(0f, piece.rotationOffset, 0f);
                var gizmo = holder.AddComponent<RoadPieceSocketGizmo>();
                gizmo.settings = settings;
                gizmo.pieceIndex = i;
                gizmo.scale = 1f;
                x += width * 0.5f + gap;
            }

            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.position = new Vector3(x * 0.5f, 7f, -8f);
                camera.transform.LookAt(new Vector3(x * 0.5f, 0f, 0f));
            }
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("Road Kit Showcase: an arrow must point along a visible road (orange = ramp uphill). Edit the settings asset to fix masks/offsets — the scene follows live. This scene is not saved.");
        }

        // ------------------------------------------------------------- pieces

        static void AddSingle(List<RoadPieceDefinition> pieces, string file, EdgeMask mask, float weight)
        {
            GameObject prefab = LoadRoad(file);
            if (prefab == null) return;
            pieces.Add(new RoadPieceDefinition { prefab = prefab, connectionMask = mask, weight = weight });
        }

        static void AddTemplate(List<RoadPieceDefinition> pieces, string file, Vector2Int footprint, EdgeMask[] cellMasks, float chance)
        {
            GameObject prefab = LoadRoad(file);
            if (prefab == null) return;
            pieces.Add(new RoadPieceDefinition
            {
                prefab = prefab,
                footprintInCells = footprint,
                cellMasks = new List<EdgeMask>(cellMasks),
                placeChance = chance,
            });
        }

        /// <summary>
        /// Measure every ramp candidate and chain them street → deck: each
        /// link must start where the previous one ended and the last must
        /// reach the deck top. An incomplete chain is dropped (with a log)
        /// rather than stamped with a gap in it.
        /// </summary>
        static List<RoadPieceDefinition> BuildRampChain(float deckTop, out string report)
        {
            var sb = new StringBuilder();
            var links = new List<RoadPieceDefinition>();
            foreach (string file in RampCandidates)
            {
                GameObject prefab = LoadRoad(file);
                if (prefab == null) continue;
                if (!TryMeasureRamp(prefab, out float start, out float end, out int uphill))
                {
                    sb.AppendLine($"  {file}: no slope found — skipped.");
                    continue;
                }
                float offset = Mathf.DeltaAngle(0f, -uphill * 90f); // bring the uphill side to North
                links.Add(new RoadPieceDefinition
                {
                    prefab = prefab,
                    role = RoadPieceRole.Ramp,
                    connectionMask = NorthSouth,
                    rampStartHeight = start,
                    rampEndHeight = end,
                    rotationOffset = offset,
                });
                sb.AppendLine($"  {file}: surface {start:0.##} → {end:0.##}, uphill {"NESW"[uphill]} (rotationOffset {offset:0}°)");
            }

            var chain = new List<RoadPieceDefinition>();
            float current = 0f;
            bool progressed = true;
            while (progressed && current < deckTop - HeightTolerance)
            {
                progressed = false;
                foreach (RoadPieceDefinition link in links)
                {
                    if (chain.Contains(link)) continue;
                    if (Mathf.Abs(link.rampStartHeight - current) > HeightTolerance || link.rampEndHeight <= current + HeightTolerance) continue;
                    chain.Add(link);
                    current = link.rampEndHeight;
                    progressed = true;
                    break;
                }
            }

            if (chain.Count == 0 || current < deckTop - HeightTolerance)
            {
                sb.AppendLine($"  WARNING: no ramp chain climbs 0 → {deckTop:0.##}; overpasses stay off until Ramp pieces covering that range are added by hand.");
                chain.Clear();
            }
            else
            {
                sb.AppendLine($"  Ramp chain: {chain.Count} link(s), {Mathf.Atan(deckTop / chain.Count) * Mathf.Rad2Deg:0}° slope at native length.");
            }
            report = sb.ToString();
            return chain;
        }

        /// <summary>
        /// Surface height at a ramp's two ends and which side is uphill, from
        /// its vertices: the side whose highest vertex is lowest is the foot.
        /// Heights are relative to the pivot, the same frame the generator
        /// places pieces in.
        /// </summary>
        static bool TryMeasureRamp(GameObject prefab, out float start, out float end, out int uphill)
        {
            start = end = 0f;
            uphill = 0;
            List<Vector3> points = CollectVertices(prefab);
            if (points.Count == 0) return false;

            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 p in points) bounds.Encapsulate(p);
            float tolerance = Mathf.Max(bounds.size.x, bounds.size.z) * 0.03f;

            // Two passes: the whole edge decides which way is uphill (a curb
            // runs the length of the ramp, so it ranks the sides just as well),
            // then the LANE at each end gives the heights the generator uses.
            // Taking the edge maximum for those would return the curb, which is
            // a whole lane-height above the surface a wheel actually rests on.
            var sideTop = new[] { float.MinValue, float.MinValue, float.MinValue, float.MinValue }; // N, E, S, W
            var sideLane = new[] { float.MinValue, float.MinValue, float.MinValue, float.MinValue };
            float laneBandX = bounds.size.x * 0.15f;
            float laneBandZ = bounds.size.z * 0.15f;
            foreach (Vector3 p in points)
            {
                bool onLaneX = Mathf.Abs(p.x - bounds.center.x) < laneBandX;
                bool onLaneZ = Mathf.Abs(p.z - bounds.center.z) < laneBandZ;
                if (p.z > bounds.max.z - tolerance)
                {
                    sideTop[0] = Mathf.Max(sideTop[0], p.y);
                    if (onLaneX) sideLane[0] = Mathf.Max(sideLane[0], p.y);
                }
                if (p.x > bounds.max.x - tolerance)
                {
                    sideTop[1] = Mathf.Max(sideTop[1], p.y);
                    if (onLaneZ) sideLane[1] = Mathf.Max(sideLane[1], p.y);
                }
                if (p.z < bounds.min.z + tolerance)
                {
                    sideTop[2] = Mathf.Max(sideTop[2], p.y);
                    if (onLaneX) sideLane[2] = Mathf.Max(sideLane[2], p.y);
                }
                if (p.x < bounds.min.x + tolerance)
                {
                    sideTop[3] = Mathf.Max(sideTop[3], p.y);
                    if (onLaneZ) sideLane[3] = Mathf.Max(sideLane[3], p.y);
                }
            }

            int foot = 0;
            for (int i = 1; i < 4; i++)
                if (sideTop[i] < sideTop[foot]) foot = i;
            uphill = (foot + 2) & 3;
            // A ramp end with no vertex in the lane band is a mesh we don't
            // understand; fall back to the edge rather than reporting nonsense.
            start = sideLane[foot] > float.MinValue ? sideLane[foot] : sideTop[foot];
            end = sideLane[uphill] > float.MinValue ? sideLane[uphill] : sideTop[uphill];
            return end - start > 0.05f;
        }

        /// <summary>
        /// Height of a piece's driving lane above its pivot: the height of the
        /// widest flat, upward-facing surface, measured by triangle area.
        ///
        /// Deliberately NOT the bounds maximum — that is the curb at the tile
        /// edge, and taking it is what left the drivable plane a curb below the
        /// road and put a step at the foot of every ramp. Nor a vertex sample
        /// at the tile centre: these are low-poly quads with no vertices in the
        /// middle at all. Area is the one signal that works, because on every
        /// piece in the kit the lane is far wider than the curb beside it.
        /// </summary>
        /// <param name="minHeight">Ignore surfaces below this — pass the piece's mid-height for a deck, so its lane wins over the street modelled underneath it.</param>
        static float MeasureLaneHeight(GameObject prefab, float minHeight = float.MinValue)
        {
            var areaByHeight = new Dictionary<int, float>();
            Matrix4x4 worldToRoot = prefab.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                Matrix4x4 toRoot = worldToRoot * filter.transform.localToWorldMatrix;
                Vector3[] verts = mesh.vertices;
                int[] tris = mesh.triangles;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    Vector3 a = toRoot.MultiplyPoint3x4(verts[tris[i]]);
                    Vector3 b = toRoot.MultiplyPoint3x4(verts[tris[i + 1]]);
                    Vector3 c = toRoot.MultiplyPoint3x4(verts[tris[i + 2]]);

                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    float area = cross.magnitude * 0.5f;
                    if (area < 1e-6f) continue;
                    if (cross.y / (area * 2f) < 0.9f) continue; // flat and facing up

                    float y = (a.y + b.y + c.y) / 3f;
                    if (y < minHeight) continue;
                    int key = Mathf.RoundToInt(y * 1000f);
                    areaByHeight[key] = areaByHeight.TryGetValue(key, out float acc) ? acc + area : area;
                }
            }

            float bestArea = 0f;
            float lane = 0f;
            foreach (KeyValuePair<int, float> level in areaByHeight)
                if (level.Value > bestArea)
                {
                    bestArea = level.Value;
                    lane = level.Key / 1000f;
                }
            return bestArea > 0f ? lane : 0f;
        }

        /// <summary>Mid-height of a piece in its own frame — the cut that separates a bridge's deck from the street modelled beneath it.</summary>
        static float MidHeight(GameObject prefab)
        {
            List<Vector3> points = CollectVertices(prefab);
            if (points.Count == 0) return float.MinValue;
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 p in points) bounds.Encapsulate(p);
            return bounds.center.y;
        }

        static List<Vector3> CollectVertices(GameObject prefab)
        {
            var points = new List<Vector3>();
            Matrix4x4 worldToRoot = prefab.transform.worldToLocalMatrix;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                Matrix4x4 toRoot = worldToRoot * filter.transform.localToWorldMatrix;
                foreach (Vector3 v in mesh.vertices) points.Add(toRoot.MultiplyPoint3x4(v));
            }
            return points;
        }

        // ------------------------------------------------------------ helpers

        static CityGenerationSettings LoadSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CityGenerationSettings>(SettingsPath);
            if (settings == null)
                Debug.LogError($"KenneyRoadSetBuilder: settings not found at {SettingsPath} — run 'Create City Test Scene' first.");
            return settings;
        }

        static GameObject LoadRoad(string file)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{RoadsFolder}/{file}.fbx");
            if (prefab == null) Debug.LogWarning($"KenneyRoadSetBuilder: '{file}.fbx' not found in {RoadsFolder} — skipped.");
            return prefab;
        }

        /// <summary>Importer-generated mesh colliders: pre-baked at import, so they work in builds where the meshes are not CPU-readable.</summary>
        static void EnableImporterCollider(string file)
        {
            string path = $"{RoadsFolder}/{file}.fbx";
            if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.addCollider)
            {
                importer.addCollider = true;
                importer.SaveAndReimport();
            }
        }

        static Bounds MeasureBounds(GameObject prefab)
        {
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        /// <summary>XZ footprint of a prefab's combined renderer bounds, in its native units.</summary>
        static Vector2 MeasureFootprint(GameObject prefab)
        {
            Bounds bounds = MeasureBounds(prefab);
            return new Vector2(bounds.size.x, bounds.size.z);
        }
    }
}
