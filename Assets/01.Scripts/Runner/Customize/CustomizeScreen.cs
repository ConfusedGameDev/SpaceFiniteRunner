using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Haptics;
using ConfusedGameDev.FiniteRunner.Livery;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.Customize
{
    /// <summary>
    /// The customize-vehicle test scene's controller. The vehicle turns on the
    /// <see cref="CustomizeStage"/> at the right; the left column holds one
    /// <see cref="HueSliderWidget"/> per paintable material.
    ///
    /// Controls (menus poll the devices directly — the project's menu rule):
    /// left stick Y picks a slider and X slides its hue; D-pad up / down swaps
    /// the vehicle (the column slides out left and back in with the other
    /// vehicle's sliders); right stick X turns the vehicle, Y zooms, its press
    /// resets the view. A asks SAVE?, B asks REVERT CHANGES? (back to the last
    /// save), Y asks RESET TO DEFAULT? (every part of every vehicle back to its
    /// authored colour — not saved until the next SAVE). Every prompt is
    /// YES / CANCEL with focus on CANCEL, and while one is up nothing else
    /// reads input. Start / Esc leaves for the main menu through the loading
    /// curtain. Keyboard: W/S or the up/down arrows pick, A/D or left/right slide, PgUp/PgDn swap,
    /// mouse drag turns, the wheel zooms, C resets the view, Enter saves,
    /// Backspace reverts, R resets.
    ///
    /// Edits live in a working copy of the saved <see cref="VehicleColorData"/>
    /// and reach <see cref="VehicleColorProfile"/> only on SAVE. Hand-placed
    /// under ===UI=== by the scene builder; the canvas is built at play.
    /// </summary>
    public class CustomizeScreen : MonoBehaviour
    {
        const int SortingOrder = 30;
        const int PromptSortingOrder = 35;
        const int UiLayer = 5;
        const float ColumnX = -520f;
        const float TitleY = 470f;
        const float VehicleNameY = 400f;
        const float HeaderY = 330f;
        const float SlidersTop = 250f;
        const float SliderSpacing = 104f;

        [SerializeField, Required] CustomizeStage stage;
        [SerializeField, Required] VehicleColorProfile profile;

        enum SwapPhase { None, Out, In }

        MenuTheme theme;
        MenuTextLibrary texts;
        MenuNavigator promptNav;
        RectTransform root;
        RectTransform column;
        CanvasGroup columnGroup;
        Text vehicleName;
        AudioSource ui;
        readonly List<HueSliderWidget> widgets = new();

        VehicleColorData working;
        int focus;
        float openedTime;
        bool leaving;

        SwapPhase swap;
        float swapTimer;
        int swapStep;

        int heldVertical;
        float repeatTimer;

        GameObject promptRoot;
        MenuScreen prompt;
        float promptOpenedTime;

        CustomizeSettings Settings => stage != null ? stage.Settings : null;

        void Start()
        {
            theme = MenuTheme.Load();
            texts = MenuTextLibrary.Load();
            promptNav = new MenuNavigator(theme);
            MenuScreenFactory.EnsureEventSystem();
            working = profile != null ? profile.LoadSaved() : new VehicleColorData();
            if (profile == null) Debug.LogError($"{nameof(CustomizeScreen)} has no {nameof(VehicleColorProfile)} — colours will not save.", this);
            Build();
            if (stage != null)
            {
                stage.Show(0, profile);
                stage.ResetView();
            }
            RebuildSliders();
            openedTime = Time.unscaledTime;
        }

        // ------------------------------------------------------------- build

        void Build()
        {
            gameObject.layer = UiLayer;
            var canvas = GetOrAdd<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = GetOrAdd<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            GetOrAdd<GraphicRaycaster>(gameObject);
            root = (RectTransform)transform;

            ui = GetOrAdd<AudioSource>(gameObject);
            ui.playOnAwake = false;
            ui.outputAudioMixerGroup = theme.UiOutput;

            MenuScreen.MakeText("Title", root, new Vector2(ColumnX, TitleY), new Vector2(900f, 60f),
                                texts.Get(MenuTextId.Customize), 44, Color.white, theme.TitleFont, TextAnchor.MiddleCenter);

            var columnGo = new GameObject("SliderColumn", typeof(RectTransform));
            column = (RectTransform)columnGo.transform;
            column.SetParent(root, false);
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(0.5f, 0.5f);
            column.anchoredPosition = new Vector2(ColumnX, 0f);
            column.sizeDelta = new Vector2(HueSliderWidget.Width, 1080f);
            columnGroup = columnGo.AddComponent<CanvasGroup>();
            columnGroup.interactable = columnGroup.blocksRaycasts = false;

            vehicleName = MenuScreen.MakeText("Vehicle", column, new Vector2(0f, VehicleNameY), new Vector2(900f, 50f),
                                              string.Empty, 36, theme.Accent, theme.TitleFont, TextAnchor.MiddleCenter);
            MenuScreen.MakeText("Header", column, new Vector2(0f, HeaderY), new Vector2(HueSliderWidget.Width, 40f),
                                texts.Get(MenuTextId.AdjustColor), 30, Color.white, theme.BodyFont, TextAnchor.MiddleLeft);

            MenuScreen.MakeText("Hints", root, new Vector2(0f, -490f), new Vector2(1800f, 40f), HintLine(), 22,
                                theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter);
        }

        // The footer: the glyph set has no Y / stick-press art, so the pad's
        // button letters are printed with the localized captions.
        string HintLine()
        {
            string H(string button, MenuTextId caption) => $"{button} {texts.Get(caption)}";
            return string.Join("     ",
                H("LS ▲▼", MenuTextId.HintSlider), H("LS ◀▶", MenuTextId.HintHue), H("D-PAD ▲▼", MenuTextId.HintVehicle),
                H("RS ◀▶", MenuTextId.HintRotate), H("RS ▲▼", MenuTextId.HintZoom), H("R3", MenuTextId.HintResetView),
                H("(A)", MenuTextId.HintSave), H("(B)", MenuTextId.HintRevert), H("(Y)", MenuTextId.HintDefault));
        }

        void RebuildSliders()
        {
            foreach (HueSliderWidget w in widgets) w.Destroy();
            widgets.Clear();

            CustomizeVehicleEntry entry = stage != null ? stage.Entry : null;
            vehicleName.text = entry != null && entry.model != null ? $"▲  {entry.model.displayName}  ▼" : string.Empty;

            VehiclePaintTarget target = stage != null ? stage.Current : null;
            if (target == null) return;
            ApplyWorking(target);
            for (int i = 0; i < target.Slots.Count; i++)
            {
                VehiclePaintTarget.PaintSlot slot = target.Slots[i];
                var widget = new HueSliderWidget(column, theme, new Vector2(0f, SlidersTop - i * SliderSpacing),
                                                 slot.Id.ToUpperInvariant());
                widgets.Add(widget);
                widget.SetValue(slot.Hue, slot.Tinted, slot.Current);
            }
            focus = Mathf.Clamp(focus, 0, Mathf.Max(0, widgets.Count - 1));
            RefreshFocus();
        }

        void ApplyWorking(VehiclePaintTarget target) => working.ApplyTo(target, VehicleId);

        void RefreshWidgets()
        {
            VehiclePaintTarget target = stage != null ? stage.Current : null;
            if (target == null) return;
            for (int i = 0; i < widgets.Count && i < target.Slots.Count; i++)
            {
                VehiclePaintTarget.PaintSlot slot = target.Slots[i];
                widgets[i].SetValue(slot.Hue, slot.Tinted, slot.Current);
            }
        }

        void RefreshFocus()
        {
            for (int i = 0; i < widgets.Count; i++) widgets[i].SetFocused(i == focus);
        }

        string VehicleId
        {
            get
            {
                CustomizeVehicleEntry entry = stage != null ? stage.Entry : null;
                return entry != null && entry.model != null ? entry.model.modelId : string.Empty;
            }
        }

        // ------------------------------------------------------------- input

        void Update()
        {
            if (theme == null || leaving) return;
            float dt = Time.unscaledDeltaTime;

            if (prompt != null)
            {
                UpdatePrompt(dt);
                return;
            }

            PollView(dt);

            if (swap != SwapPhase.None)
            {
                AnimateSwap(dt);
                return;
            }
            if (Time.unscaledTime - openedTime < theme.InputGrace) return;

            var pad = Gamepad.current;
            var keys = Keyboard.current;

            if (MenuNavigator.PauseTogglePressed())
            {
                Leave();
                return;
            }
            if (MenuNavigator.ConfirmPressed())
            {
                OpenPrompt(MenuTextId.SavePrompt, Save);
                return;
            }
            if ((pad != null && pad.buttonEast.wasPressedThisFrame) || (keys != null && keys.backspaceKey.wasPressedThisFrame))
            {
                OpenPrompt(MenuTextId.RevertPrompt, Revert);
                return;
            }
            if ((pad != null && pad.buttonNorth.wasPressedThisFrame) || (keys != null && keys.rKey.wasPressedThisFrame))
            {
                OpenPrompt(MenuTextId.ResetDefaultPrompt, ResetToDefault);
                return;
            }

            int vehicleStep = 0;
            if ((pad != null && pad.dpad.up.wasPressedThisFrame) || (keys != null && keys.pageUpKey.wasPressedThisFrame)) vehicleStep = -1;
            if ((pad != null && pad.dpad.down.wasPressedThisFrame) || (keys != null && keys.pageDownKey.wasPressedThisFrame)) vehicleStep = 1;
            if (vehicleStep != 0 && stage != null && stage.Count > 1)
            {
                BeginSwap(vehicleStep);
                return;
            }

            PollSliders(dt, pad, keys);
        }

        // Left stick Y (or W/S, ↑/↓) steps the focus with the menus' repeat;
        // left stick X (or A/D, ←/→) slides the hue continuously.
        void PollSliders(float dt, Gamepad pad, Keyboard keys)
        {
            CustomizeSettings s = Settings;
            if (s == null || widgets.Count == 0) return;

            Vector2 stick = pad != null ? pad.leftStick.ReadValue() : Vector2.zero;
            int vertical = stick.y > s.stickDeadZone ? 1 : stick.y < -s.stickDeadZone ? -1 : 0;
            float horizontal = Mathf.Abs(stick.x) > s.stickDeadZone ? stick.x : 0f;
            if (keys != null)
            {
                if (keys.wKey.isPressed || keys.upArrowKey.isPressed) vertical = 1;
                else if (keys.sKey.isPressed || keys.downArrowKey.isPressed) vertical = -1;
                if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) horizontal = -1f;
                else if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) horizontal = 1f;
            }

            if (vertical != heldVertical)
            {
                heldVertical = vertical;
                repeatTimer = theme.RepeatDelay;
                if (vertical != 0) MoveFocus(-vertical); // sliders run top-down, so up is index-1
            }
            else if (vertical != 0)
            {
                repeatTimer -= dt;
                if (repeatTimer <= 0f)
                {
                    repeatTimer = theme.RepeatInterval;
                    MoveFocus(-vertical);
                }
            }

            if (!Mathf.Approximately(horizontal, 0f)) SlideHue(horizontal * s.hueSpeed * dt);
        }

        void MoveFocus(int step)
        {
            int next = Mathf.Clamp(focus + step, 0, widgets.Count - 1);
            if (next == focus) return;
            focus = next;
            RefreshFocus();
            Blip(theme.MoveClip);
            HapticsSystem.Instance.Pulse(theme.MoveRumblePulse);
        }

        void SlideHue(float delta)
        {
            VehiclePaintTarget target = stage != null ? stage.Current : null;
            if (target == null || focus >= target.Slots.Count) return;
            VehiclePaintTarget.PaintSlot slot = target.Slots[focus];
            slot.SetHue(slot.Hue + delta);
            working.Set(VehicleId, slot.Id, slot.Hue, true);
            widgets[focus].SetValue(slot.Hue, slot.Tinted, slot.Current);
        }

        // Right stick X / a left-button drag off the UI turns the vehicle,
        // right stick Y / the wheel zooms, R3 / C resets the view. Live even
        // mid-swap, so the model never freezes under a transition.
        void PollView(float dt)
        {
            CustomizeSettings s = Settings;
            if (stage == null || s == null) return;
            float yaw = 0f, zoom = 0f;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (mouse.leftButton.isPressed && !overUi) yaw = mouse.delta.ReadValue().x * s.dragDegreesPerPixel;
                float scroll = mouse.scroll.ReadValue().y;
                if (!Mathf.Approximately(scroll, 0f)) zoom = -Mathf.Sign(scroll) * s.zoomPerScrollNotch;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 rs = pad.rightStick.ReadValue();
                if (Mathf.Approximately(yaw, 0f) && Mathf.Abs(rs.x) > s.stickDeadZone) yaw = rs.x * s.stickDegreesPerSecond * dt;
                if (Mathf.Approximately(zoom, 0f) && Mathf.Abs(rs.y) > s.stickDeadZone) zoom = -rs.y * s.zoomSpeed * dt; // push up = closer
                if (pad.rightStickButton.wasPressedThisFrame) ResetView();
            }
            var keys = Keyboard.current;
            if (keys != null && keys.cKey.wasPressedThisFrame) ResetView();

            stage.Nudge(yaw);
            stage.Zoom(zoom);
        }

        void ResetView()
        {
            stage.ResetView();
            Blip(theme.AdjustClip);
        }

        // ------------------------------------------------------------- swap

        void BeginSwap(int step)
        {
            swap = SwapPhase.Out;
            swapTimer = 0f;
            swapStep = step;
            Blip(theme.MoveClip);
            HapticsSystem.Instance.Pulse(theme.MoveRumblePulse);
        }

        // Out: the column eases away to the left and fades. At the bottom the
        // vehicle is swapped and the sliders rebuilt; In: they ease back.
        void AnimateSwap(float dt)
        {
            CustomizeSettings s = Settings;
            float seconds = s != null ? s.swapSlideSeconds : 0.2f;
            float distance = s != null ? s.swapSlideDistance : 900f;
            swapTimer += dt;
            float t = Mathf.Clamp01(swapTimer / Mathf.Max(0.01f, seconds));

            if (swap == SwapPhase.Out)
            {
                float eased = t * t;
                SetColumn(-distance * eased, 1f - t);
                if (t < 1f) return;
                stage.Show(stage.Index + swapStep, profile);
                focus = 0;
                RebuildSliders();
                swap = SwapPhase.In;
                swapTimer = 0f;
                return;
            }

            float easedIn = 1f - (1f - t) * (1f - t);
            SetColumn(-distance * (1f - easedIn), t);
            if (t < 1f) return;
            swap = SwapPhase.None;
            SetColumn(0f, 1f);
            heldVertical = 0;
        }

        void SetColumn(float offsetX, float alpha)
        {
            column.anchoredPosition = new Vector2(ColumnX + offsetX, 0f);
            columnGroup.alpha = alpha;
        }

        // ------------------------------------------------------------- prompts

        void OpenPrompt(MenuTextId question, System.Action onYes)
        {
            promptRoot = new GameObject("Prompt", typeof(RectTransform)) { layer = UiLayer };
            var rect = (RectTransform)promptRoot.transform;
            rect.SetParent(root, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var canvas = promptRoot.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = PromptSortingOrder;
            promptRoot.AddComponent<GraphicRaycaster>();

            var dim = promptRoot.AddComponent<Image>();
            Color dimColor = theme.Backdrop;
            dimColor.a = 0.8f;
            dim.color = dimColor;

            prompt = MenuScreenFactory.BuildConfirm(rect, theme, MenuTextId.Customize, question,
                                                    MenuTextId.Yes, MenuTextId.Cancel,
                                                    () => ClosePrompt(onYes), () => ClosePrompt(null));
            prompt.Show(staggered: false);
            promptOpenedTime = Time.unscaledTime;
            promptNav.Sync();
            Blip(theme.ConfirmClip);
        }

        void UpdatePrompt(float dt)
        {
            if (Time.unscaledTime - promptOpenedTime < theme.InputGrace) return;

            int vertical = promptNav.StepVertical(dt);
            if (vertical != 0)
            {
                prompt.MoveFocus(-vertical);
                Blip(theme.MoveClip);
                HapticsSystem.Instance.Pulse(theme.MoveRumblePulse);
            }
            if (MenuNavigator.ConfirmPressed()) prompt.Focused?.Activate();
            else if (MenuNavigator.BackPressed()) ClosePrompt(null);
        }

        void ClosePrompt(System.Action onYes)
        {
            if (promptRoot != null) Destroy(promptRoot);
            promptRoot = null;
            prompt = null;
            heldVertical = 0;
            // The press that answered must not reach the page this frame or the next.
            openedTime = Time.unscaledTime;
            if (onYes != null)
            {
                onYes();
                HapticsSystem.Instance.Pulse(theme.ConfirmRumble, theme.ConfirmRumble * 0.5f, 0.15f);
            }
            else Blip(theme.BackClip);
        }

        // ------------------------------------------------------------- actions

        void Save()
        {
            if (profile == null)
            {
                Debug.LogError($"{nameof(CustomizeScreen)}: no {nameof(VehicleColorProfile)} wired — nothing was saved. " +
                               "Re-run Tools → FiniteRunner → Create Customize Vehicle Scene to re-wire it.", this);
                Blip(theme.BackClip);
                return;
            }
            profile.Save(working);
            Blip(theme.ConfirmClip);
        }

        void Revert()
        {
            working = profile != null ? profile.LoadSaved() : new VehicleColorData();
            ReapplyCurrent();
            Blip(theme.ConfirmClip);
        }

        void ResetToDefault()
        {
            working.vehicles.Clear();
            ReapplyCurrent();
            Blip(theme.ConfirmClip);
        }

        void ReapplyCurrent()
        {
            VehiclePaintTarget target = stage != null ? stage.Current : null;
            if (target == null) return;
            ApplyWorking(target);
            RefreshWidgets();
        }

        void Leave()
        {
            if (leaving || LoadingScreen.IsLoading) return;
            leaving = true;
            Blip(theme.BackClip);
            LoadingScreen.LoadMainMenu();
        }

        void Blip(AudioClip clip)
        {
            if (clip != null && ui != null) ui.PlayOneShot(clip, theme.UiVolume);
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
