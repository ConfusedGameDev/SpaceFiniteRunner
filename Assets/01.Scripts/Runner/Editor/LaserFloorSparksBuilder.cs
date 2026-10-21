using System.IO;
using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.FX;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Makes the designer-owned spark VFX of the laser gates, never
    /// overwriting: <c>PF_LaserFloorSparks</c> (a <see cref="LaserFloorSparks"/>
    /// on an authored ParticleSystem, its <c>Floor</c> collision plane and its
    /// <c>Glow</c> light), the additive star material it draws with (the
    /// shared sparkle baked to a texture asset, since a prefab cannot hold the
    /// runtime one), and the instance NESTED in <c>PF_LaserSystem</c> at the
    /// sheet's foot, so it is tuned in context. The prefab is created with the
    /// look the code used to build (the values the definition carried), at the
    /// sheet's width; after that it is the designers' — the code only stretches
    /// and switches it. Run through <see cref="LaserAssetBuilder"/>.
    /// </summary>
    public static class LaserFloorSparksBuilder
    {
        public const string PrefabPath = "Assets/03.Prefabs/Runner/PF_LaserFloorSparks.prefab";
        public const string MaterialPath = "Assets/02.Art/02.Materials/FiniteRunner/LaserFloorSparks_Mat.mat";
        public const string TextureFolder = "Assets/02.Art/03.Textures/FiniteRunner";
        public const string TexturePath = TextureFolder + "/Sparkle_Star.png";

        // The look the gate built in code before the prefab existed.
        static readonly Color SparkColor = new(1f, 0.45f, 0.15f, 1f);
        const float RatePerMeter = 12f, Gravity = 2f, Bounce = 0.528f, Depth = 0.5f, LightIntensity = 8f;
        const int MaxParticles = 1500, ImpactBurst = 60;

        /// <summary>
        /// Nests the spark prefab under <paramref name="laserRoot"/> (a loaded
        /// <c>PF_LaserSystem</c>) on the sheet's bottom edge: X across the sheet,
        /// Y back up it, at world unit scale whatever the laser root's scale.
        /// </summary>
        public static LaserFloorSparks Nest(Transform laserRoot, Transform sheet, Transform shootPoint)
        {
            LaserBeam.MeasureSheet(shootPoint, sheet, out float length, out float width);
            GameObject prefab = CreateOrLoadPrefab(Mathf.Max(1f, width));
            if (prefab == null) return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, laserRoot);
            instance.name = LaserBeam.FloorSparksName;
            Vector3 up = -shootPoint.forward; // the sheet hangs down its forward: the road's up is back up it
            instance.transform.SetPositionAndRotation(shootPoint.position + shootPoint.forward * length,
                                                      Quaternion.LookRotation(Vector3.Cross(shootPoint.right, up), up));
            instance.transform.localScale = Vector3.one / Mathf.Max(1e-4f, laserRoot.lossyScale.x);
            return instance.GetComponent<LaserFloorSparks>();
        }

        /// <summary>The spark prefab — loaded when it exists, otherwise built for a foot <paramref name="width"/> metres wide.</summary>
        public static GameObject CreateOrLoadPrefab(float width)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;

            var go = new GameObject("PF_LaserFloorSparks");
            try
            {
                Build(go, width);
                return PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static void Build(GameObject go, float width)
        {
            var sparks = go.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 18f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.96f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            // White-hot cores with the tint on the tails, like the duel's.
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(SparkColor, Color.white, 0.35f), Color.white);
            main.gravityModifier = Gravity;
            main.maxParticles = MaxParticles;
            main.stopAction = ParticleSystemStopAction.None;

            // Authored for THIS width: the gate stretches the box and the rate together.
            var emission = sparks.emission;
            emission.rateOverTime = RatePerMeter * width;

            // The foot line: a thin box along X, every spark leaving it in a
            // random direction — half of them into the road, which throws them
            // back out low and wide.
            var shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width, 0.05f, Depth);
            shape.randomDirectionAmount = 1f;

            // The road: a plane through the foot, normal the rig's up.
            var floor = new GameObject("Floor").transform;
            floor.SetParent(go.transform, false);
            var collision = sparks.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.Planes;
            collision.SetPlane(0, floor);
            collision.bounce = Bounce;
            collision.dampen = 0.25f;
            collision.lifetimeLoss = 0.15f;
            collision.radiusScale = 0.1f;

            var sizeOverLife = sparks.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.6f, 0.75f), new Keyframe(1f, 0f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 2f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = CreateOrLoadMaterial();

            var lamp = new GameObject("Glow");
            lamp.transform.SetParent(go.transform, false);
            lamp.transform.localPosition = Vector3.up * 1f;
            var glow = lamp.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = Color.Lerp(SparkColor, Color.white, 0.45f);
            glow.range = width;
            glow.intensity = LightIntensity;
            glow.shadows = LightShadows.None;

            var rig = go.AddComponent<LaserFloorSparks>();
            var so = new SerializedObject(rig);
            so.FindProperty("glow").objectReferenceValue = glow;
            so.FindProperty("impactBurstCount").intValue = ImpactBurst;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The additive star material — loaded when it exists, otherwise made from the shared sparkle, baked to a texture asset.</summary>
        public static Material CreateOrLoadMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            material = ParticleMaterials.Unlit("LaserFloorSparks_Mat", CreateOrLoadTexture(), additive: true);
            material.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        static Texture2D CreateOrLoadTexture()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture != null) return texture;

            if (!AssetDatabase.IsValidFolder(TextureFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(TextureFolder).Replace('\\', '/'), Path.GetFileName(TextureFolder));
            File.WriteAllBytes(TexturePath, SparkleVfx.SharedTexture.EncodeToPNG());
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(TexturePath) is TextureImporter importer)
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }
    }
}
