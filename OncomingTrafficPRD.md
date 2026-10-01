# PRD — Oncoming traffic: hover cars driving the track toward the ship

| | |
|---|---|
| **Status** | 2026-10-01: M1 played and approved. M2–M5 code landed and compiles; not yet play-tested, and the M5 tuning pass is still open. See §14 for where the build differs from this plan. |
| **Branch** | `feature/roadPieceBlend` (uncommitted). |
| **Scope** | Runner game only (`FiniteRunner_Test`, campaign runner levels). The city chase and its `TrafficManager` are untouched. So are `Campaign` and `SaveData`. |
| **Related** | `PatrolDuelPRD.md` (the analytic-contact and laser-kill precedents), `TrackAuthoringPRD.md`, `.claude/rules/runner-ship.md`, `.claude/rules/runner-track.md` |

---

## 1. Problem

The runner's road is empty apart from static hazards (laser gates, ramps, open edges) and the
patrol behind you. At speed the stretches between features read as dead air. We want some levels
to put **moving hazards ahead**: civilian hover traffic coming the other way, which has to be
threaded at Light Speed. That adds a steering skill that neither the static lasers nor the patrol
behind provide.

## 2. Goals

1. Selected levels have a steady stream of **oncoming cars**, visually varied, that never visibly
   pop in or out.
2. Hitting a car **hurts exactly like a laser** (hull, speed, blink, rumble), and the car explodes.
3. Traffic is a **fair** hazard. It never parks inside a laser beam or on a ramp, never appears on
   the end run-up, and never spawns once the run is effectively decided (near the end, or after HIT
   HYPERSPACE).
4. Traffic and the patrol **destroy each other** on contact. Leading the patrol into traffic is a
   play, the same way leading it into a laser is.
5. It costs nothing in levels that don't use it, and it never allocates mid-run.

## 3. Non-goals

