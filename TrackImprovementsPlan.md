# Finite Runner — Track Improvements PRD

**Scope:** the hover-ship runner (`Assets/01.Scripts/Runner/`, namespace
`ConfusedGameDev.FiniteRunner`), scene `Assets/05.Scenes/FiniteRunner_Test.unity`.
**Goal:** the track stops feeling generic. It rises and falls, banks through turns, loops that go
somewhere, and finally becomes an authored closed circuit a designer edits in the scene view.
**Status:** design agreed 2026-09-08. Nothing implemented. Milestones ship **one at a time**, each
played and tuned before the next starts.
**Supersedes:** `finiterunnerimprovements.md` (the source note). Builds on `TrackFeaturesPlan.md`.

---

## 0. Summary

| # | Milestone | One-liner | Status |
|---|---|---|---|
| M0 | **Fixes** | Loops only appear when the speed is already attainable; dash ghosts stay visible at any speed | Implemented 2026-09-08, in editor test |
| M1 | **Elevation** | The road rolls up and down inside a band, never bumpy | Implemented 2026-09-08, in editor test |
| M2 | **Banking + curves** | Turns come back and the road banks into them; features sit on level road | Not started |
| M3 | **Loop variation** | Corkscrew, yawed and elongated loops; the track continues from the loop's exit (piece-sequenced builder) | Not started |
| M4 | **Authored circuit** | Generate → edit with scene handles → save to a layout asset; closed loop rebuilt on the first frame; per-lap streaming | Not started |

