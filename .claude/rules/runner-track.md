---
description: Runner track — TrackManager, sections, endless generation, features, pads and orbs, decorator
paths:
  - "Assets/01.Scripts/Runner/Track/**"
  - "Assets/01.Scripts/Runner/**/Track*"
  - "**/TrackManager.cs"
  - "**/TrackGenerator.cs"
  - "**/TrackDecorator.cs"
  - "**/SpeedPad.cs"
  - "**/PadDefinition.cs"
---

# Runner track

The runner is spline-based, not physics-based: the ship is moved kinematically along a
`SplineContainer` (Unity Splines) and pads detect it via trigger colliders against its
kinematic rigidbody.

## `TrackManager` — owns the spline

**Distance from the track start is the authoritative coordinate.** The spline grows during the
run, which shifts what any normalized `t` means, so consumers map distance → t through
`DistanceToT()` (cached arc-length tables) every frame rather than storing t. `ShipMotor` stores
no t at all — `ApplyPose` looks the pose up from distance every frame.

It is the **only object that touches the `SplineContainer`**. The generator grows the track
through `AppendKnot` / `ClearKnots` — the single seam a future multi-spline route layer would
replace. No consumer reads the spline directly.

### Sections (`Track/TrackSection.cs`)

Stretches of track distance laid over the flat spline with their own pose function.

- **Inserting sections** (`LoopSection`) add distance: track distance = spline distance + the
  lengths of every section before it. `Length` is the whole track including inserts,
  `SplineLength` the spline alone, and `DistanceToT` takes a **spline** distance.
  `LoopSection` is a vertical circle standing on the entry pose, parameterised by arc length,
  inverted at the top, lateral along the entry's right.
- **Overlay sections** (`TubeSection`, `InsertsDistance` false) reshape the spline's own pose
  over their length through `GetSplinePoseAtDistance` and add nothing to `Length`.
- `GetPoseAtDistance` routes a distance inside a section to it; `SectionAt` /
  `SplineDistanceOf` answer the rest.
- `GetLateralBand(distance)` is the steering lane at a distance — ±`HalfWidth` on the road, a
  section's band inside one. The motor's clamp, the pad placer's range and the decorator's
  strips all ask it.
- **`AddSection` must happen before anything is placed beyond its start.** The generator
  registers a loop's section the moment it decides the spot (`pendingSection`), before that
  stream's pads and road. `ClearKnots` drops sections too.

## `TrackGenerator` (+ `Editor/TrackGeneratorEditor`)

Procedural builder **and** endless streamer. With `endless` on (default) it builds an initial
stretch in `Awake`, then each `Update` keeps `aheadDistance` of finished track ahead of the ship
(appending knots, placing pads, decorating) and culls spawned objects more than
`behindDistance` behind.

**Invariants:**

- **Knots are never removed**, so distances stay valid all run — only spawned objects are culled.
- Nothing is placed on the trailing `SettleMargin` (two segments): AutoSmooth reshapes those
  curves when the next knot lands.
- `RegenerateForRun()` fully rebuilds — endless restarts must, since the stretch behind the
  start was culled.
- `seed == 0` means non-repeatable.

### Core Settings (Odin region)

- `trackWidth` — pushed into `TrackManager.SetWidth` and `TrackDecorator.SetTrackWidth` on every
  `Generate`. One width knob drives the steering clamp, pad bounds and road meshes, which are
  authored for 60 m and stretch proportionally.
- `straightness` — 100% = dead straight; scales the Shape section's turn/heading limits down.
- `spawnTable` — one `PadSpawnEntry` per pad/orb kind (optional prefab, `PadDefinition`, boost
  multiplier × `GameManager.powerUpSpeedBoost`, colour/sway), drawn once per spacing step by
  probability. Probability sliders auto-rebalance (`NormalizeProbabilities`) so the table always
  sums to 100%. Entries without a prefab fall back to the code-built primitive with a recoloured
  boost-material instance; prefab entries keep their own materials and get their colliders forced
  to triggers. A moving orb's spawn lateral is clamped so its sway arc stays inside the track.

