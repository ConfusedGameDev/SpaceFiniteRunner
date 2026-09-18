# PRD — Physics-based runner ship with spline assist

| | |
|---|---|
| **Status** | Implemented M0–M7 (2026-09-18), play checks and tuning pending. Was: Draft. Decisions settled in the grill session on 2026-09-18. Open questions are listed at the end. |
| **Branch** | `feature/finiterunnerPhysicsBased` |
| **Source plan** | `SpaceShipUpdatePlan.md` (superseded by this document where the two disagree) |
| **Scope** | Runner game only (`FiniteRunner_Test`, campaign runner levels). The city chase is untouched. |

---

## 1. Problem

The runner ship is fully scripted along the spline. Speed comes from one launch impulse, a
constant bleed and pads. Steering sets a lateral position that is clamped to the band. Jumps and
the dash are fixed profiles. The player has no speed control and nothing to lose in a curve. On
top of that, the edge is a wall, so no bad line is ever punished. The police are a 1D rubber band
that never steers, jumps or collects anything.

We want the ship to feel like a vehicle, with speed, grip and momentum. The design still needs
the track to hold the ship on loops, in tubes and on banked curves.

## 2. Goals

1. The player controls speed with an analog throttle and brake.
2. Taking a flat, sharp curve too fast slides the ship out, and on an open edge it falls off.
3. Falling costs time, not the run. The ship respawns and relaunches.
4. Loops, tubes, jumps and the barrel roll feel the same as today.
5. The dash is a physical shove.
6. The police drive on the same simulation: they chase, collect orbs and dodge or jump ramps.

## 3. Non-goals

- Changing the city chase, the campaign or mission flow, or the win condition (the
  `RunnerLevelDefinition` objectives still decide it).
- Mobile as a platform. Touch gets auto-throttle with no brake, and no more (§5.1).
- Multi-path track (still unbuilt, not part of this work).
- Real rigidbody physics. The simulation is in track space (§4).

## 4. Settled design decisions

| # | Topic | Decision |
|---|---|---|
| D1 | Physics style | Simulated in **track space**: the state is distance, lateral and height plus their velocities, and forces act on them. It is deterministic, cannot tunnel through the track, and is shared by the ship and the police. Only an off-track fall leaves track space (world-space ballistic). |
| D2 | Tick | Fixed step (`Time.fixedDeltaTime` = 0.02) with substeps. The rendered pose is interpolated in `Update`. |
| D3 | Speed model | RT throttles up to a per-ship **cruise cap**. Only orbs push past it. Above cruise the old passive bleed pulls the speed back to cruise. LT brakes. Speed stays uncapped overall, so orbs remain the way to Light Speed. |
| D4 | Curves (grip only tested on flat curves) | **Banked sweeps always hold**: no slip test, and the ship's up follows the track. **Flat or under-banked sweeps test grip**: the demand `v²κ` is compared against a grip that grows with speed (§5.2). Any excess slides the ship outward. Danger is authored by which sweeps the generator leaves unbanked. |
| D5 | Open edges | Edges open **only on the outside of flat or under-banked sweeps**. Straights, the inside of curves, banked sweeps, ramp corridors, tubes and loops keep walls. The decorator and `IsEdgeOpen` read the **same** per-section flag, so what you see is what kills you. |
| D6 | Edge grace | Falling off requires both `|lateral| > halfWidth + edgeOverhang` and staying past it for `edgeGraceSeconds`. Counter-steer inside the grace window pulls the ship back. This covers dashes over the edge too. |
| D7 | Respawn placement | Respawn at the **first safe stretch past the fall point**. You lose time, not distance. This also covers stretches crowded with features. |
| D8 | Clock during fall | The countdown **keeps running** during the fall and the respawn. The lost time is the penalty. |
| D9 | Stall | "Stalled" fires after `stallGraceSeconds` at speed 0 **whatever the input, unless the throttle is pressed**. It is skipped while the ship is OffTrack or respawning. |
| D10 | After the win | Once the win latches: every edge counts as closed, slip is off, steering is replaced by an autopilot toward lateral 0, and dash input is ignored. The Mission Complete sequence stays deterministic. |
| D11 | Police physics | The police use the same `TrackBody`, with grip, slides and falls. The driver AI **brakes for flat curves ahead**, so falls are rare (mostly a mistimed ramp jump). A police fall redeploys the police behind the ship and never freezes the player. |
| D12 | Catch | **Distance and lateral proximity** (`gap ≤ catchDistance` and `|Δlateral| ≤ catchLateral`), **or** staying within `catchDistance` for `sustainedCatchSeconds` whatever the lateral gap. A last-moment dodge works, but dodging can't be a permanent escape. |
| D13 | Police and orbs | The police collect orbs, and a collected orb disappears. `boostShare` stays too, keeping its current asset value (**0.156**). |
| D14 | Legacy motor | `GameSettings.useLegacyMotor` exists **for M0 only**, for the A/B check. It is deleted when M0 passes. After that the git branch is the fallback. |
| D15 | Controls | Accelerate = **W / RightTrigger**, Brake = **S / LeftTrigger**. Steer (A/D, left stick) and dash (N/M, LB/RB, double tap) are unchanged. |
| D16 | Touch | Auto-throttle at full, no brake. |

