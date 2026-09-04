---
description: UI assembly — menu framework, control bindings and the CONTROLS screen, LoadingScreen, PauseMenu and the debug menu
paths:
  - "Assets/01.Scripts/UI/**"
  - "**/InfiniteCity/Scripts/UI/**"
  - "**/MenuScreen.cs"
  - "**/MenuRow.cs"
  - "**/MenuTheme.cs"
  - "**/MenuNavigator.cs"
  - "**/MenuTextLibrary.cs"
  - "**/ControlBindings.cs"
  - "**/LoadingScreen.cs"
  - "**/PauseMenu.cs"
  - "**/DebugMenu.cs"
  - "**/CityDebugMenuFactory.cs"
---

# UI assembly

`Assets/01.Scripts/UI/` — the lowest assembly every game assembly references.

## Menu framework rules

- **Menu plates auto-fit their texts**: `MenuScreen` measures every row label (and screen title)
  in all four languages (`MenuTextLibrary.MaxWidth`) and sizes the page's plates to the widest one
  plus the row type's widget reserve (`MenuRow.ReservedRightWidth`) — uniform per screen, never
  below `MenuTheme.RowWidth`. **Never hardcode plate widths**; a new row type with right-side
  widgets overrides `ReservedRightWidth` and `SetWidth`.
- **Viewport scrolling**: `SetViewport(n)` shows n rows at once and `SetFocus` scrolls the window.
  `LayoutRows` repositions rows, deactivates the ones outside (no mask) and **rewrites each
  `EntranceItem.basePosition`** — it is a struct in a list, so without the rewrite `ApplyEntrance`
  snaps scrolled rows back on the next `Show`. It pulls a header into view when the focused row
  lands on the top slot, with `▲ n` / `▼ n` overflow cues.
- `MenuRow.Focusable` is the general hook (a `StatHeaderRow` returns false, so the cursor steps
  over it); `ClearRows()` empties a page for a rebuild.
- `MenuRow.ApplyFocus` rewrites row scale and alpha **every frame** — animate a label rect, never
  the row rect.
- `MenuScreen.Interactive` guards effects so they never fight a slide transition.
- All player-facing strings are `MenuTextId` entries in `MenuTextLibrary`, translated in all four
  languages. Stat values go through `StatFormat` and are never localized.

## Control bindings

`ControlBindings.cs`, `PadControls.cs`, `BindingCapture.cs`, `ControlGlyphSet.cs`, `BindingRow.cs`,
`ControlsScreen.cs`.

**The ONE action → control table gameplay polls through.** `GameAction` (append-only, screen
order; axes are two directional actions — ShipSteerLeft/Right, CarAccelerate/CarBrake) each holds
one `Key` and one `PadControl` — an enum whose entries are all `ButtonControl`s: triggers, stick
press, d-pad, Start/Select and the four half-axes of each stick, so a stick push captures like a
button.

Readers: `IsPressed` / `WasPressedThisFrame` (either device), `KeyboardAxis` (−1/0/+1),
`PadAxis(neg, pos, deadzone)` (analog), and `Axis` = **keyboard wins over the pad** (the rule the
ship and car always had).

Retrofitted onto: `SteeringInput` (Ship), `CarInput` (Car), `AirTimeSlowMo` (air pitch/roll = the
CameraPan actions the car already takes from the camera while airborne, the clock =
Accelerate/Brake), `OrbitCameraRig` (`ReadPan` pad then keys, `LookBackHeld`), `RadioSystem`, and
`MenuNavigator.MapTogglePressed` / `CameraCyclePressed`. `DashPromptController` shows the live dash
binding.

**Rules:**

- **Menu chords are never bindable.** `IsReserved` keeps Esc / Enter / numpad Enter / Backspace /
  meta keys and B / Start out of every binding and every capture. `MenuNavigator`'s Confirm / Back
  / navigate / pause-open stay hard-wired, and **menus keep polling the devices directly — only
  gameplay reads through the table.**
- A control is unique inside its **context** (`BindingSection` Ship / Car / General): same section
  conflicts, General conflicts with everything, Ship and Car never — both steer on A/D, and M is
  the ship's dash-right AND the car's map.
- `Set(action, control)` **SWAPS** with the action already holding the control and returns it (an
  old control a third action already holds is dropped to `None`, drawn `—`) — never a silent
  overwrite. `Sanitize` on load enforces the same walking the enum.
- Stored by enum NAME as one JSON string in PlayerPrefs (`settings.bindings`, the `UserSettings`
  shape). **Domain reload is off**, so `Boot()` reloads and drops stale `Changed` listeners.

### The CONTROLS screen

