using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// The facts every Cyberpunk Megapolis car shares (CP_Quadron, CP_Minivan,
    /// CP_Taxi under Cyberpunk_Megapolis/Models/Car), factored out so the
    /// editor prefab builder (CyberpunkCarBuilder — player and police
    /// prefabs) and the runtime traffic rig (VehicleRigBuilder — civilians
    /// built straight from the FBX) adopt a kit car the same way. The kit is
    /// authored in REAL METRES (4.8–5.3 m long, so <see cref="ModelScale"/>
    /// is 1 where the Kenney models are stretched ×1.73), its length runs
    /// along X after Unity's FBX X-mirror with the low bonnet at +X — every
    /// car in the pack, checked off the vertex profiles — so the whole model
    /// is yawed by <see cref="ModelYaw"/> to face the controller's +Z; its
    /// four wheels are direct children named *_Wheel_0N_LOD0, already
    /// pivoted on their axle but carrying LOD1/LOD2 children inside the
    /// body's LODGroup (dropped, so a wheel is one mesh the controller can
    /// spin, and pruned from the group); and the pack's prefabs carry a
    /// convex body MeshCollider that must go because the rig's root
    /// BoxCollider is the chassis every consumer reads. Wheels are told
    /// apart by their position in car space, never by the kit's 01–04
    /// numbering, which differs per model.
    /// </summary>
    public static class CyberpunkCarKit
    {
        /// <summary>
        /// Yaw that turns the kit's forward (+X once imported) into the
        /// controller's +Z. Flip to +90 if a re-exported model ever drives
        /// tail-first.
        /// </summary>
        public const float ModelYaw = -90f;

        /// <summary>The kit is real metres — 1 keeps every car its authored length.</summary>
        public const float ModelScale = 1f;

        /// <summary>
        /// Ride height added to the BODY (model + chassis box) over the
        /// wheels, which stay on their axles. Authored, the kit's floors sit
        /// 0.14–0.21 m off the road with a metre of bonnet ahead of the front
        /// axle, so a chassis box fitted to the shell met the bridge ramps
        /// with its front edge before the wheels (raycasts, they climb
        /// anything) did. On top of the lift the box's underside is clamped
        /// to the axle line by the rig builders, so the car climbs on its
        /// wheels and only the visual floor stays low.
        /// </summary>
        public const float BodyLift = 0.15f;

        /// <summary>The kit's wheel objects: direct children of the model root named *Wheel* that carry a renderer.</summary>
        public static List<Transform> FindWheels(Transform kit)
        {
            var found = new List<Transform>();
            foreach (Transform child in kit)
                if (child.name.IndexOf("Wheel", System.StringComparison.OrdinalIgnoreCase) >= 0 && child.GetComponent<Renderer>() != null)
                    found.Add(child);
            return found;
        }

        /// <summary>
        /// A model that carries the kit's shape — four *Wheel* children and
        /// none of the Kenney wheel names — so a rig builder knows to take
        /// the kit path instead of the named-wheel one.
        /// </summary>
        public static bool IsKitModel(Transform model) =>
            model != null && model.Find("wheel-front-left") == null && FindWheels(model).Count == 4;

        /// <summary>Drop every collider under the model — the pack prefabs carry a convex body MeshCollider; the rig's root box is the chassis.</summary>
        public static void StripColliders(GameObject kit)
        {
            foreach (Collider collider in kit.GetComponentsInChildren<Collider>(true))
                Retire(collider);
        }

        /// <summary>One mesh per wheel: drop the LOD1/LOD2 children so the pivot spins a single renderer and the bounds are LOD0's.</summary>
        public static void FlattenWheels(IEnumerable<Transform> wheels)
        {
            foreach (Transform wheel in wheels)
                for (int i = wheel.childCount - 1; i >= 0; i--)
                    Retire(wheel.GetChild(i).gameObject);
        }

        /// <summary>
        /// Tell the four wheels apart by their position in <paramref name="car"/>
        /// space (+Z front, -X left) — the model must already be yawed under
        /// the car when this runs. False when two slots pick the same wheel,
        /// which is what a wrong <see cref="ModelYaw"/> looks like.
        /// </summary>
        public static bool TryClassifyWheels(List<Transform> wheels, Transform car,
            out Transform frontLeft, out Transform frontRight, out Transform rearLeft, out Transform rearRight)
        {
            Transform Pick(bool front, bool left) => wheels
                .OrderByDescending(w =>
                {
                    Vector3 local = car.InverseTransformPoint(w.GetComponent<Renderer>().bounds.center);
                    return (front ? 1f : -1f) * local.z + (left ? -1f : 1f) * local.x;
                })
                .First();
            frontLeft = Pick(true, true);
            frontRight = Pick(true, false);
            rearLeft = Pick(false, true);
            rearRight = Pick(false, false);
            return new[] { frontLeft, frontRight, rearLeft, rearRight }.Distinct().Count() == 4;
        }

        /// <summary>
        /// After the wheels have left the model: the body's LODGroup still
        /// lists their renderers (and the retired LOD children as nulls), so
        /// keep only renderers that are still under the group.
        /// </summary>
        public static void PruneLodGroup(GameObject kit)
        {
            var lodGroup = kit.GetComponentInChildren<LODGroup>();
            if (lodGroup == null) return;
            LOD[] lods = lodGroup.GetLODs();
            for (int i = 0; i < lods.Length; i++)
                lods[i].renderers = lods[i].renderers
                    .Where(r => r != null && r.gameObject.activeSelf && r.transform.IsChildOf(lodGroup.transform))
                    .ToArray();
            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();
        }

        /// <summary>
        /// Remove an object or component right now in the editor (the prefab
        /// builder measures bounds straight after) and safely at runtime,
        /// where the object is hidden first so the same measurements (active
        /// renderers only) exclude it before the deferred destroy lands.
        /// </summary>
        static void Retire(Object target)
        {
            if (Application.isPlaying)
            {
                if (target is GameObject go) go.SetActive(false);
                else if (target is Collider collider) collider.enabled = false;
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