## 5. Functional requirements

### 5.1 Speed and input

- **R1.1** Append `GameAction.ShipAccelerate` and `GameAction.ShipBrake` (the enum is
  append-only), with `Defaults` rows (D15) and `MenuTextId` labels in 4 languages. They appear on
  the CONTROLS screen.
- **R1.2** `IThrottleInput` (throttle 0..1, brake 0..1). The triggers read analog through
  `PadAxis`. Keys give 0 or 1, eased by `digitalThrottleRampSeconds` (see OQ5). Touch always
  gives throttle 1 and brake 0.
- **R1.3** Longitudinal forces per step:
  - below cruise: `+thrust × throttle`
  - always: `−brakeDecel × brake`
  - throttle released and below cruise: `−coastDrag`
  - above cruise: `−passiveDeceleration`
  - pad and orb impulses: unchanged (`AddSpeedImpulse`)
- **R1.4** `initialImpulse` stays the launch kick.
- **R1.5** Stall rule per D9, in `GameManager`.

### 5.2 Steering, grip and curves

- **R2.1** Steering is a lateral **force**, `steerForce × steer`, damped by `lateralDrag`. It no
  longer sets a lateral velocity.
- **R2.2** Curve demand is `a = v² × κ`, with `κ` from `GetCurvatureAtDistance`.
- **R2.3** On a section flagged banked (D4), the demand is ignored and the ship never slips.
- **R2.4** On a flat or under-banked section, available grip is
  `grip(v) = gripBase + gripPerSpeed × v`. Any demand above that becomes outward lateral
  acceleration. Because the demand grows with v² and the grip only linearly, faster means more
  dangerous, and braking is the answer.
- **R2.5** A slide faster than `slideThreshold` fires `Sliding` (camera shake, haptics, sparks)
  and costs `slideSpeedLoss`.
- **R2.6** The visual bank and roll in `ApplyHover` are driven by the real lateral acceleration.
- **R2.7** `TrackGenerator` gains `unbankedSweepChance`: the chance that a sweep is authored flat
  and gets open outer edges (D5). `TrackDecorator` stamps a new barrier-less road piece on the open
  side of those sections.

### 5.3 Falling off and respawn

- **R3.1** Append `ShipState.OffTrack`. The ship enters it per D6. The body velocity is converted
  to world space and falls under `fallGravity`, with no spline.
- **R3.2** The camera follows for `fallCameraFollowSeconds`, then holds and looks at the ship. A
  `FellOff` event drives the glitch pulse and haptics.
- **R3.3** Safe stretch = grounded plain road, not a ramp, loop or tube curl, and with no feature
  within `respawnClearance` metres ahead. The respawn distance is the first safe stretch at or past
  the fall distance (D7).
