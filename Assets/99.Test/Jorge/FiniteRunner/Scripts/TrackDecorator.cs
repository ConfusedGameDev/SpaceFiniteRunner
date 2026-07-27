using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Stamps road-kit meshes along the track spline: road surface pieces,
    /// side barriers on both edges and a goal gantry at the end. Works both
    /// in the editor (authored tracks) and at runtime (random tracks — the
    /// TrackGenerator calls Decorate after building a new spline).
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

        [Header("Goal")]
        [SerializeField] GameObject goalSignPrefab;
        [SerializeField] Vector3 goalSignScale = new(20f, 30f, 60f);

        public void Decorate()
        {
            if (track == null || decorParent == null) return;
            Clear();

            float length = track.Length;

            for (float d = roadSpacing * 0.5f; d < length; d += roadSpacing)
            {
                track.GetPoseAtDistance(d, 0f, out Vector3 pos, out Quaternion rot);

                if (roadPrefab != null)
                {
                    var piece = Stamp(roadPrefab, pos + rot * new Vector3(0f, roadYOffset, 0f),
                                      rot * Quaternion.Euler(0f, roadYaw, 0f), roadScale);
                    if (roadMaterialOverride != null) OverrideMaterials(piece, roadMaterialOverride);
                }

                if (barrierPrefab != null)
                {
                    if (Mathf.Abs(barrierLateral) < 0.01f)
                    {
                        // Full-width piece (e.g. road-straight-barrier): one centered stamp.
                        var b = Stamp(barrierPrefab, pos + rot * new Vector3(0f, roadYOffset, 0f),
                                      rot * Quaternion.Euler(0f, roadYaw, 0f), barrierScale);
                        if (roadMaterialOverride != null) OverrideMaterials(b, roadMaterialOverride);
                    }
                    else
                    {
                        track.GetPoseAtDistance(d, -barrierLateral, out Vector3 lp, out Quaternion lr);
                        var bl = Stamp(barrierPrefab, lp + lr * new Vector3(0f, roadYOffset, 0f),
                                       lr * Quaternion.Euler(0f, roadYaw, 0f), barrierScale);
                        if (roadMaterialOverride != null) OverrideMaterials(bl, roadMaterialOverride);

                        track.GetPoseAtDistance(d, barrierLateral, out Vector3 rp, out Quaternion rr);
                        var br = Stamp(barrierPrefab, rp + rr * new Vector3(0f, roadYOffset, 0f),
                                       rr * Quaternion.Euler(0f, roadYaw + 180f, 0f), barrierScale);
                        if (roadMaterialOverride != null) OverrideMaterials(br, roadMaterialOverride);
                    }
                }
            }

            if (goalSignPrefab != null)
            {
                track.GetPoseAtDistance(length - 10f, 0f, out Vector3 gp, out Quaternion gr);
                // Sign length runs along its local Z (pivot centered) — turn it to span the road.
                Stamp(goalSignPrefab, gp + gr * new Vector3(0f, roadYOffset, 0f),
                      gr * Quaternion.Euler(0f, 90f, 0f), goalSignScale);
            }
        }

        GameObject Stamp(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var piece = Instantiate(prefab, position, rotation, decorParent);
            piece.transform.localScale = scale;
            return piece;
        }

        public void Clear()
        {
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
