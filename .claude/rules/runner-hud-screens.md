---
description: Runner HUD and result screens — RaceHud, SpeedGauge, GameOverScreen, MissionCompleteScreen, runner fog
paths:
  - "Assets/01.Scripts/Runner/HUD/**"
  - "Assets/01.Scripts/Runner/Screens/**"
  - "Assets/01.Scripts/Runner/CameraFX/**"
  - "**/RaceHud.cs"
  - "**/SpeedGauge.cs"
  - "**/GameOverScreen.cs"
  - "**/MissionCompleteScreen.cs"
  - "**/MissionResults.cs"
---

# Runner HUD and result screens

## `RaceHud` (`Runner/HUD/`)

The runner's scene-wired HUD on the `RaceHUD` canvas object.

**The layout is the designer's: code never moves, resizes, re-anchors or re-fonts a HUD element.**
Every element is a hand-placed scene object — `SpeedText`, `SpeedGauge` (a rect, `gaugeRect`),
`LifeBar` (`lifeBarRect`), `Lives`, `KmhLabel`, `TargetText`, `Objectives` (a
`VerticalLayoutGroup` holding the `ObjectiveLine` template, cloned per line and hidden at Start)
and the timer (`DistanceText`, wired as `timeText`). The wedge and hull bar FILL their rects
(`SpeedGauge.BuildInto` — segments anchored as fractions of the rect, gap in px, shortest segment
`gaugeMinHeightFraction` of its height); pulses multiply each element's authored scale. **Rebuild
Preview** (Odin button) draws the segments in edit mode. Do not add position/size/font knobs back.

- **Speed is a `SpeedGauge`** (in the UI assembly since the standalone ship's HUD draws the same wedge) filling `gaugeRect` (`gauge*` knobs in the
  "Speed gauge" group): 20 segments growing taller to the right, each coloured by its **own**
  Light Speed fraction on the blue → green → hot `SpeedColor` ramp, lit from the left up to
  `speed / LightSpeed`. Full = the win.
- Beside it the km/h number, then KM/H, the LIGHT SPEED target line, and one line per extra
  objective under it (`JUMP 1/3  ×2`).
- **The life bar** (`BuildLifeBar`) is a second `SpeedGauge` filling `lifeBarRect` in
  `lifeSegments` flat cells, with the `×N` lives count (`livesText`, `GameManager.LivesLeft` — its
  font must carry the × glyph). While `GameManager.HullEnabled` is off both are hidden. One
  colour for the whole bar (full → mid → low by `ShipHealth.Fraction`), cells rounded UP, a drop
  between frames = white flash + scale punch, a blink under `lifeLowFraction`; the count punches
  when a life goes. A `RepairOrb.Collected` = a `repairColor` (green) flash + the same punch and a
  green "+N" hull-points popup — keyed on the orb, not on the fraction rising, since a restart
  refills the bar too. Knobs are the "Life bar" header.
- **Reached once is reached**: the LIGHT SPEED line turns `winColor` the frame
  `GameManager.LightSpeedReached` latches and stays so while the ship still has to make an end ramp.
- **The distance left is NOT a HUD line** — it reads on top of the `ChaseMinimap` track map (see
  `runner-ship.md`). The old `END  12.4 KM` line and its "too slow to make it" red tint are gone;
  `MenuTextId.HudDistanceToEnd` is left in the (append-only) enum, unused.
- **The countdown is a plain `MM:SS`** (`GameManager.TimeRemaining`, whole seconds rounded up, the
  string rebuilt once a second): the scene's timer text, placed by hand,
  ALWAYS `timerColor` yellow, no low-time tint. There is no bar/slider any more. Unfinished
  objective lines use `lineColor` (white, the old `timeColor`).
- Every booster hit spawns a floating "+N" at the ship (`FloatingWorldText`, spawned here);
  `MoneyChanged` is answered with a gold `+$N` floating text.
