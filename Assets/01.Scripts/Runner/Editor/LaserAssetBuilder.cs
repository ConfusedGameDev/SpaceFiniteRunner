using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Creates what the laser gates need, never overwriting anything that is
    /// already there: the additive beam material, the
    /// <see cref="LaserGateDefinition"/> asset pointing at it, and the
    /// <see cref="LaserBeam"/> on the <c>PF_LaserSystem</c> prefab with its
    /// emitters and shoot points wired by name (LaserA / LaserB, each with a
    /// ShootPoint child). The generator's Laser gates group still has to be
    /// pointed at the prefab and the definition in the scene.
    /// </summary>
    public static class LaserAssetBuilder
    {
        public const string MaterialPath = "Assets/02.Art/02.Materials/FiniteRunner/LaserBeam_Mat.mat";
        public const string DefinitionPath = "Assets/04.Data/FiniteRunner/LaserGate_Definition.asset";
        public const string PrefabPath = "Assets/03.Prefabs/Runner/PF_LaserSystem.prefab";

        [MenuItem("Tools/FiniteRunner/Install Laser Gate Assets")]
        public static void InstallFromMenu()
        {
            LaserGateDefinition definition = CreateOrLoadDefinition();
            WirePrefab();
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(definition);
            Debug.Log($"LaserAssetBuilder: laser gate assets ready — {DefinitionPath}, {MaterialPath}, {PrefabPath}.", definition);
        }

        /// <summary>The beam material — loaded when it exists, otherwise created: URP Particles/Unlit, additive, so the line's vertex colour tints it.</summary>
        public static Material CreateOrLoadMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError("LaserAssetBuilder: URP Particles/Unlit shader not found — the beam falls back to its runtime material.");
                return null;
            }

            material = new Material(shader);
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
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        /// <summary>The definition asset — loaded when it exists, otherwise created with the material wired.</summary>
        public static LaserGateDefinition CreateOrLoadDefinition()
        {
            var definition = AssetDatabase.LoadAssetAtPath<LaserGateDefinition>(DefinitionPath);
            if (definition != null) return definition;

            definition = ScriptableObject.CreateInstance<LaserGateDefinition>();
            definition.beamMaterial = CreateOrLoadMaterial();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        /// <summary>Adds the LaserBeam to the prefab (once) and wires whichever of its four references are still empty.</summary>
        public static void WirePrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError($"LaserAssetBuilder: no prefab at {PrefabPath}.");
                return;
            }

            try
            {
                var beam = root.GetComponent<LaserBeam>();
                if (beam == null) beam = root.AddComponent<LaserBeam>();

                Transform a = root.transform.Find("LaserA"), b = root.transform.Find("LaserB");
                var so = new SerializedObject(beam);
                Wire(so, "emitterA", a);
                Wire(so, "emitterB", b);
                Wire(so, "shootPointA", a != null ? a.Find("ShootPoint") : null);
                Wire(so, "shootPointB", b != null ? b.Find("ShootPoint") : null);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void Wire(SerializedObject so, string property, Transform target)
        {
            SerializedProperty prop = so.FindProperty(property);
            if (prop.objectReferenceValue != null) return; // someone's wiring: leave it
            if (target == null) Debug.LogWarning($"LaserAssetBuilder: nothing to wire into '{property}' — name the emitters LaserA / LaserB, each with a ShootPoint child.");
            prop.objectReferenceValue = target;
        }
    }
}
