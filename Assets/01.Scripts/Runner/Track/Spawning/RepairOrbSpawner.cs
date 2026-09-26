using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Repair orbs on the flight line: flown through, they give back
    /// GameSettings.repairOrbHealFraction of the hull (taken even at full hull,
    /// healing nothing past the max; the patrol never takes one — see <see cref="RepairOrb"/>). Off claimed
    /// ground and off any other pickup; off entirely whenever the hull is off.
    /// Put it after the speed orbs in the set, so it keeps off where they landed.
    /// </summary>
    [CreateAssetMenu(fileName = "Spawner_RepairOrbs", menuName = "FiniteRunner/Spawners/Repair Orbs")]
    public class RepairOrbSpawner : TrackSpawner
    {
        [Tooltip("The repair orb's look, carrying a RepairOrb, modelled at 1 m across. Empty = a code-built translucent sphere (shellColor) round a glowing cross (crossColor).")]
        [SerializeField] GameObject prefab;

        [Tooltip("Diameter of the repair orb as a share of the pad width (a boost orb's is its definition's size multiplier).")]
        [Sirenix.OdinInspector.PropertyRange(0.1f, 2f)]
        [SerializeField] float size = 1f;

        [Tooltip("Gap between the visible road and the orb's LOWEST point (the bottom of its bob), along the track's up. The centre is placed from this and the orb's radius, so at any size the orb floats clear of the road — and, sitting just above it, is always in the ship's path.")]
        [Sirenix.OdinInspector.PropertyRange(0f, 10f), Sirenix.OdinInspector.SuffixLabel("m", true)]
        [UnityEngine.Serialization.FormerlySerializedAs("height")]
        [SerializeField] float roadClearance = 0.5f;

        [Tooltip("Colour of the code-built orb's translucent shell (alpha = how see-through it is, so the cross shows inside).")]
        [SerializeField] Color shellColor = new(1f, 0.1f, 0.1f, 0.45f);

        [Tooltip("Colour of the code-built orb's glowing cross.")]
        [SerializeField] Color crossColor = new(0.1f, 1f, 0.25f, 1f);

        [System.NonSerialized] Material shellMaterial, crossMaterial; // the code-built orb's, play mode only

        public override bool IsActive(TrackSpawnContext ctx) =>
            base.IsActive(ctx) && (ctx.GameManager == null || ctx.GameManager.HullEnabled);

        protected override float Step(TrackSpawnContext ctx, float distance, float limit)
        {
            float claimEnd = ctx.ClaimEnd(distance);
            if (claimEnd >= 0f) return claimEnd;
            if (ctx.NearPickup(distance)) return -1f;

            float diameter = ctx.PadSize.x * size;
            float lateral = ctx.RandomLateral(ref Rng, distance, diameter * 0.5f + 2f);
            ctx.Track.GetPoseAtDistance(distance, lateral, out Vector3 pos, out Quaternion rot);

            GameObject orb;
            if (prefab != null)
            {
                orb = Instantiate(prefab, pos, rot, ctx.Parent);
                orb.transform.localScale *= diameter;
                if (!TrackSpawnContext.ForceTriggers(orb))
                {
                    var sphere = orb.AddComponent<SphereCollider>();
                    sphere.isTrigger = true;
                    sphere.radius = 0.5f;
                }
            }
            else orb = BuildPrimitive(ctx, pos, rot, diameter);

            if (orb.GetComponent<RepairOrb>() == null) orb.AddComponent<RepairOrb>();
            OrbHover hover = orb.GetComponent<OrbHover>();
            if (Application.isPlaying && hover == null) hover = orb.AddComponent<OrbHover>();

            // Lifted along the track's up (so it holds on a bank or a tube) until
            // its lowest point — radius and bob below the centre — clears the road.
            float bob = hover != null ? hover.BobAmplitude : 0f;
            float lift = ctx.RoadSurfaceOffset + roadClearance + bob + diameter * 0.5f;
            orb.transform.position = pos + rot * (Vector3.up * lift);
            orb.name = $"RepairOrb_{distance:00000}";
            ctx.Register(distance, orb);
            ctx.RecordPickup(distance); // coins keep off it too
            return -1f;
        }

        // No prefab: a unit sphere (the red shell, the boost material tinted)
        // round a green 3D cross of three boxes, scaled to the orb.
        GameObject BuildPrimitive(TrackSpawnContext ctx, Vector3 pos, Quaternion rot, float diameter)
        {
            var orb = new GameObject();
            orb.transform.SetParent(ctx.Parent, false);
            orb.transform.SetPositionAndRotation(pos, rot);
            orb.transform.localScale = Vector3.one * diameter;
            var trigger = orb.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.5f;

            if (Application.isPlaying && ctx.BoostMaterial != null)
            {
                if (shellMaterial == null) shellMaterial = Transparent(TrackSpawnContext.TintedCopy(ctx.BoostMaterial, shellColor, 0.6f));
                if (crossMaterial == null) crossMaterial = TrackSpawnContext.TintedCopy(ctx.BoostMaterial, crossColor, 2f);
            }
            Material shell = Application.isPlaying ? shellMaterial : null;
            Material cross = Application.isPlaying ? crossMaterial : null;
            AddPart(orb.transform, PrimitiveType.Sphere, Vector3.one, shell);
            AddPart(orb.transform, PrimitiveType.Cube, new Vector3(0.2f, 0.6f, 0.2f), cross);
            AddPart(orb.transform, PrimitiveType.Cube, new Vector3(0.6f, 0.2f, 0.2f), cross);
            AddPart(orb.transform, PrimitiveType.Cube, new Vector3(0.2f, 0.2f, 0.6f), cross); // a third arm: the orb spins, so the cross reads from any side
            return orb;

            static void AddPart(Transform parent, PrimitiveType type, Vector3 scale, Material mat)
            {
                var part = GameObject.CreatePrimitive(type);
                DestroyImmediate(part.GetComponent<Collider>()); // now, not end of frame: the root's trigger is the pickup, and a solid child would be hit by the ship's casts
                part.transform.SetParent(parent, false);
                part.transform.localScale = scale;
                if (mat != null) part.GetComponent<Renderer>().sharedMaterial = mat;
            }

            // URP Lit/Unlit switched to an alpha-blended surface — what the
            // Surface Type dropdown sets — so the cross shows through the shell.
            static Material Transparent(Material mat)
            {
                if (!mat.HasProperty("_Surface")) return mat;
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                return mat;
            }
        }

        public override void Cleanup()
        {
            DestroyRuntime(shellMaterial);
            DestroyRuntime(crossMaterial);
            shellMaterial = crossMaterial = null;
        }
    }
}
