---
description: Standalone physics ship — Ship assembly, HoverBody surface follower, HoverShip, ShipSettings, ship layers, sandbox scene + acceptance run
paths:
  - "Assets/01.Scripts/Ship/**"
  - "**/ShipSandbox.unity"
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

`Ship` (hulls) / `ShipGround` (everything a ship rides, lands on or hits) / `ShipPickup` /
`ShipVolume`. A level opts geometry in **by layer and nothing else**. The slots are constants,
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

## `HoverShip`

Identity over the body: input → `BodyControls` (or `ControlOverride`, the autopilot/test seam),
params refilled from the definition every tick, `FixedUpdate` ticks, `Update` poses **the root
transform** along the tick's substep path (`HoverBody.PoseAt` — exact on a loop) and applies
the ported bank / bob on the `visual` child. Definition and settings run as **clones taken in
Awake**. The rigidbody is kinematic and never integrates. `ICameraTarget` is implemented
(`ViewCycleLocked` is the game's gate); `ShipCameraAttach` attaches the chase camera in levels
with no game manager.

## Sandbox (`Ship/Sandbox/`, scene `05.Scenes/ShipSandbox.unity`)

`Tools → FiniteRunner → Ship → Build Sandbox Scene` builds the scene **additively and closes
it**, so the open scene and its unsaved changes are untouched. `ShipSandboxCourse` builds its
colliders at `Awake` through `SurfaceRibbonMesher` (single-sided ribbons, optional pipe curl,
inward walls — also the future source of the runner's track colliders); nothing is saved as a
mesh asset. `ShipDebugOverlay`: F1–F10 stations, 1/2/3 = 300 / cruise / Light Speed.

**`ShipSandboxAutoTest` is the acceptance run** (`Run()`, or `RunOne(station, speed, metres)`):
speed-model timings, every feature at 300 / cruise / Light Speed with the speed pinned, jump
lengths, tick cost — `[ShipTest]` log lines. "Worst surface step" is the correction applied per
substep (large beside a rolling bank far from its axis), not a residual error. A free ship on
the corkscrew loop scrapes the wall twice — it has no guide to follow the drift; that is the
guide spline's job, not a follower bug.

Over MCP: set `EditorSceneManager.playModeStartScene` to the sandbox, enter play, call `Run()`,
poll `Report` — and **reset `playModeStartScene` to null afterwards**.
