using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// The boost QTE's picture: the player's LIVE Boost binding floating in the
    /// middle of a boost orb's ring, a full billboard that always faces the
    /// camera. It is one object built from code on its own world-space canvas
    /// and is <b>never a child of the orb</b>: the orb is deactivated the moment
    /// it is taken (the result must outlive it), and <c>SpeedPad.ApplyColor</c>
    /// tints every renderer under the orb. The glyph is white (the mono face
    /// buttons, <see cref="ControlGlyphSet.ForMono"/>, or a white key cap) so
    /// the <see cref="Image.color"/> tint reads true: white while waiting, the
    /// grade's colour on a press, red on a miss. <see cref="GameFlow.BoostQte"/>
    /// drives it; this class holds no rules.
    /// </summary>
    public class BoostQtePrompt : MonoBehaviour
    {
        const float CanvasUnits = 100f; // the canvas is 100 units wide, scaled to the glyph's world size
        const float HoldSeconds = 0.35f; // a result stays up this long, then fades
        const float FadeSeconds = 0.2f;

        Canvas canvas;
        CanvasGroup group;
        Image glyph;
        Text keyLabel;
        RectTransform body;

        Vector3 position;
        float worldSize;
        float urgency;         // 0 = just appeared, 1 = at the crossing
        bool showing;
        bool resolved;
        bool perfect;
        float resolvedAt;
        Color tint = Color.white;

        public static BoostQtePrompt Spawn()
        {
            var prompt = FindFirstObjectByType<BoostQtePrompt>();
            if (prompt == null) prompt = new GameObject("BoostQtePrompt").AddComponent<BoostQtePrompt>();
            prompt.Build();
            return prompt;
        }

        void Build()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);

            canvas = GetOrAdd<Canvas>(gameObject);
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(CanvasUnits, CanvasUnits);
            group = GetOrAdd<CanvasGroup>(gameObject);
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            body = (RectTransform)bodyGo.transform;
            body.SetParent(transform, false);
            body.sizeDelta = new Vector2(CanvasUnits, CanvasUnits);

            glyph = MenuScreen.MakeImage("Glyph", body, Vector2.zero, new Vector2(CanvasUnits, CanvasUnits), null, Color.white);
            glyph.preserveAspect = true;
            glyph.raycastTarget = false;

            var theme = MenuTheme.Load();
            keyLabel = MenuScreen.MakeText("Key", body, Vector2.zero, new Vector2(CanvasUnits * 3f, CanvasUnits),
                                           string.Empty, 60, Color.white, theme.BodyFont, TextAnchor.MiddleCenter);
            keyLabel.raycastTarget = false;

            ControlBindings.Changed -= RefreshBinding; // Build can run twice — never subscribe twice
            ControlBindings.Changed += RefreshBinding;
            RefreshBinding();
        }

        void OnDestroy() => ControlBindings.Changed -= RefreshBinding;

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        bool usingPad;

        // The Boost binding off the glyph set: the white pad glyph while a
        // pad is connected, the key cap otherwise, a text label when there is no art.
        void RefreshBinding()
        {
            if (glyph == null) return;
            usingPad = Gamepad.current != null;
            var glyphs = ControlGlyphSet.Load();
            PadControl pad = ControlBindings.PadFor(GameAction.ShipBoost);
            Key key = ControlBindings.KeyFor(GameAction.ShipBoost);
            Sprite sprite = glyphs == null ? null : usingPad ? glyphs.ForMono(pad) : glyphs.For(key);

            glyph.sprite = sprite;
            glyph.enabled = sprite != null;
            keyLabel.text = sprite != null ? string.Empty : usingPad ? ControlGlyphSet.Label(pad) : ControlGlyphSet.Label(key);
            keyLabel.enabled = sprite == null;
        }

        /// <summary>Waiting for the press: follow the ring's centre, grow as the crossing nears (0..1).</summary>
        public void Track(Vector3 worldPosition, float size, float approach)
        {
            if (!showing || resolved)
            {
                showing = true;
                resolved = false;
                perfect = false;
                tint = Color.white;
                RefreshBinding();
            }
            position = worldPosition;
            worldSize = size;
            urgency = Mathf.Clamp01(approach);
        }

        /// <summary>Move a shown result along with its ring (the orb may be gone; keep the last spot then).</summary>
        public void Follow(Vector3 worldPosition, float size)
        {
            position = worldPosition;
            worldSize = size;
        }

        /// <summary>Show the verdict in <paramref name="color"/>; held briefly, then faded.</summary>
        public void Resolve(Color color, bool isPerfect)
        {
            if (!showing) return;
            resolved = true;
            perfect = isPerfect;
            tint = color;
            resolvedAt = Time.time;
        }

        /// <summary>The prompt's orb left the race without a verdict (steered past, fell): fade out, no colour.</summary>
        public void Drop()
        {
            if (!showing || resolved) return;
            resolved = true;
            resolvedAt = Time.time - HoldSeconds; // straight to the fade
        }

        /// <summary>True while the prompt is waiting for a press (not a held result).</summary>
        public bool Waiting => showing && !resolved;

        void LateUpdate()
        {
            if (group == null) return;
            if (usingPad != (Gamepad.current != null)) RefreshBinding();

            if (!showing)
            {
                group.alpha = 0f;
                return;
            }

            float alpha = 1f;
            float scale = 1f;
            if (resolved)
            {
                float since = Time.time - resolvedAt;
                alpha = 1f - Mathf.Clamp01((since - HoldSeconds) / FadeSeconds);
                // A pop on the verdict — bigger for a perfect press.
                float pop = Mathf.Clamp01(since / 0.12f);
                scale = 1f + (perfect ? 0.45f : 0.2f) * Mathf.Sin(pop * Mathf.PI) + (perfect ? 0.15f * pop : 0f);
                if (alpha <= 0f)
                {
                    showing = false;
                    group.alpha = 0f;
                    return;
                }
            }
            else
            {
                // Grows toward the crossing, with a quickening pulse.
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(8f, 22f, urgency));
                scale = Mathf.Lerp(0.75f, 1f, urgency) + 0.06f * pulse;
                alpha = Mathf.Lerp(0.55f, 1f, urgency);
            }

            group.alpha = alpha;
            glyph.color = tint;
            keyLabel.color = tint;
            body.localScale = Vector3.one * scale;

            transform.position = position;
            transform.localScale = Vector3.one * (worldSize / CanvasUnits);
            var cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
        }
    }
}
