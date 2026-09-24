using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.UI;

namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// The tug-of-war bar: a short horizontal strip low and centred on screen,
    /// with the patrol's colour pushing in from the side the patrol is actually
    /// on and the mash glyph at the end the player is pushing toward.
    ///
    /// Small on purpose. The bar is the contest but the ROAD is the stake — the
    /// patrol is shoving you toward a wall or an edge the whole time — so it
    /// has to be readable in peripheral vision rather than something you look
    /// at. Push direction is reinforced through the rumble channel for the same
    /// reason: you should be able to feel which way you are losing without
    /// taking your eyes off the track.
    ///
    /// Mirrored, always, so the meaning never changes: fill grows FROM the
    /// patrol's side, and the glyph sits opposite it. The model underneath
    /// knows nothing about sides — it tracks the patrol's progress from 0 to 1
    /// and this does the mirroring, because which way is "forward" is a
    /// presentation question.
    ///
    /// Built from code and spawned by the GameManager, like the rest of the
    /// runner's HUD. Hidden whenever the sim is paused.
    /// </summary>
    [DisallowMultipleComponent]
    public class DuelBarHud : MonoBehaviour
    {
        const float BarWidth = 420f;
        const float BarHeight = 14f;
        const float BarBottom = 210f;   // above the dash hint's row, clear of the RPG box
        const float GlyphSize = 44f;
        const float GlyphGap = 40f;     // from the bar's end to the glyph's centre

        PolicePatrol patrol;
        ShipMotor motor;
        GameSettings settings;
        MenuTheme theme;
        ControlGlyphSet glyphs;

        CanvasGroup group;
        RectTransform fill;   // the patrol's share, anchored to its own side
        Image fillImage;
        Image glyphImage;
        Text glyphLabel;
        Text readout;
        RectTransform glyphRect;

        int shownSide;        // the side the layout is currently mirrored for
        bool shownGamepad;
        float rumbleCooldown;

        /// <summary>
        /// Spawns the bar under its own overlay canvas. Null patrol or a
        /// disabled duel simply means no bar — the caller need not check.
        /// </summary>
        public static DuelBarHud Spawn(ShipMotor motor, PolicePatrol patrol, GameSettings settings)
        {
            if (motor == null || patrol == null || settings == null || !settings.patrolDuelEnabled) return null;
            var go = new GameObject("DuelBarHud");
            var hud = go.AddComponent<DuelBarHud>();
            hud.motor = motor;
            hud.patrol = patrol;
            hud.settings = settings;
            hud.Build();
            return hud;
        }

        void Build()
        {
            theme = MenuTheme.Load();
            glyphs = ControlGlyphSet.Load();

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12; // with the dash prompt: above the HUD, below the RPG box

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var holder = new GameObject("Bar", typeof(RectTransform));
            var rect = (RectTransform)holder.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, BarBottom);
            rect.sizeDelta = new Vector2(BarWidth, BarHeight);
            group = holder.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            MenuScreen.MakeImage("Track", rect, Vector2.zero, new Vector2(BarWidth, BarHeight),
                                 null, new Color(0f, 0f, 0f, 0.55f));

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fill = (RectTransform)fillGo.transform;
            fill.SetParent(rect, false);
            fill.anchorMin = fill.anchorMax = new Vector2(0.5f, 0.5f);
            fill.pivot = new Vector2(0.5f, 0.5f);
            fill.sizeDelta = new Vector2(0f, BarHeight);
            fillImage = fillGo.AddComponent<Image>();
            fillImage.color = settings.duelBarColor;
            fillImage.raycastTarget = false;

            glyphImage = MenuScreen.MakeImage("Glyph", rect, Vector2.zero, new Vector2(GlyphSize, GlyphSize),
                                              null, Color.white);
            glyphImage.preserveAspect = true;
            glyphRect = glyphImage.rectTransform;
            glyphLabel = MenuScreen.MakeText("GlyphKey", rect, Vector2.zero, new Vector2(140f, GlyphSize),
                                             string.Empty, 28, theme.TextPrimary, theme.BodyFont,
                                             TextAnchor.MiddleCenter);

            // One word, because a bar with a glyph does not teach a player who
            // has never seen it that the answer is to hammer the button.
            MenuScreen.MakeText("Prompt", rect, new Vector2(0f, 26f), new Vector2(BarWidth, 26f),
                                MenuTextLibrary.Load().Get(MenuTextId.DuelMashPrompt), 20,
                                theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);

            // Diagnostic line, off by default. Parented to the CANVAS rather
            // than the bar holder, so it survives the bar being hidden — the
            // whole reason it exists is to explain a contest that never opened.
            readout = MenuScreen.MakeText("Readout", transform, new Vector2(0f, -470f),
                                          new Vector2(1400f, 26f), string.Empty, 18,
                                          new Color(1f, 0.85f, 0.4f, 0.9f), theme.BodyFont,
                                          TextAnchor.MiddleCenter);
            readout.enabled = false;

            shownSide = 0;
            shownGamepad = !DuelMashInput.UsingGamepad; // force the first refresh
            RefreshDevice();
        }

        void Update()
        {
            if (patrol == null || motor == null || settings == null) return;

            bool debug = settings.duelDebugReadout && !motor.Paused;
            if (readout.enabled != debug) readout.enabled = debug;
            if (debug) readout.text = patrol.EncounterDebug();

            bool visible = patrol.InTugOfWar && !motor.Paused && settings.patrolDuelEnabled;
            group.alpha = visible ? 1f : 0f;
            if (!visible)
            {
                shownSide = 0; // re-mirror on the next contest, whichever side it comes from
                return;
            }

            // The patrol's side decides the whole layout, and it cannot change
            // mid-contest, so this runs once per exchange rather than per frame.
            if (patrol.EncounterSide != shownSide) Mirror(patrol.EncounterSide);
            RefreshDevice();

            // The model counts the PATROL's progress; the fill is exactly that,
            // growing from the patrol's own side of the screen.
            float share = Mathf.Clamp01(patrol.Tug);
            float width = BarWidth * share;
            fill.sizeDelta = new Vector2(width, BarHeight);
            // Anchored so it grows away from the patrol's end, not from centre.
            fill.anchoredPosition = new Vector2(shownSide * (BarWidth - width) * 0.5f, 0f);

            Rumble(share);
        }

        // Lay the bar out for a patrol on the given side: the fill enters from
        // that side and the glyph sits at the far end — the direction the
        // player is pushing. One call per contest.
        void Mirror(int side)
        {
            shownSide = side == 0 ? 1 : side;
            float glyphX = -shownSide * (BarWidth * 0.5f + GlyphGap);
            glyphRect.anchoredPosition = new Vector2(glyphX, 0f);
            glyphLabel.rectTransform.anchoredPosition = new Vector2(glyphX, 0f);
        }

        // Pad glyph when a pad is present, the key's name otherwise — the same
        // presence rule the dash prompt uses, polled so a hot-plug mid-contest
        // swaps it. The mash is not bindable, so there is nothing to look up:
        // it is always X / gamepad X.
        void RefreshDevice()
        {
            bool pad = DuelMashInput.UsingGamepad;
            Sprite sprite = pad && glyphs != null ? glyphs.For(PadControl.ButtonWest) : null;
            if (pad == shownGamepad && (sprite == null) == (glyphImage.sprite == null)) return;
            shownGamepad = pad;

            glyphImage.sprite = sprite;
            glyphImage.enabled = sprite != null;
            glyphLabel.text = sprite != null ? string.Empty
                             : pad ? ControlGlyphSet.Label(PadControl.ButtonWest)
                                   : ControlGlyphSet.Label(DuelMashInput.MashKey);
            glyphLabel.enabled = sprite == null;
        }

        // Which way you are losing, felt rather than read. It rises with the
        // patrol's share so the bar bottoming out is the loudest thing that has
        // happened — the channel is not directional (no hardware here is), so
        // intensity carries it.
        void Rumble(float share)
        {
            rumbleCooldown -= Time.unscaledDeltaTime;
            if (rumbleCooldown > 0f) return;
            rumbleCooldown = 0.12f;
            HapticsSystem.Instance.Pulse(0.15f + 0.5f * share, 0.1f + 0.35f * share, 0.14f);
        }

    }
}