- **R3.4** Sequence (a coroutine on `ShipMotor`):
  1. after `fallDurationSeconds`, place the ship at the respawn distance, in the centre lane, at
     speed 0
  2. blink between the ship's own materials and the `DashGhostTrail` ghost material (swapping the
     materials; MaterialPropertyBlock tints are ignored by the ship shader) for
     `respawnWaitSeconds` at `respawnBlinkRate`, with no control and no damage
  3. resume at `lastSpeed × (1 − respawnSpeedPenalty)`, where `lastSpeed` is the speed at the
     moment the ship left the track
  4. fire `Respawned`
- **R3.5** `PolicePatrol.Hold` (separate from `motor.Paused`, because the clock keeps running):
  from the fall until the resume, the police neither move nor catch. On release, enforce a gap of
  at least `respawnMinPatrolGap`.

### 5.4 Track features on the body

- **R4.1 Loops** stay spline-assisted: on entry the pose comes from `LoopSection.GetPose`, pass or
  fail is decided once, and `DropFromLoop` handles a failure. On exit the ship is at lateral 0 with
  its speed carried through. The edges are closed.
- **R4.2 Tubes** keep their wrap, curl and `ReturnProgress` logic. Lateral physics runs around
  the circumference, grip is not tested, and the edges are closed.
- **R4.3 Jumps** stay automatic from ramps. Takeoff sets `verticalVelocity` from the ramp angle and
  the speed, and `airGravity` is chosen so the air distance matches
  `JumpDefinition.AirDistanceFor(speed)`. Air steering keeps `airControlFactor`. Landing outside
  the band on an open edge leads to OffTrack. On walled edges (every post-ramp straight today) the
  clamp still applies. The side of a ramp is still a wall (`sideHitSpeedLoss`).
- **R4.4 Barrel roll** is unchanged: a dash in the air plays the 360° visual roll.
- **R4.5 Dash** is a lateral impulse, `dashImpulse`, damped by `lateralDrag`. The meter, its cost
  and its recharge are unchanged. `WallHit` fires on closed edges. On open edges, D6 applies.

### 5.5 Analytic pickups

- **R5.1** `PickupRegistry`: pads, orbs and coins register their distance, lateral position,
  height and radius on spawn, and unregister on despawn. It is re-initialised on boot (domain
  reload is off).
- **R5.2** Each fixed step, every `TrackBody` queries its swept range `[prevDistance, distance]`
  with lateral and height radii. This is tunnel-proof at Light Speed.
- **R5.3** `SpeedPad` and `Collectible` keep their effects and their static `Collected` events,
  but the query triggers them. Remove the long-trigger workaround (`collectibleTriggerSize`). The
  height test still makes ground orbs miss a ship in the air.

### 5.6 Police

- **R6.1** `PolicePatrol` owns a `TrackBody` and drives it through a `PatrolDriver` that outputs
  throttle, brake and steer.
- **R6.2 Chase.** Throttle toward the rubber-band target speed (`rubberBand`, `ramp`,
  `catchUpAccel` and the redeploy band are kept). Steer toward the ship's lateral position. Catch
  per D12.
- **R6.3 Curves.** Look ahead over `curveLookaheadSeconds`. For each flat sweep, the safe speed is
  the one where `v²κ = grip(v)`. Brake to arrive at or below it.
- **R6.4 Orbs.** Scan `PickupRegistry` over `orbLookaheadSeconds`, score each orb as its value
  against the steering it needs, and blend it into the steer target with `orbSeekWeight`. A
  collected orb disappears.
- **R6.5 Ramps.** Look ahead over `rampLookaheadSeconds`. If passing beside the ramp is reachable
  at `steerForce` in the time left, steer around it. Otherwise line up and jump it with the same
  jump physics.
- **R6.6 Fall.** The police redeploy behind the ship through `Redeploy()` (see OQ4 for the gap).
  The player is never frozen by a police fall.

### 5.7 Post-win

- **R7.1** Per D10, from the frame the win latches until `EndRun` pauses the motor. `FinishWin`
  still waits for Grounded and no ramp commitment.

## 6. Tunables

Every tunable is an Odin `[PropertyRange]` slider (with `[MinMaxSlider]` bands for paired values)
on a runtime-cloned asset. Each one gets a debug-menu row (`DebugMenuFactory`), is persisted in
`ShipDebugSettings` / `PatrolDebugSettings` via `ApplyTo`, and has a `MenuTextId` label in 4
languages.

