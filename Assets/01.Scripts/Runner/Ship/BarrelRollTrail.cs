using UnityEngine;
using UnityEngine.Rendering;

using ConfusedGameDev.FiniteRunner.GameFlow;
using System.Collections.Generic;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Wingtip ribbons for the airborne barrel roll: one TrailRenderer under
    /// each side of the ship's model, emitting only while the motor reports
    /// IsBarrelRolling. They are parented to the banking/rolling visual on
    /// purpose — the emitters orbit the roll axis while the ship flies on,
    /// so the ribbons come out as two short helices, which is the whole
    /// reason the roll reads as movement rather than a spin in place. The
    /// emitters sit at the model's measured half-width (in the visual's
    /// space, so any world orientation at init is fine), the trail runs on
    /// the game clock like the roll, and the launch teleport clears both
    /// ribbons so a restart never draws a line across the world.
    /// </summary>
    public class BarrelRollTrail : MonoBehaviour
    {
        ShipMotor motor;
        GameSettings settings;
        Material trailMaterial;
        bool ownsMaterial;
        TrailRenderer[] trails = new TrailRenderer[0];
        bool wasRolling;

        public void Init(ShipMotor motor, GameSettings settings)
        {
           
            this.motor = motor;
            this.settings = settings;

            if (settings.barrelRollTrailMaterial != null)
                trailMaterial = settings.barrelRollTrailMaterial;
            else
            {
                trailMaterial = BuildFallbackMaterial();
                ownsMaterial = true;
            }

            Transform visual = motor.Visual != null ? motor.Visual : motor.transform;
            Bounds bounds = MeasureVisual(visual);
            float halfWidth = Mathf.Max(bounds.extents.x, 0.25f) * settings.barrelRollTrailSpan;
            // A touch behind the centre, so the ribbon leaves the trailing edge.
            var anchor = new Vector3(0f, bounds.center.y, bounds.center.z - bounds.extents.z * 0.4f);

            trails = new[]
            {
                BuildTrail("RollTrail_L", visual, anchor + Vector3.left * halfWidth),
                BuildTrail("RollTrail_R", visual, anchor + Vector3.right * halfWidth),
            };

            motor.Launched += ClearTrails;
        }

        void OnDestroy()
        {
            if (motor != null) motor.Launched -= ClearTrails;
            if (ownsMaterial && trailMaterial != null) Destroy(trailMaterial);
        }

        // Model bounds in the visual's own space: every mesh's local bounds
        // pushed through mesh → visual, corner by corner, so a ship that is
        // banked, upside down or mid-loop when the trail is built measures
        // the same as one sitting flat.
        static Bounds MeasureVisual(Transform visual)
        {
            var filters = visual.GetComponentsInChildren<MeshFilter>();
            bool any = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                Matrix4x4 toVisual = visual.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Bounds local = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z);
                    Vector3 point = toVisual.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(point, Vector3.zero); any = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return any ? bounds : new Bounds(Vector3.zero, new Vector3(3f, 1f, 4f));
        }

        TrailRenderer BuildTrail(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            var trail = go.AddComponent<TrailRenderer>();
            trail.emitting = false;
            trail.time = settings.barrelRollTrailSeconds;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = settings.barrelRollTrailWidth;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.numCornerVertices = 0;
            trail.numCapVertices = 2;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.generateLightingData = false;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.autodestruct = false;
            trail.sharedMaterial = trailMaterial;

            Color color = settings.barrelRollTrailColor;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a * 0.6f, 0.35f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            return trail;
        }

        // Safety net when no material asset is assigned: a runtime URP
        // particle Unlit (it multiplies the trail's vertex colour, which the
        // plain URP Unlit ignores) set up for additive blending. The asset is
        // authoritative — runtime surface-type switching in URP is fragile.
        static Material BuildFallbackMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader);
            material.SetFloat("_Surface", 1f); // transparent
            material.SetFloat("_Blend", 2f);   // additive
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.One);
            material.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
            material.SetInt("_DstBlendAlpha", (int)BlendMode.One);
            material.SetInt("_ZWrite", 0);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", Color.white);
            return material;
        }

        void Update()
        {
            if (motor == null) return;
            bool rolling = motor.IsBarrelRolling;
            if (rolling == wasRolling) return;
            wasRolling = rolling;
            foreach (var trail in trails)
                if (trail != null) trail.emitting = rolling;
        }

        void ClearTrails()
        {
            wasRolling = false;
            foreach (var trail in trails)
            {
                if (trail == null) continue;
                trail.emitting = false;
                trail.Clear();
            }
        }
    }
}