- **City traffic.** `PoliceEscape/AI/TrafficManager.cs` and its types are not reused or referenced
  (they can't be: PoliceEscape → Runner, never the reverse). Only the pattern of a weighted vehicle
  list is copied.
- **Traffic in loops or tubes** (D1).
- **Same-direction traffic**, lane-changing cars that weave on purpose, or cars that react to the
  ship.
- **Car-vs-car collisions.** Spawn spacing keeps them apart.
- **Traffic as an authored placement.** It is a runtime system. Saved tracks (`TrackLayoutAsset`)
  carry no traffic data and the Scene-view authoring tools do not change.
- **Colliders on cars.** All contact is analytic, per the project invariant.
- **The patrol steering around traffic**, minimap blips, pass-by audio (see §13).

## 4. Settled design decisions

| # | Decision |
|---|---|
| D1 | **Open road only.** A car is never spawned where the stretch between it and the ship contains a loop or tube section. It therefore never has to drive one backwards. Traffic resumes once the ship has cleared the section. |
| D2 | **Enabled per level.** `RunnerLevelDefinition.traffic` references a `TrafficDefinition`; empty means no traffic. The `PF_TrafficSystem` prefab is hand-placed under `===SYSTEMS===` and idles unbound. |
| D3 | **A car hit = a laser hit.** Same hull damage (`laserDamage`), speed loss (`laserSpeedLoss`), invulnerability blink, rumble, shake, smoke and SFX. There are no separate damage knobs. |
| D4 | **The car always explodes on contact with the ship**, even while the ship is blinking invulnerable. An invulnerable ship takes no damage. |
| D5 | **HIT HYPERSPACE clears all traffic.** The moment `HyperspaceJump.Jumping` flips, every active car is recycled. Cars within the visible range explode (harmlessly); the rest vanish. No more spawns this run. |
| D6 | **No spawns near the end.** Spawning stops when the ship's `DistanceRemaining ≤ noSpawnNearEnd`, and no car is ever placed at or past `track.EndZoneStart`. Cars already on the road keep driving and recycle normally. |
| D7 | **Out-of-sight spawn, behind-the-ship recycle.** Cars appear far enough ahead that the fog hides the spawn at any speed, and return to the pool once they are `despawnBehind` metres behind the ship. |
| D8 | **Cars avoid lasers and ramps laterally.** They never jump ramps and never cross a beam. A car is only spawned if every obstacle on its path leaves a gap it fits through. |
| D9 | **Power-ups have no effect on cars.** Cars run no pickup sweep, so speed orbs, repair orbs, pads and collectibles are all ignored, and none are consumed. |
| D10 | **Car + patrol = both explode.** The car is recycled and the patrol goes through the laser-kill path (`Kill(false, false)`): fireball, redeploy, no floor raise. |
| D11 | **Mesh variety is data.** A weighted list of vehicle prefabs on the definition, each with its own scale, yaw, hover height and contact size. |
| D12 | **Pooled, never instantiated mid-run.** The whole pool is prewarmed when the level binds. |

## 5. Functional requirements

### 5.1 Driving (D1, D11)

- R1.1 A car lives in **track space**: `Distance`, `Lateral`, `Speed`, `TargetLateral`, plus the
  previous tick's `Distance` and `Lateral` for interpolation and sweeps. Distance from the track
  start is the authoritative coordinate; spline `t` is never stored.
- R1.2 It is **kinematic**. On each fixed substep (`GameSettings.simSubsteps`, like the patrol)
  `Distance -= Speed · dt`. `Lateral` eases toward `TargetLateral` at `lateralSpeed`. `Speed` is
  rolled once per spawn from `speedBand` and does not change. There is no grip or slide sim.
- R1.3 The pose is `TrackManager.GetPoseAtDistance(Distance, Lateral)` lifted by the entry's
  `hoverHeight` along the track up, and yawed 180° so the car faces the ship. Bank comes from the
  track pose. A lane change adds a small cosmetic roll on the model only.
- R1.4 Rendering interpolates between the last two ticks in `Update`, the way
  `PolicePatrol.ApplyPose` (`PolicePatrol.cs:1214`) does.
- R1.5 Cars keep `edgeMargin` inside any open edge (`TrackManager.IsEdgeOpen`), mirroring
  `PatrolDriver.KeepOffOpenEdges`. They never fall.

### 5.2 Spawning and recycling (D1, D5, D6, D7)

- R2.1 **Candidate distance**:
  `d = ship.Distance + max(minSpawnAhead, ship.Speed · spawnAheadSeconds)`, clamped to at most
  `TrackGenerator.SettledDistance` so the road, colliders and decoration are already built there.
  If clamping brings `d` closer than `minSpawnAhead`, skip this tick.
- R2.2 **A spawn is rejected** when any of these hold:
  - the system is unbound or the level has no traffic;
  - the run is over or ending (`IsEnding`, `RunOver`), or `Jumping` is true;
  - the ship is paused, off the track, falling or respawning;
  - `DistanceRemaining ≤ noSpawnNearEnd`, or `d ≥ track.EndZoneStart`;
  - any `TrackManager.Sections` entry (loop or tube) overlaps `[ship.Distance, d]` (D1);
  - another active car is within `spawnSpacing` (rolled per spawn) of `d`;
  - the path `[ship.Distance, d]` has an obstacle that leaves no lateral gap of at least the car's
    width plus `2 · avoidMargin` (R3.3).
- R2.3 **Fleet cap**: at most `maxActive` cars at once. At most one spawn per fixed tick, so a
  fresh level fills the road gradually from the far end rather than all at once.
- R2.4 **Spawn lateral**: a random point inside the free lateral band at `d`.
- R2.5 **Recycle**: a car whose `Distance < ship.Distance − despawnBehind` goes back to the pool
  (disabled, re-parented under the system). A replacement comes from the normal spawn loop.
- R2.6 **Restart**: `GameManager.Restart()` calls `traffic.ResetForRun()`, which recycles every car
  and reseeds the RNG. This follows the existing `ResetForRun` pattern; `Restart` raises no event
  today. `TrackGenerator.Regenerated` also recycles everything.
- R2.7 **Hyperspace** (D5): on the frame `Jumping` becomes true, every active car is recycled.
  Those within `visibleRange` of the ship spawn a car explosion first. The jump's
  `ForceEndAhead` cut would invalidate cars beyond the new end anyway.
- R2.8 **Ship respawn after a fall**: cars more than `despawnBehind` behind the respawn point
  recycle through R2.5. Cars near the respawn point are covered by the respawn blink (D4).

### 5.3 Avoiding lasers and ramps (D8)

- R3.1 A new plain-C# helper, **`TrackObstacles`** (no MonoBehaviour, no per-tick state), answers
  this question: "which lateral spans are blocked over `[from, to]`?" It reads
  `TrackGenerator.Placements`:
  - **Ramp** records (`JumpRamp`): `lateral ± HalfWidth` over the ramp footprint
    (`FootprintLength`). End ramps never matter, because no car reaches the run-up (D6).
  - **LaserGate** records, by variant (`LaserGate.cs:88-125`):
    - Horizontal and Triple block `lateral ± beamLength/2`;
    - Rotor blocks `lateral ± beamLength/2` (its disc);
    - Vertical blocks `lateral ± beamRadius`.

    Each span is padded by the emitter size, and its depth is `±beamRadius` (plus `beamLength/2`
    for a Rotor).
  Keep-outs are pruned behind the generator's cull line. Only ground at or ahead of the ship is
  ever asked about, which is all traffic needs.
- R3.2 **Steering**: each tick a car asks `TrackObstacles` for the blocked spans over
  `[Distance − avoidLookahead, Distance]` (its direction of travel is decreasing distance). It
  subtracts them, plus `avoidMargin` and the open-edge margins, from
  `TrackManager.GetLateralBand`. It then picks the free interval nearest its current `Lateral` and
  sets `TargetLateral` to the nearest point in that interval. `avoidLookahead` must be long enough
  for `lateralSpeed` to cover the widest move (validated with an Odin `[InfoBox]`).
- R3.3 **Feasibility at spawn**: the same helper is run over the whole path `[ship.Distance, d]`.
  If any obstacle leaves no gap ≥ car width + `2 · avoidMargin`, the spawn is rejected (R2.2). A
  spawned car can therefore always get through.
- R3.4 Cars never run a pickup or laser sweep (D9). If a car ever overlaps a beam anyway (a bad
  tune), nothing happens: no hit, no kill.

### 5.4 Ship contact (D3, D4)

- R4.1 **Swept, analytic, per substep.** Closing speed is ship speed plus car speed, so at Light
  Speed the gap can shrink by more than 40 m per physics step. A point test would tunnel. For each
  car:
  - `gapPrev = car.PrevDistance − ship.PrevDistance` and `gapNow = car.Distance − ship.Distance`;
  - contact needs the interval `[min(gapNow, gapPrev), max(gapNow, gapPrev)]` to overlap
    `±(car.halfLength + shipReach.length)`;
  - **and** `|car.Lateral − ship.Lateral| ≤ car.halfWidth + shipReach.width` (the lateral is taken
    at the crossing, interpolated between ticks);
  - **and** the ship's height above the road is below `car.hoverHeight + car.height`. A ship
    jumping a ramp over a car clears it.

  The ship's reach is the same `pickupReach` that `ShipMotor` sizes from its BoxCollider
  (`ShipMotor.cs:359/381`). Ship-side teleports (a jump of more than 400 m, as in
  `ShipMotor.SweepLaserGates`) skip the sweep that tick.
- R4.2 There is no contact while the ship is off the track, respawning, falling, `IsEnding`, or
  `Jumping`.
- R4.3 **On contact**:
  1. The car explodes: `ExplosionVfx.SpawnFireball` at the car's visual, with
     `GameSettings.explosionTextures`, `explosionScale × TrafficDefinition.explosionScale`,
     `explosionLifetime` and `explosionParticles`. The car is recycled.
  2. `TrafficSystem` raises `ShipStruck` (an event; traffic never references `GameManager`).
  3. `GameManager` handles it with the **same chain as a laser**. The body of `OnLaserHit`
     (`GameManager.cs:971-994`) is extracted into a private `ApplyHazardHit()` that both callers
     use:
     - bail out if `IsEnding`, `RunOver` or `Jumping`;
     - with the hull on, call `ShipHealth.ApplyTrafficHit()`. This is new and does the same as
       `ApplyLaserHit` (`ShipHealth.cs:205`). If it returns false (invulnerable), stop: the car has
       already exploded (D4) and the ship takes nothing;
     - `motor.ApplyImpactSpeedLoss(settings.laserSpeedLoss)`, then smoke, `laserHitRumble`,
       `laserHitShake`, `ShipAudio.PlayLaserHit()`, and the glitch pulse when the hull is off.

  A hull that reaches 0 here loses the run through the existing `Destroyed → BeginFail` path.
- R4.4 No contact means no feedback. Near misses do nothing in v1.

### 5.5 Patrol contact (D10)

- R5.1 A new contract in `Contracts` (no references):

  ```csharp
  public interface ITrafficVictim
  {
      float Distance { get; }        // track metres, mirrored like IChaseTarget
      float PrevDistance { get; }    // last tick, for the sweep
      float Lateral { get; }
      float Height { get; }          // above the road
      Vector2 Reach { get; }         // (length, width) half-extents
      bool CanBeHitByTraffic { get; } // false while hidden, gone, or killHide is running
      void HitByTraffic();
  }
  ```

  `PolicePatrol` implements it from its `TrackBody` and its existing `PickupReach`.
- R5.2 The same swept test as R4.1 runs against the patrol each substep. On contact the car
  explodes and is recycled, and `HitByTraffic()` runs the laser-kill path (`Kill(false, false)`,
  `PolicePatrol.cs:554`): fireball, rumble, shake, hit-stop, hide, redeploy, no floor raise. It
  never touches `LaserGate.Hit` or any ship event.
- R5.3 There is no traffic-vs-patrol contact during the duel's control takeover (the patrol is
  alongside the ship and the exchange owns both). Revisit only if it reads badly.