Lives under SETTINGS in both menus — `MenuScreenFactory.BuildSettings(parent, theme, openControls,
deleteProgress = null)` (the optional last argument adds the main menu's DELETE CAMPAIGN PROGRESS
row and tightens the metrics to 74/14 so seven rows keep the six-row reach; see `campaign.md`)
adds the row, and each host builds a `ControlsScreen` whose `OpenSub` / `Back` / `CloseSub` know
CONTROLS returns to SETTINGS.

It follows the LOG recipe: compact rows, SHIP / CAR / GENERAL `StatHeaderRow`s, a 9-row viewport,
RESTORE DEFAULTS last. One `BindingRow` per action (label, key cap slot, pad glyph slot, an accent
underline on the page-wide column). Left/Right picks the column; Confirm arms a `BindingCapture`
for that device and the slots give way to PRESS A KEY… / PRESS A BUTTON….

**The host calls `ControlsScreen.CaptureTick()` before its navigator every frame the page is
current and stops for the frame when it returns true** — listening, or the `InputGrace` after a
capture/cancel. So the press being bound never steps, confirms or backs out (Esc / B / Start cancel
the listen instead), the Confirm that armed it is never captured (ticked before Activate, plus the
capture's own grace after arming), and `MenuNavigator.Sync()` adopts a still-held direction so it
doesn't step once more. A swap prints SWAPPED WITH X under the list; a missing pad prints NO
GAMEPAD CONNECTED.

**Art**: `ControlGlyphSet` (`Data/Resources/FiniteRunner_ControlGlyphs.asset`, `For(Key)` /
`For(PadControl)`, `Label` text fallback) built by **`Tools → FiniteRunner → Build Control Glyphs`**
from the Kenney `06.UI/01.Sprites/Keyboard & Mouse/Double` + `Xbox Series/Double` folders. It is
the UI-assembly twin of the cheats' glyph set — **Cheats references UI, never the reverse.**

Strings are `MenuTextId` `Controls*` / `Action*` / `PressKey` / `SwappedWith` entries in all four
languages.

## `LoadingScreen` (`UI/LoadingScreen.cs`)

The PS1-style loading curtain that covers a scene trip: full backdrop, localized LOADING...
(`MenuTextId.Loading`), a filling bar and a bottom-right sprite slot for the future spinning disc.

`Load(index | name)` / `Reload(scene)` / `LoadMainMenu()` put it up, run `LoadSceneAsync` with
activation held until the bar has filled, then destroy it one frame after the new scene drew —
**callers never learn when loading ended.**

**The bar is time-driven, not a progress readout.** Unity loads a scene in one long hitch, so a bar
chasing `AsyncOperation.progress` sits at 0, freezes and slams to 1. Instead it crosses the bar in
`MenuTheme.loadingFillSeconds` (1.5 s) with each frame's step capped at 50 ms (the hitch counts as
one ordinary step), **staged around the hitch**: the hitch is the scene's ACTIVATION (Awakes, the
city prefab instantiating), during which nothing draws — so the bar climbs to the HALF mark while
the scene streams with activation held (`HitchFill`), activation is allowed with the bar parked
there, and once `isDone` the second half fills under the curtain at the same pace. A bar that
reached the end before the stall read as a hang; one that completes after it reads as a load.

`DontDestroyOnLoad`, own overlay canvas at sorting **40** (above the main menu), unscaled time,
sets `timeScale = 1` on entry, one trip at a time (`IsLoading`).

**Every scene trip goes through it** — main menu START (`MainMenuController.FinishStart`),
`PauseMenu.ExitToMainMenu` / the debug RELOAD SCENE row, and the game-over answers (runner NO in
`GameManager.ShowGameOver`, city YES/NO in `LevelManager`) — **except the city → runner completion
handoff** (`LevelManager.TransitionToNextScene`, additive behind the maxed glitch, which is its own
transition).

Knobs are theme slots: `loadingFillSeconds`, `loadingSpinner` sprite (hidden while empty),
`loadingSpinnerSpin`.

**It also owns the trip's sound**: raising the curtain crossfades the mixer into its `Loading`
snapshot (`GameAudio.SetLoading` — Gameplay bus and PauseMusic out, the `LoadingMusic` bus in, UI
untouched so the confirm blip that started the trip finishes) and loops
`MenuTheme.loadingMusicClip` (`07.Audio/03.Music/LoadingScreen/M_Loading_Test.mp3`) on a child
`AudioSource` routed to `GameAudio.LoadingMusic`. Lifting it fades back over `loadingAudioFade` and
the object lingers with its canvas disabled for that fade before destroying itself, so the music
never cuts on the frame the new scene draws. `OnDestroy` releases the duck with no fade if the
curtain dies early.

