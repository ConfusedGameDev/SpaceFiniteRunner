# Space Ship Update Plan — physics-based runner controls with spline assist

## Goal

Replace the runner's fully spline-scripted ship control with a **physics-based controller that is
assisted by the spline**. The player should feel speed, grip and momentum. The track should still
keep the ship where the design needs it: on loops, in tubes and on banked curves.

### Where we are today

- **`ShipMotor`** (`Assets/01.Scripts/Runner/Ship/ShipMotor.cs`)
  - It integrates `DistanceTravelled` and a clamped `lateralOffset` in `Update`, and poses the root
    through `TrackManager.GetPoseAtDistance`.
  - Speed comes from one launch impulse (`initialImpulse`), a constant bleed
    (`passiveDeceleration`) and pad impulses (`AddSpeedImpulse`).
  - The track edge is a hard clamp (`GetLateralBand`), so the ship cannot fall off. The only fall is
    a failed loop.
  - Jumps are parabolas in track distance, started automatically by ramps. The barrel roll is a dash
    performed in the air. The dash is a scripted lateral velocity profile.
- **Ship Rigidbody**: kinematic, with a large trigger box. Pads, orbs and coins are detected by
  physics triggers (`SpeedPad.OnTriggerEnter`, `Collectible`).
- **`PolicePatrol`** (`Runner/GameFlow/PolicePatrol.cs`)
  - It is a 1D rubber band on the centre line. It never steers, jumps or collects anything.
  - It takes a 0.7 share of every boost the ship collects.

### Requirements

1. The controller is physics-based or simulated-physics-based.
2. The player accelerates and decelerates with the triggers.
3. A player who does not manage their speed can fall off the edges, or crash in a pronounced curve.
4. Loops stay spline-assisted.
5. Banked sections align the ship correctly, and the ship stays on the track without slipping.
6. A ship that falls reappears on the track after a few seconds, in this order:
   1. It reappears at speed 0 and blinks between the ship colour and a ghost for a wait
      (default 3 s).
   2. After the wait it restarts at the last speed minus 15% (adjustable).
   3. The police stop moving during this period.
7. Jumps and the barrel roll work the same as now.
8. The dash also uses physics.
9. The police use the same system, and they:
   1. try to catch the player,
   2. try to get the power-ups,
   3. avoid jumps.

### Design decisions

| Topic | Decision |
|---|---|
| Physics style | The physics is **simulated in track space**. The state is distance, lateral and height plus their velocities, and forces act on them. This is deterministic, cannot tunnel through the track (the ship covers about 36 m per step at Light Speed), and is shared by the ship and the police. **Going past an open edge hands the ship over to a world-space ballistic fall.** |
| Speed model | RT throttles up to a per-ship **cruise cap**, and only orbs push the speed above it. Above cruise, the old passive bleed pulls it back down. LT brakes. Speed stays uncapped overall, so orbs are still the way to Light Speed. |
| Clock during a fall | The countdown **keeps running** during the fall and the respawn, so lost time is the penalty. |
| Stall loss | Braking to 0 no longer ends the run. "Stalled" fires only after `stallGraceSeconds` at speed 0 with the throttle released. |
| Police and orbs | The police collect orbs, and a collected orb disappears. This is not a race: the police are behind the player, so the orbs they take never matter to the player. The existing `boostShare` stays as a tunable. |
| Police and ramps | The police try to steer around ramps. If they are too fast to clear a ramp in time, they jump it. |

### Invariants that must survive

- **`ShipDefinition` and `PatrolDefinition` are cloned at runtime.** Never write to the assets.
- **Tunables live in ScriptableObjects** as Odin `[PropertyRange]` sliders, with `[MinMaxSlider]`
  bands for paired values. Run rules go in `GameSettings`, ship feel in `ShipDefinition`, police
  feel in `PatrolDefinition`.
- **Speeds are stored in m/s**, and **distance from the track start is the authoritative
  coordinate**.
