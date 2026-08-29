# City Generation

How the police-chase city is authored, generated and baked, and what changed recently. Companion to the city section of `CLAUDE.md`; this file is the narrative version — read it top to bottom once, then use `CLAUDE.md` as the reference.

Code: `Assets/01.Scripts/PoliceEscape/City/` (runtime, namespace `ConfusedGameDev.FiniteRunner.PoliceEscape.City`), `Assets/01.Scripts/PoliceEscape/Editor/` (baker, designer window, kit builders). Data: `Assets/04.Data/InfiniteCity/`. Output: `Assets/03.Prefabs/PoliceEscape/City.prefab`.

---

## 1. The one-paragraph version

The city is a **fixed grid of blocks** (`gridWidth × gridHeight`, each `blockSizeInCells` cells of `cellSize` metres), generated **offline in the editor** and **baked into a prefab**. Play mode does zero generation and there is no streaming — the prefab is loaded, the road graph is rebuilt from data serialized on each block, and that's it. Roads that touch a block border come from a **city-wide periodic arterial field** seeded by the city seed; everything strictly inside a block comes from that block's own seed. That split is the whole connectivity contract: any block can be rebuilt (with a new seed or not) without moving a single road on any neighbour.

---

## 2. Authoring chain

```
CityDefinition (asset)            grid size, city seed, one BlockEntry per block, districts
  ├─ CityGenerationSettings       CITY-WIDE knobs: cell size, arterial spacing/jitter, feature
  │                               geometry, road piece list, water knobs, shoreline set
  ├─ BlockSettings (per block)    INTERIOR-only knobs: connector density, turns, dead ends,
  │                               feature chances, building/decoration sets + density
  └─ DistrictDefinition           a bundle of flavour: interior BlockSettings, park knobs,
                                  curve knobs, and ONE border-relevant flag (secondary arterials)
        ↓
CityLayout (pure model)           (definition → answers). Resolves a BlockSpec per block and owns
                                  IsRoadCell(worldX, worldY): the single definition of
                                  "is there a city-level road here"
        ↓
RoadNetworkGenerator              CityLayout + block coord → ChunkData (the block's grid model)
  └─ RoadFeaturePlacer            rewrites interior cells into overpasses, forks, roundabouts,
                                  curves, bridges
        ↓
CityBaker (editor)                for every block: GenerateBlock → CityBlockBuilder.BuildBlock →
  └─ CityBlockBuilder             stamp roads / buildings / decoration / nature / water / shore,
                                  write BlockLayoutData onto the CityBlock, save prefab IN PLACE
        ↓
City.prefab
  ├─ CityRoot                     baked copies of grid facts; rebuilds RoadGraph lazily; owns every
  │                               spatial query, the pacman wrap and CityBounds
  ├─ AdditionalItems (socket)     designer content — NEVER touched by the baker
  ├─ DefaultVehicles (socket)     parked cars — NEVER touched by the baker
  └─ Block_x_y × N                CityBlock + BlockLayoutData + Roads/Buildings/Decorations/…
```

**Persistence rule:** a bake only replaces `Block_*` children. The two sockets are created once and never destroyed or reparented — that is the entire mechanism by which hand-placed content survives rebakes.

---

## 3. The connectivity contract

Two facts every block must agree on, both owned by `CityLayout`:

1. **The periodic arterial field.** World rows/columns are split into bands of `arterialSpacing` cells; each band hosts exactly one arterial line whose offset blends (by `arterialJitter`) between band-centre and a hashed random position. The hash uses the **wrapped** world index and the **city** seed (salts 101 rows / 202 columns), so any two blocks — including the pair that meets across the pacman seam — compute identical border sockets without talking to each other.

2. **Suppression.** Some blocks answer "no road" for cells the field says should carry one, city-wide:
   - a **connector-only block** keeps only its bridge line;
   - a **water block** keeps nothing;
   - a **causeway** (water + connector-only) keeps only its bridge line.

   Because `IsRoadCell` is what neighbours consult for cells beyond their own border, their streets dead-end at the right cell with both sides computing the identical mask.

