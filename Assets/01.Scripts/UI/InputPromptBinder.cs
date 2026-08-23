using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>Which family of hardware the on-screen prompts should be drawn for.</summary>
    public enum PromptDevice { KeyboardMouse, Gamepad }

    /// <summary>The four things a menu prompt can ask the player to do.</summary>
    public enum PromptAction { Navigate, Adjust, Confirm, Back }

    /// <summary>
    /// Tracks which device the player last actually touched and hands out the
    /// matching prompt art: Xbox glyph sprites for the pad, plain key names for
    /// keyboard and mouse. Everything that draws a prompt goes through here, so
    /// swapping the Xbox PNGs for an inline glyph font later is a change to this
    /// one file.
    ///
    /// The project has no .inputactions asset and every existing script polls
    /// Gamepad.current / Keyboard.current directly (PauseMenu, TuningScreen);
    /// this follows that pattern rather than introducing an action asset just
    /// for the menu.
    /// </summary>
    public static class InputPromptBinder
    {
        public static PromptDevice Device { get; private set; } = PromptDevice.KeyboardMouse;

        /// <summary>Raised when the player switches between pad and keyboard/mouse.</summary>
        public static event System.Action<PromptDevice> DeviceChanged;

        /// <summary>Call once per frame from whoever owns the menu.</summary>
        public static void Poll()
        {
            if (GamepadActive()) Set(PromptDevice.Gamepad);
            else if (KeyboardOrMouseActive()) Set(PromptDevice.KeyboardMouse);
        }

        public static Sprite Glyph(MenuTheme theme, PromptAction action) => action switch
        {
            PromptAction.Confirm => theme.GlyphConfirm,
            PromptAction.Back => theme.GlyphBack,
            PromptAction.Navigate => theme.GlyphNavigate,
            PromptAction.Adjust => theme.GlyphAdjust,
            _ => null
        };

        public static string KeyLabel(PromptAction action) => action switch
        {
            PromptAction.Confirm => "[ENTER]",
            PromptAction.Back => "[ESC]",
            PromptAction.Navigate => "[W/S]",
            PromptAction.Adjust => "[A/D]",
            _ => string.Empty
        };

        static void Set(PromptDevice device)
        {
            if (Device == device) return;
            Device = device;
            DeviceChanged?.Invoke(device);
        }

        static bool GamepadActive()
        {
            var pad = Gamepad.current;
            if (pad == null) return false;

            if (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame ||
                pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame ||
                pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame ||
                pad.dpad.up.isPressed || pad.dpad.down.isPressed ||
                pad.dpad.left.isPressed || pad.dpad.right.isPressed)
                return true;

            return pad.leftStick.ReadValue().sqrMagnitude > 0.09f;
        }

        static bool KeyboardOrMouseActive()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;

            var mouse = Mouse.current;
            if (mouse == null) return false;
            return mouse.delta.ReadValue().sqrMagnitude > 4f ||
                   mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
        }
    }

    /// <summary>
    /// One "[glyph] CAPTION" prompt. Holds both representations and shows
    /// whichever matches the last-used device, so the footer re-labels itself
    /// the moment the player picks up a pad or touches the mouse.
    /// </summary>
    public class PromptHint : MonoBehaviour
    {
        const float GlyphSize = 40f;
        const int FontSize = 24;

        MenuTheme theme;
        PromptAction action;
        Image glyph;
        Text keyText;

        public CanvasGroup Group { get; private set; }

        public static PromptHint Create(RectTransform parent, MenuTheme theme, PromptAction action,
                                        MenuTextId captionId)
        {
            var go = new GameObject($"Hint_{action}", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 10f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var hint = go.AddComponent<PromptHint>();
            hint.theme = theme;
            hint.action = action;
            hint.Group = go.AddComponent<CanvasGroup>();

            hint.glyph = MenuScreen.MakeImage("Glyph", rect, Vector2.zero, new Vector2(GlyphSize, GlyphSize),
                                              InputPromptBinder.Glyph(theme, action), theme.TextPrimary);
            var element = hint.glyph.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.preferredHeight = GlyphSize;

            hint.keyText = MenuScreen.MakeText("Key", rect, Vector2.zero, new Vector2(0f, GlyphSize),
                                               InputPromptBinder.KeyLabel(action), FontSize,
                                               theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);

            var caption = MenuScreen.MakeText("Caption", rect, Vector2.zero, new Vector2(0f, GlyphSize),
                                              MenuTextLibrary.Load().Get(captionId), FontSize,
                                              theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter);
            LocalizedLabel.Bind(caption, captionId);

            hint.Refresh(InputPromptBinder.Device);
            return hint;
        }

        void OnEnable()
        {
            InputPromptBinder.DeviceChanged += Refresh;
            Refresh(InputPromptBinder.Device);
        }

        void OnDisable() => InputPromptBinder.DeviceChanged -= Refresh;

        void Refresh(PromptDevice device)
        {
            if (glyph == null || keyText == null) return;

            // A pad glyph the theme is missing would draw as a white box, so
            // fall back to the key label rather than lie about the art.
            bool usePad = device == PromptDevice.Gamepad &&
                          InputPromptBinder.Glyph(theme, action) != null;

            glyph.gameObject.SetActive(usePad);
            keyText.gameObject.SetActive(!usePad);
        }
    }

    /// <summary>
    /// The footer row of prompts. Rebuilt whenever the current screen changes,
    /// because each screen offers a different set of actions.
    /// </summary>
    public class PromptStrip : MonoBehaviour
    {
        MenuTheme theme;
        RectTransform rect;

        public static PromptStrip Create(RectTransform parent, MenuTheme theme, float bottomMargin)
        {
            var go = new GameObject("PromptStrip", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, bottomMargin);

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 46f;
            // The hints size themselves; this group just spaces them out.
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var strip = go.AddComponent<PromptStrip>();
            strip.theme = theme;
            strip.rect = rect;
            return strip;
        }

        public void SetHints(params (PromptAction action, MenuTextId caption)[] hints)
        {
            var stale = new List<GameObject>();
            foreach (Transform child in rect) stale.Add(child.gameObject);
            foreach (var go in stale)
            {
                // Unparent first: Destroy is deferred a frame and the old hints
                // would otherwise still be laid out beside the new ones.
                go.transform.SetParent(null, false);
                Destroy(go);
            }

            foreach (var hint in hints)
                PromptHint.Create(rect, theme, hint.action, hint.caption);
        }
    }
}
