---
description: Standalone physics ship — Ship assembly, IShip contracts, HoverBody surface follower, HoverShip, ShipSettings, ship layers, acceptance scene + run
paths:
  - "Assets/01.Scripts/Ship/**"
  - "**/IRunnerShip.cs"
  - "**/RunnerShipSettingsSync.cs"
  - "**/ShipAcceptance.unity"
  - "**/HoverShip.prefab"
---

# Standalone ship (`Assets/01.Scripts/Ship/`, assembly `…FiniteRunner.Ship`)

The ship as a prefab-to-be that flies on **any collider surface with no track and no manager**.
It is being built beside the runner's track-space ship (`ShipMotor` + `TrackBody`, see
`runner-ship.md`), which keeps `FiniteRunner_Test` playable until the runner migrates. The
milestone plan and the decisions behind it live in the memory note `standalone-ship-plan`.

## Assembly rules

- `Ship` sits **below Runner** (refs: UI, Cameras, InputSystem, Splines, Mathematics). Runner
  references it; **PoliceEscape does not and must not need to** — the city is off-limits.
- So only **interfaces** from this assembly may ever be added to a Runner type PoliceEscape binds
  to (`ShipMotor`, `Collectible`, …): never a base class, never a public overload with a
  Ship-assembly parameter type on a method the city calls. `ICollector`, `GameSettings`,
  `MainMenuController` stay in Runner.
- Gate changes on the closure build **with `-p:DisableTransitiveProjectReferences=true`** —
  Unity's csprojs are SDK-style (transitive by default), so without the flag a reference
  PoliceEscape lacks would not show.
- `ShipDefinition`, `SteeringInput` (+ the three input interfaces), `ShipState` and
  `BodyControls` moved here with their `.meta`s and **kept their namespaces** — assets and
  callers never changed.

## Layers (`ShipLayers`, fixed slots 6–9)

`Ship` (hulls) / `ShipGround` / `ShipPickup` / `ShipVolume`. **What the ship rides is
`ShipSettings.groundLayers`** — Default + ShipGround out of the box, so the prefab flies over any
ordinary level as it is (the user's `shipCityTest` uses EVP's demo city, all on Default). A level
that shares its physics scene with colliders the ship must never touch (the runner during the
city handoff) narrows the mask to `ShipGround` alone. The slots are constants,
not name lookups (prefabs serialize the index); `Tools → FiniteRunner → Ship → Install Ship
Layers` names them and switches off every contact pair of the two query-only layers. Do not use
the glitch installer's "first free slot from 31 down" — it walks into the unnamed 28 / 29 the
runner scene uses. The city uses none of these, which keeps ship queries off city colliders
during the additive handoff.

## `HoverBody` — a cast-based surface follower, NOT a force integrator

Plain C#, shared by the ship and (later) the patrol, driven by the same `BodyControls` as
`TrackBody`. The reason it is not a dynamic rigidbody is the speed: 36 m per physics step at
Light Speed, a 60 m ramp is under two ticks, a loop wants 10⁴ m/s² of centripetal load.

- **A tick is split by distance** (`ShipSettings.maxStepMeters`, 4 m → 5 substeps at cruise, 10
  at Light Speed, capped by `maxSubsteps`). Slow-mo just lowers the count — the fixed timestep
  is never touched.
- Per substep: the **speed model and the lateral force-vs-drag model are `TrackBody`'s, ported
  verbatim** (one `ShipDefinition` feels the same on both; a dash still carries exactly its
  distance at any substep count) → free steering → hull sweep → surface re-seat → wall
  clearance.
- **Free steering turns the ship**: yaw rate = steer × min(`maxYawRate`, `turnAuthority` × grip
  ÷ speed), plus `freeStrafeShare` of the runner's strafe. `turnAuthority` > 1 makes full lock
  slide (`Sliding`, same grip fields as the runner's flat sweeps).
- **Surface re-seat**: five rays along −up (centre, ±`probeHalfExtents`); the plane through the
  four outer hits is the new up, the centre hit + ride height the new position, the heading is
  carried across by projection. **The up is never smoothed** — a lagging up misses a loop.
  Ride height is `ShipDefinition.hoverHeight` (physical here; `ShipSettings.visualLift` is the
  cosmetic extra).
- **Letting go**: no centre hit for `coyoteMeters`, or a crest beyond `magnetStrength` (0 =
  unlimited). `magnetic` off = world-gravity rules (steep surfaces need centripetal load, a bank
  pulls downhill). `LastTakeOffReason` says which.
- **A flight's gravity is solved per flight** so it lands `airDistancePerSpeed` × speed on (the
  track-space rule — real gravity at 1000 m/s is a 65 km jump), integrated exactly; in the air
  the body faces its velocity, so the pose is continuous with the slope it left.

### Walls — three layers, each there for a failure that really happened

