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

- **Speed is a `SpeedGauge`** wedge built in code at the top-left (`gauge*` knobs in the
  "Speed gauge" group): 20 segments growing taller to the right, each coloured by its **own**
  Light Speed fraction on the blue → green → hot `SpeedColor` ramp, lit from the left up to
  `speed / LightSpeed`. Full = the win.
- The scene's km/h number is re-seated at the wedge's right end by code at `Start` (smaller font,
  baseline on the wedge's), then KM/H, the LIGHT SPEED target line, and one code-built line per
  extra objective under it (`JUMP 1/3  ×2`).
- The countdown bar sits at the bottom, showing `GameManager.TimeRemaining`.
- Every booster hit spawns a floating "+N" at the ship (`FloatingWorldText`, spawned here);
  `MoneyChanged` is answered with a gold `+$N` floating text.
- **The HUD owns no retry and prints no result text.** Its old result/prompt texts and the R
  shortcut are gone — the two empty `Text` objects still wired in the scene can be deleted.

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

Two layouts:

- **Bare question** `Show(onRetry, onGiveUp)` — GAME OVER / RETRY? / YES / NO. The city chase
  raises this once the completion glitch has filled and held (YES reloads the level, NO to the
  main menu).
- **Retry panel** `Show(MenuTextId? reasonId, …)` — GAME OVER, a localized reason line in the
  accent colour where the question sat, then RETRY / EXIT TO MAIN MENU. The runner raises this the
  frame the run is lost; RETRY is `GameManager.Restart` **in place**, EXIT is
  `LoadingScreen.LoadMainMenu`.

**There is no Back out** — the screen demands an answer, so Esc/B do nothing on it. It freezes
scaled time, which also keeps the pause menu and the city map from stacking over it. Because the
runner retries without a scene reload, an answer tears the overlay down **before** running the
callback.

## `MissionCompleteScreen` (`Runner/Screens/`)

The mission's results panel, raised by `GameManager.ShowMissionComplete` the frame the run is won.
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