A **secondary arterial field** (salts 121/242, `secondaryArterialSpacing`) is layered on top and only materializes inside blocks whose district enables it. It is still a pure function of (city seed + authored definition, wrapped index), so a secondary street crossing into a sparse district dead-ends with both neighbours agreeing. Bridge lines follow the **primary** field only, so district edits can never move a bridge.

**Consequences for editing:** changing a block's seed or interior settings is always safe. Changing anything that affects `IsRoadCell` — `connectorOnly`, `isWater`, a district override that flips secondary arterials — invalidates the neighbours' baked border sockets: use **Rebuild + Neighbours** (masks only look one cell out, so the four edge-neighbours suffice).

---

## 4. Generating one block (`RoadNetworkGenerator.Generate`)

Block seeds drive interiors; every stream gets its own salt so tuning one thing never reshuffles another.

| Step | What | Seed / salt |
|---|---|---|
| Carve arterials | every cell where `IsRoadCell` is true becomes `Arterial` | city seed (101/202/121/242) |
| *(water block)* | flood every Empty cell to `Water`, resolve masks, **return** | — |
| *(connector block)* | resolve masks, `PlaceBridge` (ramp → deck → ramp along the bridge line); *(causeway)* then flood the rest to `Water`, **return** | — |
| Carve connectors | per lot bounded by the block's effective grid, roll `connectorDensity`; straight span or (by `turnProbability`) an L | block seed, 303 |
| Repair dead ends | strip connector cells with < 2 road neighbours (skipped when `allowDeadEnds`) | — |
| Resolve connections | 4-bit `EdgeMask` per road cell; out-of-block neighbours via `IsRoadCell` | — |
| Curved avenues | `PlaceCurves`: Bézier between two interior arterial straights, rasterized into a staircase whose node centres are pulled onto the curve | block seed, 1013 |
| Features | `Place`: overpasses at arterial crossings, forks on straight side streets, roundabout templates | block seed, 606 |
| Park lots | `MarkParkLots`: claim whole lots with the `ParkLot` flag | block seed, 1110 |

`ChunkData` is two layers: **ground** (`CellKind` + connections, ramps flagged with uphill direction/step, optional centre offset) and **upper** (`upperConnections` for decks), plus orthogonal `CellFlags` (`Curve`, `ParkLot`).

`CellKind`: `Empty` (buildable), `Arterial` / `Connector` (road), `Reserved` (owned by a feature — neither road nor lot), `Water` (open sea — neither road nor lot). `IsRoad` and `IsBuildable` are the two gates every consumer reads, which is what makes new kinds safe by construction.