| Asset | Field | Meaning | Starting value |
|---|---|---|---|
| `ShipDefinition` | `cruiseSpeed` | Throttle-only top speed (m/s) | ~250 (about the current launch speed) |
| `ShipDefinition` | `thrust` | Acceleration at full throttle below cruise (m/s²) | tune |
| `ShipDefinition` | `brakeDecel` | Deceleration at full brake (m/s²) | tune |
| `ShipDefinition` | `coastDrag` | Deceleration with no input, below cruise (m/s²) | tune |
| `ShipDefinition` | `passiveDeceleration` | Over-cruise bleed only (m/s²) | 6 (current) |
| `ShipDefinition` | `steerForce`, `lateralDrag` | Lateral handling | tune |
| `ShipDefinition` | `gripBase`, `gripPerSpeed` | `grip(v)` on flat curves (§5.2) | see OQ1 |
| `ShipDefinition` | `slideThreshold`, `slideSpeedLoss` | When a slide counts as a skid, and its cost | tune |
| `ShipDefinition` | `dashImpulse` | Lateral velocity a dash adds (replaces `dashDistance` and `dashDuration`) | match today's ~20 m shove |
| `ShipDefinition` | `airGravity` | Body gravity, checked against `JumpDefinition` | derived |
| `ShipDefinition` | `digitalThrottleRampSeconds` | Key throttle and brake easing | 0.15 |
| `GameSettings` | `stallGraceSeconds` | D9 | 2 |
| `GameSettings` | `edgeOverhang`, `edgeGraceSeconds` | D6 | ~1 m, ~0.25 s |
| `GameSettings` | `fallGravity` | Gravity during an off-track fall | 30 m/s² |
| `GameSettings` | `fallDurationSeconds`, `fallCameraFollowSeconds` | Fall timing | 1.5, 0.5 |
| `GameSettings` | `respawnWaitSeconds`, `respawnBlinkRate` | Blink | 3 s, 8 Hz |
| `GameSettings` | `respawnSpeedPenalty` | Share of the last speed lost on resume | 0.15 |
| `GameSettings` | `respawnClearance` | Feature-free distance required ahead of a respawn | tune |
| `GameSettings` | `respawnMinPatrolGap` | Minimum police gap on resume | ~150 m |
| `TrackShape` / `TrackGenerator` | `unbankedSweepChance` | Share of sweeps that are flat with open outer edges | tune |
| `PatrolDefinition` | `steerForce`, `lateralDrag`, `gripBase`, `gripPerSpeed` | Police handling | tune |
| `PatrolDefinition` | `catchLateral`, `sustainedCatchSeconds` | D12 | ~0.6 × halfWidth, ~1.5 s |
| `PatrolDefinition` | `curveLookaheadSeconds`, `orbLookaheadSeconds`, `orbSeekWeight`, `rampLookaheadSeconds` | Driver AI | tune |
| `PatrolDefinition` | `boostShare` | Unchanged | 0.156 (current asset) |

## 7. Invariants that must survive

- `ShipDefinition` and `PatrolDefinition` are cloned at runtime. Never write to the assets.
- Tunables live in ScriptableObjects: run rules in `GameSettings`, ship feel in `ShipDefinition`,
  police feel in `PatrolDefinition`.
- Speeds are stored in m/s. Distance from the track start is the authoritative coordinate.
- Serialized enums are append-only (`GameAction`, `ShipState`, `MenuTextId`).
- Every player-facing string is a `MenuTextId` in all 4 languages.
- Domain reload is off. Subscribe in `OnEnable`/`OnDisable`, and re-initialise static registries.
- `ShipMotor` keeps its public API (`DistanceTravelled`, `CurrentSpeed`, `AddSpeedImpulse`,
  `Paused`, `State`, events), so `GameManager`, `RaceHud`, cameras and objectives don't change.

## 8. Milestones

Each milestone ends in a playable `FiniteRunner_Test` and can be merged on its own.

