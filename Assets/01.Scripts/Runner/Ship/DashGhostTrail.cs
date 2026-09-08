using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Onion-skin trail for the lateral dash: while the motor reports
    /// IsDashing, semi-transparent copies of the ship's meshes are dropped
    /// and fade out, and the sideways gap the real ship opens against them
    /// is what sells the burst. The ghosts RIDE WITH THE SHIP: every
    /// snapshot is parented to a frame that sits on the flight line at the
    /// ship's own distance (lateral 0, the track pose — so it turns through
    /// loops and tubes), and only the ship's sideways offset, hover and bank
    /// at the moment of the snapshot are frozen. A world-space ghost lasted
    /// one frame at Light Speed — the chase camera is bolted 33 m behind the
    /// ship, so a ghost left behind is behind the camera before it can be
    /// seen — while a track-frame ghost is on screen for its whole life at
    /// any speed. Snapshots are spaced by METRES of lateral travel on the
    /// ground (dashDistance / dashGhostCount), so a hitch never drops one;
    /// the airborne barrel roll keeps its time spread, because those ghosts
    /// are there to show the spin. Ghosts are pooled (one material instance
    /// per pooled ghost, never a MaterialPropertyBlock — MPB tints are
    /// unreliable with the SRP Batcher, see TrackDecorator), cleared on the
    /// launch teleport and while the ship is falling off a loop.
    /// </summary>
    public class DashGhostTrail : MonoBehaviour
    {
        ShipMotor motor;
        GameSettings settings;
        TrackManager track;
        MeshFilter[] sourceMeshes = new MeshFilter[0];
        Material ghostMaterial;
        Transform frame;
        readonly List<DashGhost> pool = new();
        float lateralAtLastGhost;
        bool wasDashing;
        float rollTimer;

        public void Init(ShipMotor motor, GameSettings settings)
        {
            if (this.motor != null) this.motor.Launched -= ClearGhosts;
            this.motor = motor;
            this.settings = settings;
            track = motor.Track != null ? motor.Track : FindFirstObjectByType<TrackManager>();
            sourceMeshes = motor.Visual != null
                ? motor.Visual.GetComponentsInChildren<MeshFilter>()
                : new MeshFilter[0];
            ghostMaterial = settings.dashGhostMaterial != null
                ? settings.dashGhostMaterial
                : BuildFallbackMaterial();

            if (frame == null) frame = new GameObject("DashGhostFrame").transform;
            ClearGhosts();
            motor.Launched += ClearGhosts;
        }

        void OnDestroy()
        {
            if (motor != null) motor.Launched -= ClearGhosts;
            if (frame != null) Destroy(frame.gameObject); // takes the pooled ghosts (and their materials) with it
        }

        // Safety net when no material asset is assigned: a runtime URP Unlit
        // set up for alpha blending. The asset is authoritative — runtime
        // surface-type switching in URP is fragile, so keep the .mat assigned.
        static Material BuildFallbackMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader);
            material.SetFloat("_Surface", 1f); // transparent
            material.SetFloat("_Blend", 0f);   // alpha
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(0.6f, 0.95f, 1f, 0.45f));
            return material;
        }

        // LateUpdate: the motor has applied this frame's pose, so a snapshot
        // taken here is the ship as drawn, and the frame is already at the
        // ship's new distance when the ghosts under it are rendered.
        void LateUpdate()
        {
            if (motor == null || track == null || frame == null) return;

            // The frame: the flight line at the ship's distance, lateral 0.
            track.GetPoseAtDistance(motor.DistanceTravelled, 0f, out Vector3 position, out Quaternion rotation);
            frame.SetPositionAndRotation(position, rotation);

            if (motor.State == ShipState.Falling)
            {
                // Distance is parked at the loop's exit during the fall; ghosts
                // hanging there while the ship drops would read as a bug.
                ClearGhosts();
                return;
            }
            if (!motor.IsDashing)
            {
                wasDashing = false;
                return;
            }

            // Ghost amount is a ship stat. Read live off the definition, which
            // may be the tuning clone.
            var definition = motor.Definition;
            int count = Mathf.Max(definition.dashGhostCount, 1);
            float burst = Mathf.Max(motor.DashBurstDuration, 0.01f);

            if (!wasDashing)
            {
                // A dash starts with an immediate ghost.
                wasDashing = true;
                SpawnGhost();
                lateralAtLastGhost = motor.LateralOffset;
                rollTimer = burst / count;
                return;
            }

            if (motor.State == ShipState.Airborne)
            {
                // The barrel roll: spread over the roll's length so the ghosts
                // show the spin (lateral travel is small at air authority).
                rollTimer -= Time.deltaTime;
                if (rollTimer > 0f) return;
                rollTimer = burst / count;
                SpawnGhost();
            }
            else
            {
                // The ground dash: one ghost per slice of the dash's sideways
                // travel, whatever the frame rate.
                float spacing = Mathf.Max(definition.dashDistance / count, 0.01f);
                if (Mathf.Abs(motor.LateralOffset - lateralAtLastGhost) < spacing) return;
                SpawnGhost();
                lateralAtLastGhost = motor.LateralOffset;
            }
        }

        void SpawnGhost()
        {
            if (sourceMeshes.Length == 0 || ghostMaterial == null) return;

            DashGhost ghost = null;
            foreach (var pooled in pool)
                if (pooled != null && !pooled.gameObject.activeSelf) { ghost = pooled; break; }
            if (ghost == null)
            {
                ghost = BuildGhost();
                if (ghost == null) return;
                pool.Add(ghost);
            }

            // The frame is already at this frame's pose, so world → local of
            // each piece is exactly the ship's sideways offset, hover and bank.
            ghost.Snapshot(sourceMeshes);
            ghost.Begin(settings.dashGhostStartAlpha, settings.dashGhostLifetime, settings.dashGhostDriftMeters);
        }

        // One pooled snapshot: a root under the frame with a piece per source
        // mesh, all drawing with one material instance so the whole ghost
        // fades as one. Built once, re-posed on every use.
        DashGhost BuildGhost()
        {
            var root = new GameObject("DashGhost");
            root.transform.SetParent(frame, false);
            var material = new Material(ghostMaterial);
            var pieces = new Transform[sourceMeshes.Length];

            for (int i = 0; i < sourceMeshes.Length; i++)
            {
                var source = sourceMeshes[i];
                if (source == null || source.sharedMesh == null) continue;

                var piece = new GameObject(source.name);
                piece.transform.SetParent(root.transform, false);
                piece.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;

                var renderer = piece.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                // Every submesh draws with the ghost material.
                var materials = new Material[source.sharedMesh.subMeshCount];
                for (int m = 0; m < materials.Length; m++) materials[m] = material;
                renderer.sharedMaterials = materials;

                pieces[i] = piece.transform;
            }

            var ghost = root.AddComponent<DashGhost>();
            ghost.Init(material, pieces);
            root.SetActive(false);
            return ghost;
        }

        /// <summary>Hides every ghost — the launch teleport and a loop fall must never leave one hanging in the world.</summary>
        void ClearGhosts()
        {
            wasDashing = false;
            foreach (var ghost in pool)
                if (ghost != null) ghost.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// One pooled onion-skin snapshot under the trail's frame: re-posed from
    /// the ship's meshes on every use, it eases its shared material's alpha
    /// to zero over its lifetime (sliding back by the drift knob meanwhile)
    /// and then deactivates itself, ready for the next dash. The material
    /// instance lives as long as the object and is destroyed with it.
    /// </summary>
    public class DashGhost : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        Material material;
        Transform[] pieces = new Transform[0];
        Color baseColor;
        float startAlpha;
        float lifetime = 0.01f;
        float driftMeters;
        float age;

        public void Init(Material material, Transform[] pieces)
        {
            this.material = material;
            this.pieces = pieces ?? new Transform[0];
            baseColor = material.GetColor(BaseColor);
        }

        /// <summary>Copies each source mesh's world pose onto its piece — call with the frame already at this frame's pose.</summary>
        public void Snapshot(MeshFilter[] sources)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            int n = Mathf.Min(pieces.Length, sources.Length);
            for (int i = 0; i < n; i++)
            {
                if (pieces[i] == null || sources[i] == null) continue;
                pieces[i].SetPositionAndRotation(sources[i].transform.position, sources[i].transform.rotation);
                pieces[i].localScale = sources[i].transform.lossyScale; // frame and root are unit-scaled
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

        void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime)
            {
                gameObject.SetActive(false); // back to the pool
                return;
            }
            Apply();
        }

        void Apply()
        {
            float t = age / lifetime;
            var color = baseColor;
            color.a = startAlpha * (1f - t);
            material.SetColor(BaseColor, color);
            // The frame's forward is the track's, so back is behind the ship.
            transform.localPosition = Vector3.back * (driftMeters * t);
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
