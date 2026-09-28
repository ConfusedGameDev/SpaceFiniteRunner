using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Livery
{
    /// <summary>
    /// Makes one vehicle model paintable: every DISTINCT material on its mesh
    /// renderers becomes one <see cref="PaintSlot"/> with its own material
    /// instance, remapped onto every renderer that shared it. Whole material
    /// instances, never a MaterialPropertyBlock — the ship's shader and the
    /// SRP Batcher ignore a block tint (see <c>RespawnBlink</c>). Trails and
    /// particles keep their materials. The source assets are never touched;
    /// the instances die with the vehicle.
    /// </summary>
    public class VehiclePaintTarget : MonoBehaviour
    {
        static readonly int MainColor = Shader.PropertyToID("_MainColor"); // RealToon
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"); // URP Lit

        /// <summary>One paintable material: its authored colour and the hue the player gave it.</summary>
        public class PaintSlot
        {
            /// <summary>The source material's name — the key the saved colours use.</summary>
            public string Id { get; internal set; }
            public Color DefaultColor { get; internal set; }
            /// <summary>Where the default colour sits on the hue bar.</summary>
            public float DefaultHue { get; internal set; }
            public float Hue { get; private set; }
            /// <summary>False = the part shows its authored colour.</summary>
            public bool Tinted { get; private set; }
            /// <summary>What the part shows right now.</summary>
            public Color Current => Tinted ? Tint(Hue) : DefaultColor;

            internal Material instance;
            internal int property;
            internal float minSaturation, minValue;

            public void SetHue(float hue)
            {
                Hue = Mathf.Repeat(hue, 1f);
                Tinted = true;
                Apply();
            }

            public void ResetToDefault()
            {
                Hue = DefaultHue;
                Tinted = false;
                Apply();
            }

            // The authored saturation and brightness carry over (HDR kept), with
            // floors so a white, grey or black part still takes a real colour.
            Color Tint(float hue)
            {
                Color.RGBToHSV(DefaultColor, out _, out float s, out float v);
                Color c = Color.HSVToRGB(hue, Mathf.Max(s, minSaturation), Mathf.Max(v, minValue), true);
                c.a = DefaultColor.a;
                return c;
            }

            void Apply()
            {
                if (instance != null) instance.SetColor(property, Current);
            }
        }

        readonly List<PaintSlot> slots = new();
        readonly List<Material> owned = new();

        /// <summary>The paintable parts, in first-seen renderer order.</summary>
        public IReadOnlyList<PaintSlot> Slots => slots;

        /// <summary>True once <see cref="Initialize"/> has run.</summary>
        public bool Initialized { get; private set; }

        /// <summary>
        /// Puts <paramref name="slotOverrides"/> on the first mesh renderer with
        /// that many slots, then clones and remaps every distinct material that
        /// has a colour to paint. The hue-to-colour floors come off
        /// <paramref name="rules"/> (defaults when null). Runs once.
        /// </summary>
        public void Initialize(IReadOnlyList<Material> slotOverrides, VehicleColorProfile rules)
        {
            if (Initialized) return;
            Initialized = true;
            float minSaturation = rules != null ? rules.minSaturation : 0.75f;
            float minValue = rules != null ? rules.minValue : 0.55f;

            var renderers = new List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer || r is SkinnedMeshRenderer) renderers.Add(r);

            if (slotOverrides != null && slotOverrides.Count > 0)
            {
                foreach (Renderer r in renderers)
                {
                    if (r.sharedMaterials.Length != slotOverrides.Count) continue;
                    var mats = new Material[slotOverrides.Count];
                    for (int i = 0; i < mats.Length; i++) mats[i] = slotOverrides[i] != null ? slotOverrides[i] : r.sharedMaterials[i];
                    r.sharedMaterials = mats;
                    break;
                }
            }

            var clones = new Dictionary<Material, Material>();
            foreach (Renderer r in renderers)
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material source = mats[i];
                    if (source == null) continue;
                    if (!clones.TryGetValue(source, out Material clone))
                    {
                        clone = MakeSlot(source, minSaturation, minValue);
                        clones.Add(source, clone);
                    }
                    if (clone == null) continue;
                    mats[i] = clone;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // Null when the material has no colour property to paint.
        Material MakeSlot(Material source, float minSaturation, float minValue)
        {
            int property = source.HasProperty(MainColor) ? MainColor
                         : source.HasProperty(BaseColor) ? BaseColor : -1;
            if (property < 0) return null;

            var clone = new Material(source) { name = source.name + " (Paint)" };
            owned.Add(clone);
            Color color = source.GetColor(property);
            Color.RGBToHSV(color, out float hue, out _, out _);
            var slot = new PaintSlot
            {
                Id = source.name,
                DefaultColor = color,
                DefaultHue = hue,
                instance = clone,
                property = property,
                minSaturation = minSaturation,
                minValue = minValue,
            };
            slot.ResetToDefault();
            slots.Add(slot);
            return clone;
        }

        void OnDestroy()
        {
            foreach (Material m in owned)
                if (m != null) Destroy(m);
            owned.Clear();
            slots.Clear();
        }
    }
}
