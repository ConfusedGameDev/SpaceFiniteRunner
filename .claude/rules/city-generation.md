---
description: City generation — offline bake, CityLayout, road pieces and features, RoadGraph, street decoration
paths:
  - "Assets/01.Scripts/PoliceEscape/City/**"
  - "Assets/01.Scripts/PoliceEscape/Decoration/**"
  - "Assets/01.Scripts/PoliceEscape/Editor/**"
  - "**/CityDefinition.cs"
  - "**/CityGenerationSettings.cs"
  - "**/BlockSettings.cs"
  - "**/RoadPieceDefinition.cs"
---

# City generation

`Assets/01.Scripts/PoliceEscape/City/`, namespace `…PoliceEscape.City`.

**The city is a fixed-size grid of blocks, generated OFFLINE in the editor and baked into a
prefab** (`Assets/03.Prefabs/PoliceEscape/City.prefab`). Play mode does zero generation and
nothing loads on demand — the whole prefab is instantiated with the scene and `CityStreamer`
activity-culls it (see `city-performance.md`).

## Authoring chain

| Asset | Holds |
|---|---|
| **`CityDefinition`** (`04.Data/InfiniteCity/CityDefinition.asset`) | the grid (gridWidth×gridHeight blocks of `blockSizeInCells`), the `citySeed`, and one `BlockEntry` per block `{seed, BlockSettings override, connectorOnly + axis, isWater, districtOverride}` |
| **`CityGenerationSettings`** | CITY-WIDE knobs only: cellSize, arterial spacing/jitter, feature geometry, the road piece list — everything that must match across block borders |
| **`BlockSettings`** | INTERIOR-only knobs: connector density, turn probability, dead ends, feature chances, building/decoration sets + density multipliers |

**That split IS the connectivity contract.** City-wide knobs must never move into `BlockSettings`.

## `CityLayout` — the pure model

The pure city model both the baker and validation read. **The arterial field is periodic**
(wrapped world index, city seed, salts 101/202), so every adjacent block pair — including the pair
across the pacman wrap seam — computes identical border sockets independently.

A **connector-only** block suppresses every road but its own bridge line city-wide, so neighbours
correctly dead-end streets that would run into it.

`IsRoadCell` is a pure function of (citySeed + authored definition, wrapped index). Keep it that
way — everything downstream depends on two neighbours computing the same answer alone.

## `CityBaker` (editor)

Generates every block's `ChunkData` against the layout, stamps geometry via **`CityBlockBuilder`**
(pieces instantiated as prefab instances), writes the serialized **`BlockLayoutData`** onto each
**`CityBlock`** component, and saves the prefab **in place**.

**Only `Block_x_y` children are replaced.** The **`AdditionalItems`** and **`DefaultVehicles`**
sockets under **`CityRoot`** are created once and never touched — that is the whole persistence
mechanism for hand-placed content.

**Rebuilding ONE block (new seed or not) is safe by construction**: borders derive from the city
seed; block seeds only drive interiors. Salts off the block seed: 303 layout, 606 features, 404
pieces, 505 buildings, 707 decorations, 1013 curves, 1110 park lots, 1111 nature, 1212 shoreline.
Salts off the city seed: 101/202 primary arterials, 121/242 secondary, 909 districts.

**Changing a block's district, `connectorOnly` or `isWater` invalidates its neighbours' baked
border sockets — use Rebuild + Neighbours.** Masks look 1 cell out, so 4 neighbours suffice.

### `CityDesignerWindow`

`Tools → Police Escape → City Designer`, the project's first `OdinEditorWindow`. Edits the
definition through a clickable block grid (orange = bridge, blue = override, red = validation
failure, tinted underneath by district `mapColor` with a legend), validates
(arterialSpacing ≤ blockSizeInCells; city cells % spacing == 0 or the wrap-seam band may lose its
arterial; bridge feasibility; water connectivity), and drives whole-city or per-block rebakes.
**Clear Redundant Block Overrides** nulls value-identical clone `settingsOverride`s so districts
actually take effect — hand-tuned outliers survive, assets stay on disk.

## Runtime: `CityRoot`

Rebuilds the `RoadGraph` lazily from the serialized layouts (pure array walks — survives domain
reloads) and owns all spatial queries: nearest road cell, straight-runway spawns, `IsCellClear`,
`TryGetBlock` (the lazy coord → `CityBlock` lookup filled beside `RebuildGraph`).

- **Pacman wrap** (`CityWrap`): only the PLAYER teleports to the opposite edge — the periodic
  arterials make the road continue seamlessly. Abandoned NPCs die to their normal despawn ticks.
- **`CityBounds`**: NPCs may only spawn/roam in the player's block plus edge-neighbours within
  `npcEdgeEnterDistance`, released at `npcEdgeExitDistance` (hysteresis).
- `IsCellClear` ignores `DecorationProp` colliders so sidewalk props never disqualify spawn cells.

`CityManager` is a thin facade + play-mode boot (spawns managers/HUD/rain, delegates every query
to `CityRoot`) so its ~15 consumers kept their signatures.