- **The HUD owns no retry and prints no result text.** Its old result/prompt texts and the R
  shortcut are gone — the two empty `Text` objects still wired in the scene can be deleted.

## `DuelBarHud` (`Runner/HUD/`) — the tug of war and the kill prompt

Spawned by `GameManager` on its own overlay canvas at sorting **12** (with the dash prompt: above the
HUD, below the RPG box). `Spawn` returns null for a missing patrol or `patrolDuelEnabled` off, so the
caller never checks.

- **The bar is small, low and centred** (D32), because the bar is the contest but the ROAD is the
  stake — the patrol is shoving you toward a wall or an edge the whole time, so it has to be readable
  in peripheral vision rather than looked at. Push direction is reinforced through the rumble channel
  for the same reason (`Rumble`, intensity rising with the patrol's share, on unscaled time).
- **It is MIRRORED, always**: the model underneath counts the PATROL's progress 0→1 and knows nothing
  about sides; the fill grows from the patrol's real side and the mash glyph sits at the far end —
  the end you are pushing toward. `Mirror(side)` runs once per contest, not per frame, because the
  side cannot change mid-exchange. The meaning never changes: push it AWAY from the patrol.
- **The mash glyph is `PadControl.ButtonWest` / `Key.X`, fixed and non-bindable** — see
  `ui-menus.md` for why that is a deliberate exception. **The finisher prompt is the opposite**: it
  shows the player's own `ShipDashLeft` / `ShipDashRight` binding for the side the patrol is on,
  re-read on `ControlBindings.Changed`, and pulses on **unscaled** time — the one thing in the
  exchange that must not slow down with the world. **The kill is a single PRESS of that binding, not
  a dash** (since 2026-09-25): the exchange holds the ship's controls, so the ship swallows the dash
  and `PolicePatrol.PollFinisher` reads the binding raw each frame. The wrong shoulder is a miss.
  The window (`finisherWindowSeconds`, 1.5) counts REAL seconds like the bar. Device choice is the dash prompt's presence rule
  (`Gamepad.current != null`), polled for hot-plug; `InputPromptBinder.Poll()` is NOT usable here
  because it is only called from the menus and its value is stale during a run.
- **The diagnostic line is parented to the CANVAS, not the bar holder**, so it survives the bar being
  hidden — the whole reason it exists is to explain a contest that never opened. On
  `GameSettings.duelDebugReadout`, it prints `PolicePatrol.EncounterDebug()`: the state, gap, lateral,
  damage pool, escalation tier and — while Cruising — the three gates that are invisible when they
  refuse (`commit in`, `reach`, `ground`). All three of those hid real bugs during the build.
- Hidden whenever `motor.Paused`.

## Runner fog

The runner scene carries its own hand-placed `DistanceFog` driver with its own settings asset,
`Data/FiniteRunner_RunnerFog.asset` — fog 1000–1500 m in the scene's pale palette, sky untouched,
far glitch from 1150 m, far clip clamped to 1800 m. The legacy `RenderSettings` fog is OFF.

**That band is tied to the generator's `aheadDistance` (1600 m in the scene):** finished road
exists only that far ahead, so the fog end must stay below it or the road's edge pops into view.
Move both together.

## `GameOverScreen` (`Runner/Screens/`, namespace `…FiniteRunner.Screens`)

The shared death screen, on the themed menu framework, driven by two callbacks so each game
decides what an answer means. It lives here rather than in either game's UI folder because
**both scenes show it** and `PoliceEscape` references `Runner`, never the reverse.

Three layouts:

- **Bare question** `Show(onRetry, onGiveUp)` — GAME OVER / RETRY? / YES / NO. The city chase
  raises this once the completion glitch has filled and held (YES is `LevelManager.RestartLevel`
  **in place** — no scene load, see `city-level-flow.md`; NO to the main menu).
- **Retry panel** `Show(MenuTextId? reasonId, …, MenuTextId titleId = GameOver)` — the title
  plate (the runner passes `MissionFailed`), a localized reason line in the accent colour, the
  RETRY? question, then YES / NO. The title is lifted a label's height (`MenuScreen.SetTitle(id,
  lift)`) so both lines fit between it and the rows. The runner raises it from `EndRun`, once the
  MISSION FAILED banner has torn away (`GameManager.FinishFail`); YES is `GameManager.Restart`
  **in place**, NO is `LoadingScreen.LoadMainMenu`. Reasons: `LoseCaught`, `LoseTimeOut`,
  `LoseMissedRamp` (end reached with the objectives met, not on a ramp),
  `LoseTooSlow` (end reached with Light Speed open) / `LoseObjectivesIncomplete` (another
  objective open).

- **Final** `ShowFinal(reasonId, onContinue)` — the runner's last life lost: the GAME OVER plate,
  the reason, a breathing PRESS ANY BUTTON line; **no rows, no prompt strip, no retry**. After the
  input grace `MenuNavigator.AnyPressed()` (any key, mouse button or pad button — the attract
  screen's poll, promoted to the UI assembly) runs `onContinue`. The mission is already forfeited
  when it opens (`runner-ship.md`); the runner's `onContinue` loads the Store.

**There is no Back out** — the screen demands an answer, so Esc/B do nothing on it. It freezes
scaled time, which also keeps the pause menu and the city map from stacking over it. Because both
games retry without a scene reload, an answer tears the overlay down **before** running the
callback.

## `MissionAccomplishedBanner` (`Runner/Screens/`)

The endings' exclamation mark, and the ONE text an ending prints before its panel. MISSION
ACCOMPLISHED (`MenuTextId.MissionAccomplished`, four languages) slams onto the upper third of the
screen over the planted fly-past shot: `GameManager.FinishWin` raises it (`Show(settings)`) the
step the ship leaves an end ramp with the win, calls `Dismiss(ramp + hold)` where the glitch ramp
starts, and `KillBanner()`s whatever is left in `EndRun` (before the panel) and in `Restart`.
**MISSION FAILED is the same banner**: `Show(settings, MenuTextId.MissionFailed, colour, colour)`
with `GameSettings.failBannerColor` for the letters and the underline, raised by
`GameManager.FinishFail` on EVERY loss, held `failBannerHoldSeconds`, torn away over
`failBannerDismissSeconds`, then `EndRun` opens the retry panel. The letter timings are the
`winBanner*` knobs for both.

- Its own `ScreenSpaceOverlay` canvas at sorting order 22 — above the HUD (10), messages (15)
  and pause (20), below the debrief/game over (25). No raycaster; it is a picture.
- **One object per character**, slots laid out on the theme's `TitleFont` advances
  (`MenuTextLibrary.MeasureWidth` per glyph; the space is `"A A" − "AA"`), the font shrunk to
  fit `MaxWordWidth` — so it auto-fits all four languages, never a hardcoded width.
- **Juice**: a dark band wipes out from the centre; each letter drops from 3.2× (ease-in — the
  impact is the fast part), bounces once, and lands with a red/cyan split that converges over
  0.18 s, a rising-pitch `PlaceholderBlip` and a kick that dips the whole word. The LAST letter
  fires `GameSettings.winBannerShake`, a `GlitchController.Pulse(winBannerGlitchPunch)`, a haptic
  thump, a white flash and the accent underline wiping out under the word. While holding, one
  random letter flickers off-register now and then; the word breathes.
- **Dismiss** tears the letters apart sideways behind a growing split and fades the root; a
  word still entering finishes first (a zero camera hold still shows the whole word).
- Unscaled time throughout. Tunables (`winBannerDelaySeconds`, `winBannerLetterStaggerSeconds`,
  `winBannerLetterSlamSeconds`, `winBannerGlitchPunch`, `winBannerShake`) sit in `GameSettings`'
  "Mission complete" group next to the camera hold and glitch timings.

## `MissionCompleteScreen` (`Runner/Screens/`)

The mission's results panel, raised by `GameManager.ShowMissionComplete` once the win's wind-down
is over (ship grounded, glitch ramped to max — see `runner-ship.md`), behind the full glitch.
No win line, no HUD text — the panel freezes the clock.

**A mission is a city level plus the escape run after it, and it is PAID here, once.**

### Data

Assembly-neutral **`MissionCompleteData`**: title, optional `VideoClip`, the city's flat
`baseReward`, `ObjectiveResult` rows for the main and run objectives, `ChallengeResult` rows, a
`RankTable`. The city's rows cross the additive scene handoff through the profile's `lastLevel`
record — `PlayerStats.RecordLevelCompleted` takes the objective/challenge rows, the bonus and the
rank table and **banks nothing**. The runner's rows come off the live `GameManager` state.

### Layout and reveal

Layout is the mission brief's clothes: a left column of `ResultRow`s under `StatHeaderRow`s, video
holder at right showing NO SIGNAL until `RunnerLevelDefinition.completeVideo` is assigned.

**The rows are text lines, not plates**: `ResultRow` disables its plate and draws a WHITE label
(not `theme.TextPrimary`, which the shipped theme asset has at 49% grey — fine on a plate, unreadable
on the bare backdrop) with an accent value, like the section headers. The title is white for the
same reason. `ResultRow.RequiredWidth` scales the screen's 34 pt label measurement to its 28 pt
render and reserves 220 px for the value. After the TOTAL row, `results.FitColumn(ColumnLeft,
ColumnMaxWidth)` pins the column's LEFT edge to the title's glyph edge (−880) and caps its width so
the widest row ends at −20, clear of the video plate (+35); a label that still cannot fit shrinks
its own font (`FitLabel`, floor 18 pt) rather than running into the value. Fit before the labels
are cleared for typing — `FitLabel` measures what the row shows.

Played as a **`RevealSequencer`** of steps on unscaled time — plain C#, no coroutines, so a skip
resolves in one frame:

1. Each row fades in.
2. Its label is TYPED (`TypewriterStep`: block cursor, the newest character scrambled for two
   frames, a rising-pitch blip via `RpgMessageSystem.PlaceholderBlip()`, and a scale punch on the
   **label rect, never the row rect** — `MenuRow.ApplyFocus` rewrites row scale/alpha every frame).
3. Its money or `×N` COUNTS UP (`CountUpStep`, ease-out).
4. A TOTAL row joins at the first count-up and is recomputed every frame **from what the rows
   currently SHOW**: `(bonus + Σ rewards) × Π max(1, multiplier)`. `MissionPayout.Total` in
   SaveData is the same formula on final values. A failed challenge prints FAILED and multiplies
   nothing.
5. The RANK letter slams onto the screen centre at 3× and settles under the video.
   Rank = `RankTable.RankFor(total)` — S/A/B/C/D money thresholds authored on the city
   `LevelDefinition.rankTable`; the runner definition's table is used only when no city level
   preceded the run.
6. NEXT MISSION / RETRY / EXIT TO MENU slide in
   (`LoadingScreen.Load(Store)` on a campaign session, `level.nextSceneName` in direct play /
   `GameManager.Restart` in place / `LoadingScreen.LoadMainMenu`).

**The slam is where `PlayerStats.RecordMissionCompleted(data.missionId, total, rank)` banks** —
the FULL total, every completion (replaying is the intended money farm; there is no delta
banking since profile v3) — and where the campaign mission latches complete. `missionId` comes
from `MissionSession.Current` ("" in direct play, which then pays without a record).

**Skip** is a long press of A / Enter (the cinema's ring — `UiSprites.Ring`, `Radial360`, armed
after one seen release, `SkipHoldSeconds` const) and jumps the reveal to the slam. Button confirms
wait for a release after the buttons appear, so the press that finished the hold can't answer.
Back does nothing.
