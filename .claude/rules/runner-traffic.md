---
description: Runner oncoming traffic — TrafficSystem, TrafficCar, the pool, spawn rules, TrackObstacles avoidance, swept contact with the ship and the patrol
paths:
  - "Assets/01.Scripts/Runner/Traffic/**"
  - "**/TrackObstacles.cs"
  - "**/TrafficContracts.cs"
  - "**/TrafficInstaller.cs"
---

# Runner oncoming traffic

Design and decisions: `OncomingTrafficPRD.md` (D1–D12). Runner only — the city's
`PoliceEscape/AI/TrafficManager` is a different system and is never referenced.

## Pieces

| Type | Where | Role |
|---|---|---|
| `TrafficDefinition` | `Runner/Traffic/` | The tunables: weighted `vehicles` table (`TrafficVehicle`: prefab, weight, scale, yaw, hover height, contact half length / half width / height), Fleet, Driving, Impact. Cloned at bind; gameplay reads only the clone. |
| `TrafficSystem` | `Runner/Traffic/`, `PF_TrafficSystem` nested in `PF_Systems` | Pool, spawn loop, steering, swept contact, `ShipStruck` event, `Suspended`. `static ForecastContact` (seconds to the first car a body holding its line would meet, −1 none; same overlap test as a real contact, pure) — `DodgeSlowMo` asks it. |
| `TrafficCar` | `Runner/Traffic/` | One pooled shell: track-space state (`Distance`, `PrevDistance`, `Lateral`, `PrevLateral`, `Speed`, `TargetLateral`, `HomeLateral`), kinematic `Step`, interpolated `ApplyPose`. No colliders. |
| `TrafficDebugPage` | `Runner/Traffic/` | The pause menu's TRAFFIC tab (registry order 5), shown only while `TrafficSystem.Live` is bound. Rows write the asset (`DebugAssetEdits.Touch`) and mirror onto the clone. |
| `TrackObstacles` | `Runner/Track/` | Blocked lateral spans over a stretch: built non-end `JumpRamp.Active` and `LaserGate`s from `PickupRegistry` (beam bounds + `EmitterReach`, none for a Vertical beam). `Collect`, `FreeIntervals`, `Nearest`. Reads BUILT objects, so only ask about settled road. |
| `ITrafficBody` / `ITrafficVictim` | `Contracts/TrafficContracts.cs` | What traffic reads of a body it can hit. `ShipMotor` implements `ITrafficBody` (explicitly; reach = its BoxCollider: `pickupReach` + half length); `PolicePatrol` implements `ITrafficVictim` (`HitByTraffic` → `Kill(false, false)`). |
| `TrafficInstaller` | `Runner/Editor/` | **Tools → FiniteRunner → Install Oncoming Traffic**: `Traffic_Default` asset, `PF_TrafficSystem` prefab, nested + wired into `PF_Systems`. Idempotent; never touches a level. |

## Wiring

- A level opts in with `RunnerLevelDefinition.traffic` (null = none). `GameManager.Awake` calls
  `traffic.Bind(motor, motor, patrol, motor.Track, generator, this, settings, level.traffic)` after
  the patrol's init and subscribes `ShipStruck`; a null definition (or none spawnable) unbinds, and
  unbound the system idles (sandbox `OncomingTraffic`).
- `GameManager.Restart` → `traffic.ResetForRun()` (empty road, `Suspended` off, fresh RNG, sweep
  history dropped). `TrackGenerator.Regenerated` also recycles every car.
- `GameManager.Update` sets `traffic.Suspended = hyperspace.Jumping` every frame. Turning on
  clears the road: cars within `visibleRange` ahead explode (harmless), the rest vanish.
- The pool is built at bind: `maxActive` shells PER vehicle entry, disabled under `Pool`. Nothing
  is instantiated or destroyed mid-run (the explosion VFX aside — `ExplosionVfx` is unpooled).

## Spawn rules (`TrySpawn`, at most one per fixed tick)

`d = ship.Distance + max(minSpawnAhead, ship.Speed · spawnAheadSeconds)`, clamped to
`generator.SettledDistance` and to just before `track.EndZoneStart`; skipped when that leaves it
closer than `minSpawnAhead`. Rejected when: `Suspended`, the fleet is full, the ship is not
`Steady`, the run is ending/over, `DistanceRemaining ≤ noSpawnNearEnd`, another car is within the
rolled `spawnSpacing`, **any loop or tube section overlaps `[ship.Distance, d]`** (D1 — the path
only shrinks, so a clear one stays clear), or some obstacle on that path leaves no gap
(`PathPassable`, each obstacle against the lane at its middle). The lane is a random point of the
free lanes at `d`. Recycled once `Distance < ship.Distance − despawnBehind` (or < 0).

## Driving

Kinematic in track space, one step per fixed tick: `Distance -= Speed·dt`, `Lateral` moves toward
`TargetLateral` at `lateralSpeed`. `Steer` sets `TargetLateral` every tick to the free point
nearest `HomeLateral` over `[Distance − avoidLookahead, Distance]` — the lane (`GetLateralBand`)
less the car's half width, `edgeMargin` off any open edge here or at the lookahead's end, and every
obstacle padded by half width + `avoidMargin`. No free lane = hold the line. Cars never fall, never
take pickups (D9), never collide with each other.

## Contact (analytic, swept — never colliders)

`Contacts()` after the step. Per body it keeps last tick's distance/lateral itself (a jump over
400 m is a teleport: history reset). `Touches`: the along-track gap, swept from last tick to this,
overlaps `±(car.halfLength + reach.z)`; the lateral difference at the crossing (interpolated) is
within `car.halfWidth + reach.x`; and the body's bottom (`TrafficHeight − reach.y`) is below the
car's roof (`hoverHeight + height`) — a ship on a jump clears a car.

- **Ship** (only while `TrafficSolid` and the run is live): the car explodes and is recycled
  **always** (D4), then `ShipStruck` → `GameManager.OnTrafficStruck` → `ApplyHazardHit(true)`, the
  chain `OnLaserHit` shares: bail on ending/over/jumping, `ShipHealth.ApplyTrafficHit()` (= the laser
  hit; the blink absorbs it and then nothing plays), `laserSpeedLoss`, smoke, `laserHitRumble`,
  `laserHitShake`, `PlayLaserHit`, the glitch with the hull off.
- **Patrol** (`TrafficSolid` false while hidden, gone, caught or `InExchange`): car explodes, then
  `HitByTraffic()` — the laser kill's `Kill(spendsDashMeter: false, raiseFloor: false)`.

Car explosions are `ExplosionVfx.SpawnFireball` with the GameSettings explosion textures /
lifetime / particles, scaled by `explosionScale × TrafficDefinition.explosionScale`, at the car's
sim pose (not its interpolated transform).
