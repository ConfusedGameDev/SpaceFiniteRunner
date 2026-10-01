using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Stamps road-kit meshes along the track spline: road surface pieces and
    /// side barriers on both edges. Streaming-friendly — the TrackGenerator
    /// calls <see cref="DecorateUpTo"/> as the endless track grows and
    /// <see cref="CullBefore"/> to drop pieces left behind the ship.
    /// Straight pieces stamped every ~20 m conform fine to the long-radius
    /// sweeps this game uses; the grid corner pieces from the kit are not used.
    /// <b>The barrier walls stand on the ROAD's outer lip</b>, the top of the
    /// banked shoulders (<see cref="TrackManager.GetRoadBand"/>), not on the
    /// crease where a shoulder leaves the lane: a full-width piece is authored
    /// to span the lane, so it is stretched by the slab's share of it, and the
    /// placeholder wall and the open-edge marker are lifted to the lip. The
    /// bank inside is road the ship may ride.
    /// <b>Where the track says an edge is open</b>
    /// (<see cref="TrackManager.IsEdgeOpen"/> — the outer side of a flat
    /// sweep, both sides of an open straight) <b>the wall is left off that
    /// side</b>: the same flag the
    /// physics reads, so a missing wall is always a real drop. With side
    /// barriers that is just a skipped stamp; with a full-width barrier piece
    /// (both walls in one mesh) the piece is swapped for
    /// <see cref="oneSidedBarrierPrefab"/>, or for a code-built placeholder
    /// wall on the closed side until that art exists. The open edge itself
    /// gets a low marker strip so it reads from a distance.
    /// <b>Pieces are blended along the track</b> (<see cref="pieceBlend"/>):
    /// a rigid piece is posed once at its centre, so where the bank changes
    /// its neighbour sits at a different roll and the outer edges step apart
    /// by metres (dark wedges between the pieces on a banked sweep).
    /// <see cref="PieceBlend.Blended"/> bends each vertex to its own distance
    /// along the road, so neighbouring pieces follow the same curve and bank
    /// and meet exactly. <see cref="PieceBlend.NotBlended"/> keeps the gaps on
    /// purpose, and two sliders move each piece between the two — 0 =
    /// blended, 1 = rigid, past 1 the piece's ends over-rotate away from the
    /// track so the gaps open wider: <see cref="pieceMorph"/> for the curve
    /// (yaw, pitch and the piece's spine), <see cref="pieceZMorph"/> for the
    /// twist round the forward (Z) axis, the bank. The meshes must be
    /// Read/Write enabled; a piece whose mesh is not stays rigid.
    /// </summary>
    public class TrackDecorator : MonoBehaviour
    {
        /// <summary>How stamped pieces meet along the track. Serialized: append only.</summary>
        public enum PieceBlend
        {
            /// <summary>Every piece bent along the track; neighbours meet without gaps.</summary>
            Blended = 0,
            /// <summary>Pieces keep their own pose; <see cref="pieceMorph"/> sets how far.</summary>
            NotBlended = 1,
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] TrackManager track;
        [SerializeField] Transform decorParent;

        [Header("Road surface")]
        [SerializeField] GameObject roadPrefab;
        [Tooltip("X spans the road width, Z is the stamp length along the track.")]
        [SerializeField] Vector3 roadScale = new(120f, 2f, 20f);
        [SerializeField, Min(1f)] float roadSpacing = 20f;

        [Tooltip("Yaw applied to each road piece. The kit tiles run along local X, so 90 aligns them with the track.")]
        [SerializeField] float roadYaw = 90f;

        [Tooltip("If set, replaces the road pieces' material so the track reads against bright surroundings. (MPB tints are unreliable with the SRP Batcher.)")]
        [SerializeField] Material roadMaterialOverride;
        [Tooltip("Vertical offset of road pieces below the ship's flight line.")]
        [SerializeField] float roadYOffset = -1.2f;

        [Tooltip("How stamped pieces (road, barriers, walls, markers, tube strips) meet. Blended = bent along the track vertex by vertex, so they follow the curve and the bank and meet without gaps. Not Blended = pieces keep their own pose and open wedges where the bank changes; Piece Morph and Piece Z Morph set how much. Needs Read/Write on the piece meshes. In edit mode a change re-stamps the road already drawn; in play it applies to pieces stamped after it.")]
        [SerializeField] PieceBlend pieceBlend = PieceBlend.Blended;

        [Tooltip("Not Blended only: the CURVE (yaw and pitch, where the piece's spine runs). 0 = follows the track like Blended, 1 = rigid (the plain gaps), above 1 = each piece's ends over-rotate away from the curve so the gaps open wider.")]
        [SerializeField, ShowIf(nameof(IsNotBlended)), PropertyRange(0f, 3f)] float pieceMorph = 1f;

        [Tooltip("Not Blended only: the TWIST round the forward (Z) axis, the bank. 0 = each piece rolls with the track like Blended, 1 = rigid (the plain bank steps), above 1 = each piece's ends over-roll so the edges step up and down unevenly.")]
        [SerializeField, ShowIf(nameof(IsNotBlended)), PropertyRange(0f, 3f)] float pieceZMorph = 1f;

        bool IsNotBlended => pieceBlend == PieceBlend.NotBlended;

        // How far each piece is from fully bent, curve (x) and twist (y):
        // 0 = bent, 1 = rigid, > 1 = over-rotated.
        Vector2 Morph => IsNotBlended ? new Vector2(pieceMorph, pieceZMorph) : Vector2.zero;

        // The blend the road on screen was stamped with: set when a fresh
        // road starts stamping, so an edit-mode change can tell it is stale.
        // NonSerialized: a script reload must not carry it over.
        [System.NonSerialized] Vector2 stampedMorph;

#if UNITY_EDITOR
        // Edit mode: a change to Piece Blend / Piece Morph / Piece Z Morph
        // (undo included) re-stamps the road already drawn, so the preview
        // always shows the current look. Deferred, since OnValidate may not
        // create or destroy objects; the same handler is queued once however
        // many times a slider drag validates.
        void OnValidate()
        {
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall -= RestampForBlend;
            UnityEditor.EditorApplication.delayCall += RestampForBlend;
        }

        void RestampForBlend()
        {
            // Nothing stamped by this instance (none yet, or a script reload
            // dropped the list): nothing it can re-stamp without doubling.
            if (this == null || Application.isPlaying || stamped.Count == 0 || stampedMorph == Morph) return;
            stampedMorph = Morph;
            Restamp(0f, stampCursor);
        }
#endif

        [Header("Side barriers")]
        [SerializeField] GameObject barrierPrefab;
        [SerializeField] Vector3 barrierScale = new(4f, 25f, 20f);
        [Tooltip("Material for the barrier pieces (full-width and one-sided) and the placeholder wall. Empty = the road override. The NeonRoad shader reads the piece's MESH extents, so a kit piece with a different mesh needs its own material.")]
        [SerializeField] Material barrierMaterialOverride;
        [Tooltip("Lateral distance of the barrier strip from the track center.")]
        [SerializeField] float barrierLateral = 30.5f;

        [Header("Open edges (flat sweeps)")]
        [Tooltip("Full-width barrier piece with a wall on its LEFT side only, used where the right edge is open (and turned round for an open left edge). Empty = a code-built placeholder wall on the closed side.")]
        [SerializeField] GameObject oneSidedBarrierPrefab;
        [Tooltip("Placeholder wall: thickness and height, metres.")]
        [SerializeField] Vector2 placeholderWallSize = new(2f, 12f);
        [Tooltip("Material of the placeholder wall (a unit cube: local Y is its height). Empty = the barrier material, then the road override.")]
        [SerializeField] Material placeholderWallMaterial;
        [Tooltip("Marker strip along an open edge: width and height, metres. 0 width = none.")]
        [SerializeField] Vector2 openEdgeMarkerSize = new(1.5f, 0.6f);
        [Tooltip("Material of the open-edge marker (a hot emissive reads best). Empty = the road material.")]
        [SerializeField] Material openEdgeMaterial;

        [Header("Tubes")]
        [Tooltip("Target width of one road strip round a tube section, metres of arc. The band is cut into equal strips no wider than this, each a tube road piece scaled to the strip.")]
        [SerializeField, Min(2f)] float tubeStripWidth = 25f;
        [Tooltip("The piece a tube strip is made of. Empty = the road piece. A profiled slab (banked shoulders) makes a ribbed pipe: give tubes a flat piece.")]
        [SerializeField] GameObject tubeRoadPrefab;
        [Tooltip("Scale of the tube piece at the reference width, same axes as the road scale. Only read when a tube piece is set.")]
        [SerializeField] Vector3 tubeRoadScale = new(40f, 10f, 60f);
        [Tooltip("Material of the tube strips. Empty = the road override. The NeonRoad shader is per-mesh, so a different tube piece needs its own material.")]
        [SerializeField] Material tubeMaterialOverride;

        // The road/barrier scales and the barrier lateral above are authored
        // for this track width; SetTrackWidth stretches them proportionally.
        const float ReferenceTrackWidth = 60f;

        // Streaming state: distance of the next stamp, and every live piece
        // tagged with the distance it was stamped at (for culling).
        float stampCursor;
        float widthScale = 1f;
        readonly List<(float distance, GameObject go)> stamped = new();
        // The end markers: placed by the generator, not by StampAt, so a
        // re-stamp leaves them standing (it could not put them back).
        readonly HashSet<GameObject> endMarkers = new();

        /// <summary>
        /// Adapts the authored piece scales to the given full track width
        /// (Core Settings on the TrackGenerator). Only affects pieces stamped
        /// afterwards — the generator regenerates, so everything restamps.
        /// </summary>
        /// <summary>Where the road pieces' surface sits along the track's up, relative to the flight line (negative = below it). Anything that must float over the visible road measures from here.</summary>
        public float RoadYOffset => roadYOffset;

        public void SetTrackWidth(float width) => widthScale = Mathf.Max(0.05f, width / ReferenceTrackWidth);

        Material BarrierMaterial => barrierMaterialOverride != null ? barrierMaterialOverride : roadMaterialOverride;

        // A full-width barrier piece is authored to span the LANE, and its
        // walls stand on the ends of that span. They belong on the road's
        // outer lip instead — past the banked shoulders, which are road the
        // ship may ride — so the piece is stretched across the whole slab.
        float BarrierWidthFactor => widthScale * (track != null ? track.RoadHalfWidth / Mathf.Max(0.01f, track.HalfWidth) : 1f);

        // The kit tiles are yawed to align with the track, so which local axis
        // spans the road width depends on that yaw: near ±90 it is Z, else X.
        Vector3 ScaleAcrossWidth(Vector3 scale) => ScaleAcrossWidth(scale, widthScale);

        Vector3 ScaleAcrossWidth(Vector3 scale, float factor)
        {
            if (Mathf.Abs(Mathf.Cos(roadYaw * Mathf.Deg2Rad)) < 0.5f) scale.z *= factor;
            else scale.x *= factor;
            return scale;
        }

        /// <summary>Clears everything and re-stamps the whole current track.</summary>
        public void Decorate()
        {
            Clear();
            DecorateUpTo(track != null ? track.Length : 0f);
        }

        /// <summary>Stamps road and barriers from the last stamped point up to <paramref name="distance"/>.</summary>
        public void DecorateUpTo(float distance)
        {
            if (track == null || decorParent == null) return;
            if (stampCursor <= 0f) stampCursor = roadSpacing * 0.5f;
            if (stamped.Count == 0) stampedMorph = Morph; // a fresh road

            float limit = Mathf.Min(distance, track.Length);
            while (stampCursor < limit)
            {
                StampAt(stampCursor);
                stampCursor += roadSpacing;
            }
            if (!Application.isPlaying) TrackGenerator.MarkPreview(decorParent); // a preview never lands in the scene file
        }

        /// <summary>Destroys every stamped piece before <paramref name="distance"/>.</summary>
        public void CullBefore(float distance)
        {
            for (int i = stamped.Count - 1; i >= 0; i--)
            {
                if (stamped[i].distance >= distance) continue;
                endMarkers.Remove(stamped[i].go);
                if (stamped[i].go != null) SafeDestroy(stamped[i].go);
                stamped.RemoveAt(i);
            }
        }

        void StampAt(float d)
        {
            if (track.SectionAt(d) is TubeSection tube)
            {
                StampTube(d, tube);
                return;
            }

            track.GetPoseAtDistance(d, 0f, out Vector3 pos, out Quaternion rot);

            if (roadPrefab != null)
            {
                var piece = Stamp(d, 0f, roadPrefab, pos + rot * new Vector3(0f, roadYOffset, 0f),
                                  rot * Quaternion.Euler(0f, roadYaw, 0f), ScaleAcrossWidth(roadScale));
                if (roadMaterialOverride != null) OverrideMaterials(piece, roadMaterialOverride);
            }

            bool openLeft = track.IsEdgeOpen(d, -1);
            bool openRight = track.IsEdgeOpen(d, 1);
            if (openLeft) StampOpenEdgeMarker(d, -1);
            if (openRight) StampOpenEdgeMarker(d, 1);
            int openSide = openLeft == openRight ? 0 : openLeft ? -1 : 1; // the ONE open side, when there is just one

            if (barrierPrefab != null)
            {
                if (openLeft && openRight && Mathf.Abs(barrierLateral) < 0.01f)
                {
                    // Both walls gone: the full-width piece has nothing left to show.
                }
                else if (openSide != 0 && Mathf.Abs(barrierLateral) < 0.01f)
                {
                    // The full-width piece carries both walls: swap it for the
                    // one-sided piece (authored wall on the left, so an open
                    // LEFT edge turns it round), or stand a placeholder wall
                    // on the closed side.
                    if (oneSidedBarrierPrefab != null)
                    {
                        var b = Stamp(d, 0f, oneSidedBarrierPrefab, pos + rot * new Vector3(0f, roadYOffset, 0f),
                                      rot * Quaternion.Euler(0f, roadYaw + (openSide < 0 ? 180f : 0f), 0f), ScaleAcrossWidth(barrierScale, BarrierWidthFactor));
                        if (BarrierMaterial != null) OverrideMaterials(b, BarrierMaterial);
                    }
                    else StampPlaceholderWall(d, -openSide);
                }
                else if (Mathf.Abs(barrierLateral) < 0.01f)
                {
                    // Full-width piece (e.g. road-straight-barrier): one centered stamp.
                    var b = Stamp(d, 0f, barrierPrefab, pos + rot * new Vector3(0f, roadYOffset, 0f),
                                  rot * Quaternion.Euler(0f, roadYaw, 0f), ScaleAcrossWidth(barrierScale, BarrierWidthFactor));
                    if (BarrierMaterial != null) OverrideMaterials(b, BarrierMaterial);
                }
                else
                {
                    float lateral = barrierLateral * widthScale;
                    if (!openLeft)
                    {
                        track.GetPoseAtDistance(d, -lateral, out Vector3 lp, out Quaternion lr);
                        var bl = Stamp(d, -lateral, barrierPrefab, lp + lr * new Vector3(0f, roadYOffset, 0f),
                                       lr * Quaternion.Euler(0f, roadYaw, 0f), barrierScale);
                        if (BarrierMaterial != null) OverrideMaterials(bl, BarrierMaterial);
                    }

                    if (!openRight)
                    {
                        track.GetPoseAtDistance(d, lateral, out Vector3 rp, out Quaternion rr);
                        var br = Stamp(d, lateral, barrierPrefab, rp + rr * new Vector3(0f, roadYOffset, 0f),
                                       rr * Quaternion.Euler(0f, roadYaw + 180f, 0f), barrierScale);
                        if (BarrierMaterial != null) OverrideMaterials(br, BarrierMaterial);
                    }
                }
            }
        }

        // Placeholder for the one-sided barrier art: a plain wall standing on
        // the closed side's edge, as long as one stamp.
        void StampPlaceholderWall(float d, int side)
        {
            float thickness = Mathf.Max(0.1f, placeholderWallSize.x);
            float height = Mathf.Max(0.1f, placeholderWallSize.y);
            float lateral = side * (track.RoadHalfWidth + thickness * 0.5f);
            float lift = track.HasShoulders(d) ? track.ShoulderRise : 0f;
            track.GetPoseAtDistance(d, lateral, out Vector3 pos, out Quaternion rot);
            StampBox(d, pos + rot * new Vector3(0f, roadYOffset + lift + height * 0.5f, 0f), rot,
                     new Vector3(thickness, height, roadSpacing),
                     placeholderWallMaterial != null ? placeholderWallMaterial : BarrierMaterial, lateral);
        }

        // A low strip along the open edge, so the missing wall reads as a
        // marked drop and not as a hole in the streaming.
        void StampOpenEdgeMarker(float d, int side)
        {
            if (openEdgeMarkerSize.x <= 0f) return;
            float lateral = side * (track.RoadHalfWidth - openEdgeMarkerSize.x * 0.5f);
            float lift = track.HasShoulders(d) ? track.ShoulderRise : 0f;
            track.GetPoseAtDistance(d, lateral, out Vector3 pos, out Quaternion rot);
            StampBox(d, pos + rot * new Vector3(0f, lift + openEdgeMarkerSize.y * 0.5f, 0f), rot,
                     new Vector3(openEdgeMarkerSize.x, Mathf.Max(0.05f, openEdgeMarkerSize.y), roadSpacing),
                     openEdgeMaterial != null ? openEdgeMaterial : roadMaterialOverride, lateral);
        }

        /// <summary>
        /// Marks a gap between two of the ramps a finite track ends in: a low
        /// strip in the open-edge material down the whole gap, from the foot
        /// of the ramps to the end of the road, so the lane that leads
        /// nowhere reads as a drop. Keyed on its far end for the cull.
        /// </summary>
        public void StampEndMarker(float startDistance, float lateralCentre, float width, float length)
        {
            if (track == null || width <= 0f || length <= 0f) return;
            float height = Mathf.Max(0.05f, openEdgeMarkerSize.y);
            track.GetPoseAtDistance(startDistance + length * 0.5f, lateralCentre, out Vector3 pos, out Quaternion rot);
            StampBox(startDistance + length, pos + rot * new Vector3(0f, height * 0.5f, 0f), rot,
                     new Vector3(width, height, length),
                     openEdgeMaterial != null ? openEdgeMaterial : roadMaterialOverride);
            endMarkers.Add(stamped[stamped.Count - 1].go);
        }

        // A code-built box: a picture only, so its collider goes (nothing on
        // the track may trip the ship's trigger volume). Given a lateral, it
        // was posed at (distance, lateral) and is bent along the track there;
        // without one (the end markers, on the straight run-up) it stays rigid.
        void StampBox(float distance, Vector3 position, Quaternion rotation, Vector3 size, Material material, float lateral = float.NaN)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "OpenEdgePiece";
            var collider = box.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            box.transform.SetParent(decorParent, false);
            box.transform.SetPositionAndRotation(position, rotation);
            box.transform.localScale = size;
            if (material != null) box.GetComponent<Renderer>().sharedMaterial = material;
            stamped.Add((distance, box));
            if (!float.IsNaN(lateral)) Bend(box, distance, lateral);
        }

        /// <summary>
        /// A tube is the same road bent into a pipe: the lane at this distance
        /// (an arc, easing in over the curl) is cut into equal strips no wider
        /// than <see cref="tubeStripWidth"/>, each an ordinary road piece at
        /// its own lateral, so the section's pose function bends the road for
        /// us. No barriers on a tube: the band clamp is the fence, and a
        /// wall standing off a curved road only reads as clutter.
        /// </summary>
        void StampTube(float d, TubeSection tube)
        {
            track.GetLateralBand(d, out float min, out float max);
            float bandWidth = Mathf.Max(1f, max - min);
            int strips = Mathf.Max(1, Mathf.CeilToInt(bandWidth / Mathf.Max(2f, tubeStripWidth)));
            float stripWidth = bandWidth / strips;

            GameObject prefab = tubeRoadPrefab != null ? tubeRoadPrefab : roadPrefab;
            Material material = tubeMaterialOverride != null ? tubeMaterialOverride : roadMaterialOverride;
            if (prefab != null)
            {
                Vector3 scale = ScaleAcrossWidth(tubeRoadPrefab != null ? tubeRoadScale : roadScale, stripWidth / ReferenceTrackWidth);
                for (int i = 0; i < strips; i++)
                {
                    float lateral = min + (i + 0.5f) * stripWidth;
                    track.GetPoseAtDistance(d, lateral, out Vector3 pos, out Quaternion rot);
                    var piece = Stamp(d, lateral, prefab, pos + rot * new Vector3(0f, roadYOffset, 0f),
                                      rot * Quaternion.Euler(0f, roadYaw, 0f), scale);
                    if (material != null) OverrideMaterials(piece, material);
                }
            }

        }

        // A piece posed at the track pose (distance, lateral), then bent there.
        GameObject Stamp(float distance, float lateral, GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var piece = Instantiate(prefab, position, rotation, decorParent);
            piece.transform.localScale = scale;
            stamped.Add((distance, piece));
            Bend(piece, distance, lateral);
            return piece;
        }

        // Poses (bent, morphed) at (distance + along) for the piece being bent,
        // keyed on the along offset: a kit mesh has few distinct rows of vertices.
        readonly Dictionary<int, (Vector3 position, Quaternion rotation)> bendPoses = new();

        /// <summary>
        /// Re-poses every vertex of <paramref name="piece"/> on the track: in
        /// the frame of the pose it was stamped at, (distance, lateral), a
        /// vertex is (x across, y up, z along); it moves to the pose at
        /// distance + z, keeping its x and y in that pose's frame. With a
        /// <see cref="Morph"/> that pose is blended (unclamped) toward the
        /// rigid one — the stamp pose carried z along its own forward — so 1
        /// is the rigid piece and above 1 over-rotates it: the turn from the
        /// stamp pose is split into its swing (the curve, with the spine's
        /// position) and its twist round Z (the bank), each scaled by its own
        /// morph. Normals and
        /// tangents turn with the pose. Each piece gets its own mesh copies,
        /// freed with it (<see cref="BentPiece"/>).
        /// </summary>
        void Bend(GameObject piece, float distance, float lateral)
        {
            Vector2 morph = Morph;
            // Rigid: the stamp already is the piece, no mesh copy needed.
            if ((morph - Vector2.one).sqrMagnitude < 1e-6f || track == null) return;
            track.GetPoseAtDistance(distance, lateral, out Vector3 basePosition, out Quaternion baseRotation);
            Quaternion toBase = Quaternion.Inverse(baseRotation);
            bendPoses.Clear();
            BentPiece owner = null;

            foreach (var filter in piece.GetComponentsInChildren<MeshFilter>())
            {
                Mesh source = filter.sharedMesh;
                if (source == null || !source.isReadable) continue;

                Mesh mesh = Instantiate(source);
                mesh.name = source.name + " (bent)";
                if (!Application.isPlaying) mesh.hideFlags = HideFlags.DontSaveInEditor;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector4[] tangents = mesh.tangents;
                bool hasNormals = normals.Length == vertices.Length;
                bool hasTangents = tangents.Length == vertices.Length;

                Transform t = filter.transform;
                Matrix4x4 toWorld = t.localToWorldMatrix;
                Matrix4x4 toLocal = t.worldToLocalMatrix;
                // Normals go through the inverse transpose (the pieces are scaled unevenly).
                Matrix4x4 normalToWorld = toLocal.transpose;
                Matrix4x4 normalToLocal = toWorld.transpose;

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 local = toBase * (toWorld.MultiplyPoint3x4(vertices[i]) - basePosition);
                    int key = Mathf.RoundToInt(local.z * 100f);
                    if (!bendPoses.TryGetValue(key, out var pose))
                    {
                        float along = key * 0.01f;
                        track.GetPoseAtDistance(distance + along, lateral, out Vector3 p, out Quaternion r);
                        if (morph != Vector2.zero)
                        {
                            Vector3 rigid = basePosition + baseRotation * new Vector3(0f, 0f, along);
                            p = Vector3.LerpUnclamped(p, rigid, morph.x);
                            // The turn from the stamp pose, in its frame = swing × twist round Z.
                            Quaternion delta = toBase * r;
                            if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w); // shortest way round
                            Quaternion twist = TwistAboutZ(delta);
                            Quaternion swing = delta * Quaternion.Inverse(twist);
                            r = baseRotation
                                * Quaternion.SlerpUnclamped(Quaternion.identity, swing, 1f - morph.x)
                                * Quaternion.SlerpUnclamped(Quaternion.identity, twist, 1f - morph.y);
                        }
                        pose = (p, r);
                        bendPoses[key] = pose;
                    }

                    Quaternion turn = pose.rotation * toBase;
                    vertices[i] = toLocal.MultiplyPoint3x4(pose.position + pose.rotation * new Vector3(local.x, local.y, 0f));
                    if (hasNormals)
                        normals[i] = normalToLocal.MultiplyVector(turn * normalToWorld.MultiplyVector(normals[i])).normalized;
                    if (hasTangents)
                    {
                        Vector3 tangent = toLocal.MultiplyVector(turn * toWorld.MultiplyVector(tangents[i])).normalized;
                        tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
                    }
                }

                mesh.vertices = vertices;
                if (hasNormals) mesh.normals = normals;
                if (hasTangents) mesh.tangents = tangents;
                mesh.RecalculateBounds();
                filter.sharedMesh = mesh;

                if (owner == null) owner = piece.AddComponent<BentPiece>();
                owner.Own(mesh);
            }
        }

        // The part of a rotation that turns about local Z (swing-twist split).
        static Quaternion TwistAboutZ(Quaternion q)
        {
            var twist = new Quaternion(0f, 0f, q.z, q.w);
            float length = Mathf.Sqrt(twist.z * twist.z + twist.w * twist.w);
            if (length < 1e-6f) return Quaternion.identity;
            return new Quaternion(0f, 0f, twist.z / length, twist.w / length);
        }

        /// <summary>
        /// Takes down and re-stamps every piece whose stamp distance lies in
        /// [<paramref name="from"/>, <paramref name="to"/>] (only what was
        /// already stamped) — the road editor's live drag, where only that
        /// stretch of road changed shape — and the whole drawn road when the
        /// piece blend changes in edit mode. The end markers are left alone.
        /// </summary>
        public void Restamp(float from, float to)
        {
            if (track == null || decorParent == null) return;
            for (int i = stamped.Count - 1; i >= 0; i--)
            {
                float d = stamped[i].distance;
                if (d < from || d > to || endMarkers.Contains(stamped[i].go)) continue;
                if (stamped[i].go != null) SafeDestroy(stamped[i].go);
                stamped.RemoveAt(i);
            }
            float first = roadSpacing * 0.5f;
            int k = Mathf.Max(0, Mathf.CeilToInt((from - first) / roadSpacing));
            for (float d = first + k * roadSpacing; d <= to && d < stampCursor; d += roadSpacing) StampAt(d);
            if (!Application.isPlaying) MarkPreview();
        }

        /// <summary>Flags every stamped piece as an edit-mode preview (never saved into the scene).</summary>
        public void MarkPreview() => TrackGenerator.MarkPreview(decorParent);

        public void Clear()
        {
            stamped.Clear();
            endMarkers.Clear();
            stampCursor = 0f;
            if (decorParent == null) return;
            for (int i = decorParent.childCount - 1; i >= 0; i--)
                SafeDestroy(decorParent.GetChild(i).gameObject);
        }

        public static void SafeDestroy(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        /// <summary>Replaces every renderer's materials on an instance.</summary>
        public static void OverrideMaterials(GameObject go, Material material)
        {
            foreach (var rend in go.GetComponentsInChildren<Renderer>())
            {
                var mats = rend.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                rend.sharedMaterials = mats;
            }
        }

        /// <summary>Tints every renderer of an instance via property block (unreliable with SRP Batcher — prefer OverrideMaterials).</summary>
        public static void Tint(GameObject go, Color color)
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var rend in go.GetComponentsInChildren<Renderer>())
            {
                rend.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, color);
                rend.SetPropertyBlock(mpb);
            }
        }
    }
}
