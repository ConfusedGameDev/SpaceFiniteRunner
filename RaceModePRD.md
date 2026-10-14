# PRD — Race mode: rival hover ships racing the player to the end ramps

| | |
|---|---|
| **Status** | 2026-10-14: M1–M3 in and played (laser avoidance reworked after a soak: decided-gate zones, measured vertical-beam width, side-first planning — 60 gate passes, no wrecks). M4 in: `RivalKillCam` (clock owner on the DodgeSlowMo protocol, rig focus shot, ship autopilot, shield + grace blink, countdown frozen, bounty paid on the cut back) — verified in play with captures. Deviation: the patrol is not paused, its timers ride the slowed clock. M5 in: the RACE debug page (sliders mirrored onto the live clone, queued wreck-nearest / wreck-all / finish-all, ghost wireframes), `Sandbox_Race`, the Mission Complete RACE section (place, wrecks, bounties — display only), docs (`runner-race.md`, CLAUDE.md, HUD, cameras, ship). Validator clean; the sandbox smoke test still to run on a saved scene. Gaps: R4.2's forward kick, R4.3's weighted shove. |
| **Branch** | `race-mode` |
| **Scope** | Runner game only (`FiniteRunner_Test`, campaign runner levels). The city chase, `Campaign` and `SaveData` are untouched. |
| **Related** | `PatrolDuelPRD.md` (the analytic ram / shove / laser-kill precedents), `OncomingTrafficPRD.md` (contact contract, `TrackObstacles`, per-level definition pattern), `.claude/rules/runner-ship.md`, `.claude/rules/runner-traffic.md`, `.claude/rules/runner-hud-screens.md`, `.claude/rules/cameras.md` |

---

## 1. Problem

Every runner level asks the same question: can you reach Light Speed and get off the end ramps in
time? Some levels should ask a different one: **can you beat these other pilots there?** A race
level puts a pack of rival hover ships on the same finite track, all heading for the end ramps.
The level goal is a finishing position. That gives the speed game a direct, readable opponent ahead
of you (the patrol is behind you), and it makes the hazards the player already knows (lasers, open
edges, flat sweeps) into weapons: shove a rival off the road or bait it into a beam, and it is out.

## 2. Goals

1. A level can declare a **race**: N rivals (around 3–9) start on a grid with the ship and race to the
   end ramps on the same countdown.
2. The HUD shows a **live position** ("POS 3/8"), and the minimap shows **every rival** as a dot.
3. A new mandatory objective, **Race Position**, with three modes:
   - **Win** — finish 1st.
   - **Top X** — finish in position X or better.
   - **Exactly X** — finish in position X exactly.
4. Rivals can be **rammed and shoved**. A rival that **hits a laser, falls off an open edge, goes
   through a gap between the end ramps, or is rammed to death explodes** and is out of the race (DNF).
5. A rival destroyed near the ship plays a **kill cam**: about a second of heavy slow-mo with the
   camera cut to the explosion, then back to the ship.
6. Rivals feel like **pilots with personalities** (a fast, clean one; a reckless one; a slow one),
   not a rubber-banded blob, but the pack stays in sight.
7. **Destroying a rival pays a money bonus** (a bounty), banked like collectible money, with the gold
   `+$N` floating text.
8. Zero cost in levels without a race; no allocation mid-run.

## 3. Non-goals

