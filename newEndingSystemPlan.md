# New Ending System — finite track, end ramps, MISSION FAILED


## Context

Today the runner's track is endless and the win latches the instant Light Speed is reached
(clock stops, autopilot, nothing can be lost). The new design makes the run a **point-to-point
escape**: the track has a fixed per-level length, ends in **three side-by-side ramps over a void**,
and the win is only banked when the ship leaves one of those ramps.

Confirmed rules:

- **Win** = all mandatory objectives latched (Light Speed reached ONCE is enough; HUD shows it
  done) **AND** the ship takes one of the 3 end ramps. Countdown, patrol catch and stall all stay
  LIVE until the lip. On the win the ship keeps flying forward off the ramp (no landing) under the
  existing MISSION ACCOMPLISHED banner → glitch ramp → Mission Complete panel (pay/latch in
  `MissionCompleteScreen.Slam()` untouched).
- **Fail** = timed out, caught, stalled, reached the end without the objectives (ramp or not =
  `TooSlow`), or reached the end off a ramp (`MissedRamp`). EVERY fail plays a **MISSION FAILED**
  letter-slam banner, then a panel: title MISSION FAILED, localized reason line, `RETRY?` YES / NO
  (YES = `GameManager.Restart` in place, NO = `LoadingScreen.LoadMainMenu`).
- **Track length** is fixed per level (`RunnerLevelDefinition.trackLengthMeters`, 0 = `GameSettings`
  fallback). HUD shows distance remaining.
- **Patrol** reaching the track end always falls and is never replaced.

Assumption to flag: level 3's only objective is Jump Count 10, so under "all mandatory objectives
+ end ramp" it is winnable without Light Speed. Kept as-is (the rule generalises); the TooSlow
reason string is generic ("OBJECTIVES INCOMPLETE") unless the missing one is the speed goal.

## Verified facts that shape the design

- Past the built end today the pose freezes at the last knot inside a walled corridor — a body
  never falls. The end must be authored (`TrackManager.HasEnd/EndDistance`, `TrackBody` end rule).
- The last knot cannot be placed exactly at an authored length (chords ≠ arc on curves, loop
  inserts). So: pin the **end-zone start** as a pseudo feature spot (existing `landOnSpot`
  explicit-tangent knot), hold everything after it collinear (chord = arc), and make
  `track.EndDistance = track.Length` after the final knot the authority. Authored length = target.
- `TrackBody.StepJump` keeps only ONE `blockingRamp` (last scanned) → in a gap between two end
  ramps only one side is walled. Replace with two lateral limits built from every spanning,
  uncommitted ramp.
- `entryMargin` 2.5 would let a wall-hugging ship ride the outer ramp's margin strip and be judged
  "missed". End ramps use their own `JumpDefinition` with `entryMargin = 0`; outer ramps extend
  0.5 m past the wall.
- `ShipMotor.FindRespawnDistance` pushes past ramps to their `EndDistance` → would respawn AT the
  end. Skip end ramps and clamp to `track.EndZoneStart`.
- `PolicePatrol.Step`'s "never through the ship" 1 m clamp + `UpdateCatch` would pin the patrol
  behind a ship that has left the end, and could "catch" it. Bypass when `target.HasLeftTrackEnd`.
- `EvaluateObjectives` with zero objectives is a LIVE speed test, not latched → needs an explicit
  latched `ObjectivesMet`.
