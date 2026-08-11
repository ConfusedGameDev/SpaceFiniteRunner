# Police Escape — Procedural Infinite City

**Namespace root:** `ConfusedGameDev.FiniteRunner.PoliceEscape`
**Core loop:** Drive through an endless procedural city while police cars hunt you. Survive as long as possible / shake pursuit.

**Assets:**
- Buildings: `Assets/99.Test/Jorge/InfiniteCity/Buildings`
- Roads: `Assets/99.Test/Jorge/InfiniteCity/Roads`

---

## 0. Architecture Overview

```
CityManager (entry point, orchestrator)
 ├─ ChunkStreamer          — decides which chunks exist based on player position
 ├─ RoadNetworkGenerator   — per-chunk deterministic road layout + cross-chunk graph
 ├─ CityPopulator          — fills non-road cells with buildings
 ├─ RoadGraph              — runtime waypoint graph derived from roads (shared by AI)
 ├─ PlayerCarController    — WheelCollider vehicle
 └─ PatrolManager          — spawns/despawns AI cars, runs pursuit state machines
```

**Data flow:** `CityGenerationSettings` (SO) → generator produces a `ChunkData` (pure C# grid model, no GameObjects) → chunk instantiation spawns road prefabs → `CityPopulator` reads free cells from `ChunkData` + `BuildingSet` (SO) → spawns buildings → `RoadGraph` registers the chunk's road cells as navigable nodes for the AI.

Keeping `ChunkData` as a plain data model (separate from instantiation) is what makes "recalculate", "build over time", and deterministic infinite streaming all cheap to support.

**Namespaces:**
- `ConfusedGameDev.FiniteRunner.PoliceEscape.City` — generation + streaming
- `ConfusedGameDev.FiniteRunner.PoliceEscape.Population` — building placement
- `ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles` — player + shared car physics
- `ConfusedGameDev.FiniteRunner.PoliceEscape.AI` — patrol/pursuit
- `ConfusedGameDev.FiniteRunner.PoliceEscape.Editor` — editor-only tooling

---

## 1. Procedural City System

### 1.1 Grid & tile model
- City is a uniform grid of cells (cell size = road prefab footprint, e.g. 20×20 m — confirm against actual road prefab bounds; add a settings field, don't hardcode).
- Road prefabs are classified by connector shape: **Straight, Corner, T-Junction, Crossroad, (optional) Dead-end**. Each prefab entry in settings declares which of its 4 edges carry a road connection (N/E/S/W bitmask) so the generator can pick + rotate by socket matching rather than by prefab name.
- A cell is either `Road(connectionMask)` or `Empty` (→ populator territory).

### 1.2 Layout algorithm (bias toward "fun to drive")
Goal from the brief: **long straights, occasional turns, multiple routes** (a connected network, not a maze).
- Generate **arterial roads first**: long straight runs that span the chunk (and continue across chunk borders — see 1.4), spaced every `arterialSpacing` cells with jitter.
- Add **secondary connectors** between arterials with a turn probability, so blocks vary in size and there's always more than one way around.
- Post-pass: detect and repair dead ends (extend to nearest road or delete the stub) unless `allowDeadEnds` is on.
- Everything driven by a single `System.Random` seeded per chunk (see 1.4). Same seed ⇒ same city. Expose `globalSeed` in settings; `Randomize Seed` button.

### 1.3 Settings — `CityGenerationSettings : ScriptableObject`
Odin-decorated, grouped with `[TabGroup]`/`[FoldoutGroup]`:
- `globalSeed`, `cellSize`, `chunkSizeInCells`
- `initialCitySizeInChunks` (the "start parameters" — e.g. 3×3 generated before play)
- `arterialSpacing`, `turnProbability`, `connectorDensity`, `allowDeadEnds`
- Road prefab list: `RoadPieceDefinition { prefab, connectionMask, weight }`
- `[Button] Recalculate`, `[Button] Clear`, `[Button] Randomize Seed`
- Validation: `[ValidateInput]` that at least one Straight/Corner/T/Cross piece exists.

### 1.4 Infinite streaming
- **Determinism across chunks:** each chunk seeds its RNG with `Hash(globalSeed, chunkX, chunkY)`. Border agreement: whether a road crosses a shared chunk edge is decided by hashing the *edge* (`Hash(globalSeed, edgeId)`), so both neighbors independently compute the same crossing points. Arterials become globally continuous "for free" by deriving their lines from world-space coordinates, not chunk-local ones.
- **Streamer:** track player chunk coordinate. Load all chunks within `loadRadius`; unload beyond `unloadRadius` (keep `unloadRadius > loadRadius` for hysteresis so driving along a border doesn't thrash).
- **Pooling:** road and building instances come from pools; unloading returns to pool instead of Destroy. Enable GPU instancing on shared materials — static batching isn't available for streamed content.
- **Time-sliced build ("watch it grow"):** instantiation runs through a budget queue (`maxSpawnsPerFrame` or ms budget). This is one mechanism serving two features: the optional visualized build-up in editor, and hitch-free streaming at runtime. `instantBuild` toggle for editor recalcs.
- ⚠️ **Floating-point origin shift:** an infinite world drifts far from origin; WheelCollider physics and rendering degrade past ~5–10 km. Plan an origin-shift pass (teleport everything back by N km when player exceeds threshold; chunk coords are already integer so only Transforms + Rigidbody velocities need shifting). Decide early — retrofitting is painful.

### 1.5 Editor experience
- All generation must run in edit mode (no play required) via the Recalculate button.
- Gizmos overlay: draw grid, road cells (color by piece type), chunk borders, seed label per chunk.
- Generated hierarchy: `City/Chunk_{x}_{y}/Roads|Buildings` for easy inspection; mark generated objects `NotEditable` or tag them so Clear only removes generated content.

---

## 2. Procedural City Populator

### 2.1 Placement
- Input: `ChunkData` empty cells. Group contiguous empty cells into **lots** (blocks bounded by roads).
- Building entries declare a **footprint in cells** (1×1, 2×1, 2×2 …). Fill each lot greedily from largest footprint to smallest, with weighted random selection among candidates that fit; guarantee no cell is double-occupied.
- Buildings **face the nearest road edge** (rotate toward adjacent road cell). Corner cells may prefer corner-suitable buildings (optional flag per entry).
- Optional per-entry: `randomYRotationSteps`, position jitter within cell, scale jitter — cheap variety.
- Deterministic: populator uses the same per-chunk RNG stream (offset) so recalculating reproduces identical results.

### 2.2 Settings — `BuildingSet : ScriptableObject`
- `List<BuildingDefinition> { prefab, weight, footprintInCells, allowRotation, minSpacing (optional) }` rendered as an Odin `[TableList]` — this is the "easy to replace / tune probability" requirement: swapping a prefab or dragging a weight slider is one field edit, no code.
- Support **multiple BuildingSets** (e.g. downtown vs. suburbs) selectable in `CityGenerationSettings`; later this enables district variation by noise, but v1 can use a single set.
- `[Button] Repopulate` (rebuild buildings only, keep roads — much faster iteration than full recalc).

---

## 3. Player Car System

- Rigidbody + 4 `WheelCollider`s. Standard setup: motor torque on driven wheels, steer angle on front, brake torque, handbrake for drift turns (fits the chase fantasy).
- **Stability essentials:** lower `centerOfMass` manually; wheel substeps via `WheelCollider.ConfigureVehicleSubsteps`; tune friction curves for arcade feel (forgiving lateral grip, easy slides).
- `CarConfig : ScriptableObject` (shared by player and AI): motor/brake torque curves, max steer vs. speed, mass, friction presets — one config class keeps AI cars honest (same physics as the player, different driver).
- Input via the new Input System (keyboard + gamepad).
- Chase camera: smooth follow with velocity look-ahead.
- Reset/respawn: flip detection → reposition onto nearest road cell (RoadGraph lookup makes this trivial).

## 4. Patrol Car System

- **Navigation = RoadGraph, not NavMesh.** Baking NavMesh on streamed infinite chunks is fragile; the generator already knows exactly where roads are. Each road cell contributes graph nodes/edges; chunks register/unregister their nodes on load/unload. A* over this graph gives routes; a simple path-follower steers the same WheelCollider car via target waypoint + speed control (slow into corners by path curvature).
- **State machine:**
  - `Patrol` — wander the graph (random turns at junctions, bias toward not reversing).
  - `Chase` — player detected (distance + line-of-sight raycast, wider cone at higher speeds). Repath to player's cell at interval; predictive target = player position + velocity lead.
  - `Search` — lost sight: drive to last known position, sweep nearby junctions for `searchDuration`, then decay back to Patrol. This state is what makes escaping feel earned.
- **Lifecycle:** `PatrolManager` maintains `targetPatrolCount` within active chunks; spawns on far road cells (out of player sight), despawns with unloaded chunks or when far behind. Difficulty knobs in `PursuitSettings : ScriptableObject`: patrol count, detection range, chase speed multiplier, search duration, (later) escalation over time à la wanted levels.
- Avoid AI-vs-AI pileups v1: simple forward raycast → brake; proper avoidance later.

---

## 5. Milestones

- [x] **M1 — Grid + roads, single chunk:** ChunkData model, socket-matched road spawning, settings SO + Odin buttons, gizmos. *Exit: press Recalculate, get a connected drivable-looking layout every time.* *(Code complete — verify piece masks visually on first Recalculate; bend/T orientations are best guesses, fix via `connectionMask`/`rotationOffset` per piece.)*
- [x] **M2 — Populator:** lot detection, weighted footprint placement, BuildingSet SO, Repopulate button. *(Code complete — re-run the test scene menu item to get test buildings; verify facing/packing, then wire the Kenney building FBXs into a real BuildingSet.)*
- [x] **M3 — Player car:** drivable car with tuned arcade handling inside the static city; camera; respawn. *(Code complete — run `Tools/Police Escape/Create Car Test Scene`, press Play: the city regenerates, `PlayerCarSpawner` drops the car on a road cell and attaches the chase camera. Tune `TestCarConfig.asset` / `TestChaseCameraSettings.asset` live in play mode. Generation adds colliders when `generateColliders` is on: a flat ground slab per chunk plus a fitted box per building. Road-cell lookups go through `CityManager.TryFindNearestRoadCell` until M5's RoadGraph replaces them.)*
- [x] **M4 — Infinite streaming:** chunk hashing, border continuity, pooling, time-sliced spawning, unloading. *Exit: drive in one direction for 10 minutes, stable memory + framerate.* *(Code complete & verified live — CityManager streams `loadRadiusInChunks` around the player (camera fallback), unloads beyond load + `unloadPaddingInChunks` (hysteresis), builds streamed chunks through the `maxSpawnsPerFrame` budget queue with all RNG draws inline so streamed = instant. RoadGraph registers/unregisters with chunks. Not done: instance pooling (Instantiate/Destroy churn is acceptable so far) and the origin shift — still required before runs exceed ~5 km from origin.)*
- [x] **M5 — Patrol AI:** RoadGraph, path-following car, Patrol/Chase/Search, PatrolManager. *(Code complete — `RoadGraph` (A* over road cells) rebuilt on Recalculate; `PoliceCarInput` is an `ICarInput` (same CarController physics as the player) with sight-based Chase, last-known-position Search and stuck-reverse recovery; `PatrolManager` is auto-spawned by `CityManager` when its police fields are wired and keeps `PursuitSettings.targetPatrolCount` cars inside the spawn band. Re-run the car test scene menu item to build `TestPoliceCar.prefab` + `TestPursuitSettings.asset`. AI-vs-AI avoidance is the v1 forward-ray brake only.)*
- [ ] **M6 — Game loop polish:** origin shifting, difficulty escalation, minimal HUD (pursuit indicator, survival timer), audio hooks (sirens). *(Started: circular GTA-style radar done — `Scripts/UI/Minimap` + `MinimapSettings`, spawned by CityManager, police blips colored by AI state.)*

## 6. Open Questions / Risks

1. **Road prefab connector audit** — do the existing road assets share a uniform footprint and pivot? If pivots are inconsistent, add a per-piece offset/rotation field in `RoadPieceDefinition` rather than editing prefabs.
2. **Cell size vs. building footprints** — verify building bounds actually fit the block sizes the arterial spacing produces; tune `arterialSpacing` after measuring assets.
3. **Origin shifting** (see 1.4) — decide before M4; affects everything holding world-space positions (RoadGraph should store chunk-local + chunk coord to be shift-proof).
4. **Elevation** — plan assumes a flat city. Bridges/hills are out of scope v1.
5. **Escape condition** — pure survival timer, or "lose all pursuers for N seconds = escaped"? Affects M5 tuning; leaning toward the latter (Search state already supports it).

## 7. Conventions

- All scripts live in `Assets/99.Test/Jorge/InfiniteCity/Scripts/`, organized by namespace: `Scripts/City/`, `Scripts/Population/`, `Scripts/Vehicles/`, `Scripts/AI/`, `Scripts/Editor/` (editor-only assembly).
- ScriptableObject settings live in `Assets/99.Test/Jorge/InfiniteCity/Settings/`.
- Editor-only code under an `Editor/` folder or `#if UNITY_EDITOR`; Odin attributes are fine in runtime classes.
- C# parameters use lowerCamelCase.
- Generated content is never saved into the scene as permanent objects — always reproducible from seed + settings.