### 5.6 Mesh variety and the pool (D11, D12)

- R6.1 `TrafficDefinition.vehicles` is an Odin `[TableList]` of `TrafficVehicle` entries:
  `prefab`, `weight`, `scale`, `yawOffset`, `hoverHeight`, `halfLength`, `halfWidth`, `height`.
  Picks reuse `Runner/Track/Spawning/WeightedTable.cs` (`IWeightedEntry`).
- R6.2 **Starter set**: the `Cyberpunk_Megapolis/Prefabs/Car/` air-traffic prefabs
  (`CP_Air_Traffic_Car_01`, `_Car_02`, `_Minivan`, `_Minibus`, `_Truck`, `_Bus`,
  `_Garbage_Truck`). These are wheel-less, front at +X (`yawOffset −90`), and keep their LODGroups
  and emissives. The patrol already uses `CP_Air_Traffic_Car_01`, so a tint or emissive tweak keeps
  civilians distinguishable from the cruiser.
- R6.3 **Pool**: when bound to a level with traffic, the system prewarms `maxActive` disabled
  instances **per vehicle entry**. On spawn it rolls an entry by weight among entries that have a
  free instance. Every collider on the model is stripped, as `PolicePatrol.BuildVisual` does.
  There is no `Instantiate` or `Destroy` after bind (explosion VFX aside, see §13).