1. **Hull sweep** (`Sweep`, SphereCast, collide-and-slide ≤ 3): a hit within `climbAngle` of the
   up is floor and ignored. `HitWall`: sideways = lateral motion stops (only a dash carried in
   is a slam — the runner's rule); nose-in = heading swings onto the wall, speed keeps its
   share along it.
2. **A sweep is blind to what it starts touching.** `NearestWall` retries with a slimmer hull
   (`SlimHull`) when the cast reported an initial overlap.
3. **`KeepClear`** — whisker rays from the hull centre push it back to the hull radius. This is
   the one that holds a ship grinding along a curved wall: **a straight line on a banked curve
   climbs the bank** (geodesic drift, ~5 m over 300 m at 80°), so the outer wall of a sweep is
   where every sweep starts touching, and both sphere casts go blind.
4. **The re-seat is a swept move that SLIDES** along a wall. Stopping it dead threw the height
   correction away, and the ship fell behind an unwinding bank until the probes lost the road.

## The optional guide spline (`Ship/Guide/`)

A level MAY have a line that helps ships along it; a ship with none in reach is fully free.
**Nothing is wired**: `SplineGuide` (beside any `SplineContainer`; the knots' rotations carry the
road's up, so a line through a loop or along a banked wall guides there too) registers in
`ShipGuideRegistry`, and `HoverShip.UpdateGuide` looks for one in reach — on a timer
(`guideSearchSeconds`, the search walks the whole line), only while it has none, at once after a
`Launch` teleport, and **only in its own scene**.

- **`IShipGuide`**: `TryProject(world, ref hint, out GuideSample)` (distance / lateral / height,
  the line's frame, signed curvature + = right, lane band, open edges, `gripTested`),
  `SampleAt(distance)`, `FindRespawn`, `Assist`, `CaptureRange`, `Scene`. The runner's
  `TrackGuide` (M6) will be a second implementation over `TrackManager`.
- **`SplineGuide` bakes the spline once** into an arc-length table (`sampleSpacing` 2 m) and
  every query runs on the table. A projection is a nearest-segment search in a **window around
  the last answer** (`searchWindow`) — cheap enough to ask every substep, and the only way the
  two passes of a line that crosses itself (a loop) are told apart. NaN hint = whole-line
  search. A point past an OPEN end is not "on the guide" (`EndSlack`), or a ship would stay
  locked to the last tangent for ever. Call `Rebuild()` after editing the spline at runtime.
- **What the assist does** (`HoverBody.FollowGuideHeading`, assist = ship's `guideAssist` ×
  the guide's `Assist`): the heading is eased onto the line (`guideHeadingResponse`), **locked
  to it at full assist** — the road supplies the heading and the stick strafes, the runner's
  feel. The player's own yaw is scaled by (1 − assist) and the strafe share rises to 1. **The
  guide never moves the body**: it still rides whatever surface is under it, and walls stay
  physical.
- **The tangent is taken half a substep AHEAD (midpoint rule).** Stepping along the tangent at
  the start of each substep walks off the outside of every curve, millimetres at a time — metres
  over a sweep.
- A ship flying the line backwards is helped backwards (the tangent is flipped to its heading).
- **`gripTested`** makes the guide's road the runner's flat sweep: the demand becomes v²κ, the
  excess over the ship's grip pushes it outward, and only slip faster than `slideThreshold`
  counts as a slide (at the reference numbers an R = 2500 sweep holds at cruise, drifts ~3 m at
  1.4×, slides at 1.6×).
- Track coordinates for consumers: `HoverShip.Guide` / `GuideSample` (null = free flight).

Acceptance (all at cruise unless said): hands-off R = 490 S-curve within **0.03 m** of the line;
assist 0.3 still needs steering; guide switched off mid-curve = **no position jump**; the
corkscrew loop with a guide = **zero wall hits** at 300 / 1000 / 1806 m/s (free, it scrapes).

## Contracts: `IShip`, `IRunnerShip`, `ShipRegistry`

- **`IShip`** is what the rest of the game reads off a ship, in world terms only (speed, state,
  dash / roll, `Paused`, `HasStopped`, the feedback events, `Launch` / `AddSpeedImpulse` /
  `SetDefinition`). **Both ships implement it** — `HoverShip` and the runner's `ShipMotor` — so a
  consumer is written once. It is an interface and must stay one (see Assembly rules).
- **`IRunnerShip : IShip`** (in Runner) adds what only a track can answer: `DistanceTravelled`,
  `LateralOffset`, `AirHeight`, `Track`, `CurrentRamp`, `CurrentLoop`, `Autopilot`,
  `LoopEntered` / `LoopFailed`. `ShipMotor` implements it natively; the standalone ship will
  answer it through the runner's bridge, off the guide's projection.
- **`ShipRegistry.Find(scene)`** is "which ship flies this scene" — ships register in
  OnEnable / OnDisable, the list is cleared at boot (domain reload off), and lookups are
  **scene-scoped** because the city→runner handoff keeps two scenes alive.
  `FloatingTextSystem` uses it, so it works for either ship.
- **Not done on purpose**: the rest of the runner's consumers (`RaceHud`, `ShipAudio`,
  `ShakeOnPad`, `PadEffects`, `DashMeterUI`, `DashPromptController`, `LoopSlowMo`,
  `SpeedPad.Collect`, the debug tabs, `GameManager`) still hold a `ShipMotor`. Their serialized
  scene references cannot be an interface, so how each one finds its ship is decided with the
  swap scene (M7), not speculatively.

## Feel components (Ship assembly, on the prefab)

`BarrelRollTrail` and `RespawnBlink` moved here from Runner and read any `IShip` +
`ShipSettings` (Feel group: ghost material, blink rate, roll-trail knobs). **On the prefab they
wire themselves to the `HoverShip` beside them in `Start`**; the runner's `GameManager` still
adds them to its `ShipMotor` with `Init` / `Ensure` + `Configure`. `ShipGhostMaterial` is the
shared translucent fallback (the dash ghosts delegate to it).

**`RunnerShipSettingsSync`** (Runner) is how the runner's rules reach components that only
speak `ShipSettings`: it owns ONE runtime `ShipSettings` per run and **re-pushes the
`GameSettings` values into it every frame** (the FALL & RESPAWN debug page edits that asset
live). `GameSettings` was not split and no asset was migrated.

## `HoverShip`

Implements `IShip`. **Dash / barrel roll / stall are the motor's, ported one-to-one**: the meter
recharges at the definition's rate, `TryDash(±1)` spends `ShipSettings.dashCost` and shoves the
body by exactly the definition's dash distance (verified 20.70 m at 300 and at 1806 m/s); in the
air the same shove rides under a 360° roll on its own clock; `HasStopped` latches after
`stallGraceSeconds` at a standstill with the throttle released. A `ControlOverride` swallows the
input's dash requests (an autopilot is hands-off); a ship with no throttle input holds full
throttle, like the runner's.

Identity over the body: input → `BodyControls` (or `ControlOverride`, the autopilot/test seam),
params refilled from the definition every tick, `FixedUpdate` ticks, `Update` poses **the root
transform** along the tick's substep path (`HoverBody.PoseAt` — exact on a loop) and applies
the ported bank / bob on the `visual` child. Definition and settings run as **clones taken in
Awake**. The rigidbody is kinematic and never integrates. `ICameraTarget` is implemented
(`ViewCycleLocked` is the game's gate); `ShipCameraAttach` attaches the chase camera in levels
with no game manager.

## Acceptance scene (`Ship/Sandbox/`, scene `05.Scenes/ShipAcceptance.unity`)

**`ShipSandbox.unity` and `shipCityTest.unity` are the USER's hand-made playgrounds — never
rebuild or edit them.** The scripted run has its own scene and its own reference assets
(`04.Data/Ship/Acceptance_ShipDefinition` = the Fighter as accepted, `Acceptance_ShipSettings`),
so tuning the game's ship never moves the goalposts and a test never touches the game's ship.

`Tools → FiniteRunner → Ship → Build Acceptance Scene` builds it **additively and closes it**,
so the open scene and its unsaved changes are untouched. `Update HoverShip Prefabs` adds
whatever components the ship has gained to every prefab carrying a `HoverShip` (idempotent). `ShipSandboxCourse` builds its
colliders at `Awake` through `SurfaceRibbonMesher` (single-sided ribbons, optional pipe curl,
inward walls — also the future source of the runner's track colliders); nothing is saved as a
mesh asset. `ShipDebugOverlay`: F1–F10 stations, 1/2/3 = 300 / cruise / Light Speed.

**The course can be generated in the editor** (`ShipSandboxCourse`, Editor group: **Generate**
/ **Clear**), two ways, switched by `keepGenerated`:

- **Preview (off, default)** — objects, meshes and the checker material are `HideFlags.DontSave`:
  never written to the scene, they follow the sliders (`OnValidate` regenerates), and play builds
  the very same course. **Unity does not destroy DontSave objects when a scene unloads**, so a
  preview left up would sit in play mode as a second, hierarchy-less copy of every collider: the
  component is `[ExecuteAlways]` only to take the preview down on `ExitingEditMode` / `OnDisable`
  and put it back on `EnteredEditMode` (a `SessionState` note; `OnEnable` is too early — coming
  back from play it still reads as playing). `Awake` does nothing in edit mode.
- **Kept (on)** — ordinary scene objects, saved with the scene: move a wall, delete the ramp,
  reshape a guide's spline. Play flies them **as they stand**; `Awake` only walks the course to
  rebuild its stations (`stationsOnly`) and creates nothing. Generate again throws the hand
  edits away. The meshes have no asset behind them, so they are embedded in the scene file.

**`ShipSandboxAutoTest` is the acceptance run** (`Run()`, or `RunOne(station, speed, metres)`):
speed-model timings, every feature at 300 / cruise / Light Speed with the speed pinned, jump
lengths, dash carry, the barrel roll, the stall rule (`RunFeel()` alone), tick cost — `[ShipTest]` log lines. "Worst surface step" is the correction applied per
substep (large beside a rolling bank far from its axis), not a residual error. A free ship on
the corkscrew loop scrapes the wall twice — it has no guide to follow the drift; that is the
guide spline's job, not a follower bug.

Over MCP: set `EditorSceneManager.playModeStartScene` to the acceptance scene, enter play, call `Run()`,
poll `Report` — and **reset `playModeStartScene` to null afterwards**.
