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

            if (barrierPrefab != null)
            {
                if (Mathf.Abs(barrierLateral) < 0.01f)
                {
                    // Full-width piece (e.g. road-straight-barrier): one centered stamp.
                    var b = Stamp(d, barrierPrefab, pos + rot * new Vector3(0f, roadYOffset, 0f),
                                  rot * Quaternion.Euler(0f, roadYaw, 0f), ScaleAcrossWidth(barrierScale));
                    if (roadMaterialOverride != null) OverrideMaterials(b, roadMaterialOverride);
                }
                else
                {
                    float lateral = barrierLateral * widthScale;
                    track.GetPoseAtDistance(d, -lateral, out Vector3 lp, out Quaternion lr);
                    var bl = Stamp(d, barrierPrefab, lp + lr * new Vector3(0f, roadYOffset, 0f),
                                   lr * Quaternion.Euler(0f, roadYaw, 0f), barrierScale);
                    if (roadMaterialOverride != null) OverrideMaterials(bl, roadMaterialOverride);

                    track.GetPoseAtDistance(d, lateral, out Vector3 rp, out Quaternion rr);
                    var br = Stamp(d, barrierPrefab, rp + rr * new Vector3(0f, roadYOffset, 0f),
                                   rr * Quaternion.Euler(0f, roadYaw + 180f, 0f), barrierScale);
                    if (roadMaterialOverride != null) OverrideMaterials(br, roadMaterialOverride);
                }
            }
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
