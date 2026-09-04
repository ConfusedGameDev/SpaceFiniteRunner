using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.UI
{
    /// <summary>
    /// The CONTROLS page under SETTINGS, shared by the main menu and the pause
    /// menu (the <see cref="LogScreenFactory"/> recipe: compact rows, section
    /// headers, a scrolling viewport). One <see cref="BindingRow"/> per
    /// <see cref="GameAction"/> under SHIP / CAR / GENERAL headers, RESTORE
    /// DEFAULTS at the bottom, and a notice line under the list for the
    /// swap message. Left/Right pick the device column for the whole page,
    /// Confirm arms a <see cref="BindingCapture"/> for the focused row.
    ///
    /// The host keeps driving navigation; it calls <see cref="CaptureTick"/>
    /// every frame this page is current and STOPS for the frame when it
    /// returns true — while a capture is listening the navigator must not
    /// run (Space is Confirm, a d-pad direction is a step, Esc/B are the
    /// cancel rather than Back), and for a grace after a capture or cancel
    /// the press that just landed must not fire the menu either. The
    /// Confirm that armed the listen is never captured: the host ticks
    /// before it activates, and the capture ignores the grace after arming.
    /// Sits on the screen's root object like the cheats console, so
    /// OnEnable/OnDisable follow the page and a page hidden mid-listen never
    /// stays armed.
    /// </summary>
    public class ControlsScreen : MonoBehaviour
    {
        // Rows at 330 → -166 (9 × 62) leave room for the notice line above
        // the footer strip; the title plate sits at contentTop + 150.
        const float ContentTop = 330f;
        const float RowHeight = 54f;
        const float RowSpacing = 8f;
        const int VisibleRows = 9;
        const float NoticeY = -250f;
        const float NoticeSeconds = 2.5f;

        MenuTheme theme;
        MenuScreen screen;
        readonly List<BindingRow> rows = new();
        Text notice;
        float noticeUntil;

        int column; // 0 keyboard, 1 gamepad — one column for the whole page
        BindingRow armed;
        readonly BindingCapture capture = new();
        float blockUntil;

        /// <summary>The page itself — what the host slides in and out.</summary>
        public MenuScreen Screen => screen;

        /// <summary>True while a row waits for a press.</summary>
        public bool Listening => capture.Listening;

        /// <summary>A binding landed (the host blips its confirm).</summary>
        public event System.Action Captured;

        /// <summary>A listen was cancelled (the host blips its back).</summary>
        public event System.Action Cancelled;

        /// <summary>Creates the page (hidden) with every action's row.</summary>
        public static ControlsScreen Build(RectTransform parent, MenuTheme theme)
        {
            var screen = MenuScreen.Create("ControlsScreen", parent, theme, 0f, ContentTop);
            screen.SetTitle(MenuTextId.Controls);
            screen.SetRowMetrics(RowHeight, RowSpacing);
            screen.SetViewport(VisibleRows);

            var controls = screen.gameObject.AddComponent<ControlsScreen>();
            controls.theme = theme;
            controls.screen = screen;
            controls.Populate();
            return controls;
        }

        void Populate()
        {
            screen.ClearRows();
            rows.Clear();

            BindingSection? section = null;
            foreach (var action in ControlBindings.Actions)
            {
                var actionSection = ControlBindings.SectionOf(action);
                if (section != actionSection)
                {
                    section = actionSection;
                    screen.AddRow<StatHeaderRow>(SectionLabel(actionSection));
                }

                var row = screen.AddRow<BindingRow>(LabelIdFor(action));
                row.Configure(action, this);
                rows.Add(row);
            }

            screen.AddRow<MenuRow>(MenuTextId.RestoreDefaults).Activated += () =>
            {
                ControlBindings.ResetDefaults();
                Notice(MenuTextLibrary.Load().Get(MenuTextId.DefaultsRestored));
            };

            notice = screen.AddLabel("Notice", new Vector2(0f, NoticeY), new Vector2(1200f, 40f), string.Empty,
                                     26, theme.Accent, theme.BodyFont, TextAnchor.MiddleCenter, theme.TitleLead);

            screen.SetFocus(0); // lands on the first action; the header above it stays in view
            SetColumn(column);
        }

        void OnEnable()
        {
            // Open on the device the player last touched — the column they
            // most likely came to change.
            SetColumn(InputPromptBinder.Device == PromptDevice.Gamepad ? 1 : 0);
            Refresh();
            ControlBindings.Changed += Refresh;
            UserSettings.LanguageChanged += OnLanguageChanged;
        }

        void OnDisable()
        {
            ControlBindings.Changed -= Refresh;
            UserSettings.LanguageChanged -= OnLanguageChanged;
            CancelListening();
            blockUntil = 0f;
            HideNotice();
        }

        void Update()
        {
            if (notice != null && noticeUntil > 0f && Time.unscaledTime >= noticeUntil) HideNotice();
        }

        /// <summary>Every row re-reads its bindings.</summary>
        public void Refresh()
        {
            foreach (var row in rows) row.Refresh();
        }

        /// <summary>Left/Right from any row: moves the page's column. True when it actually moved.</summary>
        public bool StepColumn(int direction)
        {
            if (capture.Listening) return false;
            int next = Mathf.Clamp(column + direction, 0, 1);
            if (next == column) return false;
            SetColumn(next);
            return true;
        }

        void SetColumn(int index)
        {
            column = index;
            foreach (var row in rows) row.SetColumn(column);
        }

        /// <summary>Confirm on a row: start listening for the selected device.</summary>
        public void BeginCapture(BindingRow row)
        {
            if (capture.Listening || row == null) return;

            var device = column == 1 ? BindingDevice.Gamepad : BindingDevice.Keyboard;
            if (device == BindingDevice.Gamepad && Gamepad.current == null)
            {
                Notice(MenuTextLibrary.Load().Get(MenuTextId.NoGamepad));
                return;
            }

            armed = row;
            capture.Arm(device);
            row.SetListening(true, ListeningText(device));
        }

        /// <summary>
        /// One frame of the page's own input. Returns true when the frame
        /// belongs to the capture — listening, or inside the grace after a
        /// capture or cancel — and the host must neither navigate nor Back.
        /// </summary>
        public bool CaptureTick()
        {
            if (Time.unscaledTime < blockUntil) return true;
            if (!capture.Listening) return false;

            bool padGone = capture.Device == BindingDevice.Gamepad && Gamepad.current == null;
            if (MenuNavigator.BackPressed() || MenuNavigator.PauseTogglePressed() || padGone)
            {
                CancelListening();
                blockUntil = Time.unscaledTime + theme.InputGrace;
                Cancelled?.Invoke();
                return true;
            }

            if (capture.TryRead(theme.InputGrace, out Key key, out PadControl pad))
            {
                var action = armed.Action;
                GameAction? swapped = capture.Device == BindingDevice.Keyboard
                    ? ControlBindings.Set(action, key)
                    : ControlBindings.Set(action, pad);

                armed.SetListening(false, null);
                armed = null;
                blockUntil = Time.unscaledTime + theme.InputGrace;

                if (swapped.HasValue)
                {
                    var library = MenuTextLibrary.Load();
                    Notice(string.Format(library.Get(MenuTextId.SwappedWith), library.Get(LabelIdFor(swapped.Value))));
                }
                Refresh(); // Changed already refreshed on a real change; a no-op Set still needs the row back
                Captured?.Invoke();
                return true;
            }

            return true;
        }

        void CancelListening()
        {
            if (!capture.Listening && armed == null) return;
            capture.Cancel();
            if (armed != null) armed.SetListening(false, null);
            armed = null;
        }

        void OnLanguageChanged(MenuLanguage language)
        {
            if (armed != null && capture.Listening) armed.SetListening(true, ListeningText(capture.Device));
        }

        static string ListeningText(BindingDevice device) =>
            MenuTextLibrary.Load().Get(device == BindingDevice.Keyboard ? MenuTextId.PressKey : MenuTextId.PressButton);

        void Notice(string text)
        {
            if (notice == null) return;
            notice.text = text;
            noticeUntil = Time.unscaledTime + NoticeSeconds;
        }

        void HideNotice()
        {
            if (notice != null) notice.text = string.Empty;
            noticeUntil = 0f;
        }

        static MenuTextId SectionLabel(BindingSection section) => section switch
        {
            BindingSection.Ship => MenuTextId.ControlsSectionShip,
            BindingSection.Car => MenuTextId.ControlsSectionCar,
            _ => MenuTextId.ControlsSectionGeneral
        };

        /// <summary>The row label of an action — also the name the swap notice prints.</summary>
        public static MenuTextId LabelIdFor(GameAction action) => action switch
        {
            GameAction.ShipSteerLeft => MenuTextId.ActionSteerLeft,
            GameAction.ShipSteerRight => MenuTextId.ActionSteerRight,
            GameAction.ShipDashLeft => MenuTextId.ActionDashLeft,
            GameAction.ShipDashRight => MenuTextId.ActionDashRight,
            GameAction.CarSteerLeft => MenuTextId.ActionSteerLeft,
            GameAction.CarSteerRight => MenuTextId.ActionSteerRight,
            GameAction.CarAccelerate => MenuTextId.ActionAccelerate,
            GameAction.CarBrake => MenuTextId.ActionBrake,
            GameAction.CarHandbrake => MenuTextId.ActionHandbrake,
            GameAction.CarRespawn => MenuTextId.ActionRespawn,
            GameAction.CityMap => MenuTextId.ActionCityMap,
            GameAction.RadioPrevious => MenuTextId.ActionRadioPrevious,
            GameAction.RadioNext => MenuTextId.ActionRadioNext,
            GameAction.CameraCycle => MenuTextId.ActionCameraCycle,
            GameAction.CameraLookBack => MenuTextId.ActionLookBack,
            GameAction.CameraPanLeft => MenuTextId.ActionCameraLeft,
            GameAction.CameraPanRight => MenuTextId.ActionCameraRight,
            GameAction.CameraPanUp => MenuTextId.ActionCameraUp,
            GameAction.CameraPanDown => MenuTextId.ActionCameraDown,
            _ => MenuTextId.Controls
        };
    }
}
