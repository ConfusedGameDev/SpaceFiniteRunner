using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// Teaches the lateral dash without ever nagging early: nothing is shown
    /// until the meter fills for the first time (it starts each run empty),
    /// then a pulsing bottom-screen hint appears showing the LIVE dash
    /// binding (<see cref="ControlBindings"/>): the pad glyphs while a pad
    /// is connected, bracketed key names otherwise, swapped live like the
    /// main menu's attract prompt and re-read on every rebind. The first dash hides the hint; a meter left
    /// full for too long brings the hint back. It never speaks: the RPG box
    /// is for story beats, not tutorials. Spawned by GameManager, built from
    /// code on its own overlay canvas.
    /// </summary>
    public class DashPromptController : MonoBehaviour
    {
        [Tooltip("Game settings the hint text and message knobs come from; Spawn's argument overrides this.")]
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        GameSettings settingsAsset;

        [Tooltip("Menu theme for glyphs and fonts; empty falls back to the Resources default.")]
        [SerializeField, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        MenuTheme themeOverride;

        ShipMotor motor;
        GameSettings settings;
        MenuTheme theme;

        CanvasGroup hintGroup;
        Image leftGlyph;
        Image rightGlyph;
        Text leftKey;
        Text rightKey;

        bool firstFillSeen;
        bool hintVisible;
        float fullUnusedTimer;
        float pulseTime;

        public static DashPromptController Spawn(ShipMotor motor, GameSettings settings)
        {
            var prompt = FindFirstObjectByType<DashPromptController>();
            if (prompt == null) prompt = new GameObject("DashPrompt").AddComponent<DashPromptController>();
            prompt.motor = motor;
            prompt.settings = settings;
            prompt.Build();

            motor.MeterFilled += prompt.OnMeterFilled;
            motor.DashPerformed += prompt.OnDashPerformed;
            return prompt;
        }

        void Awake()
        {
            // Scene-placed instance whose Spawn never came (dash disabled):
            // drop the baked preview so no dead hint lingers on screen.
            if (motor == null) TearDown();
        }

        /// <summary>Editor bake: regenerates the hint preview (fully visible) so the prefab shows before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            Build();
            hintGroup.alpha = 1f;
        }

        // Root components are reused by Build — see RpgMessageSystem.TearDown.
        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            hintGroup = null;
            leftGlyph = rightGlyph = null;
            leftKey = rightKey = null;
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


        void OnDestroy()
        {
            ControlBindings.Changed -= RefreshBinding;
            if (motor == null) return;
            motor.MeterFilled -= OnMeterFilled;
            motor.DashPerformed -= OnDashPerformed;
        }

        void Build()
        {
            TearDown();
            if (settings == null) settings = settingsAsset;
            theme = themeOverride != null ? themeOverride : MenuTheme.Load();

            var canvas = GetOrAdd<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12; // above the HUD and minimap, below the RPG box

            var scaler = GetOrAdd<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // Bottom-center, clear of the RPG dialogue region.
            var holder = new GameObject("Hint", typeof(RectTransform));
            var rect = (RectTransform)holder.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 170f);
            rect.sizeDelta = new Vector2(760f, 60f);
            hintGroup = holder.AddComponent<CanvasGroup>();
            hintGroup.alpha = 0f;

            // Fixed slots left/caption/right; the device swap just toggles
            // which occupant of each slot is active (glyph vs key label).
            // What they show comes off the live dash bindings (RefreshBinding).
            leftGlyph = MenuScreen.MakeImage("GlyphL", rect, new Vector2(-300f, 0f), new Vector2(48f, 48f),
                                             null, Color.white);
            leftGlyph.preserveAspect = true;
            leftKey = MenuScreen.MakeText("KeyL", rect, new Vector2(-300f, 0f), new Vector2(240f, 48f),
                                          string.Empty, 30, theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);

            MenuScreen.MakeText("Caption", rect, Vector2.zero, new Vector2(480f, 48f),
                                settings != null ? settings.dashHintText : "DASH", 30, theme.TextPrimary, theme.BodyFont,
                                TextAnchor.MiddleCenter);

            rightGlyph = MenuScreen.MakeImage("GlyphR", rect, new Vector2(300f, 0f), new Vector2(48f, 48f),
                                              null, Color.white);
            rightGlyph.preserveAspect = true;
            rightKey = MenuScreen.MakeText("KeyR", rect, new Vector2(300f, 0f), new Vector2(240f, 48f),
                                           string.Empty, 30, theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);

            ControlBindings.Changed -= RefreshBinding; // Build runs again on a rebuild — never subscribe twice
            ControlBindings.Changed += RefreshBinding;
            RefreshBinding();
            RefreshDevice();
        }

        // The pad glyph and the key name of each dash direction, off the
        // ControlGlyphSet — so a player who moved the dash to other buttons
        // is taught the buttons they actually have.
        void RefreshBinding()
        {
            if (leftGlyph == null || rightGlyph == null) return;
            var glyphs = ControlGlyphSet.Load();
            leftGlyph.sprite = glyphs.For(ControlBindings.PadFor(GameAction.ShipDashLeft));
            rightGlyph.sprite = glyphs.For(ControlBindings.PadFor(GameAction.ShipDashRight));
            leftKey.text = $"[{ControlGlyphSet.Label(ControlBindings.KeyFor(GameAction.ShipDashLeft))}]";
            rightKey.text = $"[{ControlGlyphSet.Label(ControlBindings.KeyFor(GameAction.ShipDashRight))}]";
            RefreshDevice();
        }

        // Presence-based like the attract prompt: the moment a pad connects
        // the key labels become pad glyphs (when the set has art for the
        // bound controls), and unplugging swaps back.
        void RefreshDevice()
        {
            if (leftGlyph == null) return;
            bool pad = Gamepad.current != null && leftGlyph.sprite != null && rightGlyph.sprite != null;
            leftGlyph.gameObject.SetActive(pad);
            rightGlyph.gameObject.SetActive(pad);
            leftKey.gameObject.SetActive(!pad);
            rightKey.gameObject.SetActive(!pad);
        }

        void OnMeterFilled()
        {
            if (firstFillSeen) return;
            firstFillSeen = true;
            hintVisible = true;
        }

        void OnDashPerformed(int direction)
        {
            // Any dash proves the player got it — stop prompting.
            hintVisible = false;
            fullUnusedTimer = 0f;
        }

        /// <summary>Back to the untaught state — the hint shows again on the next run's first full meter.</summary>
        public void ResetForRun()
        {
            firstFillSeen = false;
            hintVisible = false;
            fullUnusedTimer = 0f;
        }

        void Update()
        {
            if (motor == null) return;

            // Nag timer: only while the meter sits full during live play.
            if (firstFillSeen && motor.DashMeter >= 1f && !motor.IsDashing && !motor.Paused)
            {
                fullUnusedTimer += Time.deltaTime;
                if (fullUnusedTimer >= settings.dashEncourageAfterSeconds)
                {
                    fullUnusedTimer = 0f; // re-arms: keeps nagging while ignored
                    hintVisible = true;
                }
            }
            else fullUnusedTimer = 0f;

            RefreshDevice();

            // Same pulse as the attract screen's PRESS START; invisible while
            // the sim is paused (tuning screen, pause menu, run over).
            if (hintVisible && !motor.Paused)
            {
                pulseTime += Time.deltaTime;
                float wave = 0.5f + 0.5f * Mathf.Sin(pulseTime * Mathf.PI * 2f / Mathf.Max(0.1f, theme.AttractPulseSeconds));
                hintGroup.alpha = Mathf.Lerp(theme.AttractPulseMin, theme.AttractPulseMax, wave);
            }
            else hintGroup.alpha = 0f;
        }
    }
}