### Features in a sentence each
- **Overpass** — ramp run → `road-bridge` deck cells over the crossing street → ramp down; the street underneath keeps its perpendicular pair, other under-deck cells become `Reserved` and get a pillar.
- **Fork** — `road-split` is a true Y whose stem sits on the *seam between two cells*: the T is re-stamped on the seam with half-straights refilling the outer halves, a twin branch is carved in the neighbouring row, and both seam rows keep ordinary grid nodes whose centres are pulled onto the seam (`SetCenterOffset`) so every graph edge stays axis-aligned.
- **Roundabout** — a multi-cell template (`footprintInCells` > 1×1 with per-cell `cellMasks`), stamped once at its footprint centre wherever the grid matches in any rotation.
- **Curved avenue** — chain cells stay grid-aligned in the graph (offsets ±0.45 pull nodes onto the curve, `LaneTarget`'s miter join drives it), the visual is a Unity spline through the offset centres chord-stamped with `road-straight` pieces; never a spawn cell.
- **Bridge / connector block** — flat border cell + ramp run + deck + ramp run along the arterial line nearest the block centre on the chosen axis.

---

## 5. Districts (added 2026-08-29)

A block's district is resolved by `CityDefinition.DistrictFor` (salt 909, city seed): authored `districtOverride` → seeded radial map (torus Chebyshev rings around a downtown anchor — ring 0 downtown, inner ring, outer blocks hash-picked from `outerDistricts` weights) → `defaultDistrict`. Interior knobs fall back block override → district settings → definition default → city settings (`BlockKnobs`).

A district carries: `interiorSettings`, park knobs (`isPark` / `parkLotChance` / `natureSet`), curve knobs (`curveChance` / `maxCurvedAvenues`), the `useSecondaryArterials` flag, and a `mapColor` for the designer grid. Assets live in `04.Data/InfiniteCity/Districts/` (Downtown, Midrise, Suburb, Beachfront, Park), built by `Tools → Police Escape → Create District Assets`.

**Parks are model data:** `MarkParkLots` flags whole lots (`ParkLot`); cells stay Empty so the ground slab stays drivable; the populator skips them and `LotNaturePlacer` (salt 1111) fills them from a `NatureSet` with ground/path tiles and interior/perimeter props.

---

## 6. Water & coastline (added 2026-08-29)

The layer that breaks the filled square. **Water blocks are hand-painted** (`BlockEntry.isWater` in the City Designer, next to `Connector Only`); there is no seeded water.

- **Model.** `BlockSpec.IsWater` → `IsRoadCell` returns false for every cell of the block city-wide (inserted before the connector branch, so `isWater && connectorOnly` falls through to the bridge rule = **causeway**). `CellKind.Water = 4` was appended, so old prefabs' byte kinds are untouched. A plain water block floods everything; a causeway keeps its bridge line and its Reserved under-deck cells (pillars standing in the sea).
- **Body** (`CityBlockBuilder.BuildWater`, replaces the ground slab). No collider at y = 0 over the sea, or it is invisibly drivable. Instead: a sea-floor box (top at `seaFloorDepth`), a **splash trigger filling the whole water column** from `waterLevel` down to the floor (no fall speed can tunnel through), the surface as a primitive Quad at `waterLevel` scaled ×1.002 (seamless between sea blocks; the minimap camera sees it for free), and **per-cell mini-slabs** (top at y = 0) under a causeway's two flat border tiles — flat tiles carry no collider and there is no slab here, so without them the car dropped at the causeway's first tile.
- **Splash contract** (`WaterSplashZone.Splash`, one entry point for anything that drives in). Player: `splashDamage` (0.3; a barrel is 0.35) through `LevelManager.ApplyDamage`, then `CarRespawner.Respawn()` onto the nearest road; 1 s cooldown. AI cars **die** through `CarHealth` (fuse → explosion → wreck) and the `PatrolManager` replaces them — the same bait-tactic logic as the explosive barrels.
- **Wraps only land on land.** `CityWrap` asks `CityRoot.IsOpenWater(landing)` (block is water AND the landing cell is `Water`, so a causeway deck still wraps). A water landing is refused and treated as a splash through the landing block's own zone — beyond the map rectangle there is no slab and no trigger, so letting the car continue would be a fall through the void.
- **Validation** (gated on any water block): every block is generated and registered into a `RoadGraph`, flood-filled through `TryGetNeighbour`; the largest component is the mainland and every block holding a node of another component goes red — "Water splits the roads into N islands". The graph never wraps, and that is correct: the player wraps, the police never do. Plus: a single-socket (dead-end) piece is required, and an all-water city is an error.
- **Map / minimap.** `CityMapSettings.waterColor` paints `Water` cells; causeway under-deck cells keep `reservedColor` as the bridge shadow. Marker snaps already refuse water (not road).
- **Shoreline art** (`ShorelineSet` + `ShorelinePlacer`, salt 1212). For each land-facing edge of a water block (wrapped neighbour's `IsWater`, authored facts so both seam blocks agree): a strip of cliff pieces top-flush at y = 0 facing the water; an **inner corner** where two land-facing edges meet; an **outer corner** where land touches only diagonally (that convex cap physically sits in *this* block, so exactly one block stamps it); slots under a road cell (the causeway crossing) are left open. The nature kit is human-scale, so pieces scale by stored `nativeBounds` to `targetHeight` (5 m, deeper than the water) and stretch to the slot pitch — never by the cell fit. Each gets a fitted static BoxCollider, so the car drives over the ledge and drops off its lip, and `IsCellClear` keeps spawns off them. Orientation contract: yaw 0 = the block's north shore (edges) / north-east corner (corners); per-piece `rotationOffset` is tuned by eye after a bake (Kenney FBXs mirror X on import).
- **Tooling.** `Tools → Police Escape → Create Kenney Shoreline Set` builds the set from the NaturePack rock cliffs, creates the transparent URP Lit `02.Art/02.Materials/InfiniteCity/Water.mat` (never overwritten — swap in a Shader Graph water freely) and wires both into the definition's generation asset. Knobs: settings' **Water** group (`waterLevel` −2, `seaFloorDepth` −6, `splashDamage`, `waterMaterial`, `shorelineSet`).

**How to use it:** the silhouette is whatever you paint. A single channel only proves validation; to change the outline, paint water into corners and along edges (an edge water block also turns that edge from a pacman seam into a coast). Coast resolution is one block, so grids of 6×6–8×8 with roughly a third water are where it starts to look organic.

---

## 7. Content passes (bake time, after the model)

All run per block on the block seed, all through `CityBlockBuilder.Instantiate` so pieces stay **prefab instances** (the baker swaps in `PrefabUtility.InstantiatePrefab`).

| Pass | Salt | Notes |
|---|---|---|
| Road stamping | 404 (piece pick) | socket-matched single-cell Standard pieces; ramp chains spread over `rampLengthInCells`; every piece sunk by `RoadSurfaceHeight` so the *lane* lands on y = 0 (the Kenney curb is one lane-height above the lane — the ramp-foot step bug) |
| Buildings (`CityPopulator`) | 505 | Empty, non-ParkLot cells; fitted box colliders |
| Decoration (`CityDecorator`) | 707 | light posts at 3+-socket tile corners, cones/barriers on socket-less edges; kinematic props only the player can push ("heavier wins"); explosive barrels |
| Nature (`LotNaturePlacer`) | 1111 | park lots |
| Water body + shoreline | 1212 | water blocks only |

Connector-only and water blocks skip buildings, decoration and nature.

---

## 8. Runtime (`CityRoot`)

- **`RoadGraph`** rebuilt lazily from every block's `BlockLayoutData` — pure array walks, survives domain reloads. Nodes are `RoadNode {cell, level}` (0 ground incl. ramps, 1 deck); the single neighbour rule is **mutual connection** (target must carry the opposite socket, own level first), which is what links ramp ↔ deck and keeps a deck from leaking into the street beneath. A* with a binary heap.
- **Spatial queries**: nearest road cell (height penalty ×3 so a spot under a bridge resolves to the street), random road cell, straight-runway spawns (flat ground, same-axis straights, physically clear), `IsCellClear` (ignores `DecorationProp` colliders).
- **`CityBounds`**: NPCs may only spawn/roam in the player's block plus edge-neighbours within `npcEdgeEnterDistance`, released at `npcEdgeExitDistance` (hysteresis).
- **`CityWrap`**: only the player teleports at the map edge (arterials are periodic, so the road continues); refused onto open water.
- **`CityManager`** is a thin facade + play-mode boot (spawns managers/HUD/rain) so its ~15 consumers kept their signatures.
- **Map** (`CityMapModel` / `CityMapRenderer`): one texel per cell painted from the baked layouts; routes on the shared graph.

---

## 9. Tooling

| Menu | Does |
|---|---|
| `Tools → Police Escape → City Designer` | the Odin window: clickable block grid (district tint, blue override, orange bridge, deep blue water, sky blue causeway, red validation failure), Selected block (seed / district / settings / connector / water), Rebuild (Keep Seed / New Seed / + Neighbours), Validate, Bake City, Save As New City…, Rebuild City (New Seeds), Clear Redundant Block Overrides |
| `Create Kenney Road Set` | fills the piece list from the Kenney FBXs, measures ramps, flips importer colliders on ramp/deck/pillar |
| `Road Kit Showcase Scene` | every piece with its sockets drawn, for verifying masks / `rotationOffset` |
| `Create Kenney Building Set`, `Create Kenney Decoration Set`, `Create Kenney Nature Sets`, `Create District Assets`, `Create Kenney Shoreline Set` | the content sets and the district bundle |

**Validation** (`CityBaker.Validate`, run before every bake/rebuild): arterial spacing ≤ block size; city cells % spacing (wrap-seam band); large-prefab warning; district checks (dead-end piece, secondary spacing); connector feasibility (pieces, block size ≥ `MinConnectorBlockSize`, an interior arterial line to follow); water checks (dead-end piece, not all water, graph connectivity).

---

## 10. Salt registry

Keep these unique — every stream is keyed on a seed + salt so that tuning one thing never reshuffles another.

| Salt | Stream | Seed |
|---|---|---|
| 101 / 202 | primary arterial rows / columns | city |
| 121 / 242 | secondary arterial rows / columns | city |
| 909 | district map | city |
| 303 | interior layout (connectors) | block |
| 404 | road piece picks | block |
| 505 | buildings | block |
| 606 | road features | block |
| 707 | street decoration | block |
| 1013 | curved avenues | block |
| 1110 | park lots | block |
| 1111 | nature props | block |
| 1212 | shoreline pieces | block |

---

## 11. Recent updates

**2026-08-29 — Water & coastline** (uncommitted at time of writing, branch `feat/cityImproveAgain`)
Hand-painted water blocks and causeways; `CellKind.Water`; water body with sea floor / full-column splash trigger / surface quad / causeway mini-slabs; `WaterSplashZone` (player damaged + respawned, AI killed); wrap refused onto open water; road-graph connectivity validation with red island tint; map water colour; `ShorelineSet` / `ShorelinePlacer` cliffs with corner handling; `Create Kenney Shoreline Set` tool + `Water.mat`. Settings gained a **Water** group.

**2026-08-29 — District variety** (`feat(city) added city district variety`)
`DistrictDefinition` assets and the seeded radial district map; two-tier arterial field (secondary band, district-gated, border-safe); park lots as model data + `NatureSet` / `LotNaturePlacer`; curved avenues (spline-fitted, chord-stamped, graph-safe); three building sets (Midrise / Downtown / Suburb); designer grid district tint + legend; `Clear Redundant Block Overrides`.

**2026-08-28 — Lane direction** (`wip added lane direction`)
Right-hand lane discipline for the AI drivers (`LaneRules.LaneTarget`, miter join at corners); `CenterLineOnly` nodes for fork seams and roundabout footprints.

**2026-08-25/26 — Fixed baked city** (`wip full city builder`, `dev - created test city`)
The pivot from streaming chunks to the offline-baked fixed grid: `CityDefinition` / `CityLayout` / `CityBaker` / `CityBlock` + `BlockLayoutData`; the `CityDesignerWindow`; connector-only bridge blocks; periodic arterial field and the pacman wrap; `AdditionalItems` / `DefaultVehicles` sockets; `CityManager` reduced to a facade over `CityRoot`.

**2026-08-23/24** — explosive barrels and the NPC health model (`CarHealth`, shared `Blast`) that the water splash now reuses for AI kills.

---

## 12. Known limitations / next candidates

- **Hovering over open water empties the world**: `CityBounds` is geometric, police outside the allowed blocks are destroyed, and a zero-node water block can spawn nothing until the player nears land.
- **Coast resolution is one block** — an organic silhouette needs a bigger grid (6×6–8×8) and more water than a 4×4 can express.
- **Water is manual** — a seeded coastline pass (city seed + coverage knob, hand-touched afterwards) is the obvious next step if painting proves tedious.
- **Shoreline v1 is rock cliffs everywhere**; per-district beach flavour is a later polish pass. Piece orientation (`rotationOffset`) needs a visual pass after the first bake.
- **AI routes through a roundabout's centre cell** (the island is flat, no collider).
- A player respawning mid-channel may land on the causeway deck (the ×3 height penalty makes that the nearest road) — acceptable.
