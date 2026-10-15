---
description: Runner time trials — TimeTrialDefinition, TimeTrialSystem, checkpoints and their awards, the ITimeTrialClock / ITimeTrialState contracts, the FinishWithTime objective, Arcade life loss, the TIME TRIAL debug page
paths:
  - "Assets/01.Scripts/Runner/TimeTrial/**"
  - "**/TimeTrialContracts.cs"
  - "**/TimeTrialInstaller.cs"
---

# Runner time trials

Design and decisions: `TimeTrialPRD.md` (D1–D15, R1–R5). Runner only. A **time trial level** is a
`RunnerLevelDefinition` whose `timeTrial` field holds a `TimeTrialDefinition`; empty = the plain
runner, and nothing below runs. A level that also sets `race` runs the race (a warning is logged).

## Pieces

| Type | Where | Role |
|---|---|---|
| `TimeTrialDefinition` | `Runner/TimeTrial/` | `mode` (`TimeTrialMode` BeatTheClock / Arcade, append-only), Clock (`timeLimitSeconds` BtC, `startSeconds` Arcade, `maxClockSeconds`, `lowTimeWarningSeconds`), Checkpoints (`autoCount`, `firstCheckpointMeters`, `featureClearance`, `paceShare`, `paceFloorKmh`, `graceSeconds`, `awardBand`), Bonuses (`cleanCheckpointBonus`, `patrolKillBonus`, `lifePenaltySeconds`, `trafficKillPays`, `scrapeBreaksClean`), `dirtyCheckpointPenalty`, `patrolEnabled`, Gate look, `debugLog`. Cloned at bind. |
| `TimeTrialCheckpoint` | `Runner/TimeTrial/` | One checkpoint + its optional overrides, packed in a `Checkpoint` `TrackPlacement` (`variant` = `Overrides` bits; `data` = award, clean, dirty, kill; `height` = life). `LayAuto` is THE auto layout, shared by the run and the editor. |
| `CheckpointGate` | `Runner/TimeTrial/` | One gate's picture (pylons + beam, or `gatePrefab` colliders stripped) + its floating `TextMesh` label (rich text, `Compose` / `Seconds`). Built by the system in play and by the editor preview in edit mode, always under `TrackGenerator.CheckpointParent`; an edit preview carries `Owner` + `RecordIndex`. |
| `CheckpointPreviewGates`, `CheckpointGateEditor` | `Runner/Editor/` | The edit preview: registered as `TrackGenerator.CheckpointPreview`, so the generator builds a gate per Checkpoint record like a pad (rebuilt on every edit, `DontSaveInEditor`); the picked trial (EditorPrefs), the preview label, the five override rows. Selecting a gate edits its record in the track asset (distance, overrides, prev / next, delete, Save Track). |
| `TimeTrialSystem` | `Runner/TimeTrial/`, `PF_TimeTrialSystem` under `===SYSTEMS===` (nested in `PF_Systems`) | Lays the checkpoints, judges crossings, sizes awards, counts clean / kills / lives, asks the clock for time, draws the gates. Implements `ITimeTrialState`. `Live` = the bound instance. |
| `ITimeTrialClock`, `ITimeTrialState`, `TimeAwardKind` | `Contracts/TimeTrialContracts.cs` | The clock the trial moves (the `GameManager` implements it: `AddTime` clamps to `[0, maxClockSeconds]`, refused while ending / over / paused) and the read-only state the HUD, minimap and panel read (`ClockAdjusted(delta, kind)`). |
| `TimeTrialDebugPage` | `Runner/TimeTrial/` | The pause menu's TIME TRIAL tab (registry order 7, after RACE's 6), shown only while bound. |
| `TimeTrialInstaller` | `Runner/Editor/` | **Tools → FiniteRunner → Install Time Trials**: `TimeTrial_BeatTheClock` / `TimeTrial_Arcade` (`04.Data/FiniteRunner/TimeTrial/`), `PF_TimeTrialSystem`, the PF_Systems wiring (`GameManager.timeTrial`), two test levels (`FiniteRunner_LevelDefinition_BeatTheClock` / `_Arcade`). Idempotent, never overwrites. |
| `GameModeMenu` | `Runner/Editor/` | **Tools → FiniteRunner → Set Game Mode → Standard / Race / Beat the Clock / Arcade**: points the open scene's `GameManager.level` at that mode's level (ticks the one in use). Each level plays its own copy of one track: `Track_Standard`, `Track_Race`, `Track_BeatTheClock`, `Track_Arcade` (the only one with Checkpoint records). |