| M | Scope | Done when |
|---|---|---|
| **M0** | `TrackManager` queries (`GetFrameAtDistance`, `GetCurvatureAtDistance`, `GetBankAtDistance`, `IsEdgeOpen`). The plain C# `TrackBody` runs in `FixedUpdate` with substeps and interpolation. `ShipMotor` becomes a driver with the old rules ported 1:1. The `useLegacyMotor` flag is added (D14). | By eye, it plays the same in an A/B, **and** these numbers match within 2%: time to cross 1 km with no input, jump air distance at 3 speeds, dash lateral distance. Then the flag is deleted. |
| **M1** | Throttle and brake (§5.1), stall grace. | RT reaches cruise and holds it, LT slows the ship, orbs push past cruise before the speed bleeds back, and stall follows D9. |
| **M2** | Lateral force, grip, banked vs flat sweeps, `unbankedSweepChance`, a barrier-less piece on open edges (§5.2). | A flat sweep taken too fast throws the ship to the outer edge unless it brakes. Banked sweeps hold at every speed. Walls are missing exactly where `IsEdgeOpen` is true. |
| **M3** | OffTrack, fall, respawn past the fall point, blink, `PolicePatrol.Hold` (§5.3). Post-win lockdown (§5.7). | Leaving an open edge gives a fall, then a 3 s blink with the police frozen, then a relaunch at 85% on the far side. A win never ends in a fall. |
| **M4** | Loops, tubes, jumps, barrel roll and dash on the body (§5.4). | Every feature plays as before, and the dash feels like a shove. |
| **M5** | `PickupRegistry` and swept queries (§5.5). | Orbs, pads and coins behave the same at every speed, and the patrol can query them. |
| **M6** | Police on `TrackBody` with the `PatrolDriver` (§5.6), and the D12 catch. | The police weave for orbs, brake for flat sweeps, dodge ramps when they can, jump when they must, and still catch a bad driver. |
| **M7** | Debug rows and persistence, the CONTROLS screen, the store upgrade remap (OQ3), docs. | Everything is tunable from the debug menu, the docs match the code, and the store upgrades affect the new fields. |

### Progress

- **M0 — done 2026-09-18.** `Runner/Simulation/TrackBody.cs`, the `TrackManager` body queries,
  `ShipMotor` as the driver (fixed tick + track-space interpolation, `GameSettings.simSubsteps`).
  The A/B flag and the probe are deleted (D14).
- **M1 — code landed 2026-09-18, play check pending.** `GameAction.ShipAccelerate` / `ShipBrake`
  (the CONTROLS screen now lists by section, so they sit under SHIP; they reuse the existing
  Accelerate / Brake labels — no new `MenuTextId`), `IThrottleInput` on `SteeringInput`, the
  throttle speed model in `TrackBody.StepSpeed`, `ShipDefinition` `cruiseSpeed` 250 / `thrust` 60
  / `brakeDecel` 140 / `coastDrag` 20 / `digitalThrottleRampSeconds` 0.15 (first-guess values —
  the Fighter asset picks them up as defaults), `GameSettings.stallGraceSeconds` 2 with the stall
  timer in `ShipMotor.UpdateStall`, `LoopReachable` on the new bleed rule. Debug-menu rows for
  the new stats are M7.

- **M2 — code landed 2026-09-18, play check pending.** `TrackManager.FlatSweep` spans (grip +
  open outer edge off one flag), `unbankedSweepChance` 0.3 in `StartTurn`, the lateral force +
  `SlipAcceleration` in `TrackBody`, `ShipDefinition` `gripBase` 50 / `gripPerSpeed` 0.5 /
  `slideThreshold` 4 / `slideSpeedLoss` 0.1, `Sliding` → rumble + `GameSettings.slideShake`,
  the decorator's open edge (placeholder wall + marker strip; OQ6 art still open). **Deviation
  from §6:** no `steerForce` / `lateralDrag` fields — the force model is derived from the
  existing `lateralSpeed` (force / drag) and `handlingResponse` (drag), so current balance, the
  store upgrade and the debug rows carry over. The open edge still clamps until M3. **OQ1 is
  live:** at Light Speed (1800 m/s) a 490 m flat sweep demands ~6,650 m/s² against ~950 of grip,
  and braking from there to a holdable ~330 m/s takes ~10 s at `brakeDecel` 140 — so late-run
  flat sweeps are near-certain slides unless grip, `unbankedSweepChance` or the sweep radius are
  tuned for it.