- R6.4 Binding a different level, or a different definition, tears down and rebuilds the pool.
  This happens at bind only, never mid-run.

## 6. Tunables

### 6.1 New `TrafficDefinition` (ScriptableObject)

Lives in `Assets/04.Data/FiniteRunner/Traffic/`, with menu
`FiniteRunner/Traffic Definition`. Gameplay reads a runtime clone (`Instantiate(definition)`). The
style is Odin: `[TitleGroup]`s, a `[PropertyRange]` on every number, `[MinMaxSlider]` bands with
accessor properties, `[SuffixLabel]` + `[Tooltip]` everywhere. The values below are starting
guesses for the tuning pass.

| Group | Field | Range | Start | Meaning |
|---|---|---|---|---|
| Vehicles | `vehicles` | — | 7 CP air-traffic cars | Weighted mesh list (R6.1) |
| Fleet | `maxActive` | 0–24 | 8 | Cars alive at once |
| Fleet | `spawnSpacing` | 20–600 m band | 80–220 | Least gap to the nearest car at spawn, rolled |
| Fleet | `spawnAheadSeconds` | 1–10 s | 4 | Spawn this many seconds of ship travel ahead |
| Fleet | `minSpawnAhead` | 200–2000 m | 700 | Never closer than this (fog cover) |
| Fleet | `visibleRange` | 100–2000 m | 600 | Cars inside this explode on a hyperspace clear (R2.7) |
| Fleet | `despawnBehind` | 5–200 m | 40 | Recycle once this far behind the ship |
| Fleet | `noSpawnNearEnd` | 0–5000 m | 1500 | No spawns once the distance left is this or less |
| Driving | `speedBand` | 10–200 m/s band | 40–90 | Car cruise speed, rolled per spawn |
| Driving | `lateralSpeed` | 1–30 m/s | 8 | Lane-change rate |
| Driving | `avoidLookahead` | 20–400 m | 120 | How far down the road a car plans |
| Driving | `avoidMargin` | 0–6 m | 1.5 | Clearance kept from a beam or ramp edge |
| Driving | `edgeMargin` | 0–10 m | 4 | Clearance kept from an open edge |
| Impact | `explosionScale` | 0.2–3 | 0.8 | × `GameSettings.explosionScale` |
| Impact | `explosionRumble` | Vector3 | (0.4, 0.3, 0.25) | Pulse for a car explosion near the ship (hyperspace clear, patrol contact) |
| Impact | `explosionShake` | shake settings | — | Same, camera |