M3 introduces the **piece-sequenced builder** (the track is laid as a sequence of pieces, each
starting at the previous piece's exit pose). M4 runs that same builder offline. Do not build a
throwaway exit-blend for M3.

---

## 1. Current state (facts the design builds on)

Verified 2026-09-08 against the working tree.

**Placement**
- One decision point: `TrackGenerator.PlaceFeaturesUpTo` (`Track/TrackGenerator.cs:435-478`). Pure
  weighted draw (`PickWeighted`, `:583`), no cooldown, no history, **no warm-up**. The first
  feature spot is rolled at `featureSpacing` = 600–1200 m (`:367`); a loop is eligible on that
  first draw. Cursor advance = footprint + `ExclusionAhead` + max(spacing roll, `minSpacing`).
- Scene table: Jump 50 % / Loop 20 % / Full Tube 30 %; `aheadDistance` 1600 (content is placed
  up to 1600 m ahead of the ship), `behindDistance` 300, `segmentLength` 300–420,
  `SettleMargin` = 840 m of bare spline beyond the placed content.
- `Resources/FiniteRunner_TrackDebug.asset` has `applyOnLoad: 1` and forces `trackWidth 120`,
  **`straightness 100`** and the pad table. `FiniteRunner_FeatureDebug.asset` has `applyOnLoad: 0`.
- **The base track is dead straight and dead flat.** `AddSegment` (`:408-422`) hard-codes Y = 0,
  yaw only (`maxHeading` 55°, `maxTurnPerSegment` 24°, both × `1 − straightness/100` = 0 today).
  Knots are `BezierKnot(position)` with identity rotation, `TangentMode.AutoSmooth`, appended
  through the single seam `TrackManager.AppendKnot(float3)` (`Track/TrackManager.cs:78`). All 3D
  shape today comes from `LoopSection` / `TubeSection` and the ramp arc.

**Loops**
- Required speed is a designer curve on the **loop's** track distance, not physics:
  `GameManager.LoopRequiredSpeed` (`GameFlow/GameManager.cs:95-100`) = min(2900, 1200 + 18 per
  100 m) km/h, frozen at placement (`TrackGenerator.cs:816`). Launch is 896 km/h
  (`initialImpulse` 249 m/s); one green orb (+874 km/h with the live ×5.7 multiplier) clears the
  floor. Orbs are 0.3-size spheres that must be aimed at; brakes are 32 % of the pad table.
- Verdict once at the gate (`Ship/ShipMotor.cs:586-596`); a fail rides to the top and falls
  (`DropFromLoop`, `:599-624`): −40 % speed, distance parked at the exit.
- `LoopSection` (`Track/TrackSection.cs:67-110`): pure vertical circle in the entry plane, one
  turn, full track width, **exit = entry world point and heading**. It inserts 2πR of track
  distance; the spline stays flat under it (`InsertsDistance` true, `SplineExtent` 0). The entry
  pose (`origin`, rotation) is **baked at construction** from `track.GetPoseAtDistance`.
- `TubeSection` (`:131-214`) is an overlay: re-samples the spline pose every frame, inserts nothing.

**Dash ghosts**
- `Ship/DashGhostTrail.cs`: while `motor.IsDashing`, a frozen **world-space** mesh copy is dropped
  at the ship's pose every `DashBurstDuration / dashGhostCount` **seconds** (0.431 / 6 = 0.072 s),
  fades over `dashGhostLifetime` 0.35 s, no pool, no parent. The component is hand-placed on the
  Ship object; `GameManager` find-or-adds and `Init`s it (`GameManager.cs:163`).
- The chase camera is bolted **33.2 m behind** the ship with `positionDamping 0`
  (`Fighter_CameraSettings.asset`, `Cameras/OrbitCameraRig.cs:590-608`). A ghost's on-screen life
  is therefore 33.2 m / speed: ~7 frames at 1000 km/h, ~1 frame at 6500 km/h, and on that frame it
  sits inside the ship's own silhouette. Lifetime, fog (far clip 1800), culling mask and pooling
  are **not** the cause.

**Runs, distance, laps**
- `timeLimitSeconds` = **60** (`FiniteRunner_GameSettings.asset`). No distance objective, no
  distance stat, no HUD distance. `ShipMotor.DistanceTravelled` never wraps; `DistanceToT` clamps
  to [0, SplineLength]. Consumers of distance: the generator's stream/cull windows, the motor's
  analytic checks (`UpdateLoop`, tube return, `JumpRamp.Spans`, jump arc), `PolicePatrol.GapToShip`
  and `Redeploy`, `GameManager.UpdateLoops` label lead. `TrackManager.Length` is read by the
  decorator and the generator only.
- Nothing sets `Spline.Closed`. The `SplineContainer` lives on `===ENV===/Track` with
  `TrackManager`, `TrackGenerator`, `TrackDecorator` (component order matters: manager Awake first).
- Collected **orbs are neither destroyed nor disabled** (`Track/SpeedPad.cs:60-67`); they are
  culled behind the ship. Coins deactivate via `RunConsumables`. `CollectibleManager.ResetRun`
  resets counters only.
- City→runner handoff: `LevelManager.GlitchHandoff` holds 1.2 s at full glitch, loads
  `FiniteRunner_Test` additively; the runner's `GlitchController` opens at intensity 1 fading at
  0.4/s (~2.5 s). `TrackGenerator.Awake` already builds during that window; `ShipMotor.Start`
  launches after every Awake.
- Campaign: `MissionDefinition.runnerLevel` → `RunnerLevelDefinition` (three assets:
  `FiniteRunner_LevelDefinition`, `Campaign/FiniteRunner_Level_02`, `_03`). `GameManager.Awake`
  swaps `level` from `MissionSession.Current` (`GameManager.cs:125-133`). A runner level carries
  **no track data** today.
- Editor tooling: `Runner/Editor/TrackGeneratorEditor.cs` is one "Regenerate Track" button that
  runs `Generate()` in edit mode; the spawned pads/decor are ordinary scene objects (the scene
  is marked dirty; today `Pads`/`Decor` are empty but 6 preview knots persisted). **No
  `EditorTool`/`Handles` manipulation code exists anywhere in the project** to copy from; the
  nearest precedents are `CityDesignerWindow` (Odin window) and `CityBaker` (offline bake into a
  prefab, rebuilt from serialized data at play).

---

## 2. Cross-cutting rules

- **Knobs** live on ScriptableObjects. New shape knobs go on a new `TrackShapeSettings` asset
  (`Assets/04.Data/FiniteRunner/FiniteRunner_TrackShape.asset`) drawn `[InlineEditor]` on
  `TrackGenerator`; gameplay uses a **runtime clone** (like feature definitions) and the pause
  menu's Track debug page edits the clone, persisted through `TrackDebugSettings` in the
  `straightness` pattern. Loop/gate knobs go on `LoopDefinition` / `GameSettings` as today. Every
  knob is an Odin `[PropertyRange]` / `[MinMaxSlider]`; paired values are one band unpacked by
  accessors.
- **Seeded reproducibility**: every new roll (pitch, bank, drift, yaw, carry, turns) comes from
  the generator's existing `rng` stream, in a fixed order, so a non-zero seed reproduces the
  whole layout.
- **Distance stays the authoritative coordinate.** Sections keep routing everything; no consumer
  reads the spline. Knots are never removed. Sections are registered the moment a spot is decided.
- **Features need level road**: bank is eased to 0 before a feature's entry and held 0 across its
  footprint (a loop's entry knot, a tube's whole spline stretch, a ramp's run-up). Grade is allowed.
- **Analytic detection only** (36 m per physics step at Light Speed). Nothing new is a trigger volume.
- Enums that are serialized are append-only. Scripts carry XML summaries. Editor code goes in
  `Runner/Editor/` (namespace `FiniteRunner.EditorTools`).
- After each milestone, update `.claude/rules/runner-track.md` (and `runner-ship.md` for M0's
  ghost change) so the rule files stay true.

---

## 3. M0 — Fixes

### 3.1 Speed-gated loop placement

**Player-facing:** a loop is only ever placed when the ship, as it is right now, would reach the
gate fast enough. A red gate can still happen — by bleeding or braking after the loop was
placed — but never because the requirement was out of reach when the loop was decided.

**Mechanics** (`TrackGenerator.PlaceFeaturesUpTo`):
1. After `PickWeighted` returns an entry whose `Runtime` is a `LoopDefinition`, and only in play
   with a ship (`endless && ship != null`), predict the arrival speed:
   `gap = featureCursor − ship.DistanceTravelled`,
   `predicted = ship.CurrentSpeed − ship.Definition.passiveDeceleration × gap / max(ship.CurrentSpeed, 1)`.
2. `required = gameManager.LoopRequiredSpeed(featureCursor)`. The loop is allowed iff
   `predicted ≥ required × (1 + gateHeadroom)`.
3. If refused: draw again from the table **excluding the loop entry** (renormalised among the
   rest — add an `exclude` parameter to `PickWeighted`), same cursor, no spacing roll consumed. If
   nothing else is in the table, skip the spot (advance by a spacing roll) as the null-entry path
   does today.
4. Edit-mode preview and non-endless runs are not gated (there is no ship speed to read).

With today's numbers: launch (896 km/h) never passes the 1200 floor, so no loop can be placed in
the opening stretch; one green orb makes the next spot eligible. Because loops are decided up to
1600 m ahead and the label lead is 1800 m, the player still sees the number as early as today.

**Knobs** (`LoopDefinition`, Gate group — a runtime clone, so the debug slider never touches the
asset; `GameSettings` has no clone and no debug path, which is why the knob does not live there):

| Knob | Default | Range |
|---|---|---|
| `gateHeadroom` | 0.10 | 0–0.5 |

Exposed on the Features debug tab through `AddStat<LoopDefinition>` like loop radius, persisted in
`FeatureDebugSettings.LoopValues`.

**Files:** `Track/TrackGenerator.cs` (gate + `PickWeighted(exclude)`),
`Track/Features/LoopDefinition.cs`, `Track/Features/FeatureDebugSettings.cs`,
`Screens/DebugMenuFactory.cs` (one slider), `UI/MenuTextLibrary.cs` (one id). Tests: none in
repo; editor play only.

*Implemented 2026-09-08.*

**Done when:**
- Loop entry at 100 %, 20 seeded runs: no loop is placed while the ship is below the floor; after
  one green orb a loop appears within the next two feature spots.
- A refused loop spot yields a jump or tube at the same spot (not an empty stretch).
- Seed reproduces the layout given the same driving (the rng draw order is unchanged when no
  loop is refused).

### 3.2 Dash ghosts ride with the ship

**Player-facing:** every dash shows all `dashGhostCount` ghosts as a sideways staircase beside the
ship, at 900 km/h or at Light Speed, fading over the same lifetime as today. Through a loop or a
tube the ghosts turn with the track. A barrel roll still shows the spin.

**Mechanics** (`Ship/DashGhostTrail.cs` rewrite; `DashGhost` unchanged except pooling):
- A **ghost frame** transform (runtime object, child of the trail component's object so a
  restart teleport carries it) is placed every `LateUpdate` at
  `track.GetPoseAtDistance(motor.DistanceTravelled − drift, 0, …)`: the flight line at the
  ship's own distance, lateral 0. `TrackManager` is found once in `Init` (same lookup the motor
  uses).
- A snapshot is parented to the frame and stores its **local** pose relative to the frame at
  snapshot time (`InverseTransformPoint` / inverse rotation of each visual piece). The ship's
  forward progress moves the frame; the ghost keeps only its lateral, hover height and
  bank/roll. Nothing else about the ghost's transform is touched afterwards.
- **Spawn cadence on the ground is metres of lateral travel**: a ghost at dash start, then one
  every `definition.dashDistance / dashGhostCount` metres of `|lateralOffset − lateralAtLastGhost|`
  (`ShipMotor` exposes `LateralOffset`; the trail reads it). Spawn is evaluated every frame, so a
  frame hitch never drops a ghost. **Airborne (barrel roll) keeps the time spread**
  `barrelRollSeconds / dashGhostCount` — those ghosts exist to show the rotation, and lateral
  travel is small at air authority.
- `drift`: ghosts may slide back from the frame by `dashGhostDriftMeters × age/lifetime`
  (default 0 = pure staircase, exactly what was chosen; the knob exists for tuning only).
- **Pool** `dashGhostCount + 2` snapshot roots and their material instances; `Destroy` becomes
  deactivate + return. `ShipMotor.Launched` (already used by `BarrelRollTrail`) clears every ghost.

**Knobs** (`GameSettings`, Dash group, next to `dashGhostLifetime`):

| Knob | Default | Range |
|---|---|---|
| `dashGhostDriftMeters` | 0 | 0–30 |

`dashGhostCount`, `dashGhostLifetime`, `dashGhostStartAlpha`, `dashGhostMaterial` unchanged.

**Files:** `Ship/DashGhostTrail.cs`, `Ship/ShipMotor.cs` (public `LateralOffset` read-only),
`Data/GameSettings.cs`. Rule file `runner-ship.md` (ghost trail paragraph).

**Done when:**
- Debug speed at 1000 / 4000 / 6500 km/h: a ground dash shows all 6 ghosts beside the ship for the
  full 0.35 s; the staircase reads left/right correctly.
- Dashing inside a loop and on a full tube: ghosts stay attached to the track pose.
- Barrel roll: ghosts show the 360° in order.
- Restart mid-dash leaves no ghost behind; Profiler shows zero per-dash allocations after warm-up.

*Implemented 2026-09-08 (`DashGhostTrail` rewrite, `ShipMotor.LateralOffset` / `Track`,
`GameSettings.dashGhostDriftMeters`). Editor play checks pending.*

---

## 4. M1 — Elevation

**Player-facing:** the road climbs and dips in long swells. It never feels bumpy: grade changes
are spread over whole 300–420 m segments and capped per knot. The track always drifts back
toward its baseline height, so it never disappears into the sky or the floor.

**Mechanics** (`TrackGenerator.AddSegment`):
- New per-run state `pitch` (degrees). Per segment:
  `pitch += rng(−maxGradeStepPerKnot, +maxGradeStepPerKnot)`;
  `pitch −= baselinePull × (endPosition.y / elevationBand) × maxGradeStepPerKnot`;
  `pitch = clamp(pitch, ±maxGrade)`; if `|y| > elevationBand` the pitch is forced to the sign
  that returns (a hard band, the pull is the soft one).
- `endPosition += (sin(heading)·cos(pitch), sin(pitch), cos(heading)·cos(pitch)) × length`.
- Knots carry rotation: new `TrackManager.AppendKnot(float3 position, quaternion rotation)`
  (rotation = yaw(heading) × pitch(pitch); M2 multiplies roll in). The old overload stays for the
  first knot. Unity Splines interpolates knot rotations for the up vector, so `GetPose`, the ship,
  patrol, decorator, orbs (`OrbHover` uses track axes) and the camera (`TargetUp` binding) follow
  with no change.
- Features inherit the grade (decided): a loop stands on the slope, a tube curls from the sloped
  spline, a ramp rides it. `LoopSection.Top` and the fall path are relative to the entry pose
  already.
- Hazard from `TrackFeaturesPlan.md` §1: `right = cross(up, fwd)` in `TrackManager.GetPose` is not
  `normalizesafe`; grades are capped at 20° so it never approaches vertical.

**Knobs** (`TrackShapeSettings`, Elevation `[ToggleGroup]`):

| Knob | Default | Range |
|---|---|---|
| `elevationEnabled` | on | — |
| `elevationBand` (± m around Y=0) | 60 | 0–300 |
| `maxGrade` (deg) | 6 | 0–20 |
| `maxGradeStepPerKnot` (deg) | 3 | 0–10 |
| `baselinePull` | 0.5 | 0–1 |

`TrackShapeSettings` also takes over nothing else yet: `straightness`, `segmentLength`,
`maxHeading`, `maxTurnPerSegment` stay on the generator this milestone (moved in M4 into the layout).
`TrackDebugSettings` gains the five values (captured/flushed like `straightness`); the Track debug
page gains their sliders.

**Files:** `Track/TrackShapeSettings.cs` (new), `Track/TrackGenerator.cs`, `Track/TrackManager.cs`
(`AppendKnot` overload), `Debug/TrackDebugSettings.cs`, `UI/…/DebugMenuFactory.cs`,
`Assets/04.Data/FiniteRunner/FiniteRunner_TrackShape.asset` (new), scene wiring on `Track`.

**Done when:**
- 10 seeded runs at Light Speed: |Y| never leaves the band, grade never exceeds the cap, no
  visible kink at any knot, road stamps show no gap on crests.
- Loop, tube and ramp each work on an up-slope and a down-slope (loop gate colour, fall landing,
  tube return, ramp landing).
- The debug sliders change the next regenerate; `applyOnLoad` persists them.
- `elevationEnabled` off reproduces today's flat track byte-for-byte for the same seed.

*Implemented 2026-09-08 (`TrackShapeSettings` + asset wired on the scene's Track, `pitch` walk in
`AddSegment`, `TrackManager.AppendKnot(position, rotation)`, `TrackDebugSettings` capture/apply,
four Core Settings debug sliders). Editor play checks pending.*

---

## 5. M2 — Banking + curves

**Player-facing:** the track turns again and leans into each turn like a velodrome; straights
are level. Loops, tubes and ramps always start from level road, so a loop never leans sideways.

**Mechanics** (`TrackGenerator.AddSegment`, `PlaceFeaturesUpTo`):
- Curves: `straightness` is the existing knob. The live `FiniteRunner_TrackDebug.asset` value goes
  from 100 to a turning value (start at 60) — a data change, not a code change.
- New per-run state `bank` (degrees, + = right edge up). Per segment:
  `turnDelta = heading_new − heading_old`;
  `targetBank = clamp(turnDelta × bankPerDegreeOfTurn, ±maxBankAngle)` (sign: outer edge rises);
  `bank = MoveTowards(bank, targetBank, maxBankStepPerKnot)`;
  knot rotation = yaw × pitch × roll(bank).
- **Level-entry rule.** The next feature spot is always known in advance (`featureCursor`). While
  the spline end is inside `[featureCursor − levelLeadDistance, featureCursor]` the target bank is 0;
  after a feature is decided, bank stays 0 while `track.SectionAt(track.Length) is TubeSection`
  or while the spline end is inside a claimed footprint (`claims`). A loop refused by the M0 gate
  wastes a levelled approach — accepted.
- Lateral steering is along the banked `right`, so the ship slides along the banked surface.
  Nothing in `ShipMotor` changes. `OrbHover` and the decorator follow the pose.

**Knobs** (`TrackShapeSettings`, Banking `[ToggleGroup]`):

| Knob | Default | Range |
|---|---|---|
| `bankEnabled` | on | — |
| `maxBankAngle` (deg) | 25 | 0–60 |
| `bankPerDegreeOfTurn` | 1.5 | 0–5 |
| `maxBankStepPerKnot` (deg) | 8 | 0–30 |
| `levelLeadDistance` (m) | 900 | 0–3000 |

Same `TrackDebugSettings` / debug page treatment as M1.

**Files:** `Track/TrackShapeSettings.cs`, `Track/TrackGenerator.cs`, `Debug/TrackDebugSettings.cs`,
`UI/…/DebugMenuFactory.cs`, `Resources/FiniteRunner_TrackDebug.asset` (straightness value).

**Done when:**
- Turns are visibly banked; the camera rolls with `rollLagSeconds`; the ship never leaves the road
  on a banked turn at Light Speed; the patrol rides banked road.
- Every loop entry, tube curl-in and ramp in 10 seeded runs is on level road (bank read from the
  pose at the feature start ≤ 1°).
- `bankEnabled` off + `straightness 100` reproduces the M1 track for the same seed.

---

## 6. M3 — Loop variation + piece-sequenced builder

### 6.1 Loop geometry

**Player-facing:** loops are no longer a circle back to the same spot. A loop may corkscrew
sideways (exit one or two track widths over), leave at an angle, carry the ship forward, or turn
more than once. The gate, the number above it, the slow-mo set piece and the fall all work as today.

**Mechanics** (`Track/TrackSection.cs` `LoopSection`, `Track/Features/LoopDefinition.cs`):
- `LoopSection(startDistance, radius, origin, entryRotation, lateralDrift, forwardCarry,
  exitYawDegrees, turns)`. Parameter `u ∈ [0,1]`, `θ = 2π·turns·u`, basis yawed progressively about
  the entry up: `fwd(u) = Rot(up, exitYaw·u)·forward`, `right(u) = Rot(up, exitYaw·u)·right`.
  `position(u) = origin + fwd(u)·(R·sinθ + carry·u) + up·R·(1 − cosθ) + right(u)·drift·u + right(u)·lateral`.
  Tangent by central difference on `u`; normal = direction from `position` to the ring centre
  `origin + fwd(u)·carry·u + up·R + right(u)·drift·u` (equals today's `up·cos − fwd·sin` for a
  circle). `Length` = arc length integrated once at construction into a 256-entry table
  (distance → u); `GetPose(local)` looks `u` up in the table. A circle with drift = carry = yaw = 0,
  turns = 1 reproduces today's section exactly (regression check).
- `ExitPose` = `position(1)` with forward `fwd(1)`, up = entry up. `GetExitPose(lateral)` returns
  it; the fall lands there. `DropFromLoop` triggers at `local ≥ Length / (2·turns)` (top of the
  first turn) instead of half the length.
- `TrackSection` gains `SplineExtent` (spline metres the section covers: tube = `Length`, loop =
  the bridge span, see 6.2) and `TrackManager.Recalculate` becomes
  `Length = SplineLength + Σ (s.Length − s.SplineExtent)`; `SplineDistanceOf` subtracts the same.
  `InsertsDistance` is kept as `Length > SplineExtent` for readers.
- `LoopSlowMo` is unchanged; a 3-turn loop lasts ~3× longer on screen. Tuning, not scope.

**Knobs** (`LoopDefinition`, rolled per instance from the band, off the layout rng):

| Knob | Default | Range |
|---|---|---|
| `lateralDriftRange` (m, sign random) | 0–240 | 0–600 |
| `forwardCarryRange` (m) | 0–300 | 0–1000 |
| `exitYawRange` (deg, sign random) | 0–30 | 0–60 |
| `turnsRange` (int) | 1–1 | 1–3 |
| `radius`, `exitClearance`, `fallGravity`, `fallSpeedLoss`, label knobs | unchanged | |

`FeatureDebugSettings.loop` gains the four bands; the Features debug tab edits them.

### 6.2 Piece-sequenced builder

The track is laid as **pieces in order**, each starting at the previous piece's exit pose. The
generator still streams at runtime in M3; only the order of "lay spline" vs "decide feature" changes.

- `StreamTo(target)`: while the settled length is short, lay the next piece. The next feature spot
  `featureCursor` is known; `AddSegment` is capped so a knot lands **exactly on** `featureCursor`
  when the normal roll would cross it. At that knot the feature is decided (weighted draw, M0 gate,
  M2 level rule already satisfied by the lead):
  - **Loop**: `CreateSection` from the pose at the knot (explicit tangent along the current
    heading → the entry pose is fixed regardless of neighbours), `AddSection`, then append the
    **exit knot** at `ExitPose` with an explicit tangent along the exit forward (`TangentMode.Continuous`
    with authored tangents through a new `AppendKnot(position, rotation, tangentOut)`). The spline
    span between entry and exit knots is the **bridge**; its length is the section's
    `SplineExtent`; the ship never rides it because `GetPoseAtDistance` routes `[Start, End)` to
    the section. Builder state (`endPosition`, `heading`, `pitch`, `bank`) is set from the exit pose.
  - **Tube**: unchanged — overlay on the spline the builder keeps laying (level, per M2).
  - **Ramp**: unchanged — claims footprint, spawns once the run-up is settled.
- `pendingFeature` / `pendingClaimed` bookkeeping is replaced by "decide at the knot"; a ramp's
  settle wait remains the only pending state.
- Spacing, `ExclusionAhead`, `exitClearance` and claims are unchanged in meaning.
- Decorator stamps by track distance, so the helix road and barriers come for free; the bridge
  gets no stamps because no track distance maps to it.
- `DistanceToT` callers already go through `SplineDistanceOf`; the patrol, minimap, label lead and
  streaming windows are untouched.

**Files:** `Track/TrackSection.cs`, `Track/TrackManager.cs` (`SplineExtent`, tangent overload),
`Track/Features/LoopDefinition.cs`, `Track/Features/LoopFeature.cs` (gate at entry, unchanged
shape), `Track/TrackGenerator.cs` (builder restructure), `Ship/ShipMotor.cs` (`DropFromLoop`
trigger point, exit pose), `Debug/FeatureDebugSettings.cs`, `Loop_Definition.asset`.

**Done when:**
- Drift/carry/yaw/turns all at 0/0/0/1: byte-identical layout to M2 for the same seed.
- A loop with drift 120 m exits one track width over with a smooth road before and after (no
  visible bridge, no kink at either knot at Light Speed); yaw 30° exits 30° off; carry 300 m exits
  ahead; a 2-turn loop rides twice.
- Fail case lands on the displaced exit; the patrol passes through without a jump; gate and label
  stand at the mouth; `LoopSlowMo` runs.
- Seeded runs reproduce; no orb or ramp inside a loop; no feature starts inside a tube.

---

## 7. M4 — Authored circuit

**Player-facing:** every runner level plays a hand-finished closed circuit about 20 km round. It is
built from the same procedural rules (a Generate button seeds it), then edited in the scene view
with handles, saved to an asset, and rebuilt from that asset on the runner's first frame under the
glitch. Laps repeat the same gauntlet; loops demand a little more each lap. The old endless mode
survives behind the `endless` toggle for tuning.

### 7.1 Data — `TrackLayout`

`Track/Layout/TrackLayout.cs`, ScriptableObject, created by `Tools → FiniteRunner → Create Track
Layout` (never overwriting) under `Assets/04.Data/FiniteRunner/Tracks/<Name>_TrackLayout.asset`.

- `shape` (`TrackShapeSettings` reference), `seed`, `targetLengthKm` (default 20, 5–60), `closed`
  (bool, default on), `trackWidth`, `straightness` (moved here from the generator for layout mode).
- `pieces : List<TrackPiece>` in order. `TrackPiece.kind` (`TrackPieceKind`, append-only:
  `Straight = 0, Loop, Tube, Ramp`), and per kind:
  - Straight: `length`, `turn`, `pitch`, `bank` (the end knot's deltas — the "curvature in x, y, z").
  - Loop: `radius`, `lateralDrift`, `forwardCarry`, `exitYaw`, `turns`, `requiredKmh`.
  - Tube: `length`, `radius`, `bandDegrees`, `centreDegrees`, `curlLength`, `returnLength`,
    `steeringFactor`.
  - Ramp: `length`, `lateral`, `widthFraction`, `rampAngle`.
  Piece start distances are derived (read-only in the inspector).
- `items : List<TrackItem>`: `kind` (`TrackItemKind`, append-only: `OrbGreen = 0, OrbBlue,
  OrbPurple, BrakePad, CoinRow`), `distance`, `lateral`, `lane`, `count`/`step` for coin rows.
  Items reference the generator's existing `spawnTable` / `orbTiers` / collectible prefabs by kind,
  so visuals and values stay where they are tuned.
- `RunnerLevelDefinition.trackLayout` (new field). `GameManager.Awake` resolves the layout
  (session level's, else the generator's serialized default) and calls
  `generator.BuildForRun(layout)` before `ShipMotor.Start` launches; the generator's own `Awake`
  builds only in endless mode.

### 7.2 Build — the same builder, replayed

- `TrackGenerator.Generate(layout)` in the editor: runs the M3 builder from `seed` **recording** a
  piece per knot/feature and an item per pad/coin roll until laid length ≥ `targetLength −
  closingDistance`, then the **closing rule**: each further straight steers the heading toward the
  start (ignoring `maxHeading`, limited by `maxTurnPerSegment`), pitch toward baseline, bank to 0;
  when the end is within one segment of the start with heading within 10°, the last knot is
  dropped in favour of `Spline.Closed = true` (knot 0 keeps an explicit tangent along the start
  heading). Final length ≈ target ± a segment or two. If the closing rule fails to converge in
  `closingDistance`, Generate reports it (Debug.LogWarning + inspector message) and leaves the
  loop open.
- `TrackGenerator.Build(layout)` (editor preview and runtime): replays `pieces` deterministically —
  no rng — into knots + sections (one `Recalculate` at the end, not per knot), and loads `items`
  into a sorted table. Nothing is spawned by `Build`.
- Loops replayed from a layout use `requiredKmh` from the piece (Generate seeds it from
  `LoopRequiredSpeed(distance)`), so the M0 gate does not apply in layout mode.

### 7.3 Runtime — laps

- `TrackManager.Closed` and `Wrap(distance)` (`distance mod Length` when closed). `GetPoseAtDistance`,
  `SectionAt`, `GetLateralBand`, `SplineDistanceOf` wrap internally; `DistanceToT` wraps instead of
  clamping when closed. **`ShipMotor.DistanceTravelled` stays unwrapped** (the patrol gap, redeploy,
  streaming windows and cull all keep working on raw subtraction). `Lap => floor(d / Length)`,
  `LapCompleted(int)` event on the motor (no HUD in scope).
- Motor checks that compare against a section wrap the ship distance first (`UpdateLoop`, tube
  return). Ramps and loop gates are spawned **per lap** at unwrapped distance (`lapBase + item
  distance`), so `JumpRamp.Spans` and `LoopFeature.Section.Contains` keep unwrapped semantics via
  the spawned object's own distance; the `LoopFeature` of lap N carries
  `requiredKmh + N × loopSpeedPerLapKmh`.
- Streaming in layout mode: `spawnCursor` (unwrapped) advances to `ship.DistanceTravelled +
  aheadDistance`; items are looked up at `Wrap(cursor)` and spawned keyed on the unwrapped cursor
  so `CullBehind` is unchanged. Orbs, brakes, coins, ramps and gates therefore **respawn every lap**
  (decided). Tube and loop sections exist once, from `Build`. The decorator's `DecorateUpTo` drops
  its `min(distance, track.Length)` clamp when closed and stamps at wrapped poses.
- First frame: `Build` of ~60 knots + sections + the initial 1600 m stream is the same order of
  work as today's `Generate`, inside the 1.2 s hold + 2.5 s fade.

**Knobs:** `GameSettings.loopSpeedPerLapKmh` (default 150, 0–1000); `TrackLayout.targetLengthKm`;
`TrackShapeSettings.closingDistance` (m, default 4000, 1000–10000).

### 7.4 Editor — Generate / handles / Save

All in `Runner/Editor/`, namespace `FiniteRunner.EditorTools`; authoring happens in the runner
scene on the `Track` object (decided).

- `TrackGeneratorEditor` (Odin) gains, when a `layout` is assigned: **Generate** (seeds pieces +
  items from the rules, Undo-recorded), **Rebuild Preview**, **Save** (`AssetDatabase.SaveAssets`),
  a `closed` toggle, `targetLengthKm`, the piece list (`[ListDrawerSettings]`, kind-aware fields),
  the item list, and an **Add** palette (Ramp, Green/Blue/Purple orb, Brake pad, Coin row) that
  inserts at the track distance nearest the scene-view pivot.
- **Preview objects are never saved**: everything `Generate`/`Build` spawns in edit mode gets
  `HideFlags.DontSave`, and the preview is rebuilt on scene open from the layout. (Today's
  preview writes pads and decor into the scene.) The `SplineContainer`'s knots still serialize with
  the scene; that is harmless because `Build` rewrites them.
- `TrackLayoutTool : EditorTool` (`[EditorTool("Track Layout", typeof(TrackGenerator))]`),
  active on the Track object, draws handles per selected piece/item:
  1. **Tube size**: `Handles.RadiusHandle` at the tube's midpoint → `radius`; `Handles.Slider` at
     each end along the tangent → `length`.
  2. **Loop size**: `RadiusHandle` at the ring centre → `radius`.
  3. **Loop exit**: `Handles.Disc` about the entry up at the exit → `exitYaw`; `Slider` along the
     entry right → `lateralDrift`; along the entry forward → `forwardCarry`; a `+/−` turn button
     in the inspector → `turns`.
  4. **Piece curvature x/y/z**: `Handles.RotationHandle` at a straight's end knot, decomposed into
     `turn` (yaw), `pitch`, `bank` deltas, clamped to the shape limits; `Slider` along its tangent
     → `length`.
  5. **Items**: `FreeMoveHandle` per item projected back to (distance, lateral) by nearest-point
     search (`SplineUtility.GetNearestPoint` on the spline, then section-aware refinement);
     `Delete` removes the selection; the palette adds.
  Every edit: `Undo.RecordObject(layout)`, `EditorUtility.SetDirty`, debounced preview rebuild
  (a piece edit re-lays everything downstream — expected, ~60 knots is instant).
- Handles draw labels (`Handles.Label`, the one Handles API already in use) with the piece kind
  and its start distance.

**Files:** `Track/Layout/TrackLayout.cs`, `TrackPiece.cs`, `TrackItem.cs` (new); `Track/TrackManager.cs`
(`Closed`, `Wrap`); `Track/TrackGenerator.cs` (`Generate(layout)`, `Build`, layout streaming);
`Track/TrackDecorator.cs` (closed stamping); `Ship/ShipMotor.cs` (wrapped checks, `Lap`);
`GameFlow/GameManager.cs` (resolve + `BuildForRun`); `GameFlow/RunnerLevelDefinition.cs`
(`trackLayout`); `Data/GameSettings.cs`; `Runner/Editor/TrackGeneratorEditor.cs`,
`Runner/Editor/TrackLayoutTool.cs`, `Runner/Editor/TrackLayoutCreator.cs` (new); one layout asset
per existing runner level (3) + a default on the test scene's generator.

**Done when:**
- Generate produces a closed ~20 km circuit; the ship runs 3 laps with no visible seam, orbs
  respawn each lap, the lap-2 loop demands +150 km/h, the patrol gap is continuous across the seam.
- Each handle edits its value, Undo works, Save then Play reproduces the edited track exactly.
- After a preview the scene file's diff is knots only (no `Pads`/`Decor` children).
- The city→runner handoff shows the authored track on the first clean frame; a mission's level
  plays its own layout; direct play of `FiniteRunner_Test` plays the generator's default layout.
- `endless` on: the M3 streamer still runs unchanged.

---

## 8. Milestone order and gates

M0 → M1 → M2 → M3 → M4, each ending in a play session and a tuning pass. M2 depends on M1's knot
rotation; M3's builder depends on M2's level-entry rule; M4 depends on M3's builder and
`SplineExtent`. M0 is independent and first.

---

## 9. New assets and files (expected)

| Kind | Path |
|---|---|
| Shape settings | `Assets/04.Data/FiniteRunner/FiniteRunner_TrackShape.asset` (`TrackShapeSettings`) — M1 |
| Track layouts | `Assets/04.Data/FiniteRunner/Tracks/*.asset` (`TrackLayout`) — M4 |
| Code | `Runner/Track/TrackShapeSettings.cs` — M1 |
| Code | `Runner/Track/Layout/TrackLayout.cs`, `TrackPiece.cs`, `TrackItem.cs` — M4 |
| Editor | `Runner/Editor/TrackLayoutTool.cs`, `TrackLayoutCreator.cs` — M4 |
| Settings | `GameSettings`: `dashGhostDriftMeters` (M0), `loopSpeedPerLapKmh` (M4) |
| Definition | `LoopDefinition`: `gateHeadroom` (M0); drift / carry / yaw / turns bands — M3 |
| Level | `RunnerLevelDefinition.trackLayout` — M4 |

---

## 10. Open questions (parked, by design)

- Lap counter / lap time on the HUD (M4 raises `LapCompleted`, nothing shows it).
- Per-lap difficulty beyond loop speed (patrol floor per lap? brake density?).
- Air-lane items in the layout editor (the lane field exists at 0 %).
- Loop label lead across the closing seam on short circuits.
- Whether M4 also retires `TrackDebugSettings`' width/straightness overrides in layout mode.
- Multi-path — still deferred, still behind `TrackManager`.
