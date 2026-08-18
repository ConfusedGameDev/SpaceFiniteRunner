using UnityEngine;
using UnityEngine.UI;

namespace FiniteRunner
{
    /// <summary>
    /// An ON/OFF row — currently only Subtitles. The Kenney Red kit ships no
    /// checkbox sprite, so the box is a small squared bar end and the tick is
    /// the crosshair sprite fading in on top of it; an ON/OFF word sits beside
    /// it so the state reads at a glance even before the art is final.
    ///
    /// Right turns it on, Left turns it off, Confirm flips it — all three so a
    /// player never has to guess which input a toggle wants.
    /// </summary>
    public class MenuToggle : MenuRow
    {
        const float BoxSize = 42f;
        const float BoxRightMargin = 150f;

        Image box;
        Image check;
        Text stateText;

        bool on = true;
        float checkAlpha;
        System.Action<bool> changed;

        public bool IsOn => on;

        protected override void Build()
        {
            float right = rect.sizeDelta.x * 0.5f;

            box = MenuScreen.MakeImage("Box", rect, new Vector2(right - BoxRightMargin, 0f),
                                       new Vector2(BoxSize, BoxSize), theme.ToggleBox, new Color(1f, 1f, 1f, 0.45f));

            check = MenuScreen.MakeImage("Check", box.rectTransform, Vector2.zero,
                                         new Vector2(BoxSize * 0.78f, BoxSize * 0.78f), theme.SelectionMarker, theme.Accent);

            stateText = MenuScreen.MakeText("State", rect, new Vector2(right - BoxRightMargin * 0.5f + 12f, 0f),
                                            new Vector2(BoxRightMargin, rect.sizeDelta.y),
                                            "ON", 28, theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);
        }

        public void Configure(bool initial, System.Action<bool> onChanged)
        {
            changed = onChanged;
            Set(initial, false);
            checkAlpha = on ? 1f : 0f;
        }

        public override bool Adjust(int direction)
        {
            bool next = direction > 0;
            if (next == on) return false;
            Set(next, true);
            return true;
        }

        public override void Activate()
        {
            Set(!on, true);
            base.Activate();
        }

        // The ON/OFF word is state-driven, so it can't ride on a LocalizedLabel
        // like static texts do — refresh it by hand when the language moves.
        void OnEnable()
        {
            UserSettings.LanguageChanged += OnLanguageChanged;
            RefreshStateText();
        }

        void OnDisable() => UserSettings.LanguageChanged -= OnLanguageChanged;

        void OnLanguageChanged(MenuLanguage _) => RefreshStateText();

        void RefreshStateText()
        {
            if (stateText != null)
                stateText.text = MenuTextLibrary.Load().Get(on ? MenuTextId.On : MenuTextId.Off);
        }

        protected override void Update()
        {
            base.Update();
            if (theme == null || check == null) return;

            checkAlpha = Mathf.MoveTowards(checkAlpha, on ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            var tint = theme.Accent;
            tint.a *= checkAlpha;
            check.color = tint;
        }

        void Set(bool value, bool notify)
        {
            on = value;
            RefreshStateText();
            if (notify) changed?.Invoke(on);
        }
    }
}
