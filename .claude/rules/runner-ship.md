---
description: Runner simulation — ShipMotor, jumps/loops/tubes, GameManager, PolicePatrol, minimap, steering, tuning screen
paths:
  - "**/ShipMotor.cs"
  - "**/ShipDefinition.cs"
  - "**/GameManager.cs"
  - "**/GameSettings.cs"
  - "**/PolicePatrol.cs"
  - "**/PatrolDefinition.cs"
  - "**/ChaseMinimap.cs"
  - "**/SteeringInput.cs"
  - "**/ISteeringInput.cs"
  - "**/TuningScreen.cs"
  - "**/RunnerLevelDefinition.cs"
---

# Runner simulation

## `ShipMotor`

The simulation. Applies the launch impulse, the throttle speed model (below), queued pad
impulses (blended in at the ship's `acceleration` rate) and lateral steering clamped to
`TrackManager.HalfWidth`. Tracks `DistanceTravelled` and remaps it to spline t each frame. Exposes a `PadImpulse` event and a `Paused` flag (used by the tuning
screen, and by `GameManager` to freeze the sim when the run ends).

**The motor is a driver around a `TrackBody`** (`Runner/Simulation/TrackBody.cs`, plain C#, no
MonoBehaviour — the patrol will run the same class). The body holds the track-space state
(`Distance`, `Lateral`, `Height`, `ForwardSpeed`, `LateralVelocity`, `VerticalVelocity`) and the
rules any body shares: the speed model, the lane (edge, ramp rails and side wall, tube wrap and
assisted return) and the jump state machine, plus `State`. The motor feeds it `BodyControls`
(steer, the dash burst's velocity) and a `BodyParams` struct refilled from the live definition
clone before every tick (the body never holds an asset), and keeps what only the ship has: dash
meter, barrel roll, loop verdict and fall (it forces `Looping` / `Falling` through
`body.SetState`), the visual. The rules inside the body are still the scripted ones, ported
one-to-one; `SpaceShipUpdatePRD.md` replaces them milestone by milestone.

**The simulation ticks in `FixedUpdate`** (`GameSettings.simSubsteps` substeps;
`body.BeginTick()` once per tick, `body.Step` per substep) **and the pose is rendered in
`Update`**, interpolated in TRACK SPACE between the last two ticks (`RenderPose(alpha)`, alpha
clamped so a paused or stopped motor rests on the current state). So `DistanceTravelled`,
`LateralOffset` and `AirHeight` are the **rendered** values — what every Update-time reader
(patrol gap, ghost trail, streamer) wants — and `ShipMotor.Body` has the tick's own. Anything
that teleports the state must keep the blend honest: `body.SnapInterpolation()` (launch, loop
drop and landing), or shift the origin with the value (the tube's turn-unwinding snap to 0).

**Speed is a throttle model** (`TrackBody.StepSpeed`, tunables on `ShipDefinition`): queued
impulses blend in first and are the ONLY thing that lifts speed past `cruiseSpeed`; above cruise
`passiveDeceleration` (now the *over-cruise bleed*) pulls it back down to cruise, never through
it; at or below cruise `thrust × throttle` accelerates up to cruise and no further, and a
released throttle loses `coastDrag`; `brakeDecel × brake` works everywhere. Still no upper cap —
orbs are the way to Light Speed. `TrackGenerator.LoopReachable` predicts with the same rule (the
bleed never takes the ship below cruise).

**Steering is a lateral force against a lateral drag** (`TrackBody.StepLateral`): the drag is
`handlingResponse` (1/s) and the force `lateralSpeed × handlingResponse`, so full steer still
settles at `lateralSpeed` in about 1 / response seconds — the two existing stats (and the store
upgrade, the tuning screen and the debug rows on them) drive the force model unchanged; there is
no separate `steerForce` / `lateralDrag` field. Drag is integrated implicitly (stable at any
step). The dash burst is still an additive velocity on top. The visual bank leans on
`body.BankDemand` — the applied lateral acceleration (steer force + slip) over the full steer
force, plus the dash — not on lateral velocity.

**Grip is tested on flat sweeps only** (`TrackBody.SlipAcceleration`, `TrackManager.FlatSweepAt`
— see `runner-track.md`). There, while Grounded, the demand `v² × curvature` beyond
`gripBase + gripPerSpeed × v` becomes an outward lateral acceleration. Slipping outward faster
than `slideThreshold` is a slide: `IsSliding`, `slideSpeedLoss` (fraction of speed per second)
scrubbed, and `Sliding(excess)` fired once as it begins (`GameManager.OnSliding`: long low rumble
+ `GameSettings.slideShake`). Banked sweeps, straights, sections and the air always hold. Demand
grows with v², grip with v, so braking is the only answer past a point — at the defaults a 490 m
flat sweep holds to ~1.3 × cruise.

**Falling off and respawn.** An open edge (`TrackManager.IsEdgeOpen`) has no clamp: the body
runs past it, and staying beyond `GameSettings.edgeOverhang` (1 m) for `edgeGraceSeconds` (0.25)
takes it `ShipState.OffTrack` (`body.LeftTrack`) — counter-steer inside the window saves it, and
a wall resuming under a body still hanging out there drops it at once. The body then stands
still and the MOTOR flies the fall: the track-space velocity becomes a world velocity at the pose
it left (`OnLeftTrack`), plain ballistics under `fallGravity` with a cosmetic tumble, started
from the rendered pose so nothing pops. After `fallDurationSeconds` (1.5) `BeginRespawnWait`
seats the ship at `FindRespawnDistance` — the first plain stretch at or PAST the fall point with
no section, flat sweep or ramp inside it or starting within `respawnClearance` (150 m) ahead, so
time is lost, never distance, and never back into the sweep that threw it — via
`body.Reset(d, 0, ShipState.Respawning)`. For `respawnWaitSeconds` (3) it sits at speed 0, no
control, `AddSpeedImpulse` ignored, `RespawnBlink` (added by `GameManager`) swapping the model's
materials with the dash ghost material at `respawnBlinkRate`; then it relaunches at the speed it
fell with × (1 − `respawnSpeedPenalty`). Events: `FellOff`, `RespawnStarted(Vector3 teleport)`,
`Respawned`. **Both phases are timers in the simulation tick, not a coroutine**, so
`motor.Paused` and a menu's timeScale freeze them; the countdown keeps running through them
(only `motor.Paused` stops it) and the stall timer does not (it lives in the normal step).

`GameManager` answers: `OnFellOff` holds the patrol (`PolicePatrol.SetHold(true)`), pulses the
glitch (`fallGlitchStrength`) and rumble, and after `fallCameraFollowSeconds` cuts to the rig's
planted cinematic shot, which just watches the ship drop; `OnRespawnStarted` hands the picture
back and `NotifyWarp`s the rig across the teleport; `OnRespawned` releases the patrol with at
least `respawnMinPatrolGap`. `Restart` clears the fall camera, `PolicePatrol.Launch` the hold.

**Post-win lockdown** (`ShipMotor.Autopilot`, set the frame the win latches, cleared by
`Launch`): steering is replaced by a pull to lateral 0, throttle held, dash requests swallowed,
and `body.HoldOnTrack` closes every edge and turns the grip test off — a win never ends in a
slide or a fall. `FinishWin` still waits for Grounded, so a win latched mid-respawn plays the
respawn out first.

**A standstill is not the end by itself.** `HasStopped` latches only after
`GameSettings.stallGraceSeconds` (2) at speed 0 with the throttle released (`UpdateStall`), so the
brake can stop the ship and the throttle pulls it away again; `GameManager` still just polls
`HasStopped` for the Stalled loss.

**Hover bob and banking are visual-only**, applied to a `visual` child transform — the root stays
exactly on the flight line (the body's pose). Keep that separation when touching movement code.

**A trigger volume would be tunnelled at 20 m per physics step — never move detection onto
colliders.** Everything is analytic for that reason: ramps, loops, and since M5 the pickups
(`PickupRegistry`, see `runner-track.md`). The ship's `BoxCollider` is only the measure of its
pickup volume now.

### State

`State` (`ShipState` — Grounded / Airborne / Looping / Falling / OnTube / OffTrack /
Respawning; append-only; owned by the `TrackBody`) with `StateChanged` /
`TookOff` / `Landed` events and `AirTime` / `AirHeight` readouts.

### Jumps (`TrackBody.StepJump`)

Every step the body scans `JumpRamp.Active`:

- A ship whose centre is inside a ramp's run-up by more than `entryMargin` is **committed** —
  lateral pinned to the ramp's rails, root riding the slope.
- **Beside** a ramp, its edge is a **wall**: held outside, `WallHit` fired,
  `sideHitSpeedLoss × speed` lost, plus rumble and glitch.
- At the lip it takes off: `AddSpeedImpulse(ramp.Boost)` (so "+N", shake and rumble come free)
  into a **real flight**: `VerticalVelocity = slope × speed`, and a per-jump gravity
  (`airGravity = 2(h0 + vy·T)/T²`, `T = AirDistanceFor(speed) × jumpStrength / speed`) chosen so
  that AT THE TAKEOFF SPEED it lands exactly the authored distance on — longer and higher the
  faster the takeoff, capped. Gravity is integrated exactly (step-size independent). Speed
  changes in the air now matter: the blending takeoff boost adds a few metres (well inside
  `landingClearance`), braking shortens the jump. Landing is `Height <= 0` on the way down —
  and coming down more than `edgeOverhang` outside the band over an OPEN edge is a fall
  (`LeaveTrack`), not a landing; every landing zone is walled today, so this is dormant. In the
  air an open edge runs no fall clock.

The root's lift is along the track's up (`ApplyPose`), so the trigger box leaves the ground lane
and ground orbs and brake pads are physically missed; the visual pitches with the slope. Lateral
speed and dash are scaled by `airControlFactor` while airborne, and `ICameraTarget.BlockModeCycle`
locks Tab so the forced Far framing holds.

**The dash is a shove, not a scripted slide.** A dash calls
`body.AddLateralImpulse(±ShipDefinition.DashImpulse)` — `dashDistance × handlingResponse` m/s, so
the lateral drag stops it after exactly `dashDistance` (there is no `dashImpulse` field: the
distance stat, the store's Dash Power and the debug row keep working). The body keeps it as
`ShoveVelocity`, decayed by the same drag as steering — separate only so a wall can tell a slam
(`WallHit` on closed edges) from steering; steering can fight it, and on an open edge it can carry
the ship off (the overhang grace applies). `dashDuration` is now just the dash WINDOW
(`IsDashing`: the ghost trail's span, no new dash inside it); a wall ends it early.

**The airborne dash is a barrel roll.** A dash requested while `Airborne` keeps its sideways shove
(at air authority) and its window lasts `ShipDefinition.barrelRollSeconds` (`DashBurstDuration` —
the ghost trail spreads its snapshots over the same span, so the ghosts show the spin) while the
visual turns a full 360° in the dash direction on top of its bank (`rollAngle`, on its own timer
so a wall or landing that cuts the dash never leaves the ship on its side; `IsBarrelRolling` /
`BarrelRollStarted(int)`).

**Dash ghosts ride with the ship.** `DashGhostTrail` (hand-placed on the Ship, `Init`-ed by
`GameManager`) parents every snapshot to a frame it seats each `LateUpdate` on the flight line at
the ship's own distance (`track.GetPoseAtDistance(DistanceTravelled, 0)` — the section pose inside
loops and tubes), so only the ship's sideways offset, hover and bank at the snapshot are frozen and
the ghosts stay beside the ship at any speed. A world-space ghost was behind the bolted 33 m chase
camera within a frame at Light Speed. Ground ghosts are spaced by **metres of lateral travel**
(`dashDistance / dashGhostCount`, read off `ShipMotor.LateralOffset`); the barrel roll keeps its
time spread. Ghosts are pooled, slide back by `GameSettings.dashGhostDriftMeters` over their life
(0 = pure staircase), and are cleared on `Launched` and while `Falling`.

The same ribbons stream through an off-track fall (`State == OffTrack`) and are cleared on
`RespawnStarted`, like a launch teleport.

`BarrelRollTrail` (added by `GameManager` beside `DashGhostTrail`) parents one `TrailRenderer` per
wingtip under the rolling visual — emitters at the model's measured half-width ×
`GameSettings.barrelRollTrailSpan`, emitting only while rolling, so the ribbons come out as two
short helices. Material `Materials/BarrelRollTrail_Mat` must be **URP Particles/Unlit additive**:
the plain Unlit ignores the trail gradient. Knobs are the `barrelRollTrail*` group under dash.
`ShipMotor.Launched` clears the ribbons on a restart teleport.

### Loops (`UpdateLoop`)

Entering a `LoopFeature`'s section takes the verdict once (`CurrentSpeed >= RequiredSpeed`,
`LoopEntered(bool)`). `Looping` rides the section's own pose.

A fail drops at half the circumference (`DropFromLoop` → `Falling`): `AdvanceAlongTrack` is
skipped, `DistanceTravelled` is **parked at the exit** — so the patrol, which never slows and
always does the perfect loop, gains the whole fall — and `ApplyPose` lerps top → exit under
`fallGravity` while slerping upright. `LoopFailed` fires, `fallSpeedLoss` (40%) is taken, a glitch
and long rumble play on the drop, and it lands through the same `Landed` event as a jump.
`BlockModeCycle` also holds while falling.

**A loop is a set piece** — the window is Looping *or* Falling (the fall is the failed loop's
second half, so nothing cuts mid-drop), ended by `StateChanged(Grounded)`:

- `GameManager.OnLoopEntered` cuts to the rig's cinematic side shot
  (`OrbitCameraRig.SetCinematic`, `GameSettings.loopCinematic`; the shot is authored on
  `Fighter_CameraSettings`' Cinematic group) and hands it back `loopCinematicHoldSeconds` (0.25,
  real time) after Grounded, or at once on `Restart`.
- The motor keeps `loopSection` / `loopDefinition` from the gate: the `LoopFeature` object is
  culled by the generator by its exit, but the state must never hang on a scene object either
  way (keyed on its mouth it was destroyed mid-loop, which read as "loop detection fails").
  The ship's shot is **planted**: a level tripod 350 m off the loop's flank, centred on it
  (`cinematicLead` = `cinematicHeight` = the loop radius, 100), panning after the ship — a
  tracking shot beside a 200 m loop shows nothing of the loop.
- `LoopSlowMo` (`Ship/`, added to the ship by `GameManager.Awake`, reads `GameSettings` live)
  eases the world clock down over `loopSlowMoBlendIn` and back over `loopSlowMoBlendOut`. The
  depth is **speed-aware**: the resting scale is `loopApparentSpeedKmh / speed` (650 km/h — the
  speed the loop is *shown* at), clamped to `loopMinTimeScale`..`loopTimeScale` (0.08..0.5), so a
  loop lasts the same ~3.5 real seconds entered at 1300 or at 6000. A flat 0.75 was invisible at
  Light Speed, where the whole loop is a third of a second. Ship, patrol and countdown all ride
  `Time.deltaTime`, so the loop costs no run time — it only plays longer. It follows the city
  `AirTimeSlowMo` clock contract exactly: enter only when the clock reads exactly 1, cancel
  silently (restoring `fixedDeltaTime` only) when a menu takes it, re-arm after the resume. Never
  owns while `motor.Paused`.

## `GameManager`

Win/lose and the countdown.

- **Win** = every mandatory objective of `level` (`RunnerLevelDefinition`,
  `Data/FiniteRunner_LevelDefinition.asset`, drawn inline) is `Satisfied`. `EvaluateObjectives`
  latches each entry once met; takeoffs are counted in `OnTookOff`. **The win latches the frame
  it is met but the run does not end yet** (`FinishWin`, unscaled time): the lose checks and the
  countdown stop, the ship flies on until `State == Grounded` with no committed `CurrentRamp` (a
  jump, loop, loop fall or tube plays out first), the MISSION ACCOMPLISHED banner slams in
  (`MissionAccomplishedBanner`, see `runner-hud-screens.md`) over the planted fly-past shot
  (`winCameraHoldSeconds`), the banner is dismissed and the `GlitchController` ramps to max over
  `GameSettings.winGlitchRampSeconds` (its fade zeroed, remembered and handed back once the
  panel is up), holds `winGlitchHoldSeconds`, then `EndRun` raises `ShowMissionComplete`.
  `PauseMenu.CanPause` refuses while `HasWon`; `Restart` stops the routine and zeroes the glitch.
  `GameSettings.lightSpeedKmh` is only the fallback for a definition with no Reach Speed target.
- **Lose** = the patrol catches up, `TimeRemaining` hits 0, or the ship stalls out
  (`motor.HasStopped` — the stall grace above, not merely speed 0). It raises
  `ShowGameOver(RunOutcome)` the same frame, mapping Caught / TimedOut / Stalled to
  `MenuTextId.LoseCaught` / `LoseTimeOut` / `LoseStalled`.
- Both endings call `ClearMessages()` so no story line sits frozen under the panel, and neither
  speaks a line or prints HUD text.

**The level definition mirrors the city's**: enum-typed `RunnerObjective` entries
(`RunnerObjectiveType` ReachSpeed / JumpCount — append-only, `[ShowIf]` per type, a per-objective
`reward`) in a mandatory `objectives` list, plus `optionalChallenges` (`RunnerOptionalChallenge`,
a `multiplier`) that are live from launch with no accept step.
`Tools → FiniteRunner → Create Runner Level Definition` creates the asset (never overwriting) and
wires an empty `GameManager.level`.

**It owns no tunables** — they all live on the `GameSettings` asset it draws inline
(`Data/FiniteRunner_GameSettings.asset`). Add new knobs there, not as fields on the manager;
patrol chase tunables live on `PatrolDefinition` instead.

`Awake` wires the scene's `PolicePatrol` (`patrol.Init(motor)`; deactivates it when
`GameSettings.patrolEnabled` is off) and spawns the `PauseMenu` **after** that init so the debug
menu can bind to the patrol's live definition.

`Awake` also finds the scene's `RunnerMusic` (`RunnerMusic.Apply`, after the CRT screen — see
`audio.md`); `FinishWin` fades it out over the glitch ramp + hold, `EndRun` fades a loss at the
asset's time, and `Restart` replays it from a new random point. The ship's own sounds are
`ShipAudio`, added beside `LoopSlowMo` (`ShipAudio.Ensure(motor).Configure(settings,
LightSpeedKmh)` while `GameSettings.sfxEnabled`); its engine gates on `motor.Paused`, so the
endings and `Restart` need no audio hook.

The timer only ticks while the motor isn't paused. `Restart()` rebuilds the track via
`TrackGenerator.RegenerateForRun()`, relaunches ship and patrol, calls
`CollectibleManager.ResetRun()`, replays the music, and reopens the tuning screen if it is enabled.

`ShipMotor.Launch()` fires up to three times per run, so it cannot be used to count attempts —
`GameManager` counts one on the first frame the motor is unpaused.

## `PolicePatrol`

The chaser: a scene object whose chase tunables live on its `PatrolDefinition` asset
(`Data/Police_PatrolDefinition.asset`, all m/s and metres), **cloned in `Init`** so the debug menu
edits the live run and never the asset — the same rule as the ship. Run-level rules (enabled,
minimap range, redeploy) stay on `GameSettings`.

- **It drives the same `TrackBody` as the ship**, through `PatrolDriver` (`GameFlow/`, plain C#,
  stateless) which outputs the same `BodyControls` the player's input does — so it is held to
  the ship's physics (steering force, grip on flat sweeps, open edges, ramps and jumps, tubes,
  the swept pickup query) and takes every loop perfectly (no gate is asked of it). It ticks in
  `FixedUpdate` with `simSubsteps` and renders an interpolated pose like the motor:
  `DistanceTravelled` / `GapToShip` are the RENDERED values (minimap), the catch, the warning and
  the redeploy judge on `SimGap` (the ticks' own, off `ShipMotor.Body`).
- **Rubber band**: targets the ship's current speed × a rubber-band factor, never below a minimum
  floor (launch speed + slow ramp). **The band IS the body's speed model**: `cruiseSpeed` = the
  target, `thrust` and the over-cruise bleed both = `catchUpAccel`, throttle held — so the speed
  moves toward the target at that rate either way, exactly the old MoveTowards. It extrapolates
  straight back while still behind the start line (negative body distance) and freezes whenever
  the ship's motor is paused.
- **The driver**: steers for the ship's lateral (nearest equivalent round a full tube); a boost
  orb it can still reach inside `orbLookaheadSeconds` pulls the line toward itself by
  `orbSeekWeight`, fading out as the gap closes inside the warn distance; a ramp in its line
  inside `rampLookaheadSeconds` is steered ROUND when the sideways travel fits in the time left,
  else it lines up with the middle and jumps it (the body does the jump); the line is kept 6 m
  inside any open edge; and for the next flat sweep inside `curveLookaheadSeconds` it solves the
  speed its own grip holds the tightest point at (`v²κ = gripBase + gripPerSpeed·v`, × 0.9) and
  brakes (`brakeDecel`) to arrive at it, capping the rubber band to that speed meanwhile.
- **Orbs it collects itself are used up** (`SpeedPad.Take()` — silent: `SpeedPad.Collected` is
  the player's event) and give it `orbBoostShare` of their boost as speed above the band, which
  bleeds back. Brake pads and coins are the player's alone.
- **A patrol fall** (`body.LeftTrack`) redeploys it behind the ship at once
  (`Redeploy(raiseFloor: false)` — new number, the inbound event, but NOT the raised floor an
  outrun patrol brings). The player is never frozen or penalised by it.
- **Handling knobs** on `PatrolDefinition`: `lateralSpeed` 22 / `handlingResponse` 6 (the same
  derived force-against-drag steering as the ship; below the ship's 30 so the player can
  out-dodge it), `gripBase`, `gripPerSpeed`, `brakeDecel`, and the Driver group above. Debug rows
  and `PatrolDebugSettings` persistence for them are M7.
- **Boost share** (`PatrolDefinition.boostShare`, 0.156 on the asset): every speed-up the ship collects — orbs,
  ramp takeoffs, anything through `ShipMotor.AddSpeedImpulse`, heard via `PadImpulse` — gives the
  patrol that fraction of the ship's actual gain (after weight) in the same frame. A +100 km/h orb
  is +70 km/h for the patrol, so boosts stop buying the gap. Brakes are never shared and the floor
  is untouched.
- **Catch** (`UpdateCatch`, `HasCaught` polled by `GameManager`): inside `catchDistance` the
  patrol stops gaining (its target is capped to the ship's speed, and it is never let closer
  than 1 m) and works on the sideways gap; it catches when ALSO within `catchLateral` (18 m)
  across the track, or after `sustainedCatchSeconds` (1.5) inside the catch distance whatever
  the sideways gap — a last-moment dodge works, dodging forever does not.
- **`Hold`** (`SetHold`) stops it moving and catching while the ship is off the track or waiting
  to relaunch — separate from `motor.Paused` because the clock keeps running. Releasing it drops
  a patrol closer than the given gap back to that gap and suppresses the taunt for it.
- **`Warned(gap)` fires ONCE per approach** when the gap drops inside the warn distance, re-armed
  once the ship opens it again, plus a proximity rumble (`ProximityRumble`, from
  `GameSettings.patrolProximityRumble`). **The patrol draws no floating text** — `GameManager`
  answers `Warned` with the "Right on your tail" RPG line (`patrolWarningMessage`, `{0}` =
  metres) only while `showPatrolWarnings` is on and the message box is idle, so a stale gap is
  never queued.
- **Redeploy keeps the chase from going stale** (`SetRedeployRule()`): outrun the patrol past
  `patrolRedeployBand.y` and it teleports back in `patrolRedeployBand.x` metres behind the ship as
  patrol N+1 (`PatrolNumber`, a rumble, the `Redeployed(int)` event — `GameManager` answers with
  the "Patrol N inbound" line, `GameSettings.patrolInboundMessage`, only while `showPatrolAlert`
  is on, which it is not by default) at `patrolRedeploySpeedFactor` × the ship's current speed,
  and that speed becomes the rubber band's new floor. **One object, never a growing fleet.**
- Its cruiser visual (hull, cabin, alternating red/blue lights) is built from primitives in code;
  colliders are stripped so it can't trip pad triggers.

## `ChaseMinimap`

Right-edge chase gauge, spawned by `GameManager` with the patrol: a vertical strip with the ship
diamond pinned at the top, the patrol icon (red/blue flicker) climbing as the gap closes, and the
gap in metres underneath. Built from code on its own overlay canvas — no scene wiring.

## `SteeringInput` / `ISteeringInput` / `IThrottleInput`

The motor only reads `ISteeringInput.SteerAxis` (−1..+1). The current implementation reads the
Ship actions of `ControlBindings` (A/D and the left stick by default) plus the touch screen
halves; the interface exists so a VR implementation can be swapped in later.

The same component is the `IThrottleInput` (`Throttle` / `Brake`, 0..1): `ShipAccelerate` (W /
right trigger) and `ShipBrake` (S / left trigger). Triggers are read analog; a key eases to full
over `ShipDefinition.digitalThrottleRampSeconds` (the motor pushes it in each tick, so the debug
clone stays live); the further-down of key and trigger wins. **Touch has no throttle control**: a
touch-only device holds throttle 1 / brake 0. A ship with no `IThrottleInput` holds full throttle.

Gamepad South also restarts on the result screen (`RaceHud`) and launches from the tuning screen,
with a 0.3 s grace period so one press can't do both. Start is reserved for the pause menu.

## `TuningScreen`

Pre-run point allocation across Launch Speed / Acceleration / Handling / Weight. It applies tuning
to a **runtime clone** of the base `ShipDefinition` via `ShipMotor.SetDefinition()` — never mutate
the asset on disk. `ShipDefinition.maxSpeed` no longer exists; the first stat raises the launch
impulse instead.

**Off by default since the Store** (`GameSettings.useTuningScreen`). `GameManager.Awake` parks the
scene's screen with `TuningScreen.Park()`: the **component** is disabled before its `Start` and
the panel hidden — never `SetActive(false)` on its object, because the component sits on the
`RaceHUD` canvas object beside `RaceHud` and every HUD text (deactivating it took the whole HUD
down once). The field is nulled so `Restart` can't reopen it, and the manager sets the motor's
definition itself through `ShipUpgradeApplier.BuildRunDefinition` (clone + store multipliers + the
armed ship debug overrides) before `ShipMotor.Start` launches.

With the toggle on, `StartRun` re-clones the base every launch and applies the store levels on top
of its points, then the debug overrides — never twice on one clone.
