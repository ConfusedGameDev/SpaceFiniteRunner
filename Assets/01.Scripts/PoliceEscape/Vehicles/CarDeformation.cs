using System;
using System.Collections.Generic;
using EVP;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Visible body damage for every city car — player, police and traffic —
    /// through EVP5's <see cref="VehicleDamage"/>: each impact crumples the
    /// body meshes around the contact point and knocks the wheel meshes off
    /// true, <see cref="Repair"/> straightens them back out. Purely cosmetic:
    /// the gameplay damage (the player's corruption meter, the NPCs'
    /// <see cref="CarHealth"/>) keeps its own speed-based rules.
    ///
    /// This component OWNS the VehicleDamage rather than letting the prefabs
    /// author one, because the wiring is derived from the rig, not designed:
    /// <b>meshes</b> are every active body MeshFilter under the car that is
    /// not part of a wheel (all body LODs — the LODGroup culls, it does not
    /// deactivate, so a dent must land on every level or pop at distance),
    /// <b>nodes</b> are the wheel MESHES under the visual pivots (EVP writes
    /// the pivots' world pose every frame, so a pivot could never hold a
    /// bend; the mesh child's local pose is free), and <b>colliders</b> stay
    /// empty — the root BoxCollider is the chassis every system reads and
    /// no car carries body MeshColliders. Knobs live on the
    /// <see cref="CarConfig"/>'s "Damage (EVP)" group and are pushed live by
    /// <see cref="EvpCarBackend"/>. EVP-only by construction: VehicleDamage
    /// requires the VehicleController and reads its impacts, so the built-in
    /// backend shows no dents.
    ///
    /// Two EVP quirks it papers over. VehicleDamage never unsubscribes from
    /// <c>onImpact</c>, so <see cref="Detach"/> removes its delegate by hand —
    /// an authored (parked, reusable) controller would otherwise keep
    /// deforming through a destroyed component. And its OnDisable restores
    /// the meshes, which is right for a backend toggle but wrong for a kill:
    /// <see cref="CarHealth"/> detaches with <c>keepDents</c>, which empties
    /// the arrays before the destroy so the wreck keeps the damage that
    /// killed it.
    ///
    /// It also owns the MESH INSTANCES. VehicleDamage deforms
    /// <c>MeshFilter.mesh</c>, which copies the shared kit mesh per car, and
    /// Unity never frees such a copy with its GameObject — a long run
    /// despawning hundreds of traffic cars would leak them. So a backend
    /// toggle puts the shared asset back on the filter and destroys the copy,
    /// a despawn destroys the copies in OnDestroy, and a wreck hands them to
    /// <see cref="CarHealth"/> (the one component that outlives the kill) to
    /// destroy with the hull.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    [DisallowMultipleComponent]
    public class CarDeformation : MonoBehaviour
    {
        VehicleDamage damage;
        VehicleController vehicle;
        bool wheelsEnabled;
        bool detached;

        // The body filters VehicleDamage deforms and the SHARED meshes they
        // carried before it instanced them — what tells a copy from an asset.
        MeshFilter[] filters = Array.Empty<MeshFilter>();
        Mesh[] originals = Array.Empty<Mesh>();

        /// <summary>The EVP component doing the work (read-only telemetry: meshDamage, isRepairing).</summary>
        public VehicleDamage Damage => damage;

        /// <summary>Whether the wheel meshes were wired as damage nodes at install — the config's evpDamageWheels at that moment.</summary>
        public bool WheelsEnabled => wheelsEnabled;

        /// <summary>
        /// Add and wire a VehicleDamage on <paramref name="car"/>, which must
        /// already carry <paramref name="vehicle"/>. VehicleDamage snapshots
        /// its arrays in OnEnable, which AddComponent fires at once on an
        /// active object — before the arrays exist — so the object is cycled
        /// inactive around the add (a no-op inside EvpCarBackend.Install,
        /// which already holds it inactive). Deactivating a rigidbody drops
        /// its motion, hence the velocity carry-over.
        /// </summary>
        public static CarDeformation Install(CarController car, VehicleController vehicle)
        {
            if (car == null || vehicle == null) return null;
            // A detached one is still findable until its deferred Destroy
            // lands at the end of the frame: report "not yet" rather than
            // adopt it, and the backend retries next step.
            var existing = car.GetComponent<CarDeformation>();
            if (existing != null) return existing.detached ? null : existing;

            GameObject go = car.gameObject;
            CarConfig config = car.config;
            bool wheels = config == null || config.evpDamageWheels;
            MeshFilter[] meshes = CollectBodyMeshes(car);
            Transform[] nodes = wheels ? CollectWheelNodes(car) : Array.Empty<Transform>();
            if (meshes.Length == 0)
                Debug.LogWarning($"CarDeformation: no body meshes found on {car.name} — nothing will dent.", car);

            bool wasActive = go.activeSelf;
            var body = go.GetComponent<Rigidbody>();
            Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
            Vector3 angular = body != null ? body.angularVelocity : Vector3.zero;
            if (wasActive) go.SetActive(false);

            var damage = go.AddComponent<VehicleDamage>();
            damage.meshes = meshes;
            damage.nodes = nodes;
            damage.colliders = Array.Empty<MeshCollider>();
            damage.nodeRotationRate = 10f;
            // R is the player's respawn key; repair is called by the game, never by a hotkey.
            damage.enableRepairKey = false;

            var deformation = go.AddComponent<CarDeformation>();
            deformation.damage = damage;
            deformation.vehicle = vehicle;
            deformation.wheelsEnabled = wheels;
            deformation.filters = meshes;
            deformation.originals = new Mesh[meshes.Length];
            for (int i = 0; i < meshes.Length; i++) deformation.originals[i] = meshes[i].sharedMesh;
            deformation.Apply(config);

            if (wasActive)
            {
                go.SetActive(true);
                if (body != null)
                {
                    body.linearVelocity = velocity;
                    body.angularVelocity = angular;
                }
            }
            return deformation;
        }

        /// <summary>
        /// Push the config's damage knobs onto the VehicleDamage — called
        /// every physics step by the backend so the debug sliders are live.
        /// The wheel toggle is not applied here: the node list is sized at
        /// OnEnable, so the backend re-installs when it changes.
        /// </summary>
        public void Apply(CarConfig config)
        {
            if (damage == null || config == null) return;
            damage.minVelocity = config.evpDamageMinSpeed;
            damage.multiplier = config.evpDamageMultiplier;
            damage.damageRadius = config.evpDamageRadius;
            damage.maxDisplacement = config.evpDamageMaxDisplacement;
            damage.maxVertexFracture = config.evpDamageVertexFracture;
            damage.nodeDamageRadius = config.evpDamageRadius;
            damage.maxNodeRotation = config.evpDamageWheelBend;
            damage.vertexRepairRate = config.evpDamageRepairRate;
        }

        /// <summary>Start the progressive repair (vertices and wheel nodes ease home at the config's repair rate).</summary>
        public void Repair()
        {
            if (damage != null) damage.Repair();
        }

        /// <summary>
        /// Remove the VehicleDamage and this owner. <paramref name="keepDents"/>
        /// false is the backend-toggle path: the shared kit meshes go back on
        /// the filters and the dented copies are destroyed (the wheel nodes
        /// are restored by VehicleDamage's own OnDisable). True is the wreck
        /// path: the hull stays crumpled and the copies are RETURNED — the
        /// caller owns them and must destroy them when the wreck goes.
        /// Either way VehicleDamage's mesh list is emptied first so its
        /// OnDisable cannot re-instance anything, the stale onImpact delegate
        /// is removed, and the VehicleDamage is queued before anything that
        /// destroys the VehicleController it requires.
        /// </summary>
        public Mesh[] Detach(bool keepDents)
        {
            Mesh[] kept = Array.Empty<Mesh>();
            if (damage != null)
            {
                Unsubscribe();
                kept = ReleaseMeshes(restore: !keepDents);
                damage.meshes = Array.Empty<MeshFilter>();
                if (keepDents) damage.nodes = Array.Empty<Transform>();
                Destroy(damage);
                damage = null;
            }
            detached = true;
            Destroy(this);
            return kept;
        }

        /// <summary>
        /// The whole car is going (despawn, splash, scene unload) with the
        /// damage still attached: free the mesh copies, which nothing else
        /// references. A Detach has already dealt with them.
        /// </summary>
        void OnDestroy()
        {
            if (detached) return;
            foreach (Mesh copy in ReleaseMeshes(restore: false)) Destroy(copy);
        }

        /// <summary>
        /// The per-car mesh copies VehicleDamage made (a filter whose mesh is
        /// still the shared asset was never touched). With <paramref name="restore"/>
        /// the asset goes back on the filter and the copy is destroyed;
        /// otherwise the copies are returned for the caller to own.
        /// </summary>
        Mesh[] ReleaseMeshes(bool restore)
        {
            var copies = new List<Mesh>(filters.Length);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null) continue;
                Mesh current = filter.sharedMesh;
                if (current == null || current == originals[i]) continue;
                if (restore)
                {
                    filter.sharedMesh = originals[i];
                    Destroy(current);
                }
                else copies.Add(current);
            }
            return copies.ToArray();
        }

        void Unsubscribe()
        {
            if (vehicle == null || vehicle.onImpact == null) return;
            foreach (Delegate handler in vehicle.onImpact.GetInvocationList())
                if (ReferenceEquals(handler.Target, damage))
                    vehicle.onImpact -= (VehicleController.OnImpact)handler;
        }

        // ---------------------------------------------------------- collection

        static Transform[] WheelVisuals(CarController car)
        {
            var list = new List<Transform>(4);
            foreach (Transform visual in new[] { car.frontLeftVisual, car.frontRightVisual, car.rearLeftVisual, car.rearRightVisual })
                if (visual != null) list.Add(visual);
            return list.ToArray();
        }

        static bool UnderAny(Transform transform, Transform[] roots)
        {
            foreach (Transform root in roots)
                if (transform.IsChildOf(root)) return true; // IsChildOf is true for the root itself too
            return false;
        }

        /// <summary>
        /// Every live MeshFilter with a MeshRenderer that is not part of a
        /// wheel: the body LODs, spoilers, glass, the code-built police bar.
        /// "Live" is checked by hand (<see cref="LiveUnder"/>) because this
        /// runs while EvpCarBackend holds the whole car INACTIVE — the default
        /// GetComponentsInChildren would return nothing — yet the kit's
        /// retired collider/LOD children, hidden with SetActive(false) and
        /// disabled renderers, must still be left out. Read/Write matters in
        /// a BUILD (the editor keeps every mesh readable): an unreadable mesh
        /// is skipped there with a warning instead of throwing inside EVP's
        /// deform loop.
        /// </summary>
        static MeshFilter[] CollectBodyMeshes(CarController car)
        {
            Transform[] wheels = WheelVisuals(car);
            var list = new List<MeshFilter>();
            foreach (MeshFilter filter in car.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null || !renderer.enabled) continue;
                if (!LiveUnder(filter.transform, car.transform)) continue;
                if (UnderAny(filter.transform, wheels)) continue;
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"CarDeformation: mesh '{mesh.name}' on {car.name} is not Read/Write enabled — " +
                                     "it cannot dent in a build. Enable Read/Write on its model importer.", filter);
                    if (!Application.isEditor) continue;
                }
                list.Add(filter);
            }
            return list.ToArray();
        }

        /// <summary>
        /// The wheel meshes: the renderer-bearing children of each visual
        /// pivot. A car whose visual IS the mesh (no child) gets no node for
        /// that wheel — EVP repositions the visual itself every frame.
        /// </summary>
        static Transform[] CollectWheelNodes(CarController car)
        {
            var list = new List<Transform>(4);
            foreach (Transform visual in WheelVisuals(car))
                foreach (Transform child in visual)
                    if (child.gameObject.activeSelf && child.GetComponentInChildren<Renderer>(true) != null)
                        list.Add(child);
            return list.ToArray();
        }

        /// <summary>
        /// True when every object from <paramref name="transform"/> up to (not
        /// including) <paramref name="root"/> is activeSelf — "would be visible
        /// once the car is active", independent of the car's own state.
        /// </summary>
        static bool LiveUnder(Transform transform, Transform root)
        {
            for (Transform t = transform; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }
    }
}
