---
description: City chase level flow — LevelManager, LevelDefinition objectives, optional challenges, ObjectiveHud, TargetObject
paths:
  - "**/InfiniteCity/**"
  - "**/LevelManager.cs"
  - "**/LevelDefinition.cs"
  - "**/LevelObjective.cs"
  - "**/OptionalChallenge.cs"
  - "**/ObjectiveHud.cs"
  - "**/TargetObject.cs"
  - "**/ChallengeTrigger.cs"
  - "**/CityMapScreen.cs"
---

# City chase level flow

`InfiniteCity/Scripts/`, namespace `ConfusedGameDev.FiniteRunner.PoliceEscape`.

`LevelManager` runs a **`LevelDefinition`** asset (`PoliceEscape/Level Definition`; the scene's is
`Scripts/Vehicles/Test/TestLevelDefinition.asset`). Without an asset it plays
`LevelDefinition.CreateDefault()` — reach 130 km/h → escape.

**The asset is read live every frame (no clone)**, so the debug LEVEL page's sliders edit the
asset itself.

## Objectives

An ordered, drag-reorderable `objectives` list of enum-typed `LevelObjective` entries. Only the
chosen type's knobs show, via `[ShowIf]`. `ObjectiveType` is **append-only**.

| Type | Knobs |
|---|---|
| ReachSpeed | target speed |
| EscapePolice | — |
| GoToTarget | a `TargetObject` id |
| SurviveTime | seconds |
| ChaseCar | — |
| DestroyCars | `destroyCount` + `destroyKind` / `destroyPaint` filter |
| CollectObjects = 6 | `collectCount` + `collectId` (empty = any) |
| Jump = 7 | `JumpMeasure` Distance → `jumpMeters`, AirTime → `jumpSeconds` |

Each entry also carries a `reward` (hidden on challenges), the dialogue framing (`speakerName`,
per-step `briefing`, `completionMessage`), and the level carries a `mode` and `nextSceneName`.

Steps play top to bottom and brief once when first active.

**Semantics**: ReachSpeed / EscapePolice are *state* steps; SurviveTime / GoToTarget are
*progress* steps. In `Independent` mode a finished step stays finished. In `AllMustHold` a
finished state step is re-checked every frame (speed with a 3 km/h hysteresis) and the lowest one
that stopped holding becomes current again while every later step's progress resets — regressed
steps re-activate **silently**.

**Time rules**: any non-Survive step can carry a `timeRule` (`None` / `CompleteWithin` / `HoldFor`)
with one `timeSeconds` knob.

- `CompleteWithin` is a deadline counted from activation; unmet when it runs out → the level's
  `timeUpMessage` line, then `RequestReboot` game over (`timedOut` gates the loop during the line).
- `HoldFor` completes only once the bare condition has stayed true for the whole span; a lapse
  zeroes the count, and speed uses the `HoldToleranceKmh` hysteresis once seconds are banked.

`LevelManager.Evaluate` wraps the bare `Satisfied` check. `state.timer` is the one clock
(survive / elapsed / held — `Timer(index)`), and a regression restarts it. The HUD appends
`TIME LIMIT n S` (red under 10 s) or `HOLD FOR held/span S`; the debug LEVEL page adds a seconds
slider under the step; briefings get `{1}` = the seconds.

### Counters

`ObjectiveState.tally` is shared by Destroy Cars and Collect Objects (a step is only ever one
type); `Tally(i)` / `ChallengeTally(i)` read it. `ObjectiveState.jumpBest` holds the best landed
jump so far in the step's measure.

- **DestroyCars** — `CarHealth` raises a static `Died` event at `BeginDeath` (health hits zero,
  controller and identity still on the object) and `LevelManager.OnCarDied` tallies into the
  CURRENT step only, through `VehicleIdentity.Matches`. Police and chain-explosion kills count
  like any other. `Unknown` = any, so "5 cars", "5 buses", "5 red trucks" are one step with
  different filters. `VehicleIdentity.Describe` / `DisplayName` turn the filter into HUD words
  (`DESTROY  RED TRUCK  3/5`); briefings get `{2}` = that text lower-cased.
- **CollectObjects** — `LevelManager` subscribes `Collectible.Collected` beside `CarHealth.Died`
  (OnEnable/OnDisable) and `OnCollected` tallies exactly like `OnCarDied`. HUD
  `COLLECT  FLOPPY  2/5`, briefing `{3}` = the id. **`OnCollected` ignores Money**, so a coin never
  fills a "collect anything" step.
- **Jump** — `CityStatsRecorder.JumpLanded` (horizontal metres, air seconds — every wheel off the
  ground for ≥ 0.25 s, **raised only once the car has settled on the ground for 0.2 s** so a wheel
  tap that lifts off again continues the same jump) drives `OnJumpLanded`. The step picks a `JumpMeasure`
  and reads it through `JumpTarget` / `JumpUnit` / `JumpValue(m, s)`; it completes on the first
  jump reaching the target. HUD `JUMP  38.4 / 50.0 M`, briefing `{4}` = the unit word.

