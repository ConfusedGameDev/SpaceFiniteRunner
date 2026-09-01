using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.FX;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// The NPC damage model, attached in code by the spawn managers (traffic
    /// has no prefab to author it on). Health only drops to the PLAYER — a
    /// direct shunt or an explosion; AI fender-benders are ignored so the
    /// fleet can't grind itself down without the player earning it. A wounded
    /// car slows through <see cref="SpeedFactor"/>, which the drivers multiply
    /// into their target speed: the factor eases toward the car's HEALTH
    /// (floored at a crawl) rather than snapping, so speed visibly matches how
    /// beaten-up the car is. At half health the engine blows light white
    /// smoke, at a fifth heavy black smoke joins it, and at zero the car
    /// brakes to a stop, smokes through a fuse, and goes up in the shared
    /// <see cref="Blast"/> — every <see cref="IDamageable"/> in the radius is
    /// caught, whoever caused it, which is what makes a wreck a weapon and
    /// standing next to one a mistake. The car does not vanish: the explosion
    /// chars the paint black, tears the wheels off and drops the hull onto the
    /// street as a lingering wreck, while the drivers and controller are
    /// destroyed so the managers' maintenance ticks replace the car for free.
    /// Cars caught in a blast die through this same component (they smoke
    /// their own fuse first, so chains ripple instead of recursing) and
    /// barrels caught in it detonate in turn.
    /// </summary>
    public class CarHealth : MonoBehaviour, IDamageable
    {
        // One material per sprite shared by every smoking car in the run —
        // same rule as the fireball cache.
        static readonly Dictionary<Texture, Material> PlumeMaterials = new();

        static readonly Color Charred = new(0.08f, 0.08f, 0.08f);

        [TitleGroup("Debug"), ShowInInspector, ReadOnly, ProgressBar(0f, 1f)]
        public float Health { get; private set; } = 1f;

        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public bool IsDead { get; private set; }

        /// <summary>Multiplier the drivers apply to their target speed — eased toward the car's health, never snapped.</summary>
        [TitleGroup("Debug"), ShowInInspector, ReadOnly]
        public float SpeedFactor { get; private set; } = 1f;

        VehicleHealthSettings settings;
        ParticleSystem lightSmoke;
        ParticleSystem heavySmoke;
        float fuse;
        bool exploded;

        // The wreck's crumpled body meshes — per-car copies handed over by
        // CarDeformation on the kill, freed with the hull (see OnDestroy).
        Mesh[] dentedMeshes;

        void Awake()
        {
            settings = VehicleHealthSettings.Load();
        }

        void Update()
        {
            if (exploded) return; // the wreck just lies there until its linger runs out

            // Speed MATCHES health: the target is the health value itself,
            // floored at a crawl so a nearly dead car limps instead of
            // freezing mid-road — only the ease rate keeps it from snapping.
            float target = IsDead ? 0f : Mathf.Max(settings.crawlSpeedFactor, Health);
            SpeedFactor = Mathf.MoveTowards(SpeedFactor, target, settings.speedEasePerSecond * Time.deltaTime);

            if (!IsDead) return;
            fuse -= Time.deltaTime;
            if (fuse <= 0f) Explode();
        }

        /// <summary>
        /// Only the player's car deals contact damage — the CarInput component
        /// is the player marker used project-wide. Scaled by how hard the hit
        /// landed, with the same scrape floor the player's own meter uses.
        /// </summary>
        void OnCollisionEnter(Collision collision)
        {
            if (IsDead || collision.rigidbody == null) return;
            if (collision.rigidbody.GetComponent<CarInput>() == null) return;

            float impact = collision.relativeVelocity.magnitude;
            if (impact < settings.minImpactSpeed) return;
            ApplyDamage((impact - settings.minImpactSpeed) * settings.damagePerImpactSpeed);
        }

        /// <summary>Take a bite out of the car. Reaching zero starts the death fuse.</summary>
        public void ApplyDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Health = Mathf.Max(0f, Health - amount);

            if (Health <= settings.lightSmokeHealth) SetEmitter(ref lightSmoke, true, heavy: false);
            if (Health <= settings.heavySmokeHealth) SetEmitter(ref heavySmoke, true, heavy: true);
            if (Health <= 0f) BeginDeath();
        }

        void BeginDeath()
        {
            IsDead = true;
            fuse = settings.fuseSeconds;
            SetEmitter(ref heavySmoke, true, heavy: true); // a dead car belches black even if the threshold was never crossed
        }

        /// <summary>
        /// The death blast, once: a barrel-quality fireball first, then the
        /// shared <see cref="Blast"/> damages every IDamageable in the radius
        /// and shoves what it caught — cars die (and smoke their own fuse
        /// before blowing, so no same-frame recursion), barrels detonate, the
        /// player takes it on the corruption meter through their receiver.
        /// The car itself becomes a wreck instead of disappearing.
        /// </summary>
        void Explode()
        {
            if (exploded) return;
            exploded = true;

            Vector3 origin = transform.position;
            ExplosionVfx.SpawnFireball(origin, settings.explosionTextures,
                settings.explosionScale, settings.explosionLifetime, settings.explosionParticles);

            Blast.Apply(origin, settings.blastRadius, settings.blastForce,
                        settings.blastUpModifier, settings.blastDamage, GetComponent<Rigidbody>());

            BecomeWreck();
        }

        /// <summary>
        /// What the explosion leaves behind: paint charred black, wheels torn
        /// off (colliders and meshes both, so the hull drops onto the street),
        /// every other behaviour destroyed — the driver and controller going
        /// is what makes the managers' maintenance ticks forget the car and
        /// cut a replacement in. The hull keeps its rigidbody and box collider
        /// (later blasts still shove it) and its black smoke keeps rising
        /// until the linger timer clears the street.
        /// </summary>
        void BecomeWreck()
        {
            // The dents are what the wreck should keep: detach the EVP body
            // damage with its meshes left crumpled (its OnDisable would
            // otherwise snap the hull back to showroom shape), and do it
            // FIRST — VehicleDamage [RequireComponent]s the EVP controller
            // the blanket pass below destroys, same ordering rule as the
            // drivers and the CarController.
            var deformation = GetComponent<CarDeformation>();
            if (deformation != null) dentedMeshes = deformation.Detach(keepDents: true);

            // Grab the tire meshes off the controller before it is destroyed.
            var controller = GetComponent<CarController>();
            Transform[] tires = controller != null
                ? new[] { controller.frontLeftVisual, controller.frontRightVisual,
                          controller.rearLeftVisual, controller.rearRightVisual }
                : System.Array.Empty<Transform>();

            // Everything with an Update dies with the car — driver, controller,
            // police lights — except this component, which owns the linger.
            // TWO passes, controller last: the drivers [RequireComponent] the
            // CarController, and Unity refuses to destroy a dependency while a
            // dependent still exists. Queued destroys execute in call order,
            // so the drivers are gone by the time the controller's turn comes.
            foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>())
                if (behaviour != this && !(behaviour is CarController)) Destroy(behaviour);
            if (controller != null) Destroy(controller);

            foreach (WheelCollider wheel in GetComponentsInChildren<WheelCollider>())
                Destroy(wheel);
            foreach (Transform tire in tires)
                if (tire != null) Destroy(tire.gameObject);

            // Char the paint on material INSTANCES — fleet mates share the
            // source materials and must keep their colors.
            foreach (MeshRenderer meshRenderer in GetComponentsInChildren<MeshRenderer>())
                foreach (Material material in meshRenderer.materials)
                {
                    material.color = Charred;
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Charred);
                }

            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.isKinematic = false;
                body.WakeUp(); // nothing holds the hull up any more — let it drop
            }

            if (lightSmoke != null) lightSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject, settings.wreckLingerSeconds);
        }

        /// <summary>
        /// The linger is over (or the scene is): free the crumpled mesh copies
        /// the wreck was wearing — Unity does not destroy a MeshFilter's
        /// instanced mesh with its GameObject.
        /// </summary>
        void OnDestroy()
        {
            if (dentedMeshes == null) return;
            foreach (Mesh mesh in dentedMeshes)
                if (mesh != null) Destroy(mesh);
            dentedMeshes = null;
        }

        // ------------------------------------------------------------ emitters

        void SetEmitter(ref ParticleSystem system, bool on, bool heavy)
        {
            if (system == null)
            {
                if (!on) return;
                system = BuildEmitter(heavy);
            }
            if (on && !system.isEmitting) system.Play();
            else if (!on && system.isEmitting) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>
        /// A continuous plume off the engine bay, simulated in WORLD space so
        /// it trails behind a moving car instead of riding it. The light plume
        /// is a thin white warning; the heavy one is the dense black column of
        /// a car about to go.
        /// </summary>
        ParticleSystem BuildEmitter(bool heavy)
        {
            List<Texture2D> textures = heavy ? settings.heavySmokeTextures : settings.lightSmokeTextures;
            Texture2D sprite = textures != null && textures.Count > 0
                ? textures[Random.Range(0, textures.Count)]
                : null;

            var go = new GameObject(heavy ? "HeavySmoke" : "LightSmoke");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = EmitterAnchor();
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone points up

            var particles = go.AddComponent<ParticleSystem>();
            // A fresh ParticleSystem starts playing the moment it is added —
            // halt it before configuring so no default-material particles slip
            // out; SetEmitter starts it once it is fully built.
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = heavy ? new ParticleSystem.MinMaxCurve(1.8f, 2.6f)
                                       : new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSize = heavy ? new ParticleSystem.MinMaxCurve(1f, 1.6f)
                                   : new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.05f; // smoke rises
            main.maxParticles = 64;

            var emission = particles.emission;
            emission.rateOverTime = heavy ? 12f : 6f;

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.25f;

            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f), new Keyframe(1f, 2f)));

            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(heavy ? 0.9f : 0.6f, 0f),
                        new GradientAlphaKey(heavy ? 0.7f : 0.4f, 0.5f),
                        new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var plumeRenderer = go.GetComponent<ParticleSystemRenderer>();
            plumeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            plumeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            plumeRenderer.receiveShadows = false;
            plumeRenderer.sharedMaterial = PlumeMaterial(sprite);
            return particles;
        }

        /// <summary>Top of the chassis box, a little forward — the engine bay; a plain lift when there is no box.</summary>
        Vector3 EmitterAnchor()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return Vector3.up * 1.2f;
            return box.center + Vector3.up * (box.size.y * 0.5f) + Vector3.forward * (box.size.z * 0.25f);
        }

        static Material PlumeMaterial(Texture sprite)
        {
            Texture key = sprite != null ? sprite : Texture2D.whiteTexture;
            if (PlumeMaterials.TryGetValue(key, out Material cached) && cached != null) return cached;
            Material material = ParticleMaterials.Unlit("CarSmoke", sprite, additive: false);
            PlumeMaterials[key] = material;
            return material;
        }
    }
}