- **Serialized enums are append-only**: `GameAction`, `ShipState`, `MenuTextId`.
- **Every player-facing string is a `MenuTextId`**, translated in all 4 languages.
- **Domain reload is off.** Subscribe in `OnEnable`/`OnDisable`, and re-initialise any static
  registry on boot.
- **`ShipMotor` keeps its public API** (`DistanceTravelled`, `CurrentSpeed`, `AddSpeedImpulse`,
  `Paused`, `State`, events), so `GameManager`, `RaceHud`, cameras and objectives don't change.

---

## Milestones

Each milestone ends in a playable build of `FiniteRunner_Test` and can be merged on its own.

### M0 — Track-space body and track queries (foundation, no change in feel)

**Goal:** move the simulation onto a reusable physics body without changing gameplay.

**Work**

- Add these queries to `TrackManager` (`Runner/Track/TrackManager.cs`):
  - `GetFrameAtDistance(d)`: position, forward, up and right.
  - `GetCurvatureAtDistance(d)`: signed turn rate in 1/m, positive to the right. It is computed from
    the forward vector over a small distance step and projected on the track up.
  - `GetBankAtDistance(d)`: the roll of the track up around the forward axis, relative to world up.
  - `IsEdgeOpen(d, side)`: whether the ship can fall off that side here. It is false inside tubes,
    loops and ramp corridors.
- Add a new `TrackBody` class, plain C# with no MonoBehaviour, reused by ship and police.
  - State: `distance`, `lateral`, `height`, `forwardSpeed`, `lateralVelocity` and
    `verticalVelocity`.
  - `Step(dt, in BodyControls controls)` integrates the forces.
- Run the simulation in `FixedUpdate` with substeps, and interpolate the rendered pose in `Update`.
  The HUD, cameras and objectives keep reading the same properties.
- `ShipMotor` becomes a thin driver around `TrackBody`, and the old rules are ported into it
  one-to-one.
- Add `GameSettings.useLegacyMotor` for A/B comparison. It is removed in M7.

**Done when:** the runner plays exactly as it does today, driven by the new body.

### M1 — Throttle and brake on the triggers (req. 2)

**Work**

- In `UI/ControlBindings.cs`:
  - Append `GameAction.ShipAccelerate` (W / RightTrigger) and `GameAction.ShipBrake`
    (S / LeftTrigger), with `Defaults` rows.
  - Add the `MenuTextId` action labels in 4 languages.
  - The Ship and Car sections never conflict, so reusing the triggers is safe.
- Input: add `IThrottleInput` (throttle 0..1, brake 0..1) to `Runner/Ship/SteeringInput.cs`. It
  reads the analog values through `PadAxis`, and the keyboard gives 0 or 1.
- Touch: auto-throttle by default. A touch brake gesture is an open item.
- Speed model in `TrackBody`:
  - Below cruise: `+thrust × throttle`.
  - Always: `−brakeDecel × brake`, and `−coastDrag` when the throttle is released.
  - Above cruise: `−passiveDeceleration` (the over-cruise bleed).
  - Pad impulses are unchanged (`AddSpeedImpulse` / `pendingSpeedChange`).
- Keep `initialImpulse` as the launch kick.
- Change the stall rule in `GameManager` (`Runner/GameFlow/GameManager.cs`) to the grace timer.

**Tunables**

| Asset | Field | Meaning |
|---|---|---|
| `ShipDefinition` | `cruiseSpeed` | Top speed reachable by throttle alone (m/s). |
| `ShipDefinition` | `thrust` | Acceleration at full throttle below cruise (m/s²). |
| `ShipDefinition` | `brakeDecel` | Deceleration at full brake (m/s²). |
| `ShipDefinition` | `coastDrag` | Deceleration with no input, below cruise (m/s²). |
| `ShipDefinition` | `passiveDeceleration` | Now the over-cruise bleed only. |
| `GameSettings` | `stallGraceSeconds` | Time at 0 speed with no throttle before "Stalled". |