The custom inspector is an `OdinEditor` (so Odin attributes render) and adds the
"Regenerate Track" preview button.

### Track features (`Track/Features/`)

A second seeded table, `featureTable` of `FeatureSpawnEntry` (name, optional unit prefab, a
`TrackFeatureDefinition` asset, probability with the same rebalancing rule via `IWeightedEntry`,
`minSpacing`, boost `multiplier`, colour). The roadmap for these lives in `TrackFeaturesPlan.md`:
jumps (1), loops (2), cylinder sections (3) are built; multi-path is the one left.

`PlaceFeaturesUpTo` runs before the pads at every stream, draws an entry per `featureSpacing`
step, **claims its footprint the moment its spot is decided** (even while it waits for the settle
margin — `pendingFeature`), creates it once settled, and advances the cursor by footprint +
`ExclusionAhead` (a jump's longest arc, so nothing waits under a landing) + max(spacing roll,
`minSpacing`). `PlacePadsUpTo` skips claimed ground.

Every entry gets a **runtime clone** of its definition (`Runtime`) in play — the debug menu edits
the clone, never the asset.

- **`CreateJump`** spawns a `JumpRamp` (start, length, lateral, half width =
  `HalfWidth × widthFraction`, boost = `powerUpSpeedBoost × multiplier`) with a picture only: the
  entry's unit prefab scaled to (width, lip, length), or a code-built slab pitched to `rampAngle`
  with a rail per edge. Colliders stripped, `featureMaterial` tinted per entry.
- **A feature with an `InsertLength`** (`LoopDefinition`: `Circumference`) has zero
  `SplineExtent`, so it never waits for the settle margin: `PlaceFeaturesUpTo` calls
  `CreateSection` and `track.AddSection` at decision time (bumping its own `limit` by the insert;
  `StreamTo` re-reads `settled` afterwards). `CreateLoop` spawns the `LoopFeature` (section +
  `LoopRequiredSpeed(distance)`) with a portal-frame gate — two posts + crossbar, or the entry's
  unit prefab scaled by the radius — whose renderers take the gate colour. The ring's road comes
  from the decorator.
- **A tube** (`TubeDefinition`, `ClaimsFootprint` false) is nothing but its section:
  `CreateSection(track, start, roll01)` rolls the length off the layout rng, `CreateFeature`
  builds nothing. **A feature with a section never waits for the settle margin** — only a
  road-bound ramp does; a 4.5 km tube could never fit inside the settled stretch, and its pose is
  only sampled where pads and road are placed. Pads keep spawning across it (`PadMargin` inside
  `GetLateralBand`) and the decorator stamps the pipe.

Loop knobs (`radius`, `exitClearance`, `fallGravity`, `fallSpeedLoss`, gate colours) live on
`Loop_Definition.asset`. The debug Features tab edits radius / gravity / loss through the generic
`AddStat<T>`, and per-tube radius / band / curl through `FeatureDebugSettings.tubes`, matched by
entry name.

### Collectibles streaming

The "Collectibles" toggle group (`spawnCollectibles`, optional `collectiblePrefab`,
`collectibleSpacing` between rows, `collectibleGroupSize` coins per row a `collectibleStep`
apart at one lateral, `collectibleValue`, `collectibleTriggerSize`, coin size/colour).
`PlaceCollectiblesUpTo` runs after the pads in every stream, skips claimed ground and any
distance within a pad length of a pad (`padDistances`, pruned with the cull).

`collectibleTriggerSize` is **20 m long** because at Light Speed the ship covers ~36 m per
physics step against a 12 m trigger box.

## Feature geometry

### Loops

Mandatory vertical loops the whole track width wide, inserted into the track's **distance** — the
spline stays flat — so pads, patrol, road stamps and streaming ride them unchanged and the
decorator draws the ring's road chord by chord for free.

