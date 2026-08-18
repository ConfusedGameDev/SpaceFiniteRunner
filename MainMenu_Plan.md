# Implementation Prompt — Space Finite Runner Main Menu

> Paste this whole file into Claude Code (or your agent of choice) with the project open.
> Everything below was verified against the real project on disk on 2026-08-18.

---

## 0. Task in one line

Build a controller-first Main Menu for **Space Finite Runner**: an attract screen that eases the menu in with a slow, smooth motion on first input, five entries (Start / Settings / Cheats / Credits / Exit), full gamepad + keyboard + mouse control, Xbox button prompts, and a new Audio Mixer driven from the Settings screen.

---

## 1. Project facts (already verified — do not re-investigate)

| Thing | Value |
|---|---|
| Unity | `6000.7.0a3` |
| Render pipeline | URP `17.7.0` |
| Input | `com.unity.inputsystem` `1.19.0` — **no `.inputactions` asset exists anywhere in the project** |
| UI | `com.unity.ugui` `2.6.0` (TextMeshPro ships inside it; `Assets/TextMesh Pro/` exists) |
| Odin Inspector | Present and used (`using Sirenix.OdinInspector;`) |
| Audio | **No AudioMixer asset exists anywhere in the project** |
| Namespace | `FiniteRunner` |
| Scripts live in | `Assets/99.Test/Jorge/FiniteRunner/Scripts/` |
| Scenes live in | `Assets/99.Test/Jorge/FiniteRunner/Scenes/` — only `FiniteRunner_Test.unity` exists |
| `Assets/01.Scripts/` and `Assets/05.Scenes/` | Empty. Ignore them. |

### Existing systems you must fit alongside (read them before writing anything)

- `GameManager.cs` — owns win/lose, spawns `PolicePatrol`, `ChaseMinimap`, and `PauseMenu` in `Awake()`.
- `GameSettings.cs` — `ScriptableObject` holding **balance** tunables (`CreateAssetMenu` → `FiniteRunner/Game Settings`). This is gameplay balance, **not** player preferences. Do not put volume/subtitles in here.
- `PauseMenu.cs` — the closest reference for style. It builds its entire UI **from code** on its own `ScreenSpaceOverlay` canvas (`sortingOrder = 20`), reads input by polling `Gamepad.current` / `Keyboard.current`, and is spawned by `GameManager` with no scene wiring.
- `TuningScreen.cs` — pre-run ship setup panel; uses serialized scene references and legacy `UnityEngine.UI.Text`. Its `Show()` is called on `Start()` and on `GameManager.Restart()`.
- `ShipMotor.cs`, `HapticsSystem.cs`, `RpgMessageSystem.cs`, `TrackGenerator.cs`.

### Conventions to match

- Namespace everything `FiniteRunner`.
- XML `<summary>` doc block on every class explaining *why* it exists, in the voice of the existing files.
- Prefer code-built UI on a self-owned canvas (the `PauseMenu.Spawn(...)` pattern) over hand-wired scene hierarchies — it is how this project does menus.
- Odin attributes (`[Title]`, `[PropertyRange]`, `[Required]`, `[InlineEditor]`) for anything a designer touches.
- Serialized fields private + `[SerializeField]`, not public.

---

## 2. Flow

```
Scene load
   │
   ▼
ATTRACT  ──  logo/title only + "PRESS  (A)  " prompt, gently pulsing
   │
   │  any gamepad button / any key / mouse click
   ▼
MENU IN  ──  slow, smooth eased entrance (see §4)
   │
   ▼
MAIN MENU  ── Start · Settings · Cheats · Credits · Exit
   │
   ├─ Start    → menu eases out, hand off to gameplay (see §3.1)
   ├─ Settings → sub-screen slides in from the right, menu slides left
   ├─ Cheats   → sub-screen, empty placeholder
   ├─ Credits  → sub-screen, static text
   └─ Exit     → confirm, then quit
```

`B` (East) backs out of any sub-screen to the Main Menu. `B` on the Main Menu does nothing (or returns to Attract — pick one and be consistent; do **not** quit from `B`).

---

## 3. Screens

### 3.1 Start
Leaves the menu and begins a run. The scene currently boots straight into `TuningScreen.Show()` from its own `Start()`. Reconcile this: the menu must be in front at boot and gameplay must not begin behind it.