### 6.2 New `RunnerLevelDefinition` field

- `traffic` (`TrafficDefinition`, may be null). Tooltip: *"Oncoming traffic for this level. Empty =
  none."*

### 6.3 Reused, not duplicated

`laserDamage`, `laserSpeedLoss`, `laserHitRumble`, `laserHitShake`, `laserSmoke*`,
`hitInvulnerabilitySeconds`, `explosionTextures/Scale/Lifetime/Particles` (`GameSettings`).

### 6.4 Debug menu

A **Traffic** tab built in the style of `DebugMenuFactory.BuildPatrolTab`
(`DebugMenuFactory.cs:511`). It has sliders for `maxActive`, `speedBand` min/max,
`spawnAheadSeconds`, `minSpawnAhead` and `noSpawnNearEnd`. Each row writes the asset
(`DebugAssetEdits.Touch`) and mirrors the value onto the live clone. The tab is hidden when the
level has no traffic. New `MenuTextId` entries (append-only) are translated in all four languages.

## 7. Code touch points

| Where | Change |
|---|---|
| `Runner/Traffic/TrafficDefinition.cs` | New ScriptableObject (§6.1) and `TrafficVehicle` entry |
| `Runner/Traffic/TrafficSystem.cs` | New. Pool, spawn loop, sweeps, `ShipStruck` event, `Bind`, `ResetForRun` |
| `Runner/Traffic/TrafficCar.cs` | New. Pooled shell: track-space state, pose, interpolation |
| `Runner/Track/TrackObstacles.cs` | New plain-C# blocked-span helper (R3.1) |
| `Contracts/TrafficContracts.cs` | New `ITrafficVictim` |
| `Runner/GameFlow/GameManager.cs` | Bind traffic (beside the patrol, around :381); subscribe to `ShipStruck`; extract `ApplyHazardHit()` from `OnLaserHit` (:971); call `traffic.ResetForRun()` in `Restart` (:1015); feed it `IsEnding`, `RunOver`, `Jumping` and `DistanceRemaining` through a small run-state read (the existing `IRunState`) |
| `Runner/Ship/ShipHealth.cs` | `ApplyTrafficHit()` beside `ApplyLaserHit` (:205) |
| `Runner/GameFlow/PolicePatrol.cs` | Implement `ITrafficVictim`; `HitByTraffic()` → `Kill(false, false)` |
| `Runner/GameFlow/RunnerLevelDefinition.cs` | `traffic` field |
| `Runner/Screens/DebugMenuFactory.cs`, `UI/MenuTextLibrary.cs` | Traffic tab and strings |
| `Runner/Editor/SystemSandboxes.cs` | `("Traffic", "Assets/03.Prefabs/FiniteRunner/PF_TrafficSystem.prefab")` |
| `03.Prefabs/FiniteRunner/PF_TrafficSystem.prefab` | New, placed under `===SYSTEMS===` in `FiniteRunner_Test` |
| `04.Data/FiniteRunner/Traffic/Traffic_Default.asset` | New starter definition |

