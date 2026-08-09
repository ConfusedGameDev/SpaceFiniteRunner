using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Entry point and orchestrator of the procedural city. Recalculate works
    /// in edit mode (no play required): it clears previous output, generates a
    /// ChunkData per chunk of the initial grid and stamps socket-matched road
    /// pieces under City/Chunk_{x}_{y}/Roads. Generated objects carry DontSave
    /// flags — they are never written into the scene file and are rebuilt from
    /// seed + settings on demand (play mode regenerates automatically).
    /// Gizmos overlay the grid model: road cells colored by socket count,
    /// chunk borders, per-chunk seed labels.
    /// </summary>
    public class CityManager : MonoBehaviour
    {
        const int SaltPiecePick = 404;

        [Required, InlineEditor]
        [Tooltip("All generation tunables live on this asset — add new knobs there, not here.")]
        public CityGenerationSettings settings;

        [TitleGroup("Gizmos")]
        [Tooltip("Draw the grid model overlay (road cells, chunk borders, seed labels).")]
        public bool drawGizmos = true;

        readonly List<CityChunk> chunks = new();

        void Awake()
        {
            // Generated content is never saved with the scene, so a play-mode
            // session always starts empty and rebuilds from seed + settings.
            if (Application.isPlaying) Recalculate();
        }

        // ------------------------------------------------------------- buttons

        [TitleGroup("Actions")]
        [Button("Recalculate", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void Recalculate()
        {
            if (settings == null)
            {
                Debug.LogWarning("CityManager: assign a CityGenerationSettings asset first.");
                return;
            }

            // Fresh seed every press — unless a saved seed is locked in on the settings.
            settings.PrepareSeedForRecalculate();

            Clear();
            int half = settings.initialCitySizeInChunks / 2;
            int size = settings.initialCitySizeInChunks;
            for (int cy = -half; cy < size - half; cy++)
            for (int cx = -half; cx < size - half; cx++)
            {
                var coord = new Vector2Int(cx, cy);
                var data = RoadNetworkGenerator.Generate(settings, coord);
                BuildChunk(coord, data);
            }
        }

        [TitleGroup("Actions")]
        [Button("Clear", ButtonSizes.Large)]
        public void Clear()
        {
            chunks.Clear();
            // Also sweep by component so orphans from before a domain reload are found.
            var stale = GetComponentsInChildren<CityChunk>(true);
            foreach (var chunk in stale)
            {
                if (chunk == null) continue;
                if (Application.isPlaying) Destroy(chunk.gameObject);
                else DestroyImmediate(chunk.gameObject);
            }
        }

        // ------------------------------------------------------------ building

        void BuildChunk(Vector2Int coord, ChunkData data)
        {
            var chunkGo = new GameObject($"Chunk_{coord.x}_{coord.y}");
            ApplyGeneratedFlags(chunkGo);
            chunkGo.transform.SetParent(transform, false);
            chunkGo.transform.localPosition = new Vector3(
                coord.x * settings.chunkSizeInCells * settings.cellSize,
                0f,
                coord.y * settings.chunkSizeInCells * settings.cellSize);

            var chunk = chunkGo.AddComponent<CityChunk>();
            chunk.Initialize(coord, data);
            chunks.Add(chunk);

            var roadsGo = new GameObject("Roads");
            ApplyGeneratedFlags(roadsGo);
            roadsGo.transform.SetParent(chunkGo.transform, false);

            // Piece picking gets its own deterministic stream, separate from layout.
            var rng = new System.Random(DeterministicHash.Combine(settings.globalSeed, SaltPiecePick, coord.x, coord.y));
            var missingMasks = new HashSet<EdgeMask>();

            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                if (!data.IsRoad(x, y)) continue;
                EdgeMask mask = data.GetConnections(x, y);
                if (mask == EdgeMask.None) continue;

                if (!TryPickPiece(mask, rng, out var piece, out int quarterTurns))
                {
                    missingMasks.Add(mask);
                    continue;
                }

                var instance = Instantiate(piece.prefab, roadsGo.transform);
                ApplyGeneratedFlags(instance);
                instance.transform.localPosition = new Vector3((x + 0.5f) * settings.cellSize, 0f, (y + 0.5f) * settings.cellSize);
                instance.transform.localRotation = Quaternion.Euler(0f, quarterTurns * 90f + piece.rotationOffset, 0f);
                if (settings.scaleToCellSize && settings.pieceNativeSize > 0.0001f)
                    instance.transform.localScale = Vector3.one * (settings.cellSize / settings.pieceNativeSize);
            }

            foreach (var mask in missingMasks)
                Debug.LogWarning($"CityManager: no road piece matches socket mask [{mask}] — those cells were left empty. Add a matching piece to the settings (dead ends need a single-socket piece).");
        }

        /// <summary>
        /// Weighted pick among every (piece, rotation) pair whose rotated socket
        /// mask equals the cell's mask. Symmetric pieces match at several
        /// rotations; each counts as its own candidate so weights stay honest.
        /// </summary>
        bool TryPickPiece(EdgeMask target, System.Random rng, out RoadPieceDefinition picked, out int quarterTurns)
        {
            picked = null;
            quarterTurns = 0;
            float totalWeight = 0f;

            foreach (var piece in settings.roadPieces)
            {
                if (piece?.prefab == null) continue;
                for (int turns = 0; turns < 4; turns++)
                {
                    if (piece.connectionMask.RotateCw(turns) != target) continue;
                    totalWeight += piece.weight;
                    // Reservoir-style single pass: replace the pick with probability weight/total.
                    if ((float)rng.NextDouble() * totalWeight <= piece.weight)
                    {
                        picked = piece;
                        quarterTurns = turns;
                    }
                }
            }
            return picked != null;
        }

        static void ApplyGeneratedFlags(GameObject go)
        {
            // DontSave keeps edit-mode output out of the scene file; in play
            // mode normal scene teardown handles cleanup, so don't set it there
            // (DontSave objects would survive the reload and leak).
            go.hideFlags = Application.isPlaying
                ? HideFlags.NotEditable
                : HideFlags.DontSave | HideFlags.NotEditable;
        }

        // -------------------------------------------------------------- gizmos

        void OnDrawGizmos()
        {
            if (!drawGizmos || settings == null) return;

            // After a domain reload the runtime chunk list is empty but the
            // spawned objects may still exist — read data off the markers.
            foreach (var chunk in GetComponentsInChildren<CityChunk>())
            {
                if (chunk.Data == null) continue;
                DrawChunkGizmos(chunk);
            }
        }

        void DrawChunkGizmos(CityChunk chunk)
        {
            ChunkData data = chunk.Data;
            float cell = settings.cellSize;
            Vector3 origin = chunk.transform.position;
            float sideMeters = data.SizeInCells * cell;

            // Chunk border + seed label.
            Gizmos.color = Color.green;
            Vector3 center = origin + new Vector3(sideMeters * 0.5f, 0f, sideMeters * 0.5f);
            Gizmos.DrawWireCube(center, new Vector3(sideMeters, 0.1f, sideMeters));
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                origin + new Vector3(0.5f, 0f, 0.5f) * cell,
                $"Chunk {chunk.Coord}  seed {RoadNetworkGenerator.ChunkSeed(settings, chunk.Coord)}");
#endif

            for (int y = 0; y < data.SizeInCells; y++)
            for (int x = 0; x < data.SizeInCells; x++)
            {
                if (!data.IsRoad(x, y)) continue;
                EdgeMask mask = data.GetConnections(x, y);
                Gizmos.color = mask.ConnectionCount() switch
                {
                    1 => Color.red,                                  // dead end
                    2 => mask.RotateCw(2) == mask
                        ? new Color(0.3f, 0.9f, 1f)                  // straight
                        : Color.yellow,                              // corner
                    3 => Color.magenta,                              // T-junction
                    4 => Color.white,                                // crossroad
                    _ => Color.grey,
                };
                Vector3 cellCenter = origin + new Vector3((x + 0.5f) * cell, 0.05f, (y + 0.5f) * cell);
                Gizmos.DrawCube(cellCenter, new Vector3(cell * 0.85f, 0.05f, cell * 0.85f));
            }
        }
    }
}
