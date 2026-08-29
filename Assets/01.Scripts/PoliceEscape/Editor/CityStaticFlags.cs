using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Editor
{
    /// <summary>
    /// Static flags for the baked city are a BAKE PRODUCT, never hand-set:
    /// every block child that can neither move nor be pushed (road pieces,
    /// buildings, cliffs, park tiles, the sea floor) is flagged Occluder +
    /// Occludee so the scene can compute an occlusion map; transparent
    /// surfaces (the water quad) are Occludee-only, since a see-through
    /// occluder would hide what shows through it; and anything carrying a
    /// Rigidbody on itself or an ancestor (decoration props, explosive
    /// barrels, nature props, a parked car) stays dynamic, because a flagged
    /// mover is culled against its bake-time position long after the player
    /// has kicked it across the street. The sockets (AdditionalItems,
    /// DefaultVehicles) are never touched, same as the baker itself. Flags
    /// are only written where they differ, so re-running is idempotent and
    /// never adds prefab-instance overrides for nothing. Called per block
    /// from <see cref="CityBaker"/>; the menu item flags an already-baked
    /// prefab in place without a rebake.
    /// </summary>
    public static class CityStaticFlags
    {
        const StaticEditorFlags Solid = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
        const StaticEditorFlags Transparent = StaticEditorFlags.OccludeeStatic;

        /// <summary>Flag one baked block's content. Returns how many objects changed.</summary>
        public static int ApplyToBlock(GameObject blockGo, bool batching)
        {
            int changed = 0;
            StaticEditorFlags extra = batching ? StaticEditorFlags.BatchingStatic : 0;
            foreach (Transform child in blockGo.transform)
            {
                StaticEditorFlags wanted = child.name switch
                {
                    "Roads" or "Buildings" or "Shoreline" or "Nature" or "Decorations" or "SeaFloor" => Solid | extra,
                    "WaterSurface" => Transparent,
                    _ => 0, // WaterSplashZone (a trigger, no renderer) and anything unknown
                };
                if (wanted != 0) changed += Flag(child.gameObject, wanted);
            }
            return changed;
        }

        /// <summary>Flag every block under a city root, leaving the two sockets alone. Returns how many objects changed.</summary>
        public static int ApplyToCity(GameObject cityRoot, bool batching, bool progressBar = false)
        {
            int changed = 0;
            CityBlock[] blocks = cityRoot.GetComponentsInChildren<CityBlock>(true);
            try
            {
                for (int i = 0; i < blocks.Length; i++)
                {
                    if (progressBar)
                        EditorUtility.DisplayProgressBar("City static flags", $"Block {blocks[i].coord}", (float)i / blocks.Length);
                    changed += ApplyToBlock(blocks[i].gameObject, batching);
                }
            }
            finally
            {
                if (progressBar) EditorUtility.ClearProgressBar();
            }
            return changed;
        }

        /// <summary>
        /// Flag the city prefab on disk in place, for a prefab baked before
        /// the flags existed or after toggling the Performance knobs, without
        /// the (slow) full rebake. Re-bake the scene occlusion afterwards.
        /// </summary>
        [MenuItem("Tools/Police Escape/Apply City Static Flags")]
        public static void ApplyToPrefab()
        {
            string path = CityBaker.DefaultPrefabPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogError($"CityStaticFlags: no city prefab at {path}. Bake the city first.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                CityRoot city = root.GetComponent<CityRoot>();
                CityGenerationSettings generation = city != null && city.definition != null ? city.definition.generation : null;
                bool batching = generation != null && generation.staticBatching;
                int changed = ApplyToCity(root, batching, progressBar: true);
                if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"CityStaticFlags: {changed} object(s) flagged in {path}" + (changed > 0
                    ? ". Re-run Tools > Police Escape > Bake Occlusion Culling in the scene."
                    : " (already up to date)."));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Recursive flagging that stops at the first Rigidbody: the whole
        /// subtree moves with it, so none of it may be static. Only objects
        /// that own a Renderer take flags; an empty group object would just
        /// be one more prefab override for the occlusion baker to ignore.
        /// </summary>
        static int Flag(GameObject go, StaticEditorFlags wanted)
        {
            if (go.GetComponent<Rigidbody>() != null) return 0;
            int changed = 0;
            if (go.GetComponent<Renderer>() != null && GameObjectUtility.GetStaticEditorFlags(go) != wanted)
            {
                GameObjectUtility.SetStaticEditorFlags(go, wanted);
                changed++;
            }
            foreach (Transform child in go.transform) changed += Flag(child.gameObject, wanted);
            return changed;
        }
    }
}