Preferred approach — pick one, state which you chose, and keep it reversible:
- **A (recommended):** the menu lives on its own canvas above everything, sets `Time.timeScale = 0` / `motor.Paused = true` while open, and on Start hands control to `TuningScreen.Show()` (or `StartRun()` if `autoLaunch` is on). No new scene.
- **B:** a separate `MainMenu.unity` scene that additively/singly loads `FiniteRunner_Test.unity` on Start.

Whichever you pick, the pause menu (`Start` button during gameplay) must keep working unchanged.

### 3.2 Settings
Four rows, each row focusable and adjustable with Left/Right:

| Row | Control | Range / values | Writes to |
|---|---|---|---|
| Master Volume | slider | 0–100, step 5 | Mixer param `MasterVolume` |
| Music Volume | slider | 0–100, step 5 | Mixer param `MusicVolume` |
| FX Volume | slider | 0–100, step 5 | Mixer param `SFXVolume` |
| Subtitles | toggle | ON / OFF | `PlayerPrefs` + a runtime event |

- Left/Right (D-pad or left stick) adjusts the focused row; holding repeats after a short delay.
- Mouse: click-drag on the bar, click on the toggle.
- Changes apply **live** (hear the volume move while dragging) and persist immediately.
- Subtitles toggle should raise a static event (e.g. `PlayerSettings.SubtitlesChanged`) so `RpgMessageSystem` can subscribe later — wire the event now even though nothing consumes it yet.

### 3.3 Cheats
Intentionally empty for now. Build the screen shell — title bar reading `CHEATS`, an empty content area, the standard `(B) BACK` footer — with a single placeholder line such as `NOTHING HERE YET`. Structure it so rows can be added later without touching the navigation code.

### 3.4 Credits
Static, exactly this content and order:

```
MASTER OF DISASTER
Jorge Pedrero

TOWN FOOL
Diego Perez
```

Role lines in the accent colour and smaller; name lines large and white. No scrolling needed at this length.

### 3.5 Exit
Confirmation step (`ARE YOU SURE?` → `(A) YES  /  (B) NO`), then:
- In editor: `UnityEditor.EditorApplication.isPlaying = false`
- In build: `Application.Quit()`

`PauseMenu.ExitGame()` already has this exact `#if UNITY_EDITOR` shape — reuse it rather than reinventing.

---

## 4. Motion spec — the part that matters most

The whole feel of this request is **slow and smooth**. Nothing snaps.

**Menu entrance (Attract → Main Menu)**
- Duration ≈ **0.8 s**. Use an ease-out curve (`Mathf.SmoothStep`, or an exposed `AnimationCurve` so it can be tuned without a recompile).
- Items slide in from the left by ~120 px **and** fade 0 → 1, **staggered ~0.06 s per item** so they arrive one after another rather than as a block.
- The title/logo settles slightly ahead of the items.
- Everything drives on **`Time.unscaledDeltaTime`** — the menu will very likely be running at `timeScale = 0`.

**Selection movement (moving between items)**
- The highlight does **not** teleport. It eases toward the focused row over ~**0.15 s**.
- The focused row scales to ~1.06 and brightens; unfocused rows sit at ~0.75 alpha. Both interpolated, never instant.
- Stick input: dead zone 0.5, first repeat after 0.45 s, then every 0.12 s. D-pad: one step per press, same repeat timing when held.

**Screen transitions (Main ↔ sub-screen)**
- ~0.35 s. Outgoing panel slides left + fades out, incoming slides in from the right. Reversed on back.
- Only one panel accepts input at a time; input is locked for the duration of the transition.

**Attract prompt**
- `PRESS (A)` pulses alpha between ~0.35 and 1.0 on a ~1.6 s sine loop.

**Requirement:** put every duration, offset and curve on serialized, Odin-ranged fields on the controller so Jorge can retune the feel in the inspector without editing code.

---

## 5. Input spec

There is no `.inputactions` asset today, and every existing script polls `Gamepad.current` / `Keyboard.current` directly (`PauseMenu`, `TuningScreen`). **Follow the existing polling pattern** — do not introduce an `.inputactions` asset just for the menu unless you also migrate the rest, which is out of scope for this task.

