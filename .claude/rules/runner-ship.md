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

The simulation. Applies the launch impulse, constant `passiveDeceleration` (no upper cap), queued
pad impulses (blended in at the ship's `acceleration` rate), lateral steering clamped to
`TrackManager.HalfWidth`, and sets `HasStopped` at speed 0. Tracks `DistanceTravelled` and remaps
it to spline t each frame. Exposes a `PadImpulse` event and a `Paused` flag (used by the tuning
screen, and by `GameManager` to freeze the sim when the run ends).

**Hover bob and banking are visual-only**, applied to a `visual` child transform — the root stays
exactly on the flight line so trigger detection is unaffected. Keep that separation when touching
movement code.

**A trigger volume would be tunnelled at 20 m per physics step — never move detection back onto
colliders.** Everything below is analytic for that reason.

### State

`State` (`ShipState` — Grounded / Airborne / Looping / Falling / OnTube) with `StateChanged` /
`TookOff` / `Landed` events and `AirTime` / `AirHeight` readouts.

### Jumps (`UpdateJump`)

Every frame it scans `JumpRamp.Active`:

- A ship whose centre is inside a ramp's run-up by more than `entryMargin` is **committed** —
  lateral pinned to the ramp's rails, root riding the slope.
- **Beside** a ramp, its edge is a **wall**: held outside, `WallHit` fired,
  `sideHitSpeedLoss × speed` lost, plus rumble and glitch.
- At the lip it takes off: `AddSpeedImpulse(ramp.Boost)` (so "+N", shake and rumble come free)
  into `y(s) = h0 + slope·s − a·s²`, tangent to the ramp, landing at exactly
  `JumpDefinition.AirDistanceFor(speed)` metres — longer and higher the faster the takeoff,
  capped.

The root's lift is along the track's up (`ApplyPose`), so the trigger box leaves the ground lane
and ground orbs and brake pads are physically missed; the visual pitches with the slope. Lateral
speed and dash are scaled by `airControlFactor` while airborne, and `ICameraTarget.BlockModeCycle`
locks Tab so the forced Far framing holds.

**The airborne dash is a barrel roll.** A dash requested while `Airborne` keeps its sideways burst
(at air authority) but spreads it over `ShipDefinition.barrelRollSeconds` (`DashBurstDuration` —
the ghost trail spreads its snapshots over the same span, so the ghosts show the spin) while the
visual turns a full 360° in the dash direction on top of its bank (`rollAngle`, on its own timer
so a wall or landing that cuts the dash never leaves the ship on its side; `IsBarrelRolling` /
`BarrelRollStarted(int)`).

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
  jump, loop, loop fall or tube plays out first), the `GlitchController` ramps to max over
  `GameSettings.winGlitchRampSeconds` (its fade zeroed, remembered and handed back once the
  panel is up), holds `winGlitchHoldSeconds`, then `EndRun` raises `ShowMissionComplete`.
  `PauseMenu.CanPause` refuses while `HasWon`; `Restart` stops the routine and zeroes the glitch.
  `GameSettings.lightSpeedKmh` is only the fallback for a definition with no Reach Speed target.
- **Lose** = the patrol catches up, `TimeRemaining` hits 0, or the ship stops. It raises
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

The timer only ticks while the motor isn't paused. `Restart()` rebuilds the track via
`TrackGenerator.RegenerateForRun()`, relaunches ship and patrol, calls
`CollectibleManager.ResetRun()`, and reopens the tuning screen if it is enabled.

`ShipMotor.Launch()` fires up to three times per run, so it cannot be used to count attempts —
`GameManager` counts one on the first frame the motor is unpaused.

## `PolicePatrol`

The chaser: a scene object whose chase tunables live on its `PatrolDefinition` asset
(`Data/Police_PatrolDefinition.asset`, all m/s and metres), **cloned in `Init`** so the debug menu
edits the live run and never the asset — the same rule as the ship. Run-level rules (enabled,
minimap range, redeploy) stay on `GameSettings`.

- **Rubber band**: targets the ship's current speed × a rubber-band factor, blending toward it at
  a catch-up acceleration, never below a minimum floor (launch speed + slow ramp). It advances by
  distance along the track centre, extrapolating straight back when still behind the start line,
  and freezes whenever the ship's motor is paused.
- **Boost share** (`PatrolDefinition.boostShare`, 0.7): every speed-up the ship collects — orbs,
  ramp takeoffs, anything through `ShipMotor.AddSpeedImpulse`, heard via `PadImpulse` — gives the
  patrol that fraction of the ship's actual gain (after weight) in the same frame. A +100 km/h orb
  is +70 km/h for the patrol, so boosts stop buying the gap. Brakes are never shared and the floor
  is untouched.
- Reaching the catch distance triggers a game over (`HasCaught`, polled by `GameManager`).
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

## `SteeringInput` / `ISteeringInput`

The motor only reads `ISteeringInput.SteerAxis` (−1..+1). The current implementation reads the
Ship actions of `ControlBindings` (A/D and the left stick by default) plus the touch screen
halves; the interface exists so a VR implementation can be swapped in later.

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