## Road network

Per block, `RoadNetworkGenerator` carves arterials (via the layout's periodic road predicate) and
interior connectors into a `ChunkData`, resolves each cell's 4-bit `EdgeMask`, then
**`RoadFeaturePlacer`** rewrites interior cells into features (salt 606 off the BLOCK seed;
nothing within one cell of a block border):

- **Overpasses** at arterial crossings: ramp run → `road-bridge` deck cells over the crossing
  street → ramp run down. The street under the deck keeps its ground road reduced to the
  perpendicular pair; ground under other deck cells becomes `CellKind.Reserved` and gets a
  `bridge-pillar`.
- **Forks** — `road-split` is a true Y: one 0.6-wide entrance on one long side, two exits on the
  other at ±half a cell, so its stem sits on the **seam between two cells**. A straight side
  street leaving an arterial T therefore gets: the T re-stamped on the seam with
  `road-straight-half` pieces refilling the outer half cells, 0–`forkStemCells` straights on the
  seam, the split piece, and a twin branch carved in the neighbouring row that rejoins the far
  arterial with its own T. The seam cells of both rows keep ordinary grid nodes whose centres are
  pulled onto the seam via `ChunkData.SetCenterOffset`, so every graph edge stays axis-aligned.
- **Multi-cell templates** — `road-roundabout` 3×3: a piece whose `footprintInCells` > 1×1
  declares per-cell `cellMasks` (`None` = must be empty → Reserved) and is stamped once at its
  footprint centre wherever the grid matches in any rotation, rolled by `placeChance`.
- **Connector-only (bridge) blocks**: `RoadFeaturePlacer.PlaceBridge` turns the block's single
  arterial line into flat border cells → ramp run → elevated deck (`Reserved` ground beneath) →
  ramp run, using the ordinary ramp/deck stamping.

`ChunkData` is two layers — ground (`kind`/`connections`, ramps flagged with uphill direction +
step, optional centre offset) and upper (`upperConnections`). `IsBuildable` (Empty only) is what
`CityPopulator` reads; **Reserved is neither road nor lot**.

### Kenney road kit facts

Read off the FBX vertices (Unity mirrors the FBX X axis on import):

- `road-straight` runs E–W at yaw 0; `road-end` opens West; `road-bend` N+E.
- `road-slant-high` climbs its **lane** 0.01→0.51 in one tile (`road-slant` only half that).
- `road-bridge` carries an N–S deck lane at 0.51 **and** the E–W street plus supports underneath
  (`includesUnderpass`, so nothing else is stamped under it).

**Every height in the kit is a LANE height, never the tile's bounds top — that is the curb, 0.01
native above the lane.** `RoadPieceDefinition.laneHeight` records it for Standard pieces and
`CityBlockBuilder.Stamp` sinks every stamped piece by `RoadSurfaceHeight` (lane × piece scale) so
the asphalt lands on the block's ground slab at y = 0. Flat tiles carry no collider and ride that
slab; ramps and decks carry real mesh colliders. Before the sink there was a curb-high step at the
foot of every ramp (cars stuck) and every car sat 0.25 m inside the road.

The builder measures lanes by **triangle area** (the widest flat up-facing surface) — the tiles
are low-poly with no vertices at the tile centre, so a point sample there finds nothing and falls
back to the curb.

`RoadPieceDefinition.role` (Standard / Ramp / Deck / Pillar / Fork / HalfStraight): only
single-cell Standard pieces are socket-matched by `TryPickPiece`. The Fork convention is stem
West, exits East at `rotationOffset` 0. Ramp links carry measured
`rampStartHeight`/`rampEndHeight` and are spread over `rampLengthInCells` (stretched along their
uphill axis). `overpassChance` / `overpassDeckCells` / `placeFeatures` live in the settings'
"Road features" group.

**Editor tooling**: `Tools → Police Escape → Create Kenney Road Set` fills the list from the FBXs
(measures the ramp chain off its vertices, flips importer colliders on ramp/deck/pillar so
elevated surfaces are drivable — flat tiles ride the block's ground slab);
`Road Kit Showcase Scene` lays every piece out with its sockets drawn
(`RoadPieceSocketGizmo`, reads the asset live) so masks and `rotationOffset` get verified
visually; `Use Box Road Pieces` reverts to the primitive test set.

## `RoadGraph`

Nodes are **`RoadNode {cell, level}`** with baked centres (deck height on level 1, part-way up on
ramps).

**The single neighbour rule is mutual connection** (`TryGetNeighbour`): the target must carry the
opposite socket, own level first then the other. That is what links ramp ↔ deck and keeps a deck
from leaking into the street beneath. `TryGetNodeAt` picks the level by height;
`TryGetNearestNode` penalises vertical distance ×3.

AI drivers (`PoliceCarInput`, `TrafficCarInput`) keep a `planHead` node with their waypoint list
because a deck shares XZ with the street below. Spawners only use flat ground nodes
(`Level == 0 && !IsRamp`); `CityManager.TryFindNearestRoadCell(..., groundOnly)` serves the player
spawn and `TargetObject`.

### Roundabouts are a ring only in the graph

The baked data under the template is a plus (4-way centre, four arms, four Reserved corners — the
island is flat, no collider). `RoadGraph.RegisterRoundabouts` (run at every `RegisterChunk`, **no
rebake**) recognises the shape — a 3×3 Template whose centre is a 4-way and whose corners are
Reserved — and synthesises the ring: corner nodes pulled `CornerPull` (1 − 1/√2) cells toward the
centre so they sit on the same circle as the arms, arms gain their sideways sockets, all nine
cells centre-line-only and off the spawn set (`IsFlatGround` excludes `RoundaboutRole` ≠ None;
both spawners use it).

`TryGetNeighbour` carries the graph's one **directed** rule (`RoundaboutAllows`): the centre may
be left but never entered, and ring edges are counter-clockwise only (right-hand traffic). Lifted
by the `cutThrough` argument (also on `TryFindPath`), which is what a chase gets —
`PoliceCarInput.PathToPosition` passes `State == Chase`, `TrafficCarInput.ExtendWander` passes
`Fleeing`; patrol, search and civilians go round. Both wander loops drop `straightBias` on a
roundabout node ("straight" there means "keep circling"). `RoadGraphVisualizer` draws with
`cutThrough: true` so refused directions don't read as missing neighbours.

## Street decoration (`Scripts/Decoration/`, namespace `…PoliceEscape.Decoration`)

`CityDecorator` (salt 707 off the block seed, run at bake time after the populator) scatters props
from the block's effective `DecorationSet` onto **road tiles only**:

- `IntersectionCorner` spots = the four corner quads of 3+-socket tiles (light posts).
- `RoadEdge` spots = the midpoints of socket-less tile edges (cones, barriers).
- Ramps, deck/underpass cells, feature-covered cells and fork seam rows are skipped. Spots sit at
  curb height (`RoadSurfaceHeight`) and props face the tile centre.

**Every prop is a kinematic rigidbody that only the player can push.** Each carries a padded box
wake-trigger (`wakeDistance`); when the player's car (`CarInput`) enters it, a prop *lighter* than
the car flips dynamic **before contact** so the collision resolves by true mass ratio and never
brakes the car — a prop *heavier* than the car stays kinematic and the car halts against it (the
"heavier wins" rule). AI cars only ever bump a static obstacle. First real player contact adds the
set's `impactMomentum` impulse for juice.

**Per-prop mass is the whole feel dial**: cone ~2 kg flies (capped by `maxLaunchFactor`), light
post ~350 kg with high `angularDamping` topples slowly, barrier ~3000 kg barely moves.

**`explosive` props** get an `ExplosiveBarrel` on top. The barrel is not Kenney art — it is a
Unity primitive cylinder the set builder makes, authored in the kit's units off the cone's height,
weight 1.5 against the cone's 6 so a street is never lined with them. Any car above
`detonationSpeed` sets it off, and **detonation is positional, not about who touched it**:
everything inside `blastRadius` is caught. That is what makes leading a cruiser past one worth
doing, and standing next to the one you just clipped a mistake. The player takes
`explosionDamage` (0.35) through `LevelManager.ApplyDamage(amount, reason)`; a caught police car
is destroyed outright and the `PatrolManager`'s next maintenance tick cuts a replacement in at its
spawn band, away from the player. The fireball is a code-built burst off one randomly-picked
sprite from `02.Art/05.Particles/SmokeAndExplosions/Explosion` (nine complete variants, not
flipbook frames), spawned **unparented** so it outlives the barrel, with
`ParticleSystemStopAction.Destroy` so nothing has to clean it up.

**Props and barrels are baked into the prefab**, so their runtime tuning fields must be
`[SerializeField]` — a plain private field deserializes as zero and silently kills the prop
physics.

**Prop size is a target world height, not the cell fit.** The decorator scales every prop by
`cellSize ÷ nativeCellSize` (×36.9 — the ROAD's scale, where the kit's tile stands for ~7 m of
street), so a cone fitted like a tile stood 3.5 m tall beside real-metre cars. The builder's table
carries a height in metres per prop (cone 0.75, barrier 1.05, construction light 1.3, light posts
`LightPostHeight` 7.5, barrel 1.1) and writes
`scaleMultiplier = target ÷ (nativeHeight × cellFit)` (`HeightFit`, the same rule the nature
builder uses). To resize a prop: change its height in the builder table (or `scaleMultiplier` on
the set asset), re-run `Tools → Police Escape → Create Kenney Decoration Set` (which builds the tuned set from
`Roads/Decorators` and wires it into the test settings), then **rebake the city**.

## `DefaultVehicle`

Cars under the DefaultVehicles socket park kinematic with a padded wake trigger — the
DecorationProp contract. They wake dynamic when the player nears, are **never culled**
(TrafficManager only culls what it spawned) and die only to the player via `CarHealth`. Their
`CarController` never enables, so a woken one is a pure rigidbody in both physics backends.
