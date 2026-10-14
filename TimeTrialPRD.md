# PRD — Time Trial levels: Beat the Clock and Arcade Time Trial

| | |
|---|---|
| **Status** | 2026-10-14: M1–M5 implemented; full chain compiles (Roslyn, every assembly); Validate System Independence clean (0 new). Played in `FiniteRunner_Test` over MCP: Arcade on autopilot — 8 auto checkpoints, awards 7–14 s at the crossing speed, clean bonus paid, a hull-0 life respawned with full hull (−1 life, −5 s), won with 50 s left, Mission Complete TIME TRIAL rows shown; Beat the Clock — 90 s, no patrol, no hyperspace, time-out → loss + life, retry resets clock and elapsed, won off a ramp at 3375 km/h (well under Light Speed). Deviation: D6 award sized at the crossing speed, not cruise (see D6). **Not yet exercised in play**: the patrol kill bonus (finisher / laser), the arrest-as-a-life path, GAME OVER on the last Arcade life, authored checkpoints from the editor palette, the debug page, `Sandbox_TimeTrial` (Build System Sandboxes + Smoke Test need the open scene saved). Tuning open: start time, grace, award band, BtC limit. |
| **Branch** | `race-mode` (follow-up to race mode) |
| **Scope** | Runner game only (`FiniteRunner_Test`, campaign runner levels). The city chase, `Campaign` and `SaveData` are untouched. |
| **Related** | `RaceModePRD.md` (the per-level system pattern this copies), `PatrolDuelPRD.md` (the kill), `.claude/rules/runner-ship.md`, `.claude/rules/runner-track.md`, `.claude/rules/runner-hud-screens.md` |

---

## 1. Problem

Every runner level is the same race against one countdown: reach Light Speed, get off the end ramps
in time. The countdown is a backdrop; the pressure comes from the speed goal and the patrol. Two
level types should make **time itself** the game:

- **Beat the Clock** — a set time to finish the track. No Light Speed goal: just reach the end ramps
  before the clock runs out.
- **Arcade Time Trial** — the classic arcade loop. The clock starts nearly empty; every
  **checkpoint** adds just enough time to reach the next one. Flying clean, killing patrols and
  keeping your lives is what builds a buffer.

## 2. Goals

1. A level can declare a **time trial** (`RunnerLevelDefinition.timeTrial`) in one of two modes:
   Beat the Clock or Arcade.
2. **Beat the Clock**: the level's own time limit; win = leave the track by an end ramp before 0, at
   any speed.
3. **Arcade**: a short starting clock; **checkpoints** along the track each add the time it takes to
   reach the next one at a reference pace (`cruise × paceShare + grace`), capped.
4. Arcade **bonuses and penalties are seconds on the clock**:
   - a checkpoint reached with no hit since the last one: `+cleanCheckpointBonus`
   - a patrol destroyed: `+patrolKillBonus`
   - a life lost: `−lifePenaltySeconds`
5. In Arcade, **losing a life does not end the run**: at 0 hull the ship explodes and respawns down
   the track with a full hull, a life and some time gone. A patrol arrest does the same. Only the
   clock or the last life ends the run.
6. The HUD makes the clock the star: tenths of a second, a low-time warning, a flash and
   `+4.2s CHECKPOINT` / `CLEAN +2s` / `PATROL DOWN +3s` / `−5s` floaters, `CHECKPOINT n/N`. The
   minimap shows the checkpoints.
7. Checkpoints are **generated** on a generated track and **authorable** on a saved one.
8. Zero cost in levels without a time trial; no allocation mid-run.

## 3. Non-goals

- Laps, multi-path, ghost replays, best-time leaderboards saved to `SaveData`.
- Time trial in the city chase.
- A time trial combined with a race (a level setting both runs the race, with a warning).
- Splits in Beat the Clock (checkpoints are Arcade only; BtC may get them later as display splits).

## 4. Settled design decisions

