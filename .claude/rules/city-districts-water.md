---
description: City variety layers — districts, parks, curved avenues, water and coastline, building sets
paths:
  - "Assets/01.Scripts/PoliceEscape/City/**"
  - "Assets/01.Scripts/PoliceEscape/Decoration/**"
  - "**/DistrictDefinition.cs"
  - "**/NatureSet.cs"
  - "**/ShorelineSet.cs"
  - "**/ShorelinePlacer.cs"
  - "**/WaterSplashZone.cs"
  - "**/LotNaturePlacer.cs"
  - "**/CityPopulator.cs"
  - "**/BuildingDefinition.cs"
---

# Districts, parks, curved avenues, water

Read alongside `city-generation.md` — these are the anti-uniformity layers over the generator.
Both respect the same rule: **`IsRoadCell` stays a pure function of (citySeed + authored
definition, wrapped index)**, so two neighbouring blocks always agree on their border sockets.

## Districts

**`DistrictDefinition`** assets (`04.Data/InfiniteCity/Districts/`, built + wired by
`Tools → Police Escape → Create District Assets`) bundle a district's flavour: an
`interiorSettings` `BlockSettings`, park knobs (`isPark` / `parkLotChance` / `natureSet`), curve
knobs (`curveChance` / `maxCurvedAvenues`), and the ONE border-relevant flag,
`useSecondaryArterials`.

A block's district is resolved by `CityDefinition.DistrictFor` (salt 909):
authored `BlockEntry.districtOverride` → seeded radial map (torus Chebyshev rings around a
downtown anchor derived from the city seed — ring 0 downtown, inner ring midrise, outer blocks
hash-pick from `outerDistricts` weights) → `defaultDistrict`.

Fallback chain for interior knobs (`BlockKnobs`):
**block override → district settings → definition default → city settings.**

### Two-tier arterial field

How road rhythm varies without breaking the border contract. The primary field (salts 101/202) is
untouched — its connectivity guarantee stands. A denser secondary band field (salts 121/242,
`secondaryArterialSpacing` on `CityGenerationSettings`) materializes only inside blocks whose
district enables it.

This works because the district lookup is itself a pure function. A secondary street crossing into
a sparse district dead-ends at the last dense cell with both neighbours computing the identical
mask (which needs a single-socket piece — validated). `FindBridgeLine` stays **primary-only**, so
district edits can never move a bridge.

Changing a block's district invalidates its neighbours' baked border sockets — **Rebuild +
Neighbours**.

## Park lots

**Park lots are model data, not stamping.** `RoadNetworkGenerator.MarkParkLots` (salt 1110) claims
whole lots as `ChunkData.CellFlags.ParkLot` — cells stay Empty and the slab stays drivable —
because `CityBlock.SetData` snapshots before any content pass.

`CityPopulator` skips flagged cells. `LotNaturePlacer` (salt 1111) fills them from a
**`NatureSet`** (a `DecorationSet` subclass, so nature props ride the same DecorationProp physics)
with ground/path tiles and `LotInterior` / `LotPerimeter` props — the two `DecorationPlacement`
values appended so serialization holds. `ChunkLots.FindLots` is the one shared lot finder.

`Tools → Police Escape → Create Kenney Nature Sets` builds Park/Beach sets plus
`KenneyDecorationSet_Palms` (palm-lined streets via the ordinary decorator) from the NaturePack.
**The nature kit is human-scale while a cell is a street tile**, so the builder computes per-prop
`scaleMultiplier` from a target world height instead of letting the cell fit inflate a tree to
45 m.

## Curved avenues

`RoadFeaturePlacer.PlaceCurves` (salt 1013, runs **before** the other features) picks two interior
arterial straights ≥2 cells inside the border, fits a seeded Bézier (control clamped into the AB
box → monotone), and rasterizes it into a 4-connected staircase of Connector cells whose
`centerOffset`s (±0.45) pull the graph nodes onto the curve — so `LaneRules.LaneTarget`'s miter
join drives it with **zero AI changes**. Curve cells are flagged `CellFlags.Curve` (feature cells,
so forks and overpasses keep off; junction cells become plain stamped Ts) and ribbon-clipped cells
(0.8-cell radius) are Reserved.

`CityBlockBuilder.StampCurve` rebuilds the smooth line as a Unity spline through the **serialized
offset centres** — the offsets ARE the curve, so rebakes reproduce it — and chord-stamps
road-straight pieces (`curveChordFraction`, ×1.04 overlap). Flat chords ride the ground slab.

