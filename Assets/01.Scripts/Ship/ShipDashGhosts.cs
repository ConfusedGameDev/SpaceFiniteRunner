using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The dash's onion-skin trail for a ship with no track: while the ship
    /// reports IsDashing, translucent copies of its meshes are dropped and
    /// fade, and the sideways gap the real ship opens against them sells the
    /// burst. The ghosts RIDE WITH THE SHIP (a world-space ghost is behind
    /// the chase camera within a frame at speed): every snapshot is parented
    /// to a frame that follows the ship's own pose, and each ghost holds the
    /// ship's sideways travel at the moment it was taken — the integral of
    /// the body's lateral velocity, the dash's shove included — so as the
    /// ship keeps moving across, the ghost stays put across. The runner's
    /// <c>DashGhostTrail</c> seats its frame on the track line at lateral 0
    /// for the same effect; with no line, the ship's own frame and its own
    /// lateral do the job. Ground ghosts are spaced by METRES of sideways
    /// travel (dashDistance / dashGhostCount), so a hitch never drops one; the
    /// airborne barrel roll keeps a time spread, those ghosts being there to
    /// show the spin. Pooled, one material instance per ghost (never a
    /// MaterialPropertyBlock — unreliable under the SRP Batcher), cleared on
    /// every teleport and while the ship is out of play. On the prefab it
    /// wires itself to the <see cref="HoverShip"/> beside it in Start.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public class ShipDashGhosts : MonoBehaviour
    {
        HoverShip ship;
        MeshFilter[] sourceMeshes = new MeshFilter[0];
        Material ghostMaterial;
        bool ownsGhostMaterial;
        Transform frame;
        readonly List<Ghost> pool = new();
        float lateralNow, lateralAtLastGhost;
        bool wasDashing;
        float rollTimer;

        void Start()
        {
            ship = GetComponent<HoverShip>();
            if (ship == null) { enabled = false; return; }
            sourceMeshes = ship.Visual != null ? ship.Visual.GetComponentsInChildren<MeshFilter>() : new MeshFilter[0];
            ownsGhostMaterial = ship.Settings == null || ship.Settings.ghostMaterial == null;
            ghostMaterial = ownsGhostMaterial ? ShipGhostMaterial.BuildFallback() : ship.Settings.ghostMaterial;
            frame = new GameObject("ShipDashGhostFrame").transform;
            ship.Launched += ClearGhosts;
            ship.RespawnStarted += OnTeleport;
        }

        void OnDestroy()
        {
            if (ship != null)
            {
                ship.Launched -= ClearGhosts;
                ship.RespawnStarted -= OnTeleport;
            }
            if (frame != null) Destroy(frame.gameObject); // takes the pooled ghosts and their materials with it
            if (ownsGhostMaterial && ghostMaterial != null) Destroy(ghostMaterial);
        }

        void OnTeleport(Vector3 teleport) => ClearGhosts();

        // LateUpdate: the ship has posed its transform for this frame, so a snapshot is the ship as drawn.
        void LateUpdate()
        {
            if (ship == null || frame == null || ship.Settings == null || ship.Definition == null) return;
            frame.SetPositionAndRotation(transform.position, transform.rotation);

            HoverBody body = ship.Body;
            ShipState state = body.State;
            if (state == ShipState.OffTrack || state == ShipState.Respawning || state == ShipState.Falling) { ClearGhosts(); return; }
            if (!ship.Paused) lateralNow += body.TotalLateralVelocity * Time.deltaTime;
            foreach (Ghost ghost in pool)
                if (ghost != null && ghost.gameObject.activeSelf) ghost.Across = lateralNow;

            if (!ship.IsDashing) { wasDashing = false; return; }

            int count = Mathf.Max(ship.Definition.dashGhostCount, 1);
            float burst = Mathf.Max(ship.DashBurstDuration, 0.01f);

            if (!wasDashing)
            {
                wasDashing = true; // a dash starts with an immediate ghost
                SpawnGhost();
                lateralAtLastGhost = lateralNow;
                rollTimer = burst / count;
                return;
            }

            if (state == ShipState.Airborne)
            {
                rollTimer -= Time.deltaTime;
                if (rollTimer > 0f) return;
                rollTimer = burst / count;
                SpawnGhost();
            }
            else
            {
                float spacing = Mathf.Max(ship.Definition.dashDistance / count, 0.01f);
                if (Mathf.Abs(lateralNow - lateralAtLastGhost) < spacing) return;
                SpawnGhost();
                lateralAtLastGhost = lateralNow;
            }
        }

        void SpawnGhost()
        {
            if (sourceMeshes.Length == 0 || ghostMaterial == null) return;
            Ghost ghost = null;
            foreach (Ghost pooled in pool)
                if (pooled != null && !pooled.gameObject.activeSelf) { ghost = pooled; break; }
            if (ghost == null)
            {
                ghost = BuildGhost();
                if (ghost == null) return;
                pool.Add(ghost);
            }
            ShipSettings settings = ship.Settings;
            ghost.Snapshot(sourceMeshes, lateralNow);
            ghost.Begin(settings.dashGhostStartAlpha, settings.dashGhostLifetime, settings.dashGhostDriftMeters);
        }

        // One pooled snapshot: a root under the frame with a piece per source mesh, one material instance for the lot.
        Ghost BuildGhost()
        {
            var root = new GameObject("ShipDashGhost");
            root.transform.SetParent(frame, false);
            var material = new Material(ghostMaterial);
            var pieces = new Transform[sourceMeshes.Length];
            for (int i = 0; i < sourceMeshes.Length; i++)
            {
                MeshFilter source = sourceMeshes[i];
                if (source == null || source.sharedMesh == null) continue;
                var piece = new GameObject(source.name);
                piece.transform.SetParent(root.transform, false);
                piece.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var renderer = piece.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var materials = new Material[source.sharedMesh.subMeshCount];
                for (int m = 0; m < materials.Length; m++) materials[m] = material;
                renderer.sharedMaterials = materials;
                pieces[i] = piece.transform;
            }
            var ghost = root.AddComponent<Ghost>();
            ghost.Init(material, pieces);
            root.SetActive(false);
            return ghost;
        }

        void ClearGhosts()
        {
            wasDashing = false;
            foreach (Ghost ghost in pool)
                if (ghost != null) ghost.gameObject.SetActive(false);
        }

        /// <summary>
        /// One pooled snapshot under the frame: re-posed from the ship's meshes on every use, held at the sideways
        /// travel it was taken at (so the ship moves away from it across), fading its material's alpha to zero over
        /// its lifetime and sliding back by the drift knob, then back to the pool.
        /// </summary>
        public class Ghost : MonoBehaviour
        {
            static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

            Material material;
            Transform[] pieces = new Transform[0];
            Color baseColor;
            float startAlpha, lifetime = 0.01f, driftMeters, age, takenAt;

            /// <summary>The ship's sideways travel now — the ghost sits at (its own − this) across the frame.</summary>
            public float Across { get; set; }

            public void Init(Material material, Transform[] pieces)
            {
                this.material = material;
                this.pieces = pieces ?? new Transform[0];
                baseColor = material.GetColor(BaseColor);
            }

            /// <summary>Copies each source mesh's world pose onto its piece — with the frame already on the ship's pose.</summary>
            public void Snapshot(MeshFilter[] sources, float lateralNow)
            {
                takenAt = lateralNow;
                Across = lateralNow;
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                transform.localScale = Vector3.one;
                int n = Mathf.Min(pieces.Length, sources.Length);
                for (int i = 0; i < n; i++)
                {
                    if (pieces[i] == null || sources[i] == null) continue;
                    pieces[i].SetPositionAndRotation(sources[i].transform.position, sources[i].transform.rotation);
                    pieces[i].localScale = sources[i].transform.lossyScale;
                }
            }

            public void Begin(float startAlpha, float lifetime, float driftMeters)
            {
                this.startAlpha = startAlpha;
                this.lifetime = Mathf.Max(lifetime, 0.01f);
                this.driftMeters = driftMeters;
                age = 0f;
                Apply();
                gameObject.SetActive(true);
            }

            void LateUpdate()
            {
                age += Time.deltaTime;
                if (age >= lifetime) { gameObject.SetActive(false); return; }
                Apply();
            }

            void Apply()
            {
                float t = age / lifetime;
                Color color = baseColor;
                color.a = startAlpha * (1f - t);
                material.SetColor(BaseColor, color);
                // The frame is the ship's: right is the ship's right, back is behind it.
                transform.localPosition = Vector3.right * (takenAt - Across) + Vector3.back * (driftMeters * t);
            }

            void OnDestroy()
            {
                if (material != null) Destroy(material);
            }
        }
    }
}