The entry speed a loop demands is **fixed when it is placed**: `GameSettings` floor 1200 km/h +
18 km/h per 100 m travelled, capped at 2900 (`GameManager.LoopRequiredSpeed`). So the
portal-frame gate at the mouth (green/red against the ship's speed every frame) and the required
km/h never lie. The number is **fixed above the gate as a world-space label**
(`LoopFeature.BuildLabel`, `labelHeight` / `labelSize` on the definition), shown only inside
`labelLeadMeters` (1800 m — 300 m beyond the fog end, so the number leads the gate out of the
haze) and tinted with the gate. It is never a popup riding ahead of the ship.

Verdict is taken once at the gate: fast enough and the loop is a pass whatever happens inside;
too slow and the ship rides to the top and drops off it. No orbs inside a loop — footprint
claimed.

### Cylinder sections (tubes)

Stretches where the road curls into a pipe the ship runs round the **outside** of, its top being
the flight line.

- Lateral becomes arc round the pipe (angle = lateral / radius), so a smaller pipe spins faster.
- The steering lane is the section's band ±`bandDegrees` around `centreDegrees`.
  `FullTube_Definition` is ±180° = **unbounded**: once fully curled there is no clamp and no
  wall, the ship goes round and round with the lateral growing a circumference per turn
  (`TubeSection.Unbounded` / `IsUnboundedAt`). A ±90° variant is one asset away — the definition
  is generic.
- `steeringFactor` is 3× the road's lateral speed on the pipe, since a turn is three track widths
  of arc. **The band edge is the road edge**: a dash into it is a wall hit, plain steering
  saturates silently.
- A `curlLength` at each end eases position, up vector and band from flat to pipe and back.
- Length is rolled per instance from `lengthRange` (3–7 km — long enough for one or two full
  turns at speed).
- **The end of a tube is the system's**: over `returnLength` before the curl-out
  (`TubeSection.ReturnProgress`) the motor eases the lateral home to the band's centre with
  steering and dash locked, so the road never unrolls under a ship hanging off its side. The
  return unwinds to the NEAREST top, then snaps that to 0 as the curl-out begins — same pose, no
  jolt.
- Orbs and brake pads spawn anywhere in the band (a tube claims no footprint) — a purple orb
  under the pipe is the reason to go under. Ramps and loops never start inside one.
- The road is the ordinary road prefab stamped in strips round the band
  (`TrackDecorator.StampTube`, `tubeStripWidth`), with **no barriers on any tube** — the band
  clamp is the fence.
- `ShipState.OnTube` marks it for readers; the pose function and `TrackManager.GetLateralBand`
  do all the work.

## `SpeedPad` + `PadDefinition`

Pads call `ShipMotor.AddSpeedImpulse(speedDelta)` on trigger enter — positive boost, negative
brake — and the effect is divided by the ship's `weight`.

- `sizeMultiplier` scales the spawned pad. Speed-ups are small **hovering orbs (0.3)** on the
  flight line that must be aimed for; speed-downs are large **1.2 pads** that must be dodged.
- `floatingOrb` makes it a hovering sphere on the flight line, with an `OrbHover` bob/spin/sway
  component added at runtime. `OrbHover` bobs and sways along the **track's** up/right captured
  at spawn, not world axes, so orbs survive loops and tubes.
- **Tiered boost orbs override the definition's delta and colour per instance** via
  `SetDefinition(def, speedDelta, tint)` — the shared `PadDefinition` asset is never mutated.
  Three rarity tiers: green 1×, blue 2.5×, purple 10× of `GameManager.powerUpSpeedBoost`; the
  higher the multiplier the scarcer the orb and the more it sways. Tier weights/colours/sway live
  in `TrackGenerator.orbTiers`.
- `PadSpawnEntry.lane` (Ground / Air) is the prepared **air lane**: Air entries spawn
  `GameSettings.airLaneHeight` above the flight line along the track's up, reachable only off a
  jump. No table carries one yet.

## `TrackDecorator`

Stamps road-kit meshes (road surface, side barriers) along the spline, streaming-style:
`DecorateUpTo(distance)` advances an internal stamp cursor, `CullBefore(distance)` drops pieces
behind the ship. There is no goal gantry — there is no end goal.

MPB tints are unreliable with the SRP Batcher — hence the material-override fields.
