using System.Collections.Generic;
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
    /// </summary>
    public class TrackDecorator : MonoBehaviour
    {
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

        [Header("Side barriers")]
        [SerializeField] GameObject barrierPrefab;
        [SerializeField] Vector3 barrierScale = new(4f, 25f, 20f);
        [Tooltip("Lateral distance of the barrier strip from the track center.")]
        [SerializeField] float barrierLateral = 30.5f;

        [Header("Open edges (flat sweeps)")]
        [Tooltip("Full-width barrier piece with a wall on its LEFT side only, used where the right edge is open (and turned round for an open left edge). Empty = a code-built placeholder wall on the closed side.")]
        [SerializeField] GameObject oneSidedBarrierPrefab;
        [Tooltip("Placeholder wall: thickness and height, metres.")]
        [SerializeField] Vector2 placeholderWallSize = new(2f, 12f);
        [Tooltip("Marker strip along an open edge: width and height, metres. 0 width = none.")]
        [SerializeField] Vector2 openEdgeMarkerSize = new(1.5f, 0.6f);
        [Tooltip("Material of the open-edge marker (a hot emissive reads best). Empty = the road material.")]
        [SerializeField] Material openEdgeMaterial;

        [Header("Tubes")]
        [Tooltip("Target width of one road strip round a tube section, metres of arc. The band is cut into equal strips no wider than this, each an ordinary road piece scaled to the strip.")]
        [SerializeField, Min(2f)] float tubeStripWidth = 25f;

        // The road/barrier scales and the barrier lateral above are authored
        // for this track width; SetTrackWidth stretches them proportionally.
        const float ReferenceTrackWidth = 60f;

        // Streaming state: distance of the next stamp, and every live piece
        // tagged with the distance it was stamped at (for culling).
        float stampCursor;
        float widthScale = 1f;
        readonly List<(float distance, GameObject go)> stamped = new();

        /// <summary>
        /// Adapts the authored piece scales to the given full track width
        /// (Core Settings on the TrackGenerator). Only affects pieces stamped
        /// afterwards — the generator regenerates, so everything restamps.
        /// </summary>
        public void SetTrackWidth(float width) => widthScale = Mathf.Max(0.05f, width / ReferenceTrackWidth);

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

            float limit = Mathf.Min(distance, track.Length);
            while (stampCursor < limit)
            {
                StampAt(stampCursor);
                stampCursor += roadSpacing;
            }
        }

        /// <summary>Destroys every stamped piece before <paramref name="distance"/>.</summary>
        public void CullBefore(float distance)
        {
            for (int i = stamped.Count - 1; i >= 0; i--)
            {
                if (stamped[i].distance >= distance) continue;
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
                var piece = Stamp(d, roadPrefab, pos + rot * new Vector3(0f, roadYOffset, 0f),
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
                        var b = Stamp(d, oneSidedBarrierPrefab, pos + rot * new Vector3(0f, roadYOffset, 0f),
                                      rot * Quaternion.Euler(0f, roadYaw + (openSide < 0 ? 180f : 0f), 0f), ScaleAcrossWidth(barrierScale));
                        if (roadMaterialOverride != null) OverrideMaterials(b, roadMaterialOverride);
                    }
                    else StampPlaceholderWall(d, -openSide);
                }
                else if (Mathf.Abs(barrierLateral) < 0.01f)
                {
                    // Full-width piece (e.g. road-straight-barrier): one centered stamp.
                    var b = Stamp(d, barrierPrefab, pos + rot * new Vector3(0f, roadYOffset, 0f),
                                  rot * Quaternion.Euler(0f, roadYaw, 0f), ScaleAcrossWidth(barrierScale));
                    if (roadMaterialOverride != null) OverrideMaterials(b, roadMaterialOverride);
                }
                else
                {
                    float lateral = barrierLateral * widthScale;
                    if (!openLeft)
                    {
                        track.GetPoseAtDistance(d, -lateral, out Vector3 lp, out Quaternion lr);
                        var bl = Stamp(d, barrierPrefab, lp + lr * new Vector3(0f, roadYOffset, 0f),
                                       lr * Quaternion.Euler(0f, roadYaw, 0f), barrierScale);
                        if (roadMaterialOverride != null) OverrideMaterials(bl, roadMaterialOverride);
                    }

                    if (!openRight)
                    {
                        track.GetPoseAtDistance(d, lateral, out Vector3 rp, out Quaternion rr);
                        var br = Stamp(d, barrierPrefab, rp + rr * new Vector3(0f, roadYOffset, 0f),
                                       rr * Quaternion.Euler(0f, roadYaw + 180f, 0f), barrierScale);
                        if (roadMaterialOverride != null) OverrideMaterials(br, roadMaterialOverride);
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
            float lateral = side * (track.HalfWidth + thickness * 0.5f);
            track.GetPoseAtDistance(d, lateral, out Vector3 pos, out Quaternion rot);
            StampBox(d, pos + rot * new Vector3(0f, roadYOffset + height * 0.5f, 0f), rot,
                     new Vector3(thickness, height, roadSpacing), roadMaterialOverride);
        }

        // A low strip along the open edge, so the missing wall reads as a
        // marked drop and not as a hole in the streaming.
        void StampOpenEdgeMarker(float d, int side)
        {
            if (openEdgeMarkerSize.x <= 0f) return;
            float lateral = side * (track.HalfWidth - openEdgeMarkerSize.x * 0.5f);
            track.GetPoseAtDistance(d, lateral, out Vector3 pos, out Quaternion rot);
            StampBox(d, pos + rot * new Vector3(0f, openEdgeMarkerSize.y * 0.5f, 0f), rot,
                     new Vector3(openEdgeMarkerSize.x, Mathf.Max(0.05f, openEdgeMarkerSize.y), roadSpacing),
                     openEdgeMaterial != null ? openEdgeMaterial : roadMaterialOverride);
        }

        // A code-built box: a picture only, so its collider goes (nothing on
        // the track may trip the ship's trigger volume).
        void StampBox(float distance, Vector3 position, Quaternion rotation, Vector3 size, Material material)
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

            if (roadPrefab != null)
            {
                Vector3 scale = ScaleAcrossWidth(roadScale, stripWidth / ReferenceTrackWidth);
                for (int i = 0; i < strips; i++)
                {
                    float lateral = min + (i + 0.5f) * stripWidth;
                    track.GetPoseAtDistance(d, lateral, out Vector3 pos, out Quaternion rot);
                    var piece = Stamp(d, roadPrefab, pos + rot * new Vector3(0f, roadYOffset, 0f),
                                      rot * Quaternion.Euler(0f, roadYaw, 0f), scale);
                    if (roadMaterialOverride != null) OverrideMaterials(piece, roadMaterialOverride);
                }
            }

        }

        GameObject Stamp(float distance, GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var piece = Instantiate(prefab, position, rotation, decorParent);
            piece.transform.localScale = scale;
            stamped.Add((distance, piece));
            return piece;
        }

        public void Clear()
        {
            stamped.Clear();
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