**Done when:** RT reaches cruise and holds it, LT slows the ship down, and orbs push past cruise
before the speed bleeds back to it.

### M2 — Grip, curves and banking (req. 1, 3, 5)

**Work**

- Change steering from a set lateral velocity to a **lateral force**: `steerForce × steer`, damped by
  `lateralDrag`.
- Curve demand is `a = v² × κ`, the centripetal acceleration needed to follow the curve.
  - Bank assist cancels `bankAssist × g × tan(bank)` of that demand. The asset default is enough
    to cancel the whole demand on every authored banked section.
  - `grip` absorbs the rest up to its limit.
  - Any excess becomes **outward lateral acceleration**, so the ship slides toward the outer edge.
- When the ship slides faster than `slideThreshold`, fire a `Sliding` event and apply a
  `slideSpeedLoss`. This drives the "crash in a pronounced curve" feel (shake, haptics, sparks)
  before the ship actually leaves the track.
- **Banked sections:** the ship's up always aligns to the track up (it already does through the
  pose), and bank assist removes the slip. The track generator's `bankEnabled`, `maxBankAngle` and
  `bankPerDegreeOfTurn` stay the authoring tools.
- Visual bank and roll in `ApplyHover` are driven by the real lateral acceleration instead of the
  normalised lateral offset.

**Tunables**

| Asset | Field | Meaning |
|---|---|---|
| `ShipDefinition` | `grip` | Lateral acceleration the ship can hold without sliding (m/s²). |
| `ShipDefinition` | `steerForce` | Lateral acceleration from full steering. |
| `ShipDefinition` | `lateralDrag` | Damping of lateral velocity. |
| `ShipDefinition` | `bankAssist` | Share of the curve demand that banking cancels. |
| `ShipDefinition` | `slideThreshold` / `slideSpeedLoss` | When a slide counts as a skid, and what it costs. |

**Done when:** a sharp flat curve taken at speed throws the ship to the edge unless the player
brakes, and banked curves hold the ship at any speed.

### M3 — Falling off and respawn (req. 3, 6)

**Work**

- **Leaving the track**
  - Append `ShipState.OffTrack` (the enum is append-only).
  - The ship leaves the track when `|lateral| > halfWidth + edgeOverhang` on an open edge, or when
    it lands from a jump outside the band.
  - It then converts the body velocity to world space and falls ballistically under
    `fallGravity`, with the root driven directly and no spline.
  - The camera follows for `fallCameraFollowSeconds`, then holds and looks at the ship.
  - The glitch pulses and the haptics fire through a new `FellOff` event.
- **Safe checkpoint**
  - Every fixed step, record the last distance where the ship was grounded on the road, away from
    ramps, loops and tube curls, and not sliding.
  - Respawning always drops the ship on a plain stretch.
- **Respawn sequence** (a coroutine on `ShipMotor`)
  1. After `fallDurationSeconds`, place the ship at the checkpoint, in the centre lane, at
     **speed 0**.
  2. **Blink** between the ship's own materials and the ghost material for `respawnWaitSeconds`
     (default 3) at `respawnBlinkRate`.
     - Reuse the ghost material from `DashGhostTrail`.
     - Swap the renderer materials. MaterialPropertyBlock tints are ignored by the ship shader.
  3. The ship is invulnerable and can't be controlled during the wait.
  4. Resume at `lastSpeed × (1 − respawnSpeedPenalty)` (default 0.15), where `lastSpeed` is the
     speed at the moment the ship left the track.
  5. Fire `Respawned`.
- **Police hold**
  - Add `PolicePatrol.Hold`, separate from `motor.Paused` because the clock keeps running.
  - While it is set, the police don't move and can't catch, from the moment the ship falls until
    the ship resumes.
  - On release, enforce a gap of at least `respawnMinPatrolGap`.
- **GameManager:** the countdown keeps ticking during the fall. The Stalled check is skipped while
  the ship is OffTrack or respawning.

