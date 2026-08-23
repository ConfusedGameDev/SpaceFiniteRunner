using UnityEngine;
using UnityEngine.Rendering;

using ConfusedGameDev.FiniteRunner.GameFlow;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Onion-skin trail for the lateral dash: while the motor reports
    /// IsDashing, frozen semi-transparent copies of the ship's meshes are
    /// dropped at its world pose every few hundredths of a second and fade
    /// out where they were left — the gap the real ship opens up against
    /// them is what sells the burst of speed. Ghost fading uses a material
    /// instance per snapshot, never a MaterialPropertyBlock (MPB tints are
    /// unreliable with the SRP Batcher — see TrackDecorator).
    /// </summary>
    public class DashGhostTrail : MonoBehaviour
    {
        ShipMotor motor;
        GameSettings settings;
        MeshFilter[] sourceMeshes;
        Material ghostMaterial;
        float snapshotTimer;

        public void Init(ShipMotor motor, GameSettings settings)
        {
            this.motor = motor;
            this.settings = settings;
            sourceMeshes = motor.Visual != null
                ? motor.Visual.GetComponentsInChildren<MeshFilter>()
                : new MeshFilter[0];
            ghostMaterial = settings.dashGhostMaterial != null
                ? settings.dashGhostMaterial
                : BuildFallbackMaterial();
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

        void Update()
        {
            if (motor == null || !motor.IsDashing)
            {
                snapshotTimer = 0f; // next dash starts with an immediate ghost
                return;
            }

            snapshotTimer -= Time.deltaTime;
            if (snapshotTimer > 0f) return;

            // Ghost amount is a ship stat: spread dashGhostCount snapshots
            // evenly across the dash's duration (read live off the definition,
            // which may be the tuning screen's runtime clone).
            var definition = motor.Definition;
            snapshotTimer = Mathf.Max(definition.dashDuration, 0.01f) /
                            Mathf.Max(definition.dashGhostCount, 1);
            SpawnGhost();
        }

        void SpawnGhost()
        {
            if (sourceMeshes.Length == 0 || ghostMaterial == null) return;

            var root = new GameObject("DashGhost");

            // One material instance shared by every piece of this snapshot,
            // so the whole ghost fades as one and cleanup is a single Destroy.
            var material = new Material(ghostMaterial);

            foreach (var source in sourceMeshes)
            {
                if (source.sharedMesh == null) continue;

                var piece = new GameObject(source.name);
                piece.transform.SetParent(root.transform, false);
                piece.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                piece.transform.localScale = source.transform.lossyScale;

                piece.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;

                var renderer = piece.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                // Every submesh draws with the ghost material.
                var materials = new Material[source.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }

            var ghost = root.AddComponent<DashGhost>();
            ghost.Init(material, settings.dashGhostStartAlpha, settings.dashGhostLifetime);
        }
    }

    /// <summary>
    /// Fade-and-die behaviour of a single onion-skin snapshot: eases the
    /// shared material's alpha to zero over its lifetime, then destroys both
    /// the object and the material instance so nothing leaks.
    /// </summary>
    public class DashGhost : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        Material material;
        Color baseColor;
        float startAlpha;
        float lifetime;
        float age;

        public void Init(Material material, float startAlpha, float lifetime)
        {
            this.material = material;
            this.startAlpha = startAlpha;
            this.lifetime = Mathf.Max(lifetime, 0.01f);
            baseColor = material.GetColor(BaseColor);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }

            var color = baseColor;
            color.a = startAlpha * (1f - age / lifetime);
            material.SetColor(BaseColor, color);
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