| # | Decision |
|---|---|
| D1 | **Per level.** `RunnerLevelDefinition.timeTrial` references a `TimeTrialDefinition`; empty = no time trial. `PF_TimeTrialSystem` is hand-placed under `===SYSTEMS===` (nested in `PF_Systems`) and idles unbound — the `RaceSystem` pattern. |
| D2 | **One definition, two modes**: `TimeTrialMode { BeatTheClock, Arcade }`. Cloned at bind; gameplay never writes it. |
| D3 | **Finish = an end ramp before 0, at any speed.** A new objective type `FinishWithTime` (`minSecondsLeft`, 0 = just finish) is judged ONLY at the lip (`ObjectiveContext.finished`), never latched mid-run. With no Reach Speed objective, `HoldsSpeedGoals` asks nothing at the lip (the race precedent). A gap between ramps is still `MissedRamp`. |
| D4 | **No hyperspace jump** in either mode: it freezes the clock and moves the end. |
| D5 | **Patrol per level** (`patrolEnabled` on the definition, AND-ed with `GameSettings.patrolEnabled`). Traffic stays the level's own choice. |
| D6 | **Checkpoint award is computed, not authored**: `clamp(gap / max(crossingSpeed × paceShare, paceFloorKmh) + graceSeconds, awardBand)`, where `gap` is the distance to the NEXT checkpoint (the last one: to the end), sized the moment the checkpoint is crossed — hold the pace you came in with and you make it. **Deviation (play-tested 2026-10-14):** the first build sized it off the ship's cruise at run start, but the Fighter's thrust (25 m/s²) needs ~100 s to reach its 2664 m/s cruise and real speed comes from orbs; the run timed out before checkpoint 1. |
| D7 | **Checkpoints: authored records win, else auto-spaced.** A new `TrackPlacementKind.Checkpoint`. With none on the track, `autoCount` checkpoints are spread evenly between `firstCheckpointMeters` and the end-zone start, each nudged off feature keep-outs (loops, tubes, ramps and their landings) and laser gates. |
| D8 | **Crossing is analytic** — the ship's track `Distance` passing the checkpoint's distance. No trigger volumes (the project invariant). Crossing several in one tick (a respawn skip) pays each award, none clean. |
| D9 | **Clean** = no hit since the last checkpoint: any hull damage (hard), laser or car hit (hull on or off), a fall, a life lost. Plain wall scrape counts only with `scrapeBreaksClean`. |
| D10 | **Patrol kill bonus** on a finisher kill and a laser kill (both are the player's doing — the laser kill is aimed). A traffic kill pays only with `trafficKillPays`. A new `PolicePatrol.Killed(PatrolKillCause)` event. |
| D11 | **Arcade life loss = respawn.** Hull 0 or an arrest with lives left: the explosion, `LivesLeft−1`, `−lifePenaltySeconds`, the ship respawned through `ShipRecovery.BeginFall(skipFall)` (the fall's own respawn: wait, blink, patrol hold), a full hull on `Respawned`. An arrest also redeploys the patrol behind (`ReleaseArrest`). The last life still goes through `BeginFail` → GAME OVER. With the hull off there are no lives: hull 0 cannot happen and an arrest fails the run as today. |
| D12 | **The clock is capped** at `maxClockSeconds` — banking is bounded, arcade style. |
| D13 | **Elapsed time is its own counter.** `TimeLimit − TimeRemaining` stops meaning "how long" once time is added; the run stats record the real elapsed seconds. |
| D14 | Pay comes only through **objectives and challenges** (`FinishWithTime` with a reward, a "finish with ≥ 20 s left ×2" challenge). The Mission Complete TIME TRIAL rows are display only. |

## 5. Functional requirements

### 5.1 Clock (D3, D12, D13)
- R1.1 `GameManager.TimeLimit` = BtC's `timeLimitSeconds` (0 = the track's / GameSettings') or Arcade's `startSeconds` while a time trial is bound.
- R1.2 `AddTime(seconds)` (contract `ITimeTrialClock`) clamps to `[0, maxClockSeconds]`; refused while ending, over or paused. A negative result of 0 fails the run on the next frame (`TimedOut`).
- R1.3 Elapsed run seconds tick with the countdown; `EndRun` records them.

### 5.2 Checkpoints (D6–D8)
- R2.1 `TimeTrialSystem.ResetForRun` (bind, `generator.Regenerated`) lays the checkpoints and their awards.
- R2.2 Authored `Checkpoint` records (any lateral) are used as-is, sorted, clamped to before the end-zone start.
- R2.3 Auto layout: `autoCount` evenly between `firstCheckpointMeters` and `EndZoneStart`; a spot inside a keep-out (± `featureClearance`) slides forward past it, else back; a spot that cannot be cleared is dropped.
- R2.4 A gate stands at each checkpoint (pooled, built once the generator has settled road there, culled behind): two pylons and a beam overhead, coloured pending / clean / dirty.
- R2.5 Crossing pays `award` (+ `cleanCheckpointBonus` when clean) and resets the clean flag.

### 5.3 Bonuses and penalties (D9–D11)
- R3.1 `NotifyHit()` from the hull's `Damaged` (hard, or any with `scrapeBreaksClean`), every laser / car hit, every fall.
- R3.2 `NotifyPatrolKill(cause)` → `+patrolKillBonus` per D10.
- R3.3 `NotifyLifeLost()` → `−lifePenaltySeconds` and the clean flag broken.
- R3.4 Each adjustment raises `ClockAdjusted(delta, kind)` for the HUD.

### 5.4 Life loss in Arcade (D11)
- R4.1 Hull 0 with more than one life left → explode, respawn, revive. Hull 0 on the last life → `BeginFail(Destroyed)` (GAME OVER).
- R4.2 Arrest with more than one life left → the same, plus the patrol redeployed behind. On the last → `BeginFail(Caught)`.
- R4.3 Beat the Clock keeps today's rules: every loss ends the run.

### 5.5 HUD, map, panel
- R5.1 Timer: `SS.t` under a minute (`M:SS` above), low-time tint + pulse under `lowTimeWarningSeconds`, punch + green/red flash on each adjustment.
- R5.2 The goal line: `CHECKPOINT n/N` (Arcade), `BEAT THE CLOCK` (BtC).
- R5.3 Floaters through `FloatingTextSystem`.
- R5.4 Minimap markers at each checkpoint (passed ones dimmed).
- R5.5 Mission Complete TIME TRIAL section: time left, elapsed, checkpoints (clean / total), patrols downed, lives lost.

## 6. Tunables (`TimeTrialDefinition`)

| Group | Fields |
|---|---|
| Mode | `mode` |
| Clock | `timeLimitSeconds` (BtC), `startSeconds` (Arcade), `maxClockSeconds`, `lowTimeWarningSeconds` |
| Checkpoints | `autoCount`, `firstCheckpointMeters`, `featureClearance`, `paceShare`, `paceFloorKmh`, `graceSeconds`, `awardBand` |
| Bonuses | `cleanCheckpointBonus`, `patrolKillBonus`, `trafficKillPays`, `scrapeBreaksClean`, `lifePenaltySeconds` |
| Hazards | `patrolEnabled` |
| Gate | `gatePrefab`, `gateHeight`, `gatePendingColor`, `gateCleanColor`, `gateDirtyColor` |

`RunnerLevelDefinition`: `timeTrial`; objective type `FinishWithTime` (`minSecondsLeft`).
Debug: the TIME TRIAL page (order 7) — every numeric row, written to the asset, mirrored onto the clone.

## 7. Code touch points

| Area | Change |
|---|---|
| `Contracts/TimeTrialContracts.cs` | `ITimeTrialClock`, `ITimeTrialState`, `TimeAwardKind` |
| `Runner/TimeTrial/` | `TimeTrialDefinition`, `TimeTrialSystem`, `CheckpointGate`, `TimeTrialDebugPage` |
| `GameManager` | bind, `AddTime`, elapsed counter, `TimeLimit`, hyperspace / patrol gating, `LoseLife`, hit / kill forwarding, Mission Complete data |
| `RunnerLevelDefinition` | `timeTrial`, `FinishWithTime`, `ObjectiveContext.timeLeft/finished` |
| `PolicePatrol` | `Killed` event + cause, `ReleaseArrest()` |
| `ShipHealth` | `Revive()` |
| `ShipMotor` | `ForceRespawn(reason)` |
| Track | `TrackPlacementKind.Checkpoint`, `Build` ignores it, editor palette / gizmo / checks, reroll keeps it |
| HUD | `RaceHud` timer + goal line + floaters, `ChaseMinimap` markers, `MissionCompleteScreen` section |
| `MenuTextLibrary` | HUD / popup / panel / debug strings, four languages |
| Editor | `TimeTrialInstaller` (definitions, prefab, PF_Systems wiring, test levels), sandbox entry |

## 8. Invariants that must survive

- No time trial set → the run is exactly as before (hyperspace offered, every loss ends the run, elapsed stat equal to `TimeLimit − TimeRemaining`).
- Gameplay never writes a settings asset; the debug page writes the asset and mirrors the clone.
- Distance from the track start is the only coordinate checkpoints use; no trigger colliders.
- The system boots and idles alone in an empty scene (sandbox).
- Serialized enums append only (`RunnerObjectiveType`, `TrackPlacementKind`, `MenuTextId`).

## 9. Milestones

| # | Content | Done when |
|---|---|---|
| M1 | PRD; definition; level field; `FinishWithTime`; `ITimeTrialClock` + `AddTime` + elapsed; hyperspace / patrol gating | A BtC level wins at any speed off a ramp before 0; time-out → LoseTimeOut; a gap still loses |
| M2 | `Checkpoint` kind; `TimeTrialSystem` layout + awards + crossing; gates; installer + prefab | An Arcade clock is fed by each gate; finishable at cruise × paceShare |
| M3 | Clean bonus, kill event + bonus, `LoseLife` respawn (hull 0 + arrest), `Revive`, `ReleaseArrest` | Each adjustment fires once with the right sign; last life → GAME OVER |
| M4 | HUD timer, floaters, goal line, minimap markers, localization, Mission Complete section | Strings in four languages; the panel shows the rows |
| M5 | Debug page, sandbox, editor palette / gizmo / checks, docs, sample levels | Validator clean; Sandbox Smoke Test passes |

## 10. Verification

- Compile through `Logs/Editor.log` / the Roslyn .rsp build.
- In play (`FiniteRunner_Test` over unity-mcp): BtC win at sub-Light-Speed; BtC time-out; Arcade per-gate deltas match the awards; hull 0 → respawn, −1 life, −N s; finisher kill → +bonus; laser hit before a gate → no clean bonus; lives drained → GAME OVER → Store; retry re-lays checkpoints and resets the clock.
- A normal level and a race level unchanged.
- Validate System Independence; Build System Sandboxes + Run Sandbox Smoke Test.

## 11. Documentation to update

`.claude/rules/runner-timetrial.md` (new), CLAUDE.md (design bullet + rules index), `runner-track.md` (Checkpoint kind), `runner-hud-screens.md` (timer, floaters, panel section), `runner-ship.md` (Arcade life loss).

## 12. Risks

| Risk | Mitigation |
|---|---|
| Respawn after hull 0 lands on a ship already destroyed (`IsDestroyed` blocks hits) | `Revive` on `Respawned`; the forced fall damage is refused while destroyed, so nothing double-counts |
| A respawn skips checkpoints | Each crossed checkpoint pays (D8), none clean |
| Auto checkpoints cluster where features are dense | Slide off keep-outs forward then back; drop the unplaceable; the next award covers the longer gap |
| Award too generous on an upgraded ship | `paceShare` and `maxAward` bound it; tuned on the debug page |
| Arrest respawn loop (cruiser re-arrests at once) | `ReleaseArrest` redeploys behind; the respawn holds the patrol and releases it at `respawnMinPatrolGap` |

## 13. Open questions

- Should BtC show splits (checkpoints with no time) for pacing? (Not in this pass.)
- Best-time per level in `SaveData` for a replay incentive? (Not in this pass.)