| Action | Gamepad | Keyboard | Mouse |
|---|---|---|---|
| Move up / down | D-pad Up/Down, Left Stick Y | W/S, Arrow Up/Down | hover focuses a row |
| Adjust left / right | D-pad Left/Right, Left Stick X | A/D, Arrow Left/Right | click / drag on the bar |
| Confirm | **A** (`buttonSouth`) | Enter, Space | left click |
| Back | **B** (`buttonEast`) | Escape, Backspace | right click |
| Wake from attract | any button | any key | any click |

Rules:
- **Last-used device drives the prompt glyphs.** If the player touches the mouse or keyboard, swap the footer hints to keyboard text; if they touch the pad, swap back to Xbox glyphs. Detect by watching which device last reported activity.
- Mouse hover must move the *same* focus index the pad uses — one focus model, three input sources. No divergent "mouse hover" vs "selected" states.
- Add a short input grace period (~0.3 s) after any screen opens so the press that opened it doesn't immediately confirm inside it. `TuningScreen` already does this with `openedTime` — copy that guard.
- Optional but nice: light `HapticsSystem.Instance.Pulse(...)` on move, a slightly stronger one on confirm.

---

## 6. Audio Mixer

Create `Assets/99.Test/Jorge/FiniteRunner/Audio/FiniteRunnerMixer.mixer`.

Groups:
```
Master
 ├── Music
 └── SFX
```

Exposed parameters (exact names — the settings code binds to these strings):
- `MasterVolume` → Master group volume
- `MusicVolume` → Music group volume
- `SFXVolume` → SFX group volume

Conversion: sliders are 0–100 linear; mixer volume is dB. Map with `20 * Mathf.Log10(normalized)`, clamping to `-80 dB` when the value is 0 (log of 0 is `-Infinity` and will silently corrupt the saved value — handle this explicitly).

Also create menu `AudioSource`s routed through the mixer for navigation blip / confirm / back sounds. Leave the clip slots empty and serialized; do not ship placeholder audio.

---

## 7. Persistence