## The clock and the win

- `GameManager.TimeLimit` = Arcade's `startSeconds`, else BtC's `timeLimitSeconds` (0 = the saved
  track's / GameSettings'). The countdown is the run's as ever; the trial only calls `AddTime`.
- **Elapsed time is its own counter** (`GameManager.ElapsedRunSeconds`, ticked with the countdown,
  reset on `Restart`): `PlayerStats.RecordRunEnded` takes it — `TimeLimit − TimeRemaining` stops
  meaning anything once time is added.
- **The win is `RunnerObjectiveType.FinishWithTime`** (`minSecondsLeft`, 0 = just finish): met only
  inside `OnReachedTrackEnd` off a ramp (`ObjectiveContext.finished`, set by `judgingLip`), never
  latched mid-run. With no Reach Speed objective `HoldsSpeedGoals` asks nothing at the lip, so any
  speed wins. As a challenge it is "finish with ≥ N s left ×M".
- A time trial turns off the hyperspace jump (`hyperspace.Allowed`); `patrolEnabled` AND
  `GameSettings.patrolEnabled` decide the chase. Traffic stays the level's.

## Checkpoints (Arcade)

- **Laid on the trial's first tick after a bind or `generator.Regenerated`** (which fires BEFORE the
  new track is decided) once `EndZoneStart` exists, and again whenever `TrackManager.Revision`
  moves. Authored `TrackPlacementKind.Checkpoint` records (append-only kind 10; the generator's
  `Build` ignores it, the editor palette offers "Checkpoint (time trial)", the gizmo draws a line
  across the road, Checks warn on one inside a keep-out, Reroll keeps them) win; else `autoCount`
  spread from `firstCheckpointMeters` to `EndZoneStart`, each slid forward (else back, else dropped)
  off `TrackHazards.IsEncounterGroundClear` runs and laser / ramp records ± `featureClearance`.
