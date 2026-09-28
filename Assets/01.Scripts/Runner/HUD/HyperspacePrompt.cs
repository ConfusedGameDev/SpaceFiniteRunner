using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// The HIT HYPERSPACE call: a pulsing caption over the chord that jumps
    /// the ship to the end of the track — the two dash shoulders (LB + RB,
    /// the player's LIVE <see cref="GameAction.ShipDashLeft"/> /
    /// <see cref="GameAction.ShipDashRight"/> pad binding) while a pad is
    /// present, the keyboard chord (Q + E, <see cref="GameAction.ShipHyperspaceLeft"/>
    /// / <see cref="GameAction.ShipHyperspaceRight"/>) otherwise — Kenney
    /// glyphs off the <see cref="ControlGlyphSet"/>, bracketed key names when
    /// the set has no art, re-read on every rebind. Holds no rule: the
    /// <see cref="HyperspaceJump"/> decides when it shows
    /// (<see cref="SetVisible"/>). The dash prompt's pattern: hand-placed in
    /// <c>PF_UI</c>, <see cref="Spawn"/> finds and binds it and builds the UI
    /// from code on its own overlay canvas (sorting 12, with the dash and duel
    /// prompts), above them on screen. Device choice is the in-run presence
    /// rule (<see cref="DuelMashInput.UsingGamepad"/>, polled for hot-plug).
    /// Hidden while the sim is paused.
    /// </summary>
    public class HyperspacePrompt : MonoBehaviour
    {
        [Tooltip("Menu theme for fonts, colours and the pulse; empty falls back to the Resources default.")]
        [SerializeField, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        MenuTheme themeOverride;

        [Tooltip("Height of the prompt above the bottom of the 1080p reference screen — clear of the story box (its top is ~255), the dash hint (170) and the duel bar (210).")]
        [SerializeField, PropertyRange(100f, 700f)]
        float heightFromBottom = 420f;

        ShipMotor motor;
        MenuTheme theme;

        RectTransform holder;
        CanvasGroup group;
        Image leftGlyph;
        Image rightGlyph;
        Text leftKey;
        Text rightKey;
        Text caption;

        bool visible;
        float pulseTime;
        float popTime = float.MaxValue;

        /// <summary>True while the prompt is asked to show (the sim may still hide it on a pause).</summary>
        public bool Visible => visible;

        /// <summary>Finds the hand-placed prompt (or makes one), binds it to the ship and builds its UI. Starts hidden.</summary>
        public static HyperspacePrompt Spawn(ShipMotor motor)
        {
            var prompt = FindFirstObjectByType<HyperspacePrompt>(FindObjectsInactive.Include);
            if (prompt == null) prompt = new GameObject("HyperspacePrompt").AddComponent<HyperspacePrompt>();
            if (!prompt.gameObject.activeSelf) prompt.gameObject.SetActive(true);
            prompt.motor = motor;
            prompt.Build();
            return prompt;
        }

        /// <summary>Show or hide the call. Showing pops it in; asking twice changes nothing.</summary>
        public void SetVisible(bool show)
        {
            if (show == visible) return;
            visible = show;
            if (show)
            {
                pulseTime = 0f;
                popTime = 0f;
            }
        }

        /// <summary>A retry: hidden, nothing mid-pop.</summary>
        public void ResetForRun()
        {
            visible = false;
            popTime = float.MaxValue;
            if (group != null) group.alpha = 0f;
        }

        void Awake()
        {
            // Scene-placed instance whose Spawn never came (the jump is off):
            // drop the baked preview so no dead prompt lingers on screen.
            if (motor == null) TearDown();
        }

        /// <summary>Editor bake: regenerates the prompt preview (fully visible) so the prefab shows before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            Build();
            group.alpha = 1f;
        }

        void OnDestroy() => ControlBindings.Changed -= RefreshBinding;

        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            holder = null;
            group = null;
            leftGlyph = rightGlyph = null;
            leftKey = rightKey = null;
            caption = null;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        void Build()
        {
            TearDown();
            theme = themeOverride != null ? themeOverride : MenuTheme.Load();

            var canvas = GetOrAdd<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12; // with the dash and duel prompts: above the HUD, below the RPG box

            var scaler = GetOrAdd<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var go = new GameObject("Prompt", typeof(RectTransform));
            holder = (RectTransform)go.transform;
            holder.SetParent(transform, false);
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0f);
            holder.pivot = new Vector2(0.5f, 0.5f);
            holder.anchoredPosition = new Vector2(0f, heightFromBottom);
            holder.sizeDelta = new Vector2(900f, 140f);
            group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            caption = MenuScreen.MakeText("Caption", holder, new Vector2(0f, 34f), new Vector2(900f, 60f),
                                          MenuTextLibrary.Load().Get(MenuTextId.HyperspacePrompt), 44,
                                          theme.Accent, theme.TitleFont, TextAnchor.MiddleCenter);

            // Glyph row: LEFT + RIGHT. Each slot holds a glyph and its key
            // label; the device swap toggles which one is shown.
            leftGlyph = MenuScreen.MakeImage("GlyphL", holder, new Vector2(-80f, -32f), new Vector2(64f, 64f), null, Color.white);
            leftGlyph.preserveAspect = true;
            leftKey = MenuScreen.MakeText("KeyL", holder, new Vector2(-80f, -32f), new Vector2(140f, 56f),
                                          string.Empty, 34, theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);
            MenuScreen.MakeText("Plus", holder, new Vector2(0f, -32f), new Vector2(60f, 56f),
                                "+", 40, theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);
            rightGlyph = MenuScreen.MakeImage("GlyphR", holder, new Vector2(80f, -32f), new Vector2(64f, 64f), null, Color.white);
            rightGlyph.preserveAspect = true;
            rightKey = MenuScreen.MakeText("KeyR", holder, new Vector2(80f, -32f), new Vector2(140f, 56f),
                                           string.Empty, 34, theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);

            ControlBindings.Changed -= RefreshBinding; // Build runs again on a rebuild — never subscribe twice
            ControlBindings.Changed += RefreshBinding;
            RefreshBinding();
        }

        // The pad half is the dash shoulders, the keyboard half the two
        // hyperspace keys — both off the live bindings, so a rebind is taught.
        void RefreshBinding()
        {
            if (leftGlyph == null) return;
            RefreshDevice(force: true);
        }

        bool shownPad;
        bool deviceKnown;

        void RefreshDevice(bool force = false)
        {
            if (leftGlyph == null) return;
            bool pad = DuelMashInput.UsingGamepad;
            if (!force && deviceKnown && pad == shownPad) return;
            deviceKnown = true;
            shownPad = pad;

            var glyphs = ControlGlyphSet.Load();
            Sprite left, right;
            string leftLabel, rightLabel;
            if (pad)
            {
                PadControl l = ControlBindings.PadFor(GameAction.ShipDashLeft);
                PadControl r = ControlBindings.PadFor(GameAction.ShipDashRight);
                left = glyphs != null ? glyphs.For(l) : null;
                right = glyphs != null ? glyphs.For(r) : null;
                leftLabel = ControlGlyphSet.Label(l);
                rightLabel = ControlGlyphSet.Label(r);
            }
            else
            {
                var l = ControlBindings.KeyFor(GameAction.ShipHyperspaceLeft);
                var r = ControlBindings.KeyFor(GameAction.ShipHyperspaceRight);
                left = glyphs != null ? glyphs.For(l) : null;
                right = glyphs != null ? glyphs.For(r) : null;
                leftLabel = ControlGlyphSet.Label(l);
                rightLabel = ControlGlyphSet.Label(r);
            }

            bool art = left != null && right != null;
            leftGlyph.sprite = left;
            rightGlyph.sprite = right;
            leftKey.text = $"[{leftLabel}]";
            rightKey.text = $"[{rightLabel}]";
            leftGlyph.gameObject.SetActive(art);
            rightGlyph.gameObject.SetActive(art);
            leftKey.gameObject.SetActive(!art);
            rightKey.gameObject.SetActive(!art);
        }

        void Update()
        {
            if (group == null || motor == null) return;
            RefreshDevice();

            if (visible && !motor.Paused)
            {
                // The attract screen's breathing pulse, plus a pop on arrival.
                pulseTime += Time.unscaledDeltaTime;
                float wave = 0.5f + 0.5f * Mathf.Sin(pulseTime * Mathf.PI * 2f / Mathf.Max(0.1f, theme.AttractPulseSeconds));
                group.alpha = Mathf.Lerp(theme.AttractPulseMin, theme.AttractPulseMax, wave);

                popTime += Time.unscaledDeltaTime;
                float pop = Mathf.Clamp01(popTime / 0.25f);
                float scale = pop < 1f ? Mathf.Lerp(1.6f, 1f, 1f - (1f - pop) * (1f - pop)) : 1f;
                holder.localScale = new Vector3(scale, scale, 1f);
            }
            else group.alpha = 0f;
        }
    }
}