## `PauseMenu`

Esc / gamepad Start pauses active gameplay (`timeScale = 0` + `motor.Paused`, haptics reset) with
Resume / Settings / Debug / Exit rows on the shared themed menu framework. **Never opens over the
tuning or result screens** (`CanPause` checks `motor.Paused` and `RunOver`). Spawned by
`GameManager`, built from code on its own overlay canvas. `HapticsSystem` runs on unscaled time so
rumble decays while paused.

### The DEBUG row

Hidden when the public `debug` bool is off, or when the scene has nothing for it to edit —
`BuildDebugTabs` only builds the pages the current scene can actually offer. It opens the tabbed
developer pages in `Scripts/UI/DebugMenu.cs`: each tab is a normal compact-row `MenuScreen`
(`DebugMenu.AddTab` — bumpers / Q-E cycle tabs).

**Runner pages:**

- **Core Settings** — the generator's width/straightness/probability sliders (probabilities
  live-rebalance to always total 100%) plus a RELOAD SCENE row.
- **Multipliers** — the per-entry boost multipliers (0.1–10).
- **Features** (`BuildFeaturesTab`) — the feature spacing band (one slider slides the band, keeping
  its spread), per-entry probability / min spacing / boost rows, and the jump definition's knobs.
  Edits the runtime clone, captured into `FeatureDebugSettings`
  (`Data/Resources/FiniteRunner_FeatureDebug.asset`, applied onto the clones in `Generate`).
- **Four ship tabs** (Speed / Handling / Dash / Hover — only when the menu was spawned with a
  `ShipMotor`) edit the motor's live `ShipDefinition` clone.
- **Patrol** (only when the GameManager's patrol is initialized) edits the patrol's live
  `PatrolDefinition` clone.

Slider rows re-read the live values every time the menu opens, because the tuning clone lands after
the menu is built.

**Persistence**: track changes → `TrackDebugSettings` (`Data/Resources/FiniteRunner_TrackDebug.asset`),
ship → `ShipDebugSettings` (`Data/Resources/FiniteRunner_ShipDebug.asset`, applied on top of the
tuning clone in `TuningScreen.StartRun`), patrol → `PatrolDebugSettings`
(`Data/Resources/FiniteRunner_PatrolDebug.asset`, applied onto the patrol clone in
`PolicePatrol.Init`). All flushed to disk on
resume/reload and re-applied in play mode while their `applyOnLoad` is on — so debug tweaks survive
scene reloads, play-mode exits and editor restarts. **Untick `applyOnLoad` on an asset to return to
the authored values.**

### City pages (`CityDebugMenuFactory`, `InfiniteCity/Scripts/UI/`)

The `CarTest` scene hosts the same `PauseMenu` as a hand-placed object and gets its own pages:

- **Car Drive** and **Car Grip** — the core `CarConfig` handling knobs; the chassis ones (mass,
  center of mass) re-run `CarController.ApplyConfig` on every car sharing the config.
- **Chase Camera** — `OrbitCameraSettings` framing/recenter/speed-FOV/look-back.
- **Camera Modes** — close framing, first-person eye, mode blend.
- **Police Fleet** / **Police Chase** — the core `PursuitSettings` knobs (count, spawn band split
  into two mutually-clamping rows, despawn; detection, lose-sight, search, patrol/chase/corner
  speeds).
- **Body Damage** (`BuildDamageTab`), **Air Time**, and the vehicle-physics backend choice row.
- **Level** — the `LevelManager`'s `LevelDefinition` objectives, one row each in list order, tinted
  by status (green done / accent active, polled live via `SetLabelTintProvider`), with sliders on
  the speed and time steps and read-only `DebugLabelRow`s for the rest.

**Those cars, the rig and the level manager read their settings assets live (no runtime clone to
catch), so those sliders edit the assets themselves.** Persistence is `EditorUtility.SetDirty` + a
`CityDebugMenuFactory.Flush()` at the menu's commit points (resume, reload, `OnDestroy`, so tweaks
land on disk before play mode ends). Everything there applies live, so **the city pages never raise
the reload prompt.**

A **Weather** page is added by `RainDebugPage` (which lives with the system in `FX/Weather/`, not in
either game's factory, because both scenes spawn the same `RainSystem`) whenever the scene is
raining. `DistanceFogDebugPage` and `SpeedLinesDebugPage` are added the same way wherever their
drivers exist.

**Backing out of a debug tab after any slider change opens a localized "DO YOU WANT TO RELOAD THE
SCENE?" confirm** (YES reloads, NO continues). All debug tab titles and row labels are `MenuTextId`
entries, translated in all four languages.