- **M3 — code landed 2026-09-18, play check pending.** `ShipState.OffTrack` + `Respawning`
  (the PRD only named the first; the blink wait needed its own state for the readers), the
  open-edge overhang/grace in `TrackBody`, the world-space fall, `FindRespawnDistance`,
  `RespawnBlink`, `PolicePatrol.SetHold`, the fall camera (the rig's planted cinematic shot),
  `ShipMotor.Autopilot` / `body.HoldOnTrack` for §5.7, and the `GameSettings` "Fall and respawn"
  group. The fall and the wait are tick timers, not a coroutine (R3.4), so pause freezes them.
  `respawnClearance` defaults to 150 m — under the track's 200 m level lead, or a respawn after
  a sweep beside a feature would skip the whole feature (a 7 km tube). OQ2 (what is below the
  track) is still open.

- **D5 amended 2026-09-18 (user request):** straight runs can be open too, on BOTH sides
  (`openStraightChance` 0.5, `TrackManager.OpenStretch`) — still never beside a feature, a
  landing zone or a banked stretch. Flat-sweep generation was also fixed (a flat roll on a still
  banked road is deferred, not lost; flat sweeps fit gaps banked ones cannot; the
  `TrackDebugSettings` mirror of `unbankedSweepChance` is gone until M7). The barrel-roll
  ribbons stream during an off-track fall.

- **M4 — code landed 2026-09-18, play check pending.** Jumps fly on a real vertical velocity
  under a per-jump gravity that reproduces `AirDistanceFor(speed)` at the takeoff speed (no
  `airGravity` field — it is derived per flight); an off-band landing over an open edge is a
  fall. The dash is `body.AddLateralImpulse(DashImpulse)` with `DashImpulse = dashDistance ×
  handlingResponse` (derived like the steering force, so no `dashImpulse` field and the store's
  Dash Power still works); `dashDuration` is only the dash window now. Loops and tubes were
  already on the body since M0 and are unchanged — including lateral carried through a loop's
  exit (R4.1's "lateral 0 on exit" would be a visible snap, so it was not applied).

- **M5 — code landed 2026-09-18, play check pending.** `Simulation/PickupRegistry.cs`
  (`ITrackPickup`), `SpeedPad` and `Collectible` placed on the track by the generator,
  `TrackBody.SweepPickups` + `PickedUp`, the ship's reach measured off its `BoxCollider`.
  `SpeedPad.OnTriggerEnter` is gone; `Collectible` keeps its trigger for the city, both paths
  ending in one guarded `Collect()`. `collectibleTriggerSize` became the 2D
  `collectiblePickupSize`. New behaviour: a taken ORB now disappears (needed for M6; brake pads
  stay but bite once).

- **M6 — code landed 2026-09-18, play check pending.** `PolicePatrol` on its own `TrackBody`
  (fixed tick + interpolated pose; the rubber band expressed as the body's cruise target),
  `GameFlow/PatrolDriver.cs` (chase line, orb seek, ramp round-or-jump, open-edge margin, flat
  sweep braking), the D12 catch (`catchLateral` 18 m, `sustainedCatchSeconds` 1.5, tail-sitting
  inside the catch distance), `SpeedPad.Take()`, and a fall → `Redeploy(raiseFloor: false)`
  (OQ4: the existing drop-in gap, without the raised floor). New `PatrolDefinition` groups
  Handling / Driver; `lateralSpeed` + `handlingResponse` instead of `steerForce` + `lateralDrag`
  (same derivation as the ship), plus `orbBoostShare` 0.5 for orbs it collects itself
  (`boostShare` keeps the asset's 0.156).

- **M7 — code landed 2026-09-18, play check pending.** 31 new `MenuTextId` labels (4 languages);
  debug rows for every new ship / track / patrol tunable, a PATROL DRIVER tab and a FALL &
  RESPAWN tab (`FallRespawnDebugPage` — edits `GameSettings` live, like the fog page, since the
  run never clones it); persistence in `ShipDebugSettings` / `TrackDebugSettings` /
  `PatrolDebugSettings` with −1 = "never captured" defaults for the new keys; store remap per
  OQ3 (Handling also scales grip, Speed Multiplier also scales `cruiseSpeed`); docs pass; the
  Fighter's stale description. `useLegacyMotor` was already removed at the end of M0. **Still
  open:** OQ1 (late-run grip balance), OQ2 (what is below the track), OQ6 (one-sided barrier
  art — the placeholder wall stands in), and the per-milestone play checks.

## 9. Verification (every milestone)

1. Compile check (`Logs/Editor.log` plus Bee DLL timestamps, or the bundled Roslyn).
2. Play `FiniteRunner_Test` and confirm the milestone's "Done when".
3. Regression pass:
   - the win latches and Mission Complete plays
   - Caught, TimedOut and Stalled each show their localized reason
   - loops pass and fail
   - tubes, ramps and the ramp side wall work
   - the dash wall hit fires in tubes, and the barrel roll plays in the air
   - the HUD speed is correct
   - the `CityTest` → runner additive handoff works
   - pause and the loop slow-mo work

## 10. Documentation to update (M7)

- `.claude/rules/runner-ship.md`: the body, speed model, grip, fall and respawn, police driver.
- `.claude/rules/runner-track.md`: the edge and curvature queries, open-edge sections,
  `unbankedSweepChance`, analytic pickups.
- `.claude/rules/shared-systems.md`: collectibles are detected by query, not by trigger.
- `.claude/rules/runner-store.md`: the upgrade remap.
- `CLAUDE.md` "Runner game design": the speed model, lose conditions, falling and respawn, police
  behaviour. Fix the stale "0.7 share" (the asset is 0.156). Also update
  `Fighter_ShipDefinition`'s stale description ("Launches at 25, bleeds 3").

## 11. Risks

| Risk | Mitigation |
|---|---|
| Curve demand spans ~50× between launch and Light Speed (127 vs 6,650 m/s² on the tightest 490 m sweep), so a single grip value can't be tuned for both. | D4 limits grip tests to flat sweeps, and `grip(v)` is linear in v. Tune so a flat sweep is survivable at cruise and needs braking well before Light Speed (OQ1). |
| The police AI costs more than it adds. | M6 lands last. The M5 registry and the D11 braking rule keep the AI small. The old 1D band can be restored per commit. |
| Open edges read as unfair. | D5 (only where authored and visible), D6 (grace window) and D7 (no fall loop). |
| Pickups break during M0 through M4, while triggers still drive them on a fixed-step body. | The ship root moves via `Rigidbody.MovePosition` in `FixedUpdate`, so triggers still fire. The long-trigger workaround stays until M5. |

## 12. Open questions

Each has a working default, so no milestone is blocked. Confirm them before the milestone that
needs them.

- **OQ1 — Grip numbers (M2).** How dangerous flat sweeps should be. Default: a flat 490 m sweep is
  holdable up to about 1.3× cruise, and past that you must brake. `unbankedSweepChance` starts at
  about 0.3.
- **OQ2 — What sits below the track (M3).** Nothing was found in the scene, so it is probably
  skybox or void. Does the fall need a backdrop (fog floor, city lights), or is the glitch pulse
  enough?
- **OQ3 — Store upgrade remap (M7).** Default:
  - Handling → `steerForce` and `gripBase`
  - DashPower → `dashImpulse`
  - SpeedMultiplier → `cruiseSpeed` ×, and `passiveDeceleration` ÷
  - JumpStrength unchanged
  - `ShipNabucodonosor` stays unused by the applier
- **OQ4 — Police fall redeploy gap (M6).** Default: reuse the existing redeploy drop-in band.
- **OQ5 — Digital throttle easing (M1).** Default: a 0.15 s ramp on keys. Drop it if keys feel
  fine at 0/1.
- **OQ6 — Barrier-less road art (M2).** It needs a road piece with no wall on one side (left and
  right variants, or a mirrored one). It could be a placeholder until the art exists.