**Tunables** (`GameSettings`, because these are run rules)

| Field | Default | Meaning |
|---|---|---|
| `edgeOverhang` | ~1 m | How far past the edge the ship can hang before it falls. |
| `fallGravity` | 30 m/s² | Gravity during an off-track fall. |
| `fallDurationSeconds` | 1.5 | Time spent falling before the respawn. |
| `fallCameraFollowSeconds` | 0.5 | Time the camera follows before it holds. |
| `respawnWaitSeconds` | 3 | Blink wait at speed 0. |
| `respawnBlinkRate` | 8 Hz | Blink frequency. |
| `respawnSpeedPenalty` | 0.15 | Share of the last speed that is lost. |
| `respawnMinPatrolGap` | ~150 m | Minimum police gap when the ship resumes. |

**Done when:** flying off an edge leads to a fall, then a 3 s blink with the police frozen, then a
relaunch at 85% of the last speed.

### M4 — Loops, tubes, jumps, barrel roll and dash on the body (req. 4, 7, 8)

**Work**

- **Loops stay spline-assisted.**
  - On loop entry the body hands the pose over to `LoopSection.GetPose`. Pass or fail is decided
    once, and `DropFromLoop` still handles a failure.
  - On loop exit the body state is restored: lateral 0, and the speed carried through.
  - Loop edges are closed, so the ship can't fall off a loop.
- **Tubes** keep their wrap, curl and `ReturnProgress` logic. The edges are closed inside tubes.
  Lateral physics applies around the circumference, and grip is not tested in full tubes.
- **Jumps** stay automatic from ramps, "same as now".
  - Takeoff sets `verticalVelocity` from the ramp angle and the speed. Body gravity is tuned so the
    air distance matches `JumpDefinition.AirDistanceFor(speed)`, so the feel is preserved.
  - Landing outside the lateral band leads to OffTrack (M3).
  - The side of a ramp is still a wall, with `sideHitSpeedLoss`.
- **Barrel roll** is unchanged: a dash in the air plays the 360° visual roll.
- **Dash uses physics.**
  - The double tap applies a lateral **impulse** (`dashImpulse`), which `lateralDrag` then damps.
  - The dash meter, its cost and its recharge are unchanged.
  - On an open edge a dash can carry the ship off the track. `WallHit` fires only on closed edges
    (tubes, ramp walls).

**Tunables**

| Asset | Field | Meaning |
|---|---|---|
| `ShipDefinition` | `dashImpulse` | Lateral velocity added by a dash (m/s). It replaces `dashDistance` and `dashDuration` in the physics motor. |
| `ShipDefinition` | `airGravity` | Gravity inside the body, derived from or checked against `JumpDefinition`. |

**Done when:** every track feature plays as it did before, and the dash feels like a shove rather
than a scripted slide.

### M5 — Analytic pickups (needed by the fixed-step simulation and by the police)

**Work**

- Add a `PickupRegistry`: pads, orbs and coins register their distance, lateral position, height
  and radius when they spawn, and unregister when they despawn.
- Each fixed step, every `TrackBody` queries the swept range from its previous distance to its new
  distance, plus a lateral and height radius. This is tunnel-proof at Light Speed.
- `SpeedPad` and `Collectible` keep their effects and their static `Collected` events, but the
  query triggers them instead of `OnTriggerEnter`. Remove the long-trigger workaround
  (`collectibleTriggerSize`).
- Ground orbs are still missed in the air, because the height test does it.

**Done when:** orbs, pads and coins behave the same at every speed, and the patrol can query them.

### M6 — Police on the same system (req. 9)

**Work**

- `PolicePatrol` owns its own `TrackBody` and drives it through a `PatrolDriver` AI that outputs
  throttle, brake and steer. It is subject to the same grip, curve and fall physics.
  - If the police fall, they redeploy behind the ship through the existing `Redeploy()` path. The
    player is never frozen by a police fall.