- **Laps, checkpoints, multi-path.** The track stays a single finite run to the end ramps.
- **Rivals on `HoverShip` physics.** Rivals drive the analytic `TrackBody`, like the patrol (D1).
- **Rivals in the city chase.** `PoliceEscape` is not touched.
- **Oncoming traffic, hyperspace and the Light Speed requirement in race levels** (D5).
- **Patrol ↔ rival interaction.** The patrol only ever hunts the player (D14).
- **Rivals attacking the ship on purpose** (a rival tug of war like the patrol's). Rivals push and
  ram only as a by-product of fighting for the line (R4).
- **Persistent rival identities / a championship table** across levels.
- **Race positions saved to `SaveData`.** The result only decides the win.

## 4. Settled design decisions

| # | Decision |
|---|---|
| D1 | **Rivals are `TrackBody` racers, not traffic cars.** Traffic cars (`Runner/Traffic/TrafficCar.cs`) are a one-way `Distance -= Speed*dt` with no physics. They cannot jump, fall or drive toward the end. The patrol's `TrackBody` (`Runner/Simulation/TrackBody.cs`) already handles ramps and jumps, flat-sweep grip, open edges, the end lip (`ReachedEnd`), loops and tubes, and the pickup sweep. It is analytic, with no PhysX queries, so N of them are affordable. |
| D2 | **Enabled per level.** `RunnerLevelDefinition.race` references a `RaceDefinition`; empty means no race. `PF_RaceSystem` is hand-placed under `===SYSTEMS===` and idles unbound. |
| D3 | **A destroyed rival is DNF.** It leaves the standings for good and everyone behind it moves up a place. There is no redeploy. |
| D4 | **Kill cam = cut + autopilot.** A rival destroyed in full sim, within `killCamRange` of the ship and in front of the camera, plays the kill cam if the clock is free. For `killCamSeconds` (real time, about 1–1.5 s), time drops to `killCamTimeScale` and the camera cuts to a shot framing the explosion. Meanwhile the ship holds its line and cannot be hurt. Then it cuts back. |
| D5 | **Race levels keep the patrol and drop the rest.** The patrol hunts as normal. Oncoming traffic is off and HIT HYPERSPACE is off: it moves `EndDistance` mid-run, which would break the finish order. A race level authors no Reach Speed objective, so there is no Light Speed requirement at the lip. |
| D6 | **Win = position goal met at the player's lip AND the ship took a ramp.** The objective is judged when the ship crosses `EndDistance`, using the `HoldsSpeedGoals` pattern (`GameManager.cs:603`), not latched mid-run. Going through a gap is still `MissedRamp`. |
| D7 | **Early fail.** The run fails the moment the goal becomes impossible (R5.3), with its own banner reason. The player is not made to drive to the end of a race that is already lost. |
| D8 | **Finish order is lip order.** A racer's place is fixed when it crosses `EndDistance` on a ramp. The live position of racers still on the track is by distance. DNFs rank below everyone. |
| D9 | **Pace = skill profile + soft rubber band.** Each rival entry has a cruise speed (as a fraction of the ship's Light Speed), aggression, mistake rate and orb appetite. A rubber band, capped at a few percent, nudges rivals toward the ship's pace by gap, so the pack stays in sight without erasing skill differences. |
| D10 | **Two fidelities.** A rival runs the full `TrackBody` sim only inside a window around the ship on settled road (`generator.SettledDistance`). Outside it, the rival is a **ghost**: distance plus its pace profile, invisible, and it cannot crash. Ghosts still finish (D8). |
| D11 | **Contact is analytic, both ways**, reusing the patrol's ram and shove (`PolicePatrol.RamCheck` / `TakeRam` / `Shove`). No colliders. |
| D12 | **Pooled, never instantiated mid-run.** Rival shells and their pose objects are built at bind. |
| D13 | **Bounty for player kills.** A rival destroyed **because of the player** pays `rivalBounty` (per rival entry, so a tougher rival is worth more). It counts as the player's kill if the rival was rammed to death, or if its laser hit, fall or gap DNF comes within `creditWindow` seconds of its last contact with the ship (a shove off the edge, a ram that knocked it into a beam). A rival that wrecks itself through its own mistake pays nothing. The money follows exactly the collectible-money rules: banked at once and shown on the money HUD. The mission's wallet baseline rule (R5.6) also applies: a GAME OVER takes the wallet back to what it was before the mission started. |
| D14 | **The patrol only attacks the player.** It never targets, rams, shoves, duels or arrests a rival, and rivals never block, damage or destroy it. The two pass through each other, and the patrol's driver ignores rivals when picking its line. Rivals also ignore the patrol. The patrol's orb-seeking can still take an orb a rival wanted; whoever sweeps it first gets it. |

## 5. Functional requirements

### 5.1 Grid and start (D2, D12)

- **R1.1** At `Bind`, `RaceSystem` clones the `RaceDefinition` and builds one shell per rival entry
  (model, `TrackBody`, driver, map dot). These are parked under the system.
- **R1.2** At `ResetForRun`, rivals are placed on a grid behind and around the ship's start pose, in
  rows of `gridColumns`, spaced by `gridRowSpacing` / `gridLaneSpacing`. The ship's own grid slot
  is `playerGridSlot` (default: last row, so the player starts at the back).
- **R1.3** Rivals launch on the same launch impulse as the ship (the `GameManager.Launch` path that
  relaunches the patrol). The countdown is the level's normal one.

### 5.2 Driving (D1, D9)

- **R2.1** `RivalDriver` is extracted from `PatrolDriver` (`Runner/GameFlow/PatrolDriver.cs:32`).
  It keeps the **line, orb-seeking, ramp line-up and sweep-brake (grip-limit) logic** and drops the
  chase-only parts (steer-for-the-ship, encounter intents). `PatrolDriver` then calls the shared
  parts, so the patrol's behaviour does not change.
- **R2.2** **Racing line.** A rival holds a preferred lateral (from its grid lane, drifting with
  `aggression`). It steers round laser gates and ramps it does not mean to jump via
  `TrackObstacles.Collect` / `FreeIntervals` / `Nearest` (`Runner/Track/TrackObstacles.cs`).
- **R2.3** **Mistakes.** With probability `mistakeRate` per hazard, the driver mis-judges it. It
  brakes late for a flat sweep (and may slide off), shaves an open edge, or commits to a gap at the
  end. This is how rivals DNF on their own.
- **R2.4** **Orbs.** Rivals pick up boost orbs with probability `orbAppetite` per reachable orb,
  through the `TrackBody` pickup sweep, and use them up as the patrol does. They ignore repair orbs.
- **R2.5** **Pace.** Target speed is `cruiseFraction × LightSpeed × rubber(gap)`. Here `rubber`
  maps the signed gap to the ship onto `[1 − rubberBand, 1 + rubberBand]` over `rubberRange` metres.
  Boosts push past target and bleed back like the ship's.
- **R2.6** **Rival ↔ rival.** Rivals in full sim keep `rivalSeparation` laterally (the patrol's lane
  clamp). Rivals never destroy each other.

### 5.3 Streaming fidelity (D10)

- **R3.1** **Full-sim window.** `[ship.Distance − fullSimBehind, min(ship.Distance + fullSimAhead,
  SettledDistance)]`. Inside it the rival runs `TrackBody` at `GameSettings.simSubsteps` and is
  visible.
- **R3.2** **Ghost.** Outside the window the rival advances `distance += ghostSpeed·dt`. Here
  `ghostSpeed` is its R2.5 target pace scaled by `ghostPaceFactor` (< 1 by default, as a stand-in
  for the time it would lose to sweeps and hazards). Its model is hidden. A ghost cannot crash and
  picks up nothing.
- **R3.3** **Handoff to full sim.** When a ghost enters the window, it is re-seated with a fresh
  `TrackBody` at its distance, speed and the nearest free lateral (`TrackObstacles`). Re-seating is
  delayed while that point is inside a loop, tube or ramp span, or in mid-air terrain. The window edge
  is out of fog range, so the handoff is never seen.
- **R3.4** **Handoff to ghost.** When a rival leaves the window it keeps distance and speed. Any
  airborne or off-track state is resolved first: a falling rival finishes falling and is DNF.
- **R3.5** **Track regenerated / restart.** `generator.Regenerated` and `ResetForRun` put everything
  back on the grid.

### 5.4 Contact (D11)

- **R4.1** **Ship rams rival from behind.** This is the patrol's rear-ram test, with the
  ship as the rammer. The ship loses `ramSpeedCost` of its speed. The rival takes one point off its
  `ramHull` and is pushed forward and sideways.
- **R4.2** **Rival rams ship from behind.** The ship gains a small forward kick, and the rival loses
  the same speed share.
- **R4.3** **Side contact.** Overlapping side by side, both are shoved apart. The ship gets
  `ShipMotor.AddLateralShove` and the rival gets a lateral impulse on its `TrackBody`. The heavier
  shove goes to whoever is moving sideways faster, so steering into a rival wins the exchange.
  A rival shoved over an open edge falls and is DNF (R5.1).
- **R4.4** Rival contact never damages the ship's hull. It costs speed and line, not health.
- **R4.5** No contact while the ship is not `Steady`, during the kill cam, or once the run is
  decided.

### 5.5 Destruction, standings, objective (D3, D6–D8)

- **R5.1** **A rival is destroyed by:**
  - a laser beam (`LaserGate.Touches`, the patrol's `ConsumeLaserKill` path);
  - falling off an open edge (`OnLeftTrack`);
  - reaching `EndDistance` without a ramp under it;
  - its `ramHull` reaching 0.

  When destroyed it plays a fireball via `ExplosionVfx.SpawnFireball` at its pose, scaled by
  `explosionScale`, hides its model and dot, and becomes DNF. A destroyed rival raises the
  `RivalDestroyed(rival, cause)` event.
- **R5.2** **Standings** (`IRaceStandings` in `Contracts`):
  - `RacerCount` = rivals + ship;
  - `PlayerPosition` = 1 + finished rivals + racing rivals ahead of the ship;
  - `FinishedCount`, `RacingCount`, `DnfCount`;
  - a per-racer read (distance, state, finish place) for the minimap.

  A rival's `ReachedEnd(tookRamp: true)` fixes its finish place (D8). It flies on off the ramp
  ballistically and is then parked.
- **R5.3** **Objective `RacePosition`** is appended to `RunnerObjectiveType` (`RunnerLevelDefinition.cs:11`).
  - **Fields:** `raceMode` (Win / TopX / Exactly) and `racePosition` (X).
  - **`Satisfied` / `Progress`** widen from `(speedKmh, jumps)` to an `ObjectiveContext` struct
    (speed, jumps, position, standings). `Summary` reads "FINISH 1ST" / "FINISH TOP 3" / "FINISH 4TH"
    (localized). `Progress` shows the live position.
  - **Judged at the lip** (D6). At the ship's `OnReachedTrackEnd`, the place is `FinishedCount + 1`.
  - **Impossible** (D7). Let X be the target, F = rivals finished, R = rivals still racing.
    - Win: F ≥ 1.
    - TopX: F ≥ X.
    - Exactly: F ≥ X, or F + R + 1 < X (too few racers left to finish behind).
- **R5.4** Early fail goes through `BeginFail` with a new `RunOutcome.RaceLost` (`GameManager.cs:81`)
  and a new `MenuTextId` reason ("YOU WERE BEATEN TO THE FINISH" / "POSITION OUT OF REACH"), in all
  four languages. Losing at the lip with the wrong place (a ramp taken, goal not met) uses the same
  outcome.
- **R5.5** Win: the existing win path (`BeginEscape` → `FinishWin`). The Mission Complete panel shows
  the finishing place and the bounty total (extra stat rows, display only: the bounty is already
  banked, see R5.6).
- **R5.6** **Bounty** (D13).
  - **Tracking.** Each rival records `lastShipContactTime` whenever R4.1–R4.3 touch it.
  - **Event.** When it is destroyed, `RivalDestroyed(rival, cause, byPlayer)` sets `byPlayer` if the
    cause is ram hull, or if `now − lastShipContactTime ≤ creditWindow`.
  - **Paying out.** `GameManager` is the composition root, so it answers `byPlayer` kills by paying
    `rivalBounty` through a new `CollectibleManager.AwardMoney(amount)`. That method takes the same
    path as a Money pickup (`RunMoney`, `PlayerStats.AddMoney`, `MoneyChanged`), so `MoneyHud`
    counts up and `RaceHud` shows the gold `+$N` floating text with no new HUD code.
  - **Independence.** `RaceSystem` never finds `CollectibleManager` itself.
  - **Wallet baseline.** The bounty follows the mission wallet rule. It does not need any rule of its
    own, because the existing money path already does this:
    - **Baseline:** `MissionSession.Begin` snapshots `WalletAtStart = PlayerStats.Balance` before the
      city (after Store purchases). `GameManager` holds it as `walletAtMissionStart`.
    - **Clear the level:** the mission payout is added on top. Retrying after a win re-baselines to
      the new balance (`GameManager.cs:1094`).
    - **Ordinary failed run:** a retry keeps the money earned so far in the mission.
    - **GAME OVER (last life):** `PlayerStats.ForfeitMission(walletAtMissionStart)`
      (`PlayerStats.cs:220`) puts the wallet back to the baseline and saves. Every bounty and
      collectible earned during the mission is lost.
  - A kill-cam kill shows the `+$N` after the cut back to the ship, so it is not lost under the
    cinematic.

### 5.6 HUD and minimap

- **R6.1** `RaceHud` shows a position readout ("POS 3/8", localized label, value formatted by
  `StatFormat`) near the distance readout, when an `IRaceStandings` is bound. It pulses when the
  player's place changes. The objective line comes from the normal objective clone (R5.3).
- **R6.2** `ChaseMinimap` draws one pooled dot per rival through `MapPoint(distance)`, using the
  `mapPoliceIcon` pattern, with its own rival colour. Ghost rivals are drawn too, so you can see the
  whole field. The dot hides on DNF and stays at the finish marker once finished.
- **R6.3** Optional: a short floating text on overtakes ("+1 / −1") through `FloatingTextSystem`.

### 5.7 Kill cam (D4)

- **R7.1** **Trigger.** It plays on `RivalDestroyed` when all of these hold:
  - the rival was in full sim;
  - its distance from the ship is ≤ `killCamRange`;
  - the clock is free (no `DodgeSlowMo` / `LoopSlowMo` / `DuelSlowMo` owner);
  - the ship is not on the end run-up;
  - the run is not decided;
  - `killCamCooldown` has passed.

  Otherwise the explosion just plays in the normal view.
- **R7.2** **Clock.** A new time-scale owner, `RivalKillSlowMo` (beside `DodgeSlowMo`), follows the
  same ownership protocol: it enters only when the scale reads 1 and restores what it changed. It
  eases `Time.timeScale` / `fixedDeltaTime` to `killCamTimeScale`, holds for `killCamSeconds` of
  unscaled time, then eases back.
- **R7.3** **Camera.** A new `RunCameraDirector` / `OrbitCameraRig` **focus-target** mode. Today the
  cinematic mount (`SetCinematic`, `OrbitCameraRig.cs:236`) is always seated on the followed vehicle.
  The new mode plants a vcam at an offset from an arbitrary world pose (the rival's last pose,
  side-on, toward the camera's side of the track), looks at it, and cuts back to the chase camera
  on release. It does not need an `ICameraTarget`.
- **R7.4** **Ship during the kill cam.**
  - Stick, dash and brake are ignored; the throttle is held. The ship keeps its current lateral and
    follows the guide.
  - It is invulnerable for the kill cam plus `killCamGrace` afterward, with the normal blink.
  - The patrol's encounter timers and the countdown pause for the kill cam's real-time length, so it
    never costs the player.
- **R7.5** A second rival destroyed during a kill cam does not restart it. Its explosion simply plays.

## 6. Tunables

### 6.1 New `RaceDefinition` (ScriptableObject, cloned at bind, Odin-styled)

| Group | Fields |
|---|---|
| Rivals | `rivals` — list of `RivalEntry { name (MenuTextId), prefab, scale, yawOffset, hoverHeight, halfLength, halfWidth, mapColor, cruiseFraction, aggression, mistakeRate, orbAppetite }` |
| Grid | `gridColumns`, `gridRowSpacing`, `gridLaneSpacing`, `playerGridSlot` |
| Pace | `rubberBand` (± fraction), `rubberRange` (m), `ghostPaceFactor` |
| Fidelity | `fullSimBand` (`[MinMaxSlider]` behind/ahead, m) |
| Contact | `ramHull`, `ramSpeedCost`, `ramKick`, `sideShove`, `rivalSeparation` |
| Kill cam | `[ToggleGroup] killCam`: `killCamRange`, `killCamTimeScale`, `killCamSeconds`, `killCamGrace`, `killCamCooldown`, camera offset / distance |
| Bounty | `rivalBounty` (on each `RivalEntry`), `creditWindow` (s) |
| FX | `explosionScale` |

### 6.2 `RunnerLevelDefinition`

- `race` (a `RaceDefinition`, empty = no race). When it is set, the definition validation warns if
  `traffic` is also set or a Reach Speed objective is authored, and `GameManager` disables
  hyperspace for the run (D5).
- `RunnerObjective` gains `raceMode` and `racePosition`, shown only when the type is `RacePosition`.

### 6.3 Reused, not duplicated

- `TrackBody`, the shared driver logic from `PatrolDriver`, and `TrackObstacles`.
- `ExplosionVfx`, `GameSettings` explosion textures, `LaserGate.Touches` and `ShipMotor.AddLateralShove`.

### 6.4 Debug menu

- A **Race** page. It edits the `RaceDefinition` asset (`DebugAssetEdits.Touch`) and mirrors to the
  live clone.
- Actions: kill nearest rival (test the kill cam), DNF all, finish all, and a toggle to show ghost
  rivals as wireframes.

## 7. Code touch points

| Area | Change |
|---|---|
| `Runner/Race/` (new) | `RaceDefinition`, `RaceSystem` (bind, pool, fixed tick, standings, events), `Rival` (shell + `TrackBody` + state), `RivalDriver`, `RivalKillSlowMo` |
| `Contracts/` | `IRaceStandings` (read-only standings for HUD, minimap, objectives) |
| `Runner/GameFlow/PatrolDriver.cs` | Split out the line, orb, ramp and sweep-brake logic for reuse; the patrol's behaviour is unchanged |
| `Runner/GameFlow/RunnerLevelDefinition.cs` | `race` field; `RacePosition` objective type; `ObjectiveContext` |
| `Runner/GameFlow/GameManager.cs` | Bind `RaceSystem`; widen objective evaluation; lip judgement; early fail; `RunOutcome.RaceLost`; gate hyperspace and traffic; pause the countdown during the kill cam |
| `Runner/GameFlow/RunCameraDirector.cs`, `Cameras/OrbitCameraRig.cs` | Focus-target mode |
| `Runner/HUD/RaceHud.cs`, `Runner/HUD/ChaseMinimap.cs` | Position readout; rival dots |
| `MissionCompleteScreen` | Finishing-place and bounty stat rows |
| `Runner/Collectibles/CollectibleManager.cs` | `AwardMoney(amount)`, the Money-pickup path without a pickup |
| `Runner/GameFlow/PolicePatrol.cs` | No change beyond the driver split: the patrol never reads `IRaceStandings` (D14) |
| `UI/MenuTextLibrary` | Position label, objective summaries, loss reasons, rival names — all four languages |
| `Runner/Editor/SystemSandboxes.cs` | `Sandbox_Race` entry |
| `03.Prefabs/FiniteRunner/` | `PF_RaceSystem` under `===SYSTEMS===` in `FiniteRunner_Test` |

## 8. Invariants that must survive

- **No colliders, no triggers** on rivals. All contact is analytic, in track coordinates.
- **The ship stays a kinematic cast-based follower.** Rival contact only calls the ship's existing
  shove and speed APIs.
- **Gameplay never writes a settings asset.** Rivals read a runtime clone; the debug page writes the
  asset.
- **Tunables live in `RaceDefinition`**, not on `RaceSystem`.
- **Systems stay independent.** Unbound, `RaceSystem` boots and idles in an empty scene.
  `GameManager` binds it. HUD and minimap reach it only through `IRaceStandings`. No new
  cross-system `Find*Object*<T>`.
- **One time-scale owner at a time.** `RivalKillSlowMo` follows the existing protocol.
- **Domain reload is off.** Subscribe in `OnEnable`/`OnDisable`; reset the pool, RNG and standings
  in `Bind` / `ResetForRun`.
- **Distance from the track start is the coordinate**, and speeds are m/s.
- **Serialized enums are append-only** (`RunnerObjectiveType`, `MenuTextId`).

## 9. Milestones

| # | Content | Done when |
|---|---|---|
| M1 | `RaceDefinition`, `RaceSystem`, `Rival`, `RivalDriver` (extracted from `PatrolDriver`), grid, launch, full-sim / ghost (R3), lip finishing, `IRaceStandings` | On a race level, rivals launch from the grid, race, and finish off the ramps in an order that the debug overlay prints. No rival pops in view. The patrol plays exactly as before. |
| M2 | `RacePosition` objective, lip judgement, early fail, `RunOutcome.RaceLost`, HUD readout, minimap dots, hyperspace/traffic gating, localization | All three modes win and lose correctly, including early fail and the Exactly "too few racers" case. POS and dots track the field. |
| M3 | Contact (R4), destruction (R5.1), DNF, mistakes (R2.3), bounty (R5.6) | Ramming, shoving off an edge, baiting into a laser and gap DNFs all work, move the player up and pay the bounty with `+$N`. Rivals sometimes wreck on their own and pay nothing. The patrol never touches a rival. |
| M4 | `RivalKillSlowMo`, focus-target camera, ship autopilot and invulnerability, countdown pause | A kill near the ship plays the kill cam once. It never fights dodge, loop or duel slow-mo, and the player never takes a hit or loses time from it. |
| M5 | Debug page, sandbox entry, docs (§11), tuning pass, Mission Complete place row | Sandbox smoke test passes, the System Independence validator is clean, and debug sliders change the live race. |

## 10. Verification (every milestone)

- **Play `FiniteRunner_Test`** with a race level, and once with `race` empty, to confirm the old
  runner is unchanged, the patrol included.
- **Profiler:** cost of `RaceSystem.FixedUpdate` with 8 rivals at Light Speed. Budget: under 0.5 ms
  per fixed tick total.
- **Tools → Refactor → Validate System Independence** is clean, and **Run Sandbox Smoke Test**
  reports no errors in `Temp/SandboxSmoke.txt`.
- **Retry and GAME OVER paths** reset the grid and standings, and a retry starts a clean race.
- **Compile check** via `Logs/Editor.log` after each step.

## 11. Documentation to update (M5)

- `.claude/rules/runner-race.md` (new, path-scoped to `Runner/Race/**`).
- `CLAUDE.md` gets a "Race levels" bullet in the runner design section and a rules-index row.
- `runner-ship.md` gets a note on the `PatrolDriver` split; `runner-hud-screens.md` gets the
  position readout and rival dots; `cameras.md` gets the focus-target mode.

## 12. Risks

| Risk | Mitigation |
|---|---|
| Ghost ↔ full-sim handoff visibly pops or leaves a rival in an impossible spot | Window edges stay beyond fog range. Re-seating waits until the road is clear (R3.3). |
| A ghost's pace drifts from its full-sim pace, so rivals finish "too well" off-screen | `ghostPaceFactor` tuning. Log per-rival full-sim average speed vs ghost speed in the debug overlay. |
| The rubber band feels unfair (rivals impossible to drop or to catch) | Keep it soft and capped (D9). Skill profiles carry the difficulty. |
| Kill cam disorients at ~1600 m/s | Autopilot, invulnerability and a countdown pause (R7.4). Cooldown and range limits. Off on the run-up. |
| Kill cam competes with dodge, loop or duel slow-mo | Single-owner protocol: if the clock is taken, no kill cam (R7.1). |
| Patrol duel takes the ship's controls while rivals pass | Accepted: it is part of the chase. Tune patrol cadence lower on race levels via its `PatrolDefinition`. |
| `PatrolDriver` extraction changes patrol behaviour | M1 gate: the patrol plays identically on a non-race level. |
| CPU cost of N `TrackBody` sims | Only in-window rivals run full sim. Profile in M1. |

## 13. Open questions (for the tuning pass or later)

- Should the bounty scale with the rival's place at the time (taking out the leader pays more)?
- **Quitting mid-mission (existing behaviour, not race-specific).** Money is banked the moment it is
  earned, and only a GAME OVER rolls it back. Quitting from the pause menu to the main menu
  therefore keeps the mission's earnings so far. If earnings should only count once the level is
  cleared, that quit path should also roll back to the baseline.
- Can a rival be **armed** by a blue or purple orb into a one-shot shove, mirroring the patrol duel?
- Should a level be able to override the patrol's cadence for races?
- Rival names and taunts as RPG lines on overtakes?
- Should the Mission Complete panel show the full finishing order?