## 8. Invariants that must survive

- **No colliders, no triggers.** All contact is the swept analytic test of R4.1 and R5.2.
- **Gameplay never writes a settings asset.** Traffic reads a runtime clone. Only the debug tab
  writes the asset.
- **Tunables live in the ScriptableObject**, never as fields on `TrafficSystem`.
- **Scene-lifetime systems are hand-placed.** `PF_TrafficSystem` sits under `===SYSTEMS===`.
  The pooled cars are per-run objects parked under it.
- **Systems stay independent.** Dropped into an empty scene, the traffic system boots unbound and
  idles without errors. It finds nothing in the scene: `GameManager` binds it, and it reaches the
  ship's damage only through the `ShipStruck` event and the patrol only through `ITrafficVictim`.
- **Domain reload is off.** Subscribe in `OnEnable`/`OnDisable`, and reset the pool and RNG in
  `Bind` / `ResetForRun`.
- **Distance from the track start is the coordinate**, and speeds are m/s.
- **Serialized enums are append-only** (`MenuTextId`). No new `TrackPlacementKind` is needed.
- **Timers run in the fixed tick**, not coroutines.

## 9. Milestones

| # | Content | Done when |
|---|---|---|
| M1 | `TrafficDefinition`, `TrafficSystem`, `TrafficCar`, the pool, kinematic driving, spawn/recycle, the `RunnerLevelDefinition.traffic` hook, `Bind`/`ResetForRun` | Cars stream toward the ship on a traffic level, never pop in view, recycle behind, none on a level without traffic. They drive straight through hazards for now. |
| M2 | `TrackObstacles`, steering (R3.2), spawn feasibility (R3.3), open-edge margin, the loop/tube rule (D1) | Over a long run, no car ever touches a beam or a ramp, and none appear in a loop or tube stretch. |
| M3 | Ship sweep, `ApplyHazardHit` refactor, `ApplyTrafficHit`, car explosions; patrol sweep, `ITrafficVictim` | A car hit costs exactly a laser's hull and speed with the same feedback. The car explodes even while blinking. Patrol + car both explode and the patrol redeploys. Lasers behave as before. |
| M4 | End rules (D6), hyperspace clear (D5), restart/regenerate recycle, respawn behaviour | No spawn inside `noSpawnNearEnd` or on the run-up. Pressing the chord clears all cars. A retry starts with an empty road. |
| M5 | Debug tab, sandbox entry, docs (§11), tuning pass | Sandbox smoke test passes, System Independence validator is clean, debug sliders change live traffic. |

## 10. Verification (every milestone)

- **Play `FiniteRunner_Test`** with a level whose `traffic` is set, and once with it empty. The
  empty run must be unchanged.
- At **Light Speed**, confirm hits register: no car passes through the ship without a hit or a
  clean dodge. This is the tunnelling check for R4.1.
- Fly **into a laser on purpose** after the refactor and confirm the laser hit is identical to
  before.
- **Lose a life** to traffic, retry, and confirm the road starts empty and the life count drops.
- **Kill the hull with traffic** and confirm the normal ship explosion and MISSION FAILED flow.
- Trigger **HIT HYPERSPACE** with cars in view: they all explode or vanish and none come back.
- **Tools → Refactor → Build System Sandboxes**, then **Run Sandbox Smoke Test**
  (`Temp/SandboxSmoke.txt` clean), then **Validate System Independence**.
- Compile check per the project's usual route (Editor.log / Bee DLL mtimes).

## 11. Documentation to update (M5)