New `PlayerSettings` class (name it so it doesn't collide with `UnityEditor.PlayerSettings` — e.g. `FiniteRunner.PlayerPrefsSettings` or `FiniteRunner.UserSettings`). **Do not** add these to `GameSettings.cs`, which is balance data shared by the whole project.

Keys, defaults, and behaviour:

| Key | Type | Default |
|---|---|---|
| `settings.volume.master` | float 0–1 | 0.8 |
| `settings.volume.music` | float 0–1 | 0.7 |
| `settings.volume.sfx` | float 0–1 | 0.8 |
| `settings.subtitles` | int 0/1 | 1 (on) |

Load and apply to the mixer on first access (a `RuntimeInitializeOnLoadMethod` is fine), save on every change, and expose a static `SubtitlesChanged` event.

---

## 8. Asset inventory — exact paths, verified on disk

### 8.1 Panel / widget sprites — `Assets/99.Test/Jorge/FiniteRunner/03.UI/PNG/Red/`

Two variants exist: `Default/` (thin outline) and `Double/` (thick outline). **Use `Double/`** — it matches the `Xbox Series/Double` prompt set.

⚠️ **Important gap:** this Red folder is *not* a full UI kit. It contains only three families of sprite. There are **no** generic panel, checkbox, or arrow sprites. Work within what exists; do not invent filenames.

**Bars — use these for the volume sliders** (9-slice: `_l` left cap, `_m` tileable middle, `_r` right cap; `_square` is the squared-off end):
```
bar_round_large.png            bar_round_large_l.png   bar_round_large_m.png   bar_round_large_r.png   bar_round_large_square.png
bar_round_small.png            bar_round_small_l.png   bar_round_small_m.png   bar_round_small_r.png   bar_round_small_square.png
bar_round_gloss_large.png      (+ _l / _m / _r / _square)
bar_round_gloss_small.png      (+ _l / _m / _r / _square)
bar_square_large.png           (+ _l / _m / _r / _square)
bar_square_small.png           (+ _l / _m / _r / _square)
bar_square_gloss_large.png     (+ _l / _m / _r / _square)
bar_square_gloss_small.png     (+ _l / _m / _r / _square)
```
Suggested: `bar_round_large` as the empty track, `bar_round_gloss_large` as the filled portion.

**Headers / plates — use these for menu row backgrounds and screen title bars:**
```
button_square_header_blade_rectangle.png       button_square_header_blade_rectangle_screws.png
button_square_header_blade_square.png          button_square_header_blade_square_screws.png
button_square_header_large_rectangle.png       button_square_header_large_rectangle_screws.png
button_square_header_large_square.png          button_square_header_large_square_screws.png
button_square_header_notch_rectangle.png       button_square_header_notch_rectangle_screws.png
button_square_header_notch_square.png          button_square_header_notch_square_screws.png
button_square_header_small_rectangle.png       button_square_header_small_rectangle_screws.png
button_square_header_small_square.png          button_square_header_small_square_screws.png
```
Suggested: `button_square_header_notch_rectangle` for menu rows, `button_square_header_blade_rectangle_screws` for screen titles.

**Crosshairs:**
```
crosshair_color_a.png   crosshair_color_b.png   crosshair_color_c.png   crosshair_color_d.png
```
Suggested: `crosshair_color_a` as the selection marker to the left of the focused row, and/or as the "checked" mark inside the subtitles toggle.

**Subtitles toggle** — there is no checkbox sprite. Build it from `bar_square_small_square` as the box plus `crosshair_color_a` as the check, or fall back to an `ON` / `OFF` text pill. State which you chose.

**Import settings** for all of these: Sprite (2D and UI), Point (no filter) filtering, compression off or high quality, and set the 9-slice border on the `_l`/`_m`/`_r` bar pieces and the header plates.

### 8.2 Xbox prompts — `Assets/99.Test/Jorge/FiniteRunner/03.UI/Xbox Series/Double/`

Sprites you will need:
```
xbox_button_a.png            xbox_button_a_outline.png
xbox_button_b.png            xbox_button_b_outline.png
xbox_button_color_a.png      xbox_button_color_b.png      (coloured variants)
xbox_dpad.png                xbox_dpad_all.png            xbox_dpad_vertical.png    xbox_dpad_horizontal.png
xbox_dpad_up.png             xbox_dpad_down.png           xbox_dpad_left.png        xbox_dpad_right.png
xbox_stick_l.png             xbox_stick_l_vertical.png    xbox_stick_l_horizontal.png
xbox_button_start.png        xbox_button_menu.png
```
(Also present, not needed here: `controller_*`, `xbox_button_x/y`, `xbox_lb/rb/lt/rt`, `xbox_ls/rs`, `xbox_elite_paddle_*`, `xbox_guide`, `xbox_stick_r_*`, `xbox_stick_side_*`, `xbox_stick_top_*`, `xbox_dpad_round_*`, `xbox_button_back/view/share`.)

### 8.3 Fonts

**UI text font** — `Assets/99.Test/Jorge/FiniteRunner/03.UI/Font/`
```
Kenney Future.ttf
Kenney Future Narrow.ttf
```
Use `Kenney Future Narrow` for menu rows and `Kenney Future` for titles. Generate TMP Font Assets for both if you go TMP.

**Input glyph font** — `Assets/99.Test/Jorge/FiniteRunner/03.UI/Xbox Series/Fonts/`
```
kenney_input_xbox_series.ttf   (also .otf)
kenney_input_xbox_series_map.txt
kenney_input_xbox_series_characters.txt
```

This font maps glyphs into the Private Use Area, so prompts can be typed **inline in a text string** instead of composited as separate Images. Codepoints you need:

| Glyph | Codepoint |
|---|---|
| `xbox_button_a` | `U+E004` |
| `xbox_button_a_outline` | `U+E005` |
| `xbox_button_b` | `U+E006` |
| `xbox_button_b_outline` | `U+E007` |
| `xbox_button_color_a` | `U+E00C` |
| `xbox_button_color_b` | `U+E00E` |
| `xbox_dpad` | `U+E022` |
| `xbox_dpad_all` | `U+E023` |
| `xbox_dpad_vertical` | `U+E037` |
| `xbox_dpad_horizontal` | `U+E026` |
| `xbox_stick_l` | `U+E04F` |
| `xbox_stick_l_vertical` | `U+E056` |
| `xbox_stick_l_horizontal` | `U+E051` |
| `xbox_button_menu` | `U+E014` |
| `xbox_button_start` | `U+E018` |

The full 99-glyph table is in `kenney_input_xbox_series_map.txt` — read it if you need anything not listed.

**Recommended approach:** build a TMP Font Asset from `kenney_input_xbox_series.ttf` with the PUA range `E000-E062`, add it as a **fallback** on the main Kenney Future TMP asset, and then write footers as plain strings like `" SELECT    BACK"`. That gives correctly-baselined, auto-scaling prompts with no layout code. If you instead composite sprites, use the `Xbox Series/Double` PNGs from §8.2 and keep them in a small reusable prompt widget.

Either way: put the glyph-vs-sprite decision behind one small helper so swapping later is a one-file change.

---

## 9. Files to create

```
Assets/99.Test/Jorge/FiniteRunner/Scripts/UI/
    MainMenuController.cs      screen state machine, transitions, input routing
    MenuScreen.cs              base: Show/Hide with the eased animation, focus list, wrap-around
    MenuRow.cs                 one focusable row: label, focus visuals, activate callback
    MenuSlider.cs              volume row — bar sprites, 0–100, live mixer apply
    MenuToggle.cs              subtitles row
    InputPromptBinder.cs       last-used-device detection → glyph/text swap
    UserSettings.cs            PlayerPrefs load/save/apply + SubtitlesChanged event

Assets/99.Test/Jorge/FiniteRunner/Audio/
    FiniteRunnerMixer.mixer    Master / Music / SFX with the three exposed params
```

Plus, depending on the §3.1 choice, either a `MainMenu.unity` scene or a menu prefab spawned into `FiniteRunner_Test.unity`.

---

## 10. Constraints

- **Do not** modify `GameSettings.cs` — it is shared balance data, not player preferences.
- **Do not** break `PauseMenu` or `TuningScreen`. The pause menu must still open on `Start` during gameplay and must never open while the main menu is up.
- **Do not** add an `.inputactions` asset unless you migrate the whole project's input (out of scope).
- **Do not** use `Time.deltaTime` in menu animation — the menu runs at `timeScale = 0`.
- **Do not** invent sprite filenames. If something you want doesn't exist in §8, say so and compose it from what's there.
- Reset `Time.timeScale` to 1 in `OnDestroy` as a safety net — `PauseMenu` already does this and it's saved that project once.
- Canvas: `ScreenSpaceOverlay`, `CanvasScaler` set to `ScaleWithScreenSize` at `1920 × 1080`, `sortingOrder` **above 20** so it sits over the pause menu's canvas.

---

## 11. Acceptance checklist

Verify each of these in play mode before reporting done:

- [ ] Scene boots to the attract screen; gameplay is not running behind it.
- [ ] Any button/key/click wakes the menu; it eases in over ~0.8 s with staggered items — visibly slow and smooth, no pop.
- [ ] D-pad and left stick both move the selection; the highlight eases, never teleports.
- [ ] Holding a direction repeats at a comfortable rate; a single tap moves exactly one row.
- [ ] `A` confirms, `B` backs out of every sub-screen, `B` never quits the game.
- [ ] Mouse hover and click drive the same focus model as the pad; no double-highlight.
- [ ] Keyboard alone can reach and activate every entry.
- [ ] Prompt glyphs swap between Xbox and keyboard text when the input device changes.
- [ ] All three volume sliders audibly change their group in real time.
- [ ] Volume and subtitle values survive quitting and relaunching play mode.
- [ ] Master at 0 fully silences and does **not** write `-Infinity` or `NaN` to the mixer.
- [ ] Credits reads exactly: `MASTER OF DISASTER / Jorge Pedrero`, `TOWN FOOL / Diego Perez`.
- [ ] Cheats opens and backs out cleanly with its placeholder.
- [ ] Exit confirms first, then quits; in editor it stops play mode.
- [ ] Start hands off to gameplay with `timeScale` restored to 1 and `motor.Paused` false.
- [ ] Pausing during a run still works and the main menu does not appear over it.
- [ ] No console errors or warnings from the new scripts.

---

## 12. Open decisions to state in your summary

Answer these explicitly when you report back rather than deciding silently:

1. Same-scene overlay (§3.1 A) or separate menu scene (§3.1 B)?
2. TMP with a glyph fallback font, or legacy `Text` + composited prompt sprites?
3. How the subtitles toggle was built, given no checkbox sprite exists.
4. Anything in §8 you needed that wasn't in the asset folders.