Curve nodes keep lane discipline (`RoadGraph` excludes the Curve flag from `centerLineOnly`) but
are **never spawn cells** — `IsFlatGround`, `RoadCells`, `TryGetNearestNode(flatGroundOnly)` and
`CityRoot.IsStraightRoad` all exclude them, because a grid-aligned pose on a diagonal chord sits
half off the asphalt.

## Water and coastline

**Hand-painted water blocks** (`BlockEntry.isWater`, a per-block toggle in the City Designer next
to `connectorOnly`; **never seeded**) carve the outline into islands and channels.

The mechanism is the connector-block suppression reused with no surviving line:
`CityLayout.BlockSpec.IsWater` makes `IsRoadCell` answer "no road" for every cell of the block
city-wide, **inserted before the connector branch**, so each neighbour's street dead-ends at the
shore with both sides computing the identical mask.

**`isWater && connectorOnly` is a causeway**: it falls through to the bridge rule, so the bridge
line is the one road across the sea.

In the model it is **`ChunkData.CellKind.Water = 4`** (appended, so old prefabs' byte kinds are
untouched). A plain water block floods every cell after `CarveArterials` carved nothing; a
causeway runs the ordinary `PlaceBridge` and then floods what is still Empty — under-deck cells
stay **Reserved**, so `StampRoads` puts `bridge-pillar`s standing in the sea and the map keeps its
`reservedColor` bridge shadow.

Every consumer is safe by construction: `IsRoad` (graph nodes, spawns, decoration spots) and
`IsBuildable` (populator, park lots) both exclude Water; `RoadNetworkGenerator.FloodEmpty` never
touches a road/ramp/Reserved cell. `CityBlock.isWater` is baked beside `connectorOnly` (gizmo:
blue outline, `[water]` / `[causeway]` label).

### The water body replaces the ground slab (`CityBlockBuilder.BuildWater`)

**No collider at y = 0 over the sea** — or it is invisibly drivable. Instead:

- A sea-floor box (top at `seaFloorDepth`).
- A **splash trigger filling the whole water column** from `waterLevel` down to the floor — full
  depth rather than a thin slab, so no fall speed tunnels through — carrying `WaterSplashZone`.
- The surface as a primitive **Quad** at `waterLevel` scaled ×1.002 so adjacent sea blocks show no
  seam. It must be a built-in mesh so the prefab keeps a valid reference — a hand-built Mesh would
  not be saved. The minimap camera picks it up for free.
- **Per-cell mini-slabs (top exactly y = 0) under a causeway's flat road cells**: the bridge
  line's two border tiles are flat tiles, which carry no collider and would otherwise have no slab
  — the collider gap that dropped the car at the causeway's first tile.
- Water blocks also carry an opaque **`SeaFloor`** quad (`seaFloorMaterial`), because a
  depth-based fog fogs a transparent surface by what lies behind it — without a floor the sea
  reads as the far plane and fogs solid at the shore.

Knobs live in the settings' **"Water"** group: `waterLevel` −2, `seaFloorDepth` −6,
`splashDamage` 0.3, `waterMaterial`, `shorelineSet`.

### The splash contract

`WaterSplashZone.Splash` is the one entry point for anything that drives in (`[SerializeField]`
damage, because it is baked):

- **The player** takes `splashDamage` through `LevelManager.ApplyDamage` (glitch-pulse fallback
  without a manager) and is put back on the nearest road by their `CarRespawner` (1 s cooldown
  against double hits). **No barrier walls — the shore is a real drop.**
- **AI cars die** through `CarHealth` (fuse → explosion → wreck, exactly like a barrel kill; a car
  with no health component is destroyed) and the `PatrolManager`'s next tick replaces them — the
  same bait-tactic logic as the barrels.

**Wraps only land on land**: `CityWrap` checks `CityRoot.IsOpenWater(landing)` — block baked water
AND the landing cell is `Water`, so a causeway's deck road still wraps — and refuses the wrap as a
splash through the landing block's own zone. Beyond the map rectangle there is no slab and no
trigger, so letting the car continue would be a fall through the void forever.

### Validation