- **(a) Chase**
  - Throttle toward the rubber-band target speed. `rubberBand`, `ramp`, `catchUpAccel` and the
    redeploy band are all kept.
  - Steer toward the ship's lateral position.
  - A catch is now **physical proximity**: distance gap `≤ catchDistance` and lateral gap
    `≤ catchLateral`.
- **(b) Power-ups**
  - Scan `PickupRegistry` over `orbLookaheadSeconds`, score each orb by its value against the
    steering it needs, and blend it into the steer target with `orbSeekWeight`.
  - A collected orb is used up and disappears.
- **(c) Ramps**
  - Look ahead `rampLookaheadSeconds`.
  - If the lateral travel needed to pass beside the ramp is achievable at `steerForce` in the time
    left, steer around it.
  - Otherwise commit, line up with the ramp and jump it using the same jump physics.
- `boostShare` is kept as a tunable, with the same default, and can be turned down now that the
  police collect orbs themselves.

**Tunables** (`PatrolDefinition`)

| Field | Meaning |
|---|---|
| `steerForce`, `grip`, `lateralDrag` | Police handling, the same meaning as for the ship. |
| `catchLateral` | Lateral distance within which a catch counts. |
| `orbLookaheadSeconds`, `orbSeekWeight` | How far ahead the police look for orbs, and how strongly they chase them. |
| `rampLookaheadSeconds` | How early the police decide to avoid or jump a ramp. |

**Done when:** the police weave for orbs, dodge ramps when they can, jump when they must, and still
catch a player who drives badly.

### M7 — Tuning, debug, docs and cleanup

**Work**

- Debug menu:
  - Add a row for every new tunable in `Runner/Screens/DebugMenuFactory.cs`: ship Speed, Handling
    and Dash tabs, a new Fall and Respawn group, and the Patrol tab.
  - Persist them through `ShipDebugSettings` and `PatrolDebugSettings`, each with an `ApplyTo`.
  - Add `MenuTextId` labels in 4 languages.
- Check that the CONTROLS screen lists Accelerate and Brake.
- Store: check the upgrade appliers (`runner-store.md`) against the reinterpreted `ShipDefinition`
  fields (`passiveDeceleration`, `lateralSpeed` and `dashDistance` become new fields). Remap the
  upgrades to `cruiseSpeed`, `grip`, `dashImpulse` and so on.
- Documentation:
  - Update `.claude/rules/runner-ship.md`, `.claude/rules/runner-track.md` (the analytic pickups
    and edge queries) and `shared-systems.md` (collectibles).
  - Update the "Runner game design" section of `CLAUDE.md`: the speed model, the lose conditions,
    falling and respawn, and police behaviour.
- Remove `useLegacyMotor` and the legacy code path.

**Done when:** everything is tunable from the debug menu, the docs match the code, and there is one
motor.

---

## Verification

Run these checks for each milestone:

1. **Compile check** through `Logs/Editor.log` and the Bee DLL timestamps, or compile directly with
   the bundled Roslyn.
2. **Play `FiniteRunner_Test`** and confirm the milestone's "Done when" line.
3. **Regression pass**:
   - The win latches at Light Speed and the Mission Complete flow plays.
   - All three losses work (Caught, TimedOut, Stalled) with their localized reasons.
   - Loops pass and fail.
   - Tubes, ramps and the ramp side wall work.
   - The dash wall hit fires in tubes, and the barrel roll plays in the air.
   - The HUD speed readout is correct.
   - The city → runner additive handoff (`CityTest` completion) works.
   - Pause and the loop slow-mo work.

## Open items

- **Touch controls:** decide on a brake gesture. The default is auto-throttle with brake on a
  two-finger hold.
- **Respawn checkpoints on feature-dense stretches:** if there is no safe checkpoint within N
  metres, respawn just past the next feature.
- **Police fall redeploy gap:** reuse the redeploy drop-in gap, or give it its own value.
- **Touch and keyboard analog feel:** triggers are analog but keys are 0/1. A throttle ramp time for
  digital input may be needed.