- No new `ShipState` needed: `OffTrack` already means "body stands still, owner flies it in world
  space" and every reader behaves correctly. Use a motor-side `OffTrackMode { Fall, TerminalFall,
  Escape }`.
- Awake order between `TrackGenerator` (generates in `Awake`) and `GameManager` (swaps in the
  `MissionSession` level) is undefined → the generator **pulls** `gameManager.TrackLengthMeters`
  (as it already pulls `PowerUpSpeedBoost`, `LoopRequiredSpeed`), backed by an idempotent
  `GameManager.ResolveLevel()`. `Restart` is covered because `Generate()` re-pulls.
- Lookahead: end knot exists when the ship is ≥ 2440 m out (`aheadDistance` 1600 + `SettleMargin`
  840); once complete `settled` must become `track.Length` so ramps/road are stamped at once.
- Balance (Fighter asset: launch 249 m/s, cruise 250 m/s, Light Speed 1805 m/s, 60 s): cruise-only
  covers 15 km; a run reaching Light Speed at ~49 s has covered ~39 km. **Default length 40 000 m.**
- `TrackImprovementsPlan.md` §7 (closed circuit/laps) is superseded by point-to-point; its
  authored-layout half stays compatible (`trackLengthMeters` is where a layout's length plugs in).

## Milestones (each testable in `FiniteRunner_Test`)

All paths under `Assets/01.Scripts/`.

### M0 — Data, plumbing, HUD distance (no behaviour change at the end yet)
- `Runner/GameFlow/RunnerLevelDefinition.cs`: `[PropertyRange(0f,100000f), SuffixLabel("m")] float trackLengthMeters = 0f` (0 = fallback).
- `Runner/GameFlow/GameSettings.cs`: new `[TitleGroup("Track end")]` — `trackLengthMeters` (5000–100000, 40000), `endRunUpMeters` (600–3000, 1200; ≥ `respawnClearance` + 300), `endRampGapMeters` (4–40, 10), `endRampSideGapMeters` (0–20, 0).
- `Runner/GameFlow/GameManager.cs`: extract idempotent `ResolveLevel()` (session level → serialized → `CreateDefault`), `TrackLengthMeters`, `EndRunUpMeters`, `EndRampGapMeters`, `EndRampSideGapMeters`, `DistanceRemaining`.
- `Runner/Track/TrackGenerator.cs`: `Generate()` pulls `targetLength`; `public float EndDistance`.
- `Runner/HUD/RaceHud.cs`: code-built distance line (like `MakeObjectiveLine`), "END 12.4 KM", `failColor` when remaining/speed > `TimeRemaining`.
- `UI/MenuTextLibrary.cs`: `HudDistanceToEnd` (4 languages; enum append + field + `Entry` switch).

### M1 — Finite generation + end zone
- `Runner/Track/TrackManager.cs`: `HasEnd`, `EndDistance`, `EndZoneStart` (−1 unset), `SetEndZone`, `SetEnd`, cleared in `ClearKnots`; `GetPoseAtDistance` extrapolates along the last forward past `EndDistance`.
- `Runner/Track/Features/JumpRamp.cs`: `IsEndRamp` (optional `Configure` arg).
- `Runner/Track/TrackGenerator.cs`:
  - state `endZoneTarget`, `endTarget`, `inEndZone`, `trackComplete` (reset in `Generate`).
  - `AddSegment` returns `SpotKind { None, Feature, EndZoneStart, End }`; remaining measured to the nearest of feature cursor / zone start / end; even split when `remaining <= x + y`; `holdStraight = track.Length < straightUntil || inEndZone` (straight, level, bank 0, no flat sweep / open stretch); zone-start and end knots pinned with the explicit-tangent `AppendKnot`.
  - `NextLevelSpot` (min of `featureCursor`, `endZoneTarget`) used by `LevelRequired` / `TurnFits` so banks unwind and no sweep starts that cannot finish before the zone.
  - `StreamTo`: `while (!trackComplete && …)`, dispatch `DecideFeature` / `BeginEndZone` / `FinishTrack`; `settled = trackComplete ? track.Length : Length − SettleMargin`.
  - `DecideFeature`: refuse (advance cursor, return) when `spot + footprint + exclusion > endZoneTarget` — keeps loops, tubes and jump landings out.
  - `BeginEndZone()`: `track.SetEndZone`, `endTarget = start + endRunUp`, claim over the zone (reuses `claims`/`ClaimEnd` → no pads/coins), `featureCursor = MaxValue`.
  - `FinishTrack()`: `track.SetEnd(track.Length)`, `CreateEndRamps()`. Extract `BuildRamp(...)` from `CreateJump`; three ramps, `w = (2·HalfWidth − 2·gap − 2·sideGap)/3`, laterals `0, ±(w+gap)`, start = `End − def.length`, boost 0, outer half-width +0.5.
  - new serialized `FeatureSpawnEntry endRampEntry` (runtime-cloned like the table) + asset `Assets/04.Data/FiniteRunner/EndRamp_Definition.asset` (`entryMargin` 0, `length` ~120, `rampAngle` ~15, `sideHitSpeedLoss` ~0.05).
- `Runner/Track/TrackDecorator.cs`: `StampEndMarker(distance, lateralCentre, width)` with `openEdgeMaterial` on the gaps so they read as drops.

### M2 — Simulation at the end
- `Runner/Simulation/TrackBody.cs`: two-sided ramp walls (`rampWallMin/Max`) replacing `blockingRamp/blockSide`; `event Action<bool> ReachedEnd`; `LeaveEnd(JumpRamp)` (lip height/vertical velocity kept, `SetState(OffTrack)`, fire event); `TakeOff()` early-outs to `LeaveEnd(Ramp)` for an end ramp (no gravity solve, no `TookOff`, no jump count, no forced Far camera); in `Step` after `StepJump`: `Distance >= track.EndDistance` → `LeaveEnd(null)`. `HoldOnTrack` not consulted.
- `Runner/Ship/ShipMotor.cs`: refactor `OnLeftTrack` → `BeginWorldFlight`; `OffTrackMode`; `HasLeftTrackEnd`, `IsEscaping`, `event Action<bool> ReachedTrackEnd`, `BeginEscape()`; `StepFall` by mode (Escape: no tumble, look along velocity, never respawns; TerminalFall: gravity + tumble, never respawns); `Launch()` resets; `FindRespawnDistance` skips `IsEndRamp` and clamps to `EndZoneStart`. `FellOff` is NOT raised at the end (patrol is not put on hold).
- `GameManager` (interim): `RunOutcome.MissedRamp`, `TooSlow` (private, unserialized); `OnReachedTrackEnd` → `EndRun` via the current panel; strings `LoseMissedRamp`, `LoseTooSlow`, `LoseObjectivesIncomplete`.

### M3 — Win/lose rewrite
- `GameManager`: latched `ObjectivesMet` (+ `SpeedGoalIndex`); the latch no longer sets `HasWon`/`Autopilot`; `EvaluateObjectives` keeps running so challenges can still latch; all lose paths stay live. `OnReachedTrackEnd(tookRamp)`: evaluate once more, then `tookRamp && ObjectivesMet` → `HasWon`, `motor.BeginEscape()`, `FinishWin`; else `TooSlow` (objectives missing) / `MissedRamp`. `FinishWin` drops the wait-for-Grounded and cuts to the planted cinematic at the lip; rest unchanged. `IsEnding => HasWon || failRoutine != null`. `Restart` resets `ObjectivesMet`, restores a stuck Far camera mode. `Autopilot` stays on the motor, unused (doc updated).
- `Runner/Screens/PauseMenu.cs` (`CanPause`): `!RunOver && !IsEnding`. RPG guards (`OnPadCollected`, `OnPatrolRedeployed`, `OnPatrolWarned`) → `RunOver || IsEnding`.
- `RaceHud`: `targetText` shows a done state (`winColor`) once the speed goal is latched.

### M4 — MISSION FAILED presentation
- `Runner/Screens/MissionAccomplishedBanner.cs`: overload `Show(settings, MenuTextId textId, Color word, Color underline)`; existing `Show(settings)` forwards. Class name kept.
- `GameSettings`: `[TitleGroup("Mission failed")]` `failBannerHoldSeconds` (0–5, 2), `failBannerDismissSeconds` (0.25–2, 0.5), `failBannerColor`; slam timings reuse `winBanner*`.
- `GameManager.FinishFail(outcome)` (unscaled coroutine, every lose path routes through it): re-entry guard, clear messages, fade music, glitch pulse; Caught/Stalled/TimedOut pause the motor at once; MissedRamp/TooSlow keep the sim live and cut to the planted cinematic so the fall (and the patrol's) plays; banner → hold → dismiss → `EndRun(outcome)`. `Update` early-outs while failing; `Restart` stops it.
- `Runner/Screens/GameOverScreen.cs`: optional `titleId` on the reason overload; the reason branch (runner is its only caller) becomes title + reason + `RETRY?` + YES/NO, focus YES. The city's `Show(onRetry:, onGiveUp:)` call (`LevelManager`) and the prefab are untouched.
- Strings: `MissionFailed` (4 languages).

### M5 — Patrol
- `Runner/GameFlow/PolicePatrol.cs`: `body.ReachedEnd` → `BeginEndFall()` (world-space ballistic fall mirroring `ShipMotor.StepFall`, visual hidden after ~4 s), `IsGone`, never redeploys; `Launch` resets. While `target.HasLeftTrackEnd`: skip the 1 m clamp, `UpdateCatch`, `WarnIfClose`, distance redeploy. Mid-track `OnLeftTrack` redeploy unchanged.
- `Runner/GameFlow/PatrolDriver.cs` `PlanRamps`: ignore `IsEndRamp` (drives straight off).
- `ChaseMinimap`: hide the patrol icon while `IsGone`.

### M6 — Debug, docs
- CORE SETTINGS "TRACK LENGTH" row → `generator.TrackLengthOverride` (−1 = none), persisted in `TrackDebugSettings` with the −1 "never captured" rule; string `TrackLength`.
- Docs: `CLAUDE.md` (win/lose, "track is endless" → finite, `fastestEscapeSeconds` = launch-to-lip), `.claude/rules/runner-track.md`, `runner-ship.md`, `runner-hud-screens.md`, `cameras.md`; resolve the two "Known drift" notes touched.

## Edge cases covered
- Fall off an open edge late in the run → respawn clamped to the (walled, straight) end-zone start.
- Timeout while riding the end slope → legitimate loss; lip and catch on the same frame → lip wins (`HasWon` return precedes the catch poll).
- No loop/tube/normal-jump landing can overlap the end zone (generator refusal by footprint + exclusion).
- Restart mid-banner / mid-escape / mid-terminal-fall resets cleanly (`Launch` resets motor modes, `Restart` stops both routines).

## Verification (Unity Editor; no CLI tests — compile check via `Logs/Editor.log` / Bee DLL mtimes)
Use a temporary 6–8 km length and a lowered speed target for fast loops.
1. M1: Scene view — end zone straight/level/walled, no pads/coins/features inside, three ramps end on the last knot, road stamps stop there, `EndDistance` within metres of target; 10 random seeds.
2. HUD distance counts to 0 at the lip; level-asset length overrides fallback; played from the Store the session level's length is used.
3. Latch + ramp → escape flight, fly-past, MISSION ACCOMPLISHED, glitch, Mission Complete, payout banked once.
4. Latch + gap → MissedRamp; ramp without latch → TooSlow; latch then timeout → TimedOut; latch then caught → Caught; stall → Stalled. Each: MISSION FAILED banner → panel with the right reason in all 4 languages; YES restarts clean, NO → main menu.
5. Gap walls hold against steer and dash on both sides, no mid-ramp pop; wall-hugging ship on an outer ramp counts as on the ramp.
6. Patrol drives off the end and falls (ramp or gap), no replacement; a mid-track patrol fall still redeploys.
7. Pause refused during either ending; late respawn never lands in/past the end zone.
8. City game over unchanged in `CarTest`.

## Implementation status (2026-09-18)

M0–M6 are implemented in code and compile (UI → Runner → PoliceEscape → Runner.Editor, checked
with Unity's bundled Roslyn). NOT yet play-tested in the Editor — run the Verification list above.

Deviations from the plan as written:

- The end ramps' definition is loaded from `Assets/04.Data/Resources/FiniteRunner_EndRamp.asset`
  when `TrackGenerator.endRamp.definition` is empty (built-in numbers if that is missing too), so
  the scene needs no new wiring. Wire a prefab/definition on the generator's `End Ramp` entry to
  override it.
- An end ramp's lip and the track's end are computed apart, so `TrackBody` treats a body still
  committed to an end ramp at the end distance as having left BY the ramp (float rounding guard).
- The outer end ramps reach 0.5 m past a flush wall in total (0.25 m shift + 0.25 m half-width).
- `RaceHud`'s distance line uses the localized `HudDistanceToEnd` format only when the HUD's scene
  font can draw it; otherwise the English format (the HUD's other labels are English already).