`CityBaker.Validate`, gated on any water block, runs a full **road-graph connectivity walk**:
every block generated against the layout and registered into a `RoadGraph`, flood-filled through
`TryGetNeighbour` (mutual connection handles ramps/decks/curves for free; the graph's no-wrap rule
correctly counts a land mass reachable only across the pacman seam as severed, since police never
wrap). The largest component is the mainland; every block holding a node of any other component
goes red with a "split into N islands" error. Plus the dead-end-piece requirement (shores force
neighbour dead-ends on all four sides) and an all-water error.

Toggling `isWater` moves border roads — **Rebuild + Neighbours**. The map paints
`CellKind.Water` with `CityMapSettings.waterColor`; marker snaps already refuse it (not road).

### Shoreline art

`ShorelineSet` + `ShorelinePlacer`, salt **1212**, run from the builder's water branch under a
Shoreline child. Each land-facing edge (wrapped `BlockSpec.IsWater` of the neighbour — authored
facts, so both seam blocks agree) gets a strip of cliff pieces top-flush at y = 0 facing the
water, an **inner corner** where two land-facing edges meet, an **outer corner** where land
touches only diagonally (the convex cap sits in *this* block, so exactly one block stamps it — no
double, no gap), and the slots under a road cell (the causeway's crossing) left open.

Pieces are scaled by their stored `nativeBounds` to `targetHeight` (5 m, deeper than the water so
the cliff continues under it) and stretched to the slot pitch, **never by the cell fit**. Each
gets a fitted BoxCollider (static, so `IsCellClear` keeps spawns off them) — the car drives over
the ledge and drops off its lip.

**Orientation contract**: at yaw 0 an edge piece is the block's NORTH shore (land +Z, face −Z) and
corners are the NORTH-EAST corner; everything else is that yawed by 90° multiples plus the piece's
`rotationOffset`, tuned by eye after a bake (the Kenney X-mirror).

`Tools → Police Escape → Create Kenney Shoreline Set` builds the set from the NaturePack rock
cliffs (measuring bounds, keeping hand-tuned offsets), creates the transparent URP Lit
`02.Art/02.Materials/InfiniteCity/Water.mat` (**never overwritten** — swap in a Shader Graph
water) and wires both into the definition's generation asset.

**Accepted v1 limitation**: a player hovering over open water empties the world — `CityBounds` is
geometric, patrol-state police outside the allowed blocks get destroyed, and a zero-node water
block can spawn nothing until the player nears land.

## Building sets

`BlockLayoutData` carries the per-cell `cellFlags` array, null-guarded so pre-district prefabs
still load.

`KenneyBuildingSetBuilder` emits three sets: Midrise, Downtown (skyscraper-heavy), Suburb (from
the previously unused `low-detail-building-*` FBXs).

**`CyberpunkBuildingSetBuilder`** (`Tools → Police Escape → Create Cyberpunk Building Set`) builds
three more from the Cyberpunk Megapolis background buildings
(`Assets/Cyberpunk_Megapolis/Models/Background/Buildings`, preferring the pack's prefabs —
BoxCollider + single-LOD group): All / Skyline / Slums.

**That kit is REAL METRES with base pivots** (skyscrapers 24–42 m wide, 45–110 m tall), so its
sets carry `nativeCellSize = cellSize` (scale 1), **`lotSubdivision` 2** (every cell is a 2×2 of
~18 m sub-lots; `footprintInCells` counts sub-lots) and **`lotFill` 0.9 / `maxStretch` 1.75`**.

`CityPopulator` runs on the subdivided `LotGrid` — buildable/road-frontage read off the parent
cell, and **subdivision 1 walks and draws exactly as before so Kenney bakes don't move** — and
scales each placed model per axis so its measured `BuildingDefinition.nativeSize` fills its lot
(height follows the geometric mean by `heightFitShare`). That is what packs four shacks into a
cell and towers wall to wall. Footprints are measured against the sub-lot with a 15% overhang
tolerance. Off-centre pivots are measured into `BuildingDefinition.pivotToCenter` (XZ, native
units), which `CityPopulator.Spawn` subtracts after the yaw so the **bounds, not the pivot**, sit
on the lot centre (0 for Kenney).

`Districts Use Cyberpunk Buildings` / `Districts Use Kenney Buildings` repoint the four built-up
districts' `BlockSettings` (Downtown→Skyline, Midrise/Beachfront→All, Suburb→Slums) — **a rebake
is what makes the swap visible**. `Test Scene Uses Cyberpunk Buildings` only changes the city-wide
fallback set.