## Optional challenges

**Optional challenges are objectives**: `OptionalChallenge : LevelObjective` adds only a
`multiplier`, so a challenge is any step type with any clock, drawn with the same knobs
(`ChallengeLabel` in the list, `ChallengeSummary` = summary ×N on the brief and HUD).

Accepted challenges run **beside** the main list for the whole level
(`LevelManager.EvaluateChallenges`, own `challengeStates` parallel to `acceptedChallenges`): each
is checked every frame through the same `Satisfied(step, state, …)` until it completes and latches
— never regresses, never briefs, cinema ignored. A Complete Within deadline that runs out fails
the **challenge** (`failed`, multiplier lost) rather than the run. A completed challenge speaks
its own optional completion message; a failed one speaks the level's `challengeFailedMessage`
(`{0}` condition, `{1}` multiplier).

**Challenges are listed only on the city map screen** (`CityMapScreen.UpdateChallenges`, an
OPTIONAL CHALLENGES header under the objective rows): declined greyed `-`, accepted open `[ ]`,
done `[x]` green, failed `[!] … FAILED` in `CityMapSettings.missionFailedColor`. The debug LEVEL
page lists every challenge read-only, tinted by the same statuses (`AcceptedIndex`).

### `ChallengeTrigger` (`PoliceEscape/ChallengeTrigger.cs`)

The third hand-placed volume beside `DialogueTrigger` (orange) and `CinemaTrigger` (blue), drawn
green by the same visualizer. It carries a full inline `OptionalChallenge` (any type, clock and
multiplier) plus a description line (speaker / portrait / text with the objective's `{0}..{3}`
placeholders via the public `LevelObjective.Format`; empty text = the objective's own briefing,
empty speaker / 0 hold = the level's).

Driving in calls **`LevelManager.AcceptChallenge`** — the challenge joins `acceptedChallenges`
with a fresh state (starts counting next frame, multiplies `MissionReward`; refused while the
level is ending or if already accepted), the line is spoken, and the trigger **destroys itself**.
A challenge is taken once — no cooldown. `CityMapScreen.challengeList` merges the asset's
challenges with any accepted on the road.

## Rewards — and what banks

`MissionReward` is the brief's OFFER, `EarnedReward` the running payout. Both are
`level.RewardBase` (the flat `baseReward` + every objective's own `reward`) × completed
multipliers.

**`Complete()` banks nothing.** It records every objective row, every ACCEPTED challenge with its
outcome, the bonus and the level's `rankTable` through `RecordLevelCompleted(...)` for the
runner's Mission Complete panel, which pays the whole mission. (`PlayerStats.CompleteBonusObjective()`
fires the moment a challenge lands.)

## Completion and advance

Completion = completion line → (after it disappears) glitch slammed to max, held
`completionGlitchHoldSeconds`, then the additive scene handoff. Damage/reboot knobs stay on the
manager.

**Every objective (challenges included) can carry a completion message and a delay**: the
`Completion message` `[ToggleGroup]` (`hasCompletionMessage` + `completionMessage`, same
`{0}..{3}` placeholders as the briefing via the shared `Format`, `[MSG]` in the list label) and
`nextDelaySeconds`.

When a main step finishes, `LevelManager.BeginAdvance` raises the **`advancing` gate**
(`Advancing` — the HUD tints the line green): the car keeps driving and challenges keep tallying,
but the main list waits until the completion line has cleared (`RpgMessageSystem` `onFinished` —
**the box types on scaled time, so the world must NOT be frozen under it**) and then
`nextDelaySeconds` (scaled `WaitForSeconds`, so it freezes with the pause menu / map) before
`current++` and the next step's brief/cinema. A done step absorbs no kills or pickups meanwhile,
and an All-Must-Hold regression bumps `advanceToken` to cancel the pending advance.

## `TargetObject`

The "go to" point: a root scene object — or hand-placed under the city prefab's `AdditionalItems`
socket, which survives rebakes — with a string `id` in a static registry (`TargetObject.TryFind`).
With `snapToRoad` it slides onto the nearest road cell
(`CityManager.TryFindNearestRoadCell`, accepted only within two cells), useful after a rebake moved
the streets. Distances are horizontal.

## `ObjectiveHud` (`Scripts/UI/`)

Spawned by the manager, code-built overlay like `Speedometer`. Shows the active step: target speed
vs current, seconds left, `GO TO id — x M` (red `NO TARGET` when the id isn't registered), or
escape — with prefixes from the four `Objective*` `MenuTextId`s.

**Its ReachSpeed line shows only the target — the speedometer already shows the current speed.**
The HUD carries the objective alone; challenges live on the map screen.

`CarTestSceneBuilder` creates/loads the level asset and the hand-placed `PauseMenu`.