- **Authoring (D15)**: Edit Track → Placements → *Time trial checkpoints*: pick a trial, **Lay Auto
  Checkpoints** (replaces the track's checkpoints with `LayAuto`'s, undoable) / Clear; then drag,
  add, duplicate, delete. A selected checkpoint has five tick-to-override rows (greyed = the trial's
  value): time award (fixed, unclamped), clean bonus, dirty penalty, and the kill bonus / life penalty
  of the stretch UP TO it (`Ahead()` = the next checkpoint's; past the last, the defaults). Lateral is
  pinned to 0 and the editor ignores a checkpoint's `height` for drawing.
- **Award = ((distance to the next checkpoint, the last: to the end) ÷ max(crossing speed ×
  `paceShare`, `paceFloorKmh`) + `graceSeconds`, clamped to `awardBand`) × `awardScale`**, sized at
  the crossing. `awardScale` (0.5 on `TimeTrial_Arcade`) is the one generosity knob; a fixed award
  override is never scaled.
  Not off the ship's cruise: the Fighter's thrust (25 m/s²) needs ~100 s to reach its 2664 m/s
  cruise and real speed is the orbs' — the cruise-sized first pass timed out before checkpoint 1.
  Before crossing, `CheckpointAward` reads the pace-floor value (the most it can pay).
- **Crossing is analytic** (`IChaseTarget.Distance` passing the mark). A respawn that skips several
  pays each, only the first can be clean.
- **Dirty**: crossed after a hit → −`DirtyPenaltyOr` (`TimeAwardKind.DirtyPenalty`, `TtDirty` floater);
  only the first of a respawn skip pays it.
- **Clean** = nothing since the last checkpoint: hull `Damaged` hard (a scrape only with
  `scrapeBreaksClean`), every laser / car hit through `ApplyHazardHit` (hull on or off), every fall,
  every life. GameManager forwards them (`NotifyHit` / `NotifyScrape`).
- **Gates**: one `CheckpointGate` per checkpoint, built at layout under the track's
  `CheckpointParent` (the hand-placed **TimeTrialCheckpoints** object next to Pads in `PF_Env`, wired
  by the installer; none = the system's own child), shown while the generator has built road there
  and the ship is within 300 m past, coloured pending / clean / dirty. The generator's regenerate
  clears only edit previews (`DontSaveInEditor`) from that parent, never the system's gates.
- **The floating label** (`showGateLabels`, `labelGap`, `labelSize`, `label*Color`): pending, the
  award at the ship's speed NOW (`AwardAt(i, ship.Speed)`, or the fixed award) + `CLEAN +x` — or
  `DIRTY -x` on the next gate once the run to it is dirty; crossed, what it paid. Rewritten only when
  the tenths change. The edit preview shows the fixed award or the pace-floor `MAX`, clean / dirty,
  and any kill / life override.

## Bonuses and lives (Arcade)

- `PolicePatrol.Killed(PatrolKillCause)` (Finisher / Laser / Traffic, raised in `Kill` after the
  redeploy) → `NotifyPatrolKill`: `patrolKillBonus`, a traffic kill only with `trafficKillPays`.
- **A life is not the run** (`GameManager.TrySpendLife`, only Arcade with the hull on and more than
  one life left): hull 0 (`OnShipDestroyed`) or an arrest (`patrol.HasCaught`) → `ExplodeShip`
  (destroyed only), `LivesLeft−1`, `NotifyLifeLost` (−`lifePenaltySeconds`), an arrest also
  `PolicePatrol.ReleaseArrest()` (catch cleared, controls handed back, recycled behind, no floor
  raise), then `ShipMotor.ForceRespawn` → `ShipRecovery.BeginFall(skipFall: true)` — the fall's own
  respawn, which holds and releases the patrol. **Its events fire INSIDE that call**, so the
  explosion and the revive flag are set first; `RespawnStarted` → `ShipHealth.Revive()` (=
  `ResetForRun`). A ship already out of play (destroyed by a fall's own hit) is just flagged, or
  revived at once if it already stands in its respawn. The LAST life goes through `BeginFail` →
  GAME OVER as ever; Beat the Clock keeps today's rules (every loss ends the run).

## HUD, map, panel

- `RaceHud` (finds `ITimeTrialState` by `ContractLookup`): while `TrialActive` the clock reads
  tenths under a minute (`M:SS` above), tints and pulses under `LowTimeSeconds` (`lowTimeColor`),
  and on `ClockAdjusted` flashes `timeGainColor` / `timeLossColor` with a `clockPunch` and floats
  `CHECKPOINT +4.2S` / `CLEAN +2.0S` / `PATROL DOWN +3.0S` / `LIFE LOST -5.0S`
  (`MenuTextId.Tt*`, `{0}` = the signed seconds). The goal line reads `CHECKPOINT  n/N`
  (`HudCheckpoint`) or `BEAT THE CLOCK` (`HudBeatTheClock`).
- `ChaseMinimap.Spawn(…, trial)`: a marker per checkpoint (`checkpointMarkerColor` / sprite),
  passed ones at `checkpointPassedAlpha`; placed again whenever the count or the map moves.
- `MissionCompleteScreen`: a TIME TRIAL section (TIME LEFT, RUN TIME via `StatFormat.Lap`; Arcade
  adds CHECKPOINTS n / N, CLEAN CHECKPOINTS, PATROLS DOWNED, LIVES LOST) — display only.

## Debug

TIME TRIAL page: CROSS NEXT CHECKPOINT, LAY CHECKPOINTS AGAIN, ADD 10 SECONDS; start time, time
limit, clock cap, checkpoint count / pace / pace floor / grace, the clean bonus, the dirty penalty, the kill bonus, the life penalty, the low-time
warning, the clock log (`debugLog`: `[TimeTrial]` lines). Rows write the asset
(`DebugAssetEdits.Touch`) and mirror the clone; start time and limit bite on the next run, layout
knobs on the next layout. Odin buttons on the system: Debug Cross Next, Debug Relayout.