- New path-scoped rule **`.claude/rules/runner-traffic.md`** covering `TrafficSystem`, the pool,
  the spawn rules, `TrackObstacles`, the contacts and the tunables.
- `CLAUDE.md`: a **Traffic** bullet under *Runner game design*, plus a row in the rules index.
- `.claude/rules/runner-ship.md`: the shared `ApplyHazardHit` chain, and the patrol's
  `ITrafficVictim`.
- `.claude/rules/runner-track.md`: `TrackObstacles` as the blocked-span query.

## 12. Risks

| Risk | Mitigation |
|---|---|
| Tunnelling at Light Speed (closing speed > 40 m/step) | The relative-gap sweep in R4.1, verified in §10 |
| Cars popping in view on a long straight or with fog off | `minSpawnAhead` + `spawnAheadSeconds`, clamped to `SettledDistance`; skip a spawn rather than spawn close (R2.1) |
| Traffic starves after loops and tubes (D1) | Expected and short. The spawn loop resumes as soon as the ship passes the section exit. |
| Dense gate or ramp clusters reject every spawn | Accepted. It makes stretches with lots of hazards quieter, which is fair. Watch for it in tuning. |
| Explosion VFX allocates a GameObject per pop | Same as every explosion today. A pooled `ExplosionVfx` is a separate task (§13). |
| Refactoring `OnLaserHit` changes laser behaviour | Pure extraction, with the laser check in §10 |
| Civilians confused with the patrol cruiser (same model family) | Exclude `CP_Air_Traffic_Car_01` or tint civilians (R6.2) |

## 13. Open questions (for the tuning pass or later)

- OQ1 Should the patrol's driver steer around traffic, or is "lead it into a car" the intended
  play? (v1: it doesn't steer around.)
- OQ2 Minimap blips for oncoming cars?
- OQ3 A pass-by whoosh and a car-explosion SFX distinct from the ship's?
- OQ4 A near-miss reward (floating text, small boost)?
- OQ5 Pooled explosion VFX for all explosions.
- OQ6 Should cars ever appear in loops and tubes in a later version (they would ride the pose
  function like the patrol)?
- OQ7 Do traffic hits count in `PlayerStats` / the LOG screen?

## 14. As built (deviations from the plan above)

- **`TrackObstacles` reads the BUILT objects** (`JumpRamp.Active`, the `LaserGate`s in
  `PickupRegistry`), not `TrackGenerator.Placements`. Cars only spawn on settled road, so every
  obstacle on a car's path is built. This works for saved tracks too, and the gate spans come from
  the gate's own bounds plus a new `LaserGate.EmitterReach`.
- **The ship is an `ITrafficBody`** as well as the patrol (an `ITrafficVictim`). The contract has
  `TrafficHeight` / `TrafficReach` / `TrafficSolid`, and the system keeps the previous tick's
  distances itself (so there is no `PrevDistance` on the contract). `ShipMotor`'s reach is its
  BoxCollider: `pickupReach` plus a new half length.
- **`ApplyHazardHit(bool fromTraffic)`** is the shared chain. `OnLaserHit` and `OnTrafficStruck`
  both call it.
- **No `explosionRumble` / `explosionShake` on the definition.** A patrol contact already rumbles
  through `Kill`, and the hyperspace clear is harmless and silent, so only `explosionScale` is
  there.
- **Hyperspace** is fed by `GameManager.Update` setting `traffic.Suspended = hyperspace.Jumping`
  (`HyperspaceJump` raises no event).
- **Spawn clamp at the end**: the candidate distance is clamped to just before `EndZoneStart`
  rather than rejected, so traffic keeps flowing until `noSpawnNearEnd`.
- **Debug tab** goes through the `DebugPages` registry (`TrafficDebugPage`, order 5), keyed on a
  static `TrafficSystem.Live` set at bind. It doesn't go through `DebugMenuFactory`.
- **Install**: Tools → FiniteRunner → Install Oncoming Traffic (`TrafficInstaller`) creates
  `Traffic_Default` (six CP air-traffic cars, not `Car_01`) and `PF_TrafficSystem`, nested and
  wired inside `PF_Systems`. The sandbox entry is `OncomingTraffic`.
