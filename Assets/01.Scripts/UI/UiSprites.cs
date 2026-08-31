using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// Runtime-generated UI sprites for the code-built overlays: the Kenney
    /// packs ship no plain disc or ring, so the gauges draw their own. An
    /// anti-aliased white <see cref="Circle"/> (the minimap / speedometer
    /// disc) and a <see cref="Ring"/> — an annulus, which is what a
    /// Radial360-filled Image needs to read as a progress ring rather than a
    /// pie. Every texture is flagged DontSave so a generated sprite never
    /// serializes into a scene; tint with the Image colour.
    /// </summary>
    public static class UiSprites
    {
        /// <summary>Anti-aliased white disc filling the texture.</summary>
        public static Sprite Circle(int size) => Build(size, size * 0.5f - 1f, 0f);

        /// <summary>Anti-aliased white ring: an outer disc with a hole <paramref name="thickness"/> pixels in from its edge.</summary>
        public static Sprite Ring(int size, int thickness)
        {
            float outer = size * 0.5f - 1f;
            return Build(size, outer, Mathf.Max(0f, outer - thickness));
        }

        static Sprite Build(int size, float outerRadius, float innerRadius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float coverage = Mathf.Clamp01(outerRadius - distance + 0.5f);
                if (innerRadius > 0f) coverage *= Mathf.Clamp01(distance - innerRadius + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * coverage));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
