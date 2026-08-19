using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FiniteRunner
{
    /// <summary>
    /// Tabbed debug pages for the pause menu. Each tab is a normal
    /// <see cref="MenuScreen"/> (same rows, focus and slide language as the
    /// rest of the menus); the bumpers (or Q/E) cycle between tabs, so new
    /// debug pages are one <see cref="AddTab"/> call away. This class only
    /// tracks which tab is active — the PauseMenu owns input and transitions,
    /// exactly like it does for its other sub-screens.
    /// </summary>
    public class DebugMenu
    {
        readonly List<MenuScreen> tabs = new();
        int active;

        public MenuScreen Active => tabs.Count > 0 ? tabs[active] : null;
        public int Count => tabs.Count;

        public void AddTab(MenuScreen screen) => tabs.Add(screen);

        public bool Contains(MenuScreen screen) => screen != null && tabs.Contains(screen);

        /// <summary>Moves the active tab by <paramref name="step"/> (wraps) and returns it.</summary>
        public MenuScreen Cycle(int step)
        {
            active = ((active + step) % tabs.Count + tabs.Count) % tabs.Count;
            return tabs[active];
        }

        public void HideAllImmediate()
        {
            foreach (var tab in tabs) tab.HideImmediate();
        }

        /// <summary>-1 previous tab / +1 next tab this frame: LB/RB on pad, Q/E on keyboard.</summary>
        public static int TabStepPressed()
        {
            int step = 0;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.qKey.wasPressedThisFrame) step--;
                if (keyboard.eKey.wasPressedThisFrame) step++;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.leftShoulder.wasPressedThisFrame) step--;
                if (pad.rightShoulder.wasPressedThisFrame) step++;
            }
            return step;
        }

        /// <summary>
        /// Stamps the shared tab header on a tab screen: the tab's name plus
        /// the bumper hint, so every debug page advertises how to switch.
        /// </summary>
        public static void AddTabHeader(MenuScreen screen, MenuTheme theme, string title, int tabIndex, int tabCount)
        {
            screen.AddLabel("TabTitle", new Vector2(0f, 470f), new Vector2(1200f, 60f),
                            $"DEBUG — {title}", 44, theme.TextPrimary, theme.TitleFont,
                            TextAnchor.MiddleCenter, 0f);
            screen.AddLabel("TabHint", new Vector2(0f, 415f), new Vector2(900f, 40f),
                            $"LB ◀  TAB {tabIndex + 1}/{tabCount}  ▶ RB   (Q/E)", 24,
                            theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter, 0f);
        }
    }

    /// <summary>
    /// A debug slider row: an arbitrary float range in fixed steps, unlike
    /// <see cref="MenuSlider"/>'s fixed 0..100 volume contract. Left/Right
    /// (keyboard or pad) steps the value; confirm does nothing. Changes report
    /// out immediately so debug values apply live where they can.
    /// </summary>
    public class DebugSliderRow : MenuRow
    {
        const float TrackWidth = 220f;
        const float TrackHeight = 20f;
        const float TrackRightMargin = 104f;

        RectTransform track;
        UnityEngine.UI.Image fill;
        UnityEngine.UI.Text valueText;

        float min, max = 100f, step = 1f, value;
        string format = "0.#";
        System.Action<float> changed;
        Color? labelTint;

        /// <summary>Tints the row's label (e.g. a spawn entry's color) instead of the theme's text colors.</summary>
        public void SetLabelTint(Color color)
        {
            labelTint = color;
            ApplyFocus(true);
        }

        /// <summary>Updates the shown value without firing the change callback — for rebalancing sibling rows.</summary>
        public void SetWithoutNotify(float newValue) => SetValue(newValue, false);

        protected override void ApplyFocus(bool immediate)
        {
            base.ApplyFocus(immediate);
            if (labelTint.HasValue && label != null)
                label.color = Color.Lerp(Color.Lerp(labelTint.Value, theme.TextDim, 0.45f),
                                         labelTint.Value, Focus);
        }

        protected override void Build()
        {
            float right = rect.sizeDelta.x * 0.5f;

            var trackImage = MenuScreen.MakeImage("Track", rect,
                new Vector2(right - TrackRightMargin - TrackWidth * 0.5f, 0f),
                new Vector2(TrackWidth, TrackHeight), theme.SliderTrack, new Color(1f, 1f, 1f, 0.3f));
            track = trackImage.rectTransform;

            fill = MenuScreen.MakeImage("Fill", track, Vector2.zero, new Vector2(0f, TrackHeight),
                                        theme.SliderFill, theme.Accent);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = fillRect.anchorMax = new Vector2(0f, 0.5f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;

            valueText = MenuScreen.MakeText("Value", rect, new Vector2(right - TrackRightMargin * 0.5f, 0f),
                                            new Vector2(TrackRightMargin, rect.sizeDelta.y),
                                            "0", 24, theme.TextPrimary, theme.BodyFont, TextAnchor.MiddleCenter);
        }

        public void Configure(float min, float max, float step, float initial, string format,
                              System.Action<float> onChanged)
        {
            this.min = min;
            this.max = max;
            this.step = Mathf.Max(0.0001f, step);
            this.format = format;
            changed = onChanged;
            SetValue(initial, false);
        }

        public override bool Adjust(int direction)
        {
            float next = Mathf.Clamp(value + direction * step, min, max);
            if (Mathf.Approximately(next, value)) return false;
            SetValue(next, true);
            return true;
        }

        // Confirm on a slider does nothing — Left/Right is how it is changed.
        public override void Activate() { }

        void SetValue(float raw, bool notify)
        {
            value = Mathf.Clamp(raw, min, max);

            float t = Mathf.InverseLerp(min, max, value);
            if (fill != null)
            {
                fill.rectTransform.sizeDelta = new Vector2(TrackWidth * t, TrackHeight);
                fill.enabled = t > 0.02f; // a sliced sprite cannot draw thinner than its caps
            }
            if (valueText != null) valueText.text = value.ToString(format);

            if (notify) changed?.Invoke(value);
        }
    }

    /// <summary>
    /// Builders for the individual debug tabs. The Core Settings tab edits the
    /// TrackGenerator live (spawn table takes effect while streaming; width and
    /// straightness need a rebuild) and its RELOAD SCENE row snapshots the
    /// values into <see cref="TrackDebugSettings"/> before reloading, so the
    /// fresh scene comes up with the tweaked track.
    /// </summary>
    public static class DebugMenuFactory
    {
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const float ContentTop = 340f;

        public static MenuScreen BuildCoreSettingsTab(RectTransform parent, MenuTheme theme,
                                                      TrackGenerator generator, TrackDebugSettings saved,
                                                      System.Action reloadScene, int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_CoreSettings", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, "CORE SETTINGS", tabIndex, tabCount);

            screen.AddRow<DebugSliderRow>("TRACK WIDTH")
                  .Configure(10f, 120f, 5f, generator.TrackWidth, "0",
                             v => { generator.TrackWidth = v; saved.CaptureFrom(generator); });
            screen.AddRow<DebugSliderRow>("STRAIGHTNESS")
                  .Configure(0f, 100f, 5f, generator.Straightness, "0",
                             v => { generator.Straightness = v; saved.CaptureFrom(generator); });

            // One color-tinted percentage slider per spawn entry. Adjusting one
            // rebalances the others live, so the on-screen table always adds
            // up to exactly 100% — same rule as the inspector's spawn table.
            var table = generator.SpawnTable;
            if (table != null)
            {
                var probabilityRows = new List<DebugSliderRow>();
                for (int i = 0; i < table.Length; i++)
                {
                    int index = i;
                    var entry = table[i];
                    var row = screen.AddRow<DebugSliderRow>($"{entry.name.ToUpperInvariant()} %");
                    row.Configure(0f, 100f, 1f, entry.probability, "0", v =>
                    {
                        RebalanceProbabilities(table, probabilityRows, index, v);
                        saved.CaptureFrom(generator);
                    });
                    row.SetLabelTint(entry.color);
                    probabilityRows.Add(row);
                }
            }

            screen.AddRow<MenuRow>("RELOAD SCENE").Activated += () => reloadScene?.Invoke();
            return screen;
        }

        /// <summary>
        /// Second tab: one color-tinted boost-multiplier slider per spawn
        /// entry (× GameSettings.powerUpSpeedBoost). Applies live to newly
        /// spawned orbs and is saved with the rest of the debug values.
        /// </summary>
        public static MenuScreen BuildMultipliersTab(RectTransform parent, MenuTheme theme,
                                                     TrackGenerator generator, TrackDebugSettings saved,
                                                     int tabIndex, int tabCount)
        {
            var screen = MenuScreen.Create("Debug_Multipliers", parent, theme, 0f, ContentTop);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            DebugMenu.AddTabHeader(screen, theme, "MULTIPLIERS", tabIndex, tabCount);

            var table = generator.SpawnTable;
            if (table != null)
            {
                foreach (var entry in table)
                {
                    var captured = entry;
                    var row = screen.AddRow<DebugSliderRow>($"{captured.name.ToUpperInvariant()} ×");
                    row.Configure(0.1f, 10f, 0.1f, Mathf.Clamp(captured.multiplier, 0.1f, 10f), "0.0", v =>
                    {
                        captured.multiplier = v;
                        saved.CaptureFrom(generator);
                    });
                    row.SetLabelTint(captured.color);
                }
            }

            return screen;
        }

        // The moved slider keeps its value; every other entry scales into the
        // remainder (evenly when they were all zero) and its row's fill and
        // readout refresh without re-firing callbacks.
        static void RebalanceProbabilities(TrackGenerator.PadSpawnEntry[] table,
                                           List<DebugSliderRow> rows, int changed, float value)
        {
            float kept = Mathf.Clamp(value, 0f, 100f);
            table[changed].probability = kept;

            float othersSum = 0f;
            for (int i = 0; i < table.Length; i++)
                if (i != changed) othersSum += table[i].probability;

            float remainder = 100f - kept;
            for (int i = 0; i < table.Length; i++)
            {
                if (i == changed) continue;
                table[i].probability = othersSum > 0f
                    ? table[i].probability * remainder / othersSum
                    : remainder / (table.Length - 1);
                if (i < rows.Count) rows[i].SetWithoutNotify(table[i].probability);
            }
        }
    }
}
