---
description: Runner track — TrackManager, sections, endless generation, features, spawnables (orbs, repair orbs, lasers), decorator
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

The runner is spline-based, not rigidbody-based: the ship is a track-space `TrackBody` posed
along a `SplineContainer` (Unity Splines), and pads, orbs and coins are found by an analytic
sweep of the `PickupRegistry` (the patrol) or the physics ship's collider sweep (the player).

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

- A section covers `SplineExtent` metres of spline and `Length` metres of track; the difference,
  `InsertedLength`, is track distance the spline lacks (`InsertsDistance` = it is > 0). Track
  distance = spline distance + the inserted lengths of every section before it. `Length` is the
  whole track including inserts, `SplineLength` the spline alone, and `DistanceToT` takes a
  **spline** distance.
- **`LoopSection`** is a helix standing on the entry pose (radius, `Turns`, `LateralDrift`,
  `ForwardCarry`, `ExitYawDegrees` — all eased with a smoothstep so both mouths are tangent to
  the road), parameterised by arc length through a 256-sample table (`UAt`), inverted at each
  top, lateral along the (yawing) right. `FirstTopLocal` is where a failed loop lets go,
  `GetExitPose` / `ExitForward` where the track continues. Its `SplineExtent` is the **bridge**
  the generator lays between the entry and exit knots — spline the ship never rides.
- **Overlay sections** (`TubeSection`, `SplineExtent == Length`) reshape the spline's own pose
  over their length through `GetSplinePoseAtDistance` and add nothing to `Length`.
- `GetPoseAtDistance` routes a distance inside a section to it; `SectionAt` /
  `SplineDistanceOf` answer the rest.
- `GetLateralBand(distance)` is the steering lane at a distance — ±`HalfWidth` on the road, a
  section's band inside one. Pads, orbs, lasers, collectibles, ramp placement and the patrol
  all ask it: it is where the game is AUTHORED.
- `GetRoadBand(distance)` is the lane widened by a **banked shoulder** on each side
  (`ShoulderWidth` = `HalfWidth` × `shoulderFraction`, rising `ShoulderRise` metres to its outer
  lip) — all the road there is, and where the ship may physically BE. The collision surface, the
  walls, the barrier art and the drop off an open edge all end on that outer lip, so the bank is
  solid run-off and not a lie the art tells. `HasShoulders(distance)` is false inside a loop or a
  tube (`TrackSection.HasShoulders`), whose own band already IS the road, and false at
  `shoulderFraction` 0 — which puts everything back on the lane edge. **The two numbers describe
  the road slab art**: set them to the profile of whatever piece the decorator stamps, or the wall
  ends up in mid air.
- **Body queries** (what `TrackBody` asks): `GetFrameAtDistance` (position + forward / up /
  right), `GetCurvatureAtDistance` (signed 1/m, **positive to the right**, the forward's change
  over ±5 m projected on the track's right — a loop's pitch and the grade read as 0),
  `GetBankAtDistance` (degrees, **right edge up positive** — the generator's own convention, so
  a right-hand sweep banks negative), `FlatSweepAt(distance)` and `IsEdgeOpen(distance, side)`.
- **Flat sweeps are the one dangerous kind of road** (`TrackManager.FlatSweep`: Start, End,
  OuterSide). One object answers both the physics (grip is tested only inside one) and the
  decorator (`IsEdgeOpen` = the OUTER side of a flat sweep, nowhere else, never inside a
  section), so a missing wall is always a real drop. `TrackShapeSettings.unbankedSweepChance`
  (0.3; a CORE SETTINGS debug row) is rolled once per sweep in `StartTurn` — no draw at 0,
  so that reproduces the all-banked layout. A flat sweep must stand on level road: rolled while
  the last bank is still unwinding it is DEFERRED (`deferredFlatKnots`, started by `AddSegment`
  at the first level knot, dropped if the road ahead gets claimed) rather than lost. It has no
  bank to unwind, so `TurnFits(shape, flat: true)` lets it into gaps a banked sweep cannot fit —
  in such a gap a sweep only happens if the flat roll says so. A flat sweep gets bank target 0. Its span opens at the knot BEFORE the first turned knot (AutoSmooth
  bends the segment before a heading change too; that knot is still inside the unstamped settle
  margin) and `GrowFlatSweep` pushes `End` to each new knot after `Recalculate` — always ahead
  of the decorator. `ClearKnots` drops the spans.
- **Open straights** (`TrackManager.OpenStretch`, `TrackShapeSettings.openStraightChance` 0.5):
  a straight run may lose BOTH walls — `IsEdgeOpen` answers yes for either side, grip is not
  involved. A segment only counts as plain straight when the knots at both its ends are (no
  heading change, bank 0, no landing zone, not the feature spot, `LevelRequired` false, no
  section), which is only known while the NEXT knot is being laid — so `AddSegment` opens the
  segment behind the spline's end (still inside the unstamped settle margin) and grows one
  stretch per run. One roll per straight run, no draw at chance 0. The decorator drops the
  full-width barrier piece entirely where both sides are open and marks both edges.
- **`AddSection` must happen before anything is placed beyond its start.** The generator
  registers a section the moment it decides the spot (`DecideFeature`, at the knot that landed
  on it), before that stream's pads and road. `ClearKnots` drops sections too.
- Knots: `AppendKnot(position)` / `AppendKnot(position, rotation)` are AutoSmooth (reshaped by
  the neighbours that land after them — the reason for the settle margin);
  `AppendKnot(position, rotation, tangent)` is an explicit `Continuous` knot (in = −tangent,
  out = tangent, chord/3) that nothing reshapes — feature entries and loop exits use it. The
  tangent is given in WORLD space and converted: **a `BezierKnot` stores tangents in the knot's
  local frame**, and a world tangent handed over as-is is rotated twice (a kink at every feature
  knot, ramps facing the doubled heading — the M3 bug).
- **Knots live in the spline container's space, poses are world.** The builder walks from its own
  origin (`endPosition = 0`) and never notices the container's transform — but anything it reads
  BACK off a pose query is world and must go through `TrackManager.WorldToKnotSpace` before it
  becomes a knot. The loop's exit knot did not: the `Track` object sits at (9.75, 8.7, −18.06) in
  the test scene, so the whole track after EVERY loop was shifted by those 22.3 m — a one-frame
  pop nobody saw in track space at 1000 m/s, and the road missing under a physical ship (found in
  the M7 physics runner). `ContinueFromLoopExit` converts now.

### Track end (`HasEnd`, `EndDistance`, `EndZoneStart`)

A finite track's two marks, both TRACK distances read off the built spline (never the authored
target), set by the generator (`SetEndZone` at the knot that opens the final run-up, `SetEnd`
right after the last knot) and dropped by `ClearKnots`; −1 = not there yet. Past `EndDistance`
`GetPoseAtDistance` runs on along the last knot's line instead of freezing on it (a body
overshoots the end by up to a step). The band and `IsEdgeOpen` say nothing special there — a body
leaves at the end by `TrackBody`'s own rule (`runner-ship.md`), not by an open edge.

## `TrackGenerator` (+ `Editor/TrackGeneratorEditor`)

Procedural builder **and** streamer, in two halves that never mix (TrackAuthoring M1):

- **Deciding** lays the road (knots, sweeps, banks, features, the end) and decides everything that
  goes on it as `TrackPlacement` records (`Track/Layout/TrackPlacement.cs`: kind, distance,
  lateral, height, `variant`, `data`). **Every random draw happens here.** A finite play track is
  decided WHOLE in `Generate` (`Decide(+∞)`: ~200 knots and ~700 records, cheap), so the run knows
  its full shape and length from the first frame (`Placements` exposes the records); an endless
  preview decides as it streams (`Decide(focus + aheadDistance)`).
- **Building** (`BuildUpTo(edge)`) turns records into GameObjects only inside the stream window —
  the ship to `aheadDistance` ahead (16 km in the test scene) — in distance order, and draws
  nothing, so the same records always build the same objects. `SettledDistance` is how far it has
  BUILT; the colliders and the decoration follow it. `Build` routes generator kinds (Ramp, Loop,
  EndRamp, Collectible) to the generator's builders and every other kind to the spawner whose
  `Kind` it is. Objects are culled `behindDistance` behind.
- `Generate` builds only the first stretch FROM THE START (`BuildUpTo(aheadDistance)`), never from
  the focus: a restart regenerates before the ship relaunches, so the focus still holds last
  run's distance.
- **Verified equal**: seed 424242 decided-then-built produces exactly M0's streamed track (every
  knot, every object, the end).
- **Debug edits to spawn spacing / tier chances apply on the next Generate**, not to the road
  ahead mid-run (it is already decided). What is read at BUILD time (a tier's multiplier, colour)
  still applies live.

**In play the track is FINITE** (below); an edit-mode preview and a scene with no `GameManager`
stay endless.

### The finite track (`IsFinite`, `EndDistance`, `TargetLength`)

- **The track knows neither the ship nor the game flow** (refactor Steps 5–6). Its collaborators are
  contracts from the `Contracts` assembly: `IStreamFocus` (distance + speed it streams ahead of —
  the ship's motor implements it), `IShipPerformance` (cruise, bleed, jump strength for loop
  reachability and ramp arcs) and `ITrackRunRules` (length, run-up, ramp gaps, base boost, air
  lane, hull on/off, the loop gate's speed — `GameManager` implements it). A composition root hands
  them in with `TrackGenerator.Bind`; otherwise `Awake` discovers them by interface
  (`ContractLookup.Find<T>`), and with none present the generator runs on its own defaults (an
  endless preview).
- `Generate` PULLS the run's length: `ITrackRunRules.TrackLengthMeters` (the level's own, else
  `GameSettings.trackLengthMeters`) — in play, or when baking in the editor (a saved track carries
  its own). `endZoneTarget = length − EndRunUpMeters`.
- **The last knot cannot be placed at an authored distance** (a chord is only arc length where
  the knots are collinear, and loops insert distance), so the END ZONE START is a pinned
  pseudo-spot — `AddSegment` returns a `SpotKind` (`None / Feature / EndZoneStart / End`) and
  lands on it with the same explicit-tangent knot a feature gets — and every knot after it is
  collinear (`inEndZone` → `holdStraight`: no sweep, no bank, the grade held, never an open
  stretch, so both walls are up). Inside the zone a chord IS arc length, so `BeginEndZone` measures
  `endTarget = builtStart + runUp` and the road stops exactly one run-up on. The authoritative
  end is `track.EndDistance = track.Length` after the final knot; the authored length is a target.
- Too far for one segment and too near for two, the rest is split evenly instead of pushing the
  spot out; an end spot may land on a half-minimum segment (collinear, so harmless).
- `NextLevelSpot` = min(`featureCursor`, `endZoneTarget`) drives `LevelRequired` and `TurnFits`:
  the bank is unwound and no sweep starts that cannot finish before the zone.
- **Nothing reaches into the zone**: `DecideFeature` skips a spot whose footprint + exclusion
  (a loop, a 3–7 km tube, the longest landing off a ramp) would pass `endZoneTarget` — before the
  claim, `straightUntil` or `AddSection`, so nothing was registered — a feature cursor that the
  zone would swallow is parked, and one claim over the whole zone keeps pads and coins off.
- `FinishTrack` sets the end, flags `trackComplete` (the `Decide` loop never appends again and the
  decided distance becomes `track.Length` — no trailing margin after the last knot) and calls
  `CreateEndRamps`.
- **`CreateEndRamps`** records (`EndRamp`, `variant` = slot 0/1/2) and `BuildEndRamp` builds three `JumpRamp`s (`IsEndRamp`) side by side, lips ON the end of the
  road, equal widths `(trackWidth − 2·gap − 2·sideGap) / 3` at laterals 0 and ±(width + gap)
  (`GameSettings.endRampGapMeters` / `endRampSideGapMeters`), boost 0, the outer two reaching
  0.5 m past a flush wall; a `TrackDecorator.StampEndMarker` strip marks each gap as a drop. They
  are keyed on their END for the cull. Their definition is `endRamp.definition`, else
  `Resources/FiniteRunner_EndRamp` (a `JumpDefinition`: `entryMargin` 0, length 120, 15°, side hit
  0.05 — width fraction and arc knobs unused), else built-in numbers; cloned in play.
  `BuildRamp` is the one ramp builder, shared with `BuildJump`; the middle slot stamps the gap markers.
- Debug: CORE SETTINGS → TRACK LENGTH, written by `GameManager.SetTrackLengthFromDebug` to the
  asset the run resolves it from (the level's own length when it sets one, else `GameSettings`).

**Invariants:**

- **Knots are never removed behind the built road**, so distances stay valid all run — only spawned
  objects are culled. The one removal is `TrackManager.TruncateKnots`, the hyperspace cut-back,
  and only past the built road (below).
- **A spawned object that spans track is keyed on its END for the cull**, not its spawn point:
  a loop is 2πR of track (630 m at R = 100) against a `behindDistance` of 150 in the test scene,
  so keyed on its mouth the `LoopFeature` was destroyed with the ship still climbing it, and the
  motor dropped to Grounded mid-loop. `ShipMotor` also keeps its own `loopSection` /
  `loopDefinition` from the gate, so the state never depends on the feature object surviving.
- Nothing is placed on the trailing `SettleMargin` (two segments): AutoSmooth reshapes those
  curves when the next knot lands. (A finished finite track has no margin: nothing lands after
  its last knot.)
- `RegenerateForRun()` fully rebuilds — endless restarts must, since the stretch behind the
  start was culled.
- `seed == 0` means non-repeatable (`LastSeed` exposes the rolled seed; the inspector shows it).
- **A non-zero seed gives the same track however it streams** (one `Decide` or one per frame —
  checked by diffing knot + object dumps). Keep it that way:
  - The layout `rng` is drawn ONLY in road order (`AddSegment`, `StartTurn`, `DecideFeature`,
    `CreateSection`). Anything placed later draws from its own stream hashed off the layout state,
    like the spawners: coins use `collectibleRng`, ramp laterals `rampRng`.
  - `SpawnPendingRamps` builds in ROAD order (it used to walk the list backwards, so a batch of
    waiting ramps drew their laterals reversed).
  - Placement is staged: claimers run to `settled`, each pickup spawner `StageGuard` (a pad length +
    1) short of the stage before, coins another guard short and only whole rows. Whatever a
    placement keeps off (`ClaimEnd`'s widening, `NearPickup`) is then decided before it. The guard
    is 0 once `trackComplete`.
- `TrackManager.Revision` bumps on `ClearKnots`; caches along the track (`TrackGuide`'s curvature)
  key on it, never on `Length`.
- **Regenerate Track** (inspector) in play is `GameManager.Restart` (new layout at seed 0 — or the
  level's saved track again — ship and patrol relaunched). Edit mode has the authoring tools below.

### Saved tracks (`TrackLayout`, `TrackLayoutAsset` — TrackAuthoring M2)

- **`TrackLayout`** (`Track/Layout/`) is a whole decided track as data: the knots **as they were
  handed in** (`TrackManager.AppendedKnots` — before AutoSmooth re-derived their tangents and re-leaned
  their rotation; saving the FINAL knots and re-adding them leaned three knots a second time, a
  fraction of a degree of bank off), every knot's track distance, the sections as replayable records
  (feature index, start, the layout rng state their definition rolled from — `CreateSection` replays
  the same roll on the same road — and a loop's spline extent), flat sweeps, open straights, the
  keep-outs (the patrol duel's `IsGroundClear`), the end, the run-up and every `TrackPlacement`.
- **`TrackLayoutAsset`** wraps one, plus the shape and spawn set it was generated with (a ramp record
  indexes that feature table, an orb record those tiers — a saved track BUILDS with them, whatever
  the scene's generator holds) and `timeLimitSeconds` (0 = `GameSettings`; `GameManager.TimeLimit`
  resolves it for the HUD, retries and the run stats).
- **A level plays one** through `RunnerLevelDefinition.track` → `GameManager.AuthoredTrack` →
  `ITrackRunRules.AuthoredTrack` (typed `ScriptableObject`: the contract names no track type).
  `Generate` then `LoadLayout`s it instead of deciding: knots re-appended in order, sections
  replayed, spans / end / keep-outs / records restored, spawners `Park()`ed and the coin and feature
  cursors parked — nothing more is decided. Gameplay never writes the asset: the generator copies
  its records, and the hyperspace cut-back works on that copy (`CutBackForEnd` reads the knots).
  **Verified**: a baked track loads bit for bit (every knot's position, rotation and tangent, every
  record, the end); hyperspace cuts it back; a restart reloads it.
- **FORCE RUNTIME TRACK** (debug CORE SETTINGS, a 0/1 slider): `GameManager.ForceRuntimeTrack`, a
  runtime switch — the level's saved track is ignored from the next restart. No asset is written.
- **Edit mode = play.** `GenerateForBake(rules)` (the inspector's Generate Track) decides a whole
  finite track in edit mode with the scene's `GameManager` as the rules — length, run-up, end ramps,
  hull — and laser gates are DECIDED in edit mode too (built in play only). The ramp landing room
  is sized for a FIXED jump strength (`StoreUpgrades.MaxMultiplier`, the strongest upgrade — never
  the player's own), so one seed is one road for every player. **Verified**: an editor bake and a
  play run generated on the same seed are identical.
- **Inspector (edit mode)**: Generate Track · Save Track As… (`04.Data/FiniteRunner/Tracks/`;
  replacing a saved track asks first) · Set as Current Track / Clear Current Track (writes the scene
  `GameManager`'s level) · Preview Saved Track (`PreviewSavedTrack`: loads it as play would) ·
  Regenerate (endless preview).
- **Scene-view drawing** (`Runner/Editor/TrackGizmos.cs`, M3): a `[DrawGizmo]` for the generator,
  read off the live `TrackManager` and `Placements` — a bake, a Preview Saved Track or a run in play,
  never GameObjects. The whole centre line coarsely; round the Scene camera (`DetailRadius`) the lane
  edges, the road's lip and walls — RED open edge, ORANGE flat sweep, CYAN loop/tube, MAGENTA final
  run-up — a tick across the road every 500 m with a km label, section / run-up / end lines, and a
  mark per placement in its spawner's colour (orb tiers their own; ramps as footprints, gates as
  their beam, rotors with their swept disc). Sampled every 20 m once per track change (cached on the
  track's `Revision`, `Length`, end and record count — ~40 ms for 70 km); a repaint only culls and
  draws. Per-user switches (`TrackGizmoSettings`, EditorPrefs) in the generator's inspector: draw,
  detail radius, placements, labels.
- **Road editing** (`Runner/Editor/TrackGeneratorEditor.Editing.cs`, M4): Edit Track on the
  inspector's track asset. Knots: click to select; the position handle moves one (up/down = grade),
  a disc banks it; the inspector has numbers, insert-after, delete, previous/next/frame, and adds or
  removes open straights and flat sweeps on the selected knot's segment. Span ends drag along the
  road (projected back to a distance). Edits go into the ASSET with Undo; `TrackGenerator.ApplyEdit`
  reloads it and `RederiveAfterEdit` rewrites the knot distances (`TrackManager.KnotTrackDistance`),
  the length and end, the run-up start (same KNOT as before), the end ramps (one ramp before the end)
  and the run-up keep-out; every other placement KEEPS its distance and slides with the road.
  **Live drag** (`ApplyEditLive`): the road reloads (`LoadRoad`, no clearing) and only the stretch the
  edit reshapes is rebuilt — two knots either side of a moved knot (AutoSmooth), or between a span
  end's old and new place: the objects built from records there (`placedAt` remembers which record
  built each object) and the road art (`TrackDecorator.Restamp`). Road past that is physically
  unchanged, so what stands on it stays right; release does the full rebuild and saves the asset.
  ~10 ms a drag frame against ~60 ms for the full rebuild; nothing vanishes or doubles.
  **Saving is explicit**: Edit Track saves anything pending and snapshots the layout (JSON); edits
  only mark the asset dirty; the panel shows Unsaved changes with Save / Revert; Stop Editing with
  unsaved changes asks Save / Discard (back to the snapshot, undoable) / Keep Editing. Every change
  is `Undo.RecordObject`-ed, the re-derived fields included (they are written in the same group), and
  the undo hook lives as long as the inspector, redrawing the track on show even after editing
  stops. Generate / Preview are disabled while editing; Save Track As… also copies a saved or edited
  track on show.
  - A knot's input rotation carries its bank (`WithBank`: look down the chord through its
    neighbours, roll by the bank — the builder's own frame); moving a knot re-aims its neighbours'
    frames, each keeping its bank. The road re-leans knots to the curve, so a sharply bent knot reads
    a few degrees off the bank asked for — the inspector shows the road's own bank beside it.
  - A feature's knot (Continuous, explicit tangent) keeps bank 0, its tangent turns with it, and it
    cannot be deleted. **Locked**: the first knot, the final run-up (D9), and every knot on a track
    with loops or tubes (moving road before a section would shift it).
  - **Verified** on a copy of a saved track: a knot moved 200 m and banked 20° → length, run-up and
    end ramps followed, knot distances stayed aligned, no other placement moved; play loaded the
    edited road exactly and the collision road follows it.
- **Placement editing** (`Runner/Editor/TrackGeneratorEditor.Placements.cs`, M5 — Edit Track's
  Placements tab). Every change is a record in the asset, undoable and saved with the edit.
  - Click a placement's dot to select it; drag it along and across the road (projected back to
    distance + lateral, clamped to the lane; the window round its old and new spot rebuilds live). The
    inspector edits distance / lateral / height (prefabs) / variant (orb tier, gate variant + beam,
    rotor, wave, catalog power-up, coin value), duplicates (+30 m), deletes, and lists the 25 nearest
    the Scene camera. End ramps and loops are not moved here.
  - **Palette**: the catalog's power-ups, the four laser gate variants, every ramp entry of the
    feature table, a coin, any prefab — pick one, press Click in Scene to Place, click the road (the
    mouse ray against the road's own plane, sampled every 20 m round the camera). Esc stops.
  - **A ramp's keep-out moves with it** (`TrackGenerator.RampKeepOut`: footprint + longest landing),
    so gates and the patrol duel keep off the new spot.
  - **Checks**, the generator's own rules as warnings (red rings in the Scene view, Go buttons): a gate
    within 150 m of a keep-out or the run-up, pickups within a pad length of each other, anything off
    the lane, a ramp whose landing reaches the run-up, anything in the run-up or past the end.
  - **Reroll Placements** (`TrackGenerator.RerollPlacements`): decides the spawn set's placements —
    speed orbs, repair orbs, laser gates, coins — afresh (new seed) on the road as it stands, keeping
    ramps, loops, end ramps, catalog power-ups and custom prefabs (claims and keep-outs rebuilt from
    them). Undo brings the old ones back.
  - **`TrackPlacementCatalog`** (`Resources/FiniteRunner_PlacementCatalog`, or the generator's
    `placementCatalog`): the palette's power-ups. An entry is a speed orb tier or the repair orb (built
    by their spawners) or a NEW power-up — a `PadSpawnEntry` (prefab + `PadDefinition`) built as a
    `SpeedPad` from a `Pickup` record that indexes the entry: adding a power-up is adding an entry, no
    code. Keep the order (records index it). Brake pads are not offered.
  - **`CustomPrefab`** records index the asset's `customPrefabs` list; built standing on the road at
    their spot + height, culled like the rest.
  - **Verified** on a copy of a saved track: a catalog orb, a new catalog power-up and a custom prefab
    built where placed; a reroll kept every ramp, end ramp, power-up and prefab and re-decided the
    orbs, gates and coins.
- **Previews never reach the scene file**: everything built in edit mode — pickups, road stamps,
  end markers — is flagged `DontSaveInEditor` (`TrackGenerator.MarkPreview`), bent meshes too, and
  the buttons neither record the spline as a prefab override nor dirty the scene.

### Core Settings (Odin region)

**The track's one tuning asset is `TrackShapeSettings`** (refactor Step 2): width, straightness,
the feature table and feature spacing moved there off the generator component, next to the
shape knobs, so the whole track is tuned in one asset. The generator reads them through
`TrackWidth` / `Straightness` / `FeatureTable` / `FeatureSpacing` off `Shape` (the clone in play).

- `trackWidth` (on the shape asset) — pushed into `TrackManager.SetWidth` and `TrackDecorator.SetTrackWidth` on every
  `Generate`. One width knob drives the steering clamp, pad bounds and road meshes, which are
  authored for 60 m and stretch proportionally.
- `straightness` (on the shape asset) — 100% = dead straight; scales the Shape section's turn/heading limits down.
- `trackShape` — a `TrackShapeSettings` asset (`Data/FiniteRunner/FiniteRunner_TrackShape.asset`,
  drawn inline) holding the road's own shape: the **elevation walk** (`elevationEnabled`,
  `elevationBand` ±60 m, `maxGrade` 6°, `maxGradeStepPerKnot` 3°, `baselinePull` 0.5). Play runs
  on a runtime clone (`Shape`, made in `PrepareShape` at the top of `Generate`); the Core Settings
  debug tab writes the asset (`ShapeAsset`) and mirrors onto the clone.
  `AddSegment` keeps a `pitch` state: a random step per knot, leaned home in proportion to the
  height already gained, forced home outside the band, clamped to the grade — and it draws
  nothing while disabled, so a seed reproduces the flat track exactly. Knots go in through
  `TrackManager.AppendKnot(position, rotation)` carrying heading + grade: AutoSmooth keeps the
  rotation's up (projected onto the sloped tangent), which is how the grade reaches every pose.
  Features inherit the grade (a loop stands on the slope).
  **Turns are sweeps** (Turns group: `turnRateRange` 6–13°/knot, `turnArcRange` 25–120°,
  `minStraightKnots` 1, `alternateTurnChance` 0.7, `maxHeadingDrift` 150°): on a straight knot
  one rng draw against `1 − straightness/100` may `StartTurn` — rate and arc rolled off the
  bands, knots = arc / rate, direction alternating by chance or forced back once the heading has
  drifted past the cap — and the sweep then holds that rate per knot. `TurnFits` refuses a sweep
  whose shortest run + the full bank's unwind + the lead would not fit before `featureCursor`;
  `LevelRequired` ends one early if a feature closes in. The generator's old `maxTurnPerSegment`
  / `maxHeading` are gone. Straightness is 100 (dead straight) on the shape asset as shipped —
  the value the old track debug asset had been stamping — and `featureSpacing` is 1500–3000 m so
  a sweep has room between features.
  **The rate band is set by the grip math, not by taste.** A sweep's radius is the knot length
  (`segmentLength` 300–420 m) divided by the rate in radians, and a FLAT sweep holds only while
  `v²/R ≤ gripBase + gripPerSpeed × v` (50 + 0.5v for the Fighter), so the top speed through a
  radius is `v = (R/2 + sqrt(R²/4 + 200R)) / 2`. That is independent of the guide assist: the
  stick's own yaw is capped at `turnAuthority × grip / speed`, so player steering and road
  curvature come to the same limit either way. At 6–13°/knot the radius runs 1322–4011 m, holding
  749–2101 m/s against a 1000 m/s cruise and a 1806 m/s Light Speed: the gentlest flat sweeps are
  free at full speed, the tightest ask for a quarter off the cruise. At the old 15–35°/knot the
  radius was 491–1604 m, holding 322–892 m/s — every sweep forced a brake and the tightest wanted
  a near stop. **Keep `turnArcRange`'s minimum at or above 2 × the rate maximum**, or `TurnFits`
  (which sizes a sweep as `ceil(TurnArcMin / TurnRateMax)` knots) starts demanding gaps the
  feature spacing never leaves and sweeps quietly stop appearing.
  **Banking** (`bankEnabled`, `maxBankAngle` 80°, `bankPerDegreeOfTurn` 8, `maxBankStepPerKnot`
  45°, `levelLeadDistance` 200 m): the bank target at a knot is −(heading change) ×
  `bankPerDegreeOfTurn` (a right turn drops the right edge), capped, eased per knot, rolled into
  the knot rotation about the segment direction — no rng draws. At the defaults a sweep is a
  near-vertical wall the ship rides like an oval's banking (the F-Zero look the user asked for).
  `bankPerDegreeOfTurn` is paired to the rate band: it was 4 while sweeps turned 15–35°/knot and
  went to 8 when they dropped to 6–13, so the bank still reaches the cap on the tightest sweeps.
  Halve the rate again and double this, or the road flattens out.
  **Features need level road** (`LevelRequired`): the target is 0 while the spline end is under a
  `TubeSection` or within `levelLeadDistance` + the knots the current bank needs to unwind of the
  next `featureCursor`, so every loop stands upright, every tube curls from a flat pose and every
  ramp rides its rails.
- `spawnSet` — the `TrackSpawnSet` of everything that streams onto the track (see Spawnables).

The custom inspector is an `OdinEditor` (so Odin attributes render) and adds the
"Regenerate Track" preview button.

### Track features (`Track/Features/`)

A second seeded table, `featureTable` on the track shape asset, of `FeatureSpawnEntry` (name, optional unit prefab, a
`TrackFeatureDefinition` asset, probability with the same rebalancing rule via `IWeightedEntry`,
`minSpacing`, boost `multiplier`, colour). The roadmap for these lives in `TrackFeaturesPlan.md`:
jumps (1), loops (2), cylinder sections (3) are built; multi-path is the one left.

**The builder is piece-sequenced (M3).** `featureCursor` is the next spot. When the normal
segment roll would reach it, `AddSegment` cuts the segment to land a knot **on** the spot (never
shorter than a minimum segment — a closer spot is pushed out), snaps the bank to 0 and pins the
knot with explicit tangents, and `Decide` calls `DecideFeature` right there: one weighted draw
(+ the M0 loop gate), `CreateSection(track, spot, ref rng)`, `AddSection`, the footprint claimed,
and **the spline continues from the feature** — a loop gets an exit knot at its displaced exit
pose (`ContinueFromLoopExit`: explicit tangent along `ExitForward`, the bridge measured into
`SetSplineExtent`, the builder's heading/grade re-read from the exit, bank 0), a tube just keeps
the spline coming (level) underneath, a ramp goes on `pendingRamps` and lands once its run-up is
settled (`SpawnPendingRamps`, before the spawners). The cursor then advances by footprint +
`ExclusionAhead` (a jump's longest arc, so nothing waits under a landing) + max(spacing roll,
`minSpacing`). Every spawner skips claimed ground.

Every entry gets a **runtime clone** of its definition (`Runtime`) in play — the debug menu edits
the clone, never the asset.

- **A ramp needs a landing zone.** When `DecideFeature` draws a jump it reserves
  `straightUntil = spot + length + MaxAirDistance(JumpStrength) + landingClearance`: until the
  spline end passes it, `AddSegment` starts no sweep, ends one in progress, holds the bank at 0
  and the grade where it is, so the longest jump the ship can make always lands on the road it
  left. `JumpStrength` is the STRONGEST the Store can make it (`StoreUpgrades.MaxMultiplier` of
  `ShipJumpStrength`, at least 1) — never the player's own level, so a seed or a saved track is one
  road for everyone and the longest jump anyone can make lands on straight road. The
  exclusion the cursor skips is the same longest jump + `landingClearance`, not the definition's
  strength-1 `ExclusionAhead`.
- **`CreateJump`** spawns a `JumpRamp` (start, length, lateral, half width =
  `HalfWidth × widthFraction`, boost = `powerUpSpeedBoost × multiplier`) with a picture only: the
  entry's unit prefab scaled to (width, lip, length), or a code-built slab pitched to `rampAngle`
  with a rail per edge. Colliders stripped, `featureMaterial` tinted per entry.
  Shipped: both the Jump entry and `endRamp` use `03.Prefabs/FiniteRunner/PF_RampBase` (a unit wedge mesh + `Ramp_Mat`,
  made by **Tools → FiniteRunner → Create PF_RampBase**) — restyle the mesh/material there; size still comes from `JumpDefinition`.
- **A jump's boost is authored on the ramp prefab** (`RampBoost` on `PF_RampBase`): `greenOrbShare` (0.8) × the base boost ×
  the Green tier's multiplier is the takeoff (`JumpRamp.Boost`, paid by `ShipMotor.OnPhysicsTookOff`), and `barrelRollBonus`
  (0.2) more of it is paid ON LANDING if the ship barrel-rolled in that flight (`JumpRamp.RollBonus` → `pendingRollBoost`,
  `ForwardRoll` only marks the roll, `ForwardLanded` pays; cleared on takeoff and on a fall) — 100 % for the jump, 120 % with a roll. A prefab without `RampBoost` keeps
  the entry's `multiplier`. End ramps stay 0.
- **A loop** (`LoopDefinition.CreateSection(track, spot, ref rng)`) rolls drift, its side, carry,
  yaw, its side and turns — in that order — off the bands, builds the `LoopSection` from the
  pose at the spot knot, and `DecideFeature` appends the exit knot and continues the spline from
  it. `CreateLoop` spawns the `LoopFeature` (section + `LoopRequiredSpeed(distance)`) with a
  portal-frame gate at the mouth — two posts + crossbar, or the entry's unit prefab scaled by
  the radius — whose renderers take the gate colour. The ring's road comes from the decorator.
- **A tube** (`TubeDefinition`, `ClaimsFootprint` false) is nothing but its section:
  `CreateSection` rolls the length off the rng, `CreateFeature` builds nothing. **A feature with
  a section never waits for the settle margin** — only a road-bound ramp does; a 4.5 km tube
  could never fit inside the settled stretch, and its pose is only sampled where pads and road
  are placed. Pickups keep spawning across it (`PadMargin` inside `GetLateralBand`) and the
  decorator stamps the pipe.

Loop knobs (`radius`, `exitClearance`, `fallGravity`, `fallSpeedLoss`, gate colours) live on
`Loop_Definition.asset`. The debug Features tab edits radius / gravity / loss through the generic
`AddStat<T>` (per tube entry: radius / band / curl), each written to the definition asset and
mirrored onto the entry's runtime clone.

### Spawnables (`Track/Spawning/`)

Everything the generator streams onto the track besides features and coins is a **`TrackSpawner`**
(an abstract ScriptableObject), listed in a **`TrackSpawnSet`** asset —
`04.Data/FiniteRunner/FiniteRunner_TrackSpawnSet.asset`, drawn inline on `PF_Track`'s
`TrackGenerator` (`spawnSet`). The spawner assets live in `04.Data/FiniteRunner/Spawnables/`. The
generator knows none of them by name. **Add a kind** = a `TrackSpawner` subclass + an asset in the
set; **remove one** = take it out of the set (or untick `active` on the asset).

- **Shipped set**: `Spawner_LaserGates`, `Spawner_SpeedOrbs`, `Spawner_RepairOrbs`.
  `Spawner_BrakePads` exists but is **not in the set** — brake pads were retired once laser gates
  took over as the slow-down hazard. The `PowerUp_Brake` prefab, `BrakePad_Definition` and all
  the brake code (`SpeedPad`, `ShipHealth.brakePadDamage`, `ShipAudio.brakeClip`, the patrol's
  overshoot) are kept, so dropping the asset back into the set restores them.
- **Base fields** (every spawner): `active`, `displayName` (debug label, seed salt, and the key the
  debug snapshot matches on), `color` (debug tint), `spacing` band, `startDistance` (the first step
  lands in `start + [0, spacing.x]`), `chance` per step. Runtime-only: `Density` (the debug
  multiplier on the spacing, 0 = none), the cursor and **its own rng** —
  `hash(layout rng state, FNV(displayName))`, so a spawner never moves another's layout and the
  road never depends on what spawns.
- **Two passes** (M1): `Step` DECIDES — every roll, the claim or `RecordPickup` — and ends in
  `ctx.Emit(TrackPlacement)`; `Build(ctx, placement)` builds that record later, inside the stream
  window, drawing nothing (`CreatePad` no longer records the pickup: `Step` did). Each spawner
  declares the `Kind` it emits, which is how the generator routes a record back to it. The rolls
  stay in their old order (a laser gate: variant, coverage, lateral, rotor, wave), so seeds decide
  what they always did.
- **The loop** (`TrackSpawner.PlaceUpTo`): roll `chance`, then the subclass `Step(ctx, distance)`
  returns −1 (done, move on by the spacing ÷ `Density`), a distance to retry from (the claimed
  ground in the way), or +∞ (never again — the end zone). An inactive spawner (`IsActive` false,
  or density 0) still walks its cursor up to the limit, so nothing lands behind the ship when it
  comes back on.
- **Phases**: `ClaimsGround` spawners (laser gates) run first in every pass and `Claim` their
  stretch; `Pickup` spawners run after, **in set order** — each keeps off the pickups recorded
  before it (`NearPickup`), which is why repair orbs are listed after speed orbs. Then coins.
- **`TrackSpawnContext`** is all a spawner touches: the track, the run's rules (`ITrackRunRules`, null without a game flow), the parent,
  `padSize`, `boostMaterial`, `ClaimEnd` / `Claim`, `NearPickup` / `RecordPickup`
  (`padDistances`), `Register(endDistance, go)` for the cull (**keyed on the END**),
  `KeepOutUntil` (the feature keep-outs, flat sweeps, sections, open edges), `RandomLateral`,
  `PadMargin`, and `CreatePad` (the one `SpeedPad` builder: prefab with colliders forced to
  triggers, or a code-built orb / slab; boosts = `powerUpSpeedBoost` × multiplier, a brake keeps
  its own delta).
- **Runtime clones**: `Generate` clones each set entry (`PrepareSpawners`), index for index — `generator.Spawners`, `GetSpawner<T>()` — and
  calls `Begin`; last run's clones are `Cleanup()`-ed (nested definition clones, tint materials)
  and destroyed. Edit-mode previews run the assets themselves.
- **`SpeedOrbSpawner`**: `tiers` of `PadSpawnEntry` (Green / Blue / Purple — prefab,
  `PadDefinition`, probability rebalanced to 100 % by `WeightedTable.Normalize`, multiplier,
  colour, sway, lane), one weighted draw per step. Spacing 513–897 m — the old 400–700 m ÷ 0.78,
  so removing the brake's 22 % share left the orb count per km as it was.
- **`RepairOrbSpawner`**: 400–700 m, `chance` 0.58 (the green share it used to copy), `size` as a
  share of the pad width (1.5 = a 15 m orb), `roadClearance` (1.5 m; 0.5 let the bob dip the orb
  into the road) = the gap between the
  VISIBLE road and the orb's lowest point: the centre is lifted along the track's up by
  `ctx.RoadSurfaceOffset` (the decorator's `roadYOffset`, −1) + clearance + `OrbHover.BobAmplitude`
  + radius, so at any size it never dips into the road and, sitting just above it, is always in
  the ship's path (the ship's hit box tops out ~3.4 m over the road, so keep clearance + 2 × bob
  under that) — optional prefab else the code-built shell + cross (`shellColor` translucent red, `crossColor`
  green). Off while
  `GameManager.HullEnabled` is false.
- **`LaserGateSpawner`**: see Laser gates.
- **`BrakePadSpawner`**: one `PadSpawnEntry`, the brake material, the pad sign; 1800–3200 m
  (about the old one-in-2.5 km).
- Debug: CORE SETTINGS → one `{NAME} SPACING` row per spawner (slides the `spacing` band, keeping
  its spread) and a `%` row per orb tier; MULTIPLIERS → a `×` row per tier. Rows are built from the
  set's ASSETS (the menu can be built before the first `Generate`), write the asset and mirror onto
  the runtime clone index for index. The old runtime-only density multiplier is gone (the laser
  gates' 0.5 was baked in as a 1200–2400 m spacing).

### Repair orbs (`Track/RepairOrb.cs`)

`RepairOrb` is an `IShipPickup` only — never a `SpeedPad`, never an `ITrackPickup` — so it stays
out of the `PickupRegistry` and the patrol neither seeks nor takes it, and it raises no speed
impulse. `PickUp` looks for a `Contracts.IRepairable` on the collector (the ship's `ShipHealth` implements it; capped at the max hull) and
the orb is ALWAYS taken — at full hull it heals 0 but is still used up. Taken, it raises the static
`RepairOrb.Collected(orb, ship, healed)` (RaceHud green flash + "+N" — skipped when `healed` is 0,
ShipAudio green-orb clip, GameManager soft haptic) and deactivates.

### Collectibles streaming

The "Collectibles" toggle group (`spawnCollectibles`, optional `collectiblePrefab`,
`collectibleSpacing` between rows, `collectibleGroupSize` coins per row a `collectibleStep`
apart at one lateral, `collectibleValue`, `collectiblePickupSize`, coin size/colour).
`PlaceCollectiblesUpTo` runs after the spawners in every stream, skips claimed ground and any
distance within a pad length of a pickup (`padDistances`, pruned with the cull).

**The player's ship takes a coin by COLLIDER** (`ShipPickupSweeper`, like every other pickup): the
code-built coin carries a root `BoxCollider` trigger sized to the pickup volume. Its cylinder's own
collider is removed with `DestroyImmediate`: `Collectible.Awake` only adds a trigger when it finds
NO collider, so a deferred `Destroy` there left every coin collider-less and untakeable (the bug
fixed during M0).

- **Size**: `collectibleShareOfRepairOrb` (0.5) × the repair orb's diameter
  (`RepairOrbSpawner.Diameter`, 15 m as shipped → a 7.5 m coin); share 0 or no repair spawner in
  the set falls back to `collectibleSize`.
- **Height**: lifted along the track's up like the repair orb: `RoadYOffset +
  collectibleRoadClearance (1.5) + Collectible.HoverAmplitude + radius`, so its lowest point clears
  the visible road and it sits inside the ship's hull box (≈1.1–7.6 m over the pose).
- `collectiblePickupSize` (width, height; `FormerlySerializedAs` the old `collectibleTriggerSize`)
  is a MINIMUM for the trigger — never smaller than the coin. The analytic registry (the patrol)
  gets the same volume at the same height.
- `Collectible`'s id/value label is drawn only when SELECTED (`OnDrawGizmosSelected`) — drawn for
  every coin it covered the track, Game view included.

### Laser gates (`Track/Features/LaserGate*.cs`, `LaserBeam.cs`)

The third thing to dodge. A gate is an emitter pair (`03.Prefabs/Runner/PF_LaserSystem`: `LaserA` /
`LaserB`, each with a `ShootPoint` child whose forward is the firing direction) with a beam between
the two shoot points. **A beam never spans the track** — `LaserGateDefinition.coverageBand`
(20–30 % of the lane) — because the ship only leaves the ground at ramps: the way past is round.
Four variants by weight (`PickVariant`): `Horizontal` (one beam on the flight line), `Vertical`
(road → `verticalHeight`), `Triple` (three horizontal beams `tripleSpacing` apart, one above
another — the upper two catch a ship in the air) and `Rotor` (a horizontal beam on the flight line
turning about the track's UP, `rotorSpeedBand`, direction a coin toss). **`Vertical` is shipped at
weight 0** (`LaserGate_Definition.verticalWeight`): with only its top emitter showing it read as a
lone emitter floating over a beam out of bare road, so it is off; the code is kept.

- **Not a `TrackFeatureDefinition`.** Gates are the `LaserGateSpawner` (`Spawner_LaserGates`:
  prefab, definition drawn inline and cloned in play, spacing 600–1200 m, `startDistance` 1500,
  `clearance`), phase `ClaimsGround` — so BEFORE every pickup in every `Decide` pass, claiming their ground
  so no orb or coin sits in a beam, keyed on their END for the cull, drawing from the spawner's own
  rng. Play mode only.
- **Keep-outs** (`TrackSpawnContext.KeepOutUntil`): `featureKeepOuts` gets `(spot, spot + footprint + exclusion)`
  from `DecideFeature` for EVERY feature — a tube too, which claims nothing, and a ramp's longest
  landing — and `(start, ∞)` from `BeginEndZone` (+infinity parks the cursor for good); the
  `clearance` (150 m) is kept either side. It works because all of those are registered at
  a knot still inside the settle margin and **the clearance is shorter than the margin**, so
  nothing is ever decided behind a gate that already stands — keep it that way. Flat sweeps and
  open edges are allowed by default (`onFlatSweeps` / `onOpenEdges`): the shipped
  TrackShape asset authors 100 % of both, and excluding them left almost no gates.
- **Detection is two-phase.** `LaserGate` is an `ITrackPickup` whose volume is the box round all
  its beams (the rotor's swept disc), so the body's swept query finds it at any speed; the ship
  then asks `gate.Touches(body.SweepFrom, body.Distance, lateral, height, pickupReach)` — the
  closest approach of the stretch just flown to each beam SEGMENT in gate-local track space
  (x lateral, y height, z along), in a space squashed by the ship's reach + `beamRadius`. One
  test for all four variants. The rotor's angle is a pure function of `Time.time`, shared by the
  picture and the test, so a pause freezes both. A gate is never used up; `RehitSeconds` makes one
  pass one hit. `LaserGate.Hit` (static, `Action<LaserGate, Component>` — the track names the hit component, never the motor; the listener compares) is the player's event; the patrol's body finds gates and
  ignores them (`PolicePatrol.OnPickedUp` only knows `SpeedPad`).
- **`LaserBeam` is the picture only**, authored ON the prefab with its four references
  (`Tools → FiniteRunner → Install Laser Gate Assets` adds and wires it by name, and creates
  `LaserBeam_Mat` + `04.Data/FiniteRunner/LaserGate_Definition.asset`, never overwriting). Every
  frame the gate hands it two world muzzles: each emitter is turned until its shoot point looks
  down the beam, rolled about that line (`emitterSpinDegPerSec` — the barrel is the models' local
  Z; B rolls the other way) and slid until the shoot point is ON the muzzle. **Posed about the
  shoot point, never the emitter's pivot** (the two models have different pivots). The beam is two
  code-built `LineRenderer`s (glow + core) on one shared additive URP Particles/Unlit material
  tinted by vertex colour — no MPB, no per-instance material.
- **The wave** (`LaserGateDefinition` "Wave" toggle group: `waveChance`, `waveAmplitude`,
  `waveLength`, `waveSpeed`, `waveTaper`; rolled per gate off the spawner's rng, no draw while off): a
  TRIANGLE wave running A → B. `LaserBeam.BuildWave` puts a vertex on each muzzle and one ON every
  corner of the wave and nowhere else (corner k at `s = (k/2 + shift) × wavelength`, even = crest),
  so the corners stay sharp however it slides — never resample it at a fixed step. It swings
  along the track's up (across the track on the vertical gate), and `LaserGate.WaveReach` grows
  what burns by the amplitude on that same axis, so the picture never lies.
- **The picture is lifted off the burn** (`LaserGate.VisualLift`). At `emitterScale` 5 an emitter
  reaches ~4.3 m out from its barrel (`LaserBeam.EmitterRadius`, measured off its mesh bounds) and
  rolls about it, so a beam on the flight line — 1 m over the visible road — buried the emitters
  ~3 m in it. The burn segments stay on the flight line (where the ship's hit box is); horizontal,
  triple and rotor gates are DRAWN `roadSurface + emitterRoadClearance + EmitterRadius − lowest
  beam` higher (≈ 3.8 m), which is where the ship's MODEL rides anyway (0.9–6 m over its box). The
  vertical gate is not lifted: its burn and picture start at `ctx.RoadSurfaceOffset` and its
  bottom emitter (A) is hidden, so it rises out of the road.
- **Beams are red** (`beamColor` / `coreColor` on the definition; the material stays white).
- **A hit smokes the hull**: `GameManager.OnLaserHit` (only when the hit lands, not through the
  blink) calls `FX/SmokeVfx.SpawnTrail` on the ship's ROOT, simulating in its local space so the
  plume stays on the ship at any speed and never rolls with a barrel roll. Knobs:
  `GameSettings.laserSmoke*` (textures = the Black smoke sprites; empty = no smoke).
- Debug: CORE SETTINGS → LASER GATES SPACING (the spawner's spacing band, live).

### Analytic pickups (`Simulation/PickupRegistry.cs`)

`ITrackPickup` (distance, live lateral, height above the flight line, half extents, `Available`)
is implemented by `SpeedPad`, `Collectible` and `LaserGate` (whose box is only the broad phase — see Laser gates). The generator calls `PlaceOnTrack(distance,
lateral, height, halfExtents)` on each as it spawns them (an orb = a ball of its size, a flat pad
= its slab, the air lane = `AirLaneHeight`); they register in play while enabled and drop out on
disable/destroy (cull, consume). The registry is static and cleared on boot (domain reload off).
`TrackBody.SweepPickups` runs every step over the distance just covered — tunnel-proof at any
speed — with the body's own `pickupReach` (the ship's: half its `BoxCollider`, 2.5 × 2.3 m)
added on, the height test making a jump clear the ground lane, and laterals compared modulo the
circumference round a full tube. It raises `body.PickedUp`, which only the patrol subscribes to
(`PolicePatrol.OnPickedUp`). **The player's physics ship does not take pickups here**: pads, orbs,
repair orbs and coins are found by collider through `ShipPickupSweeper` (`ship-standalone.md` —
which is why every spawned pickup gets a trigger), and laser gates by `ShipMotor.SweepLaserGates`
over this registry. A swaying orb reports its live lateral through
`OrbHover.SwayOffset`.

**`TrackObstacles`** (`Track/TrackObstacles.cs`) is the "what must be steered ROUND here?" query:
the lateral spans of every built non-end `JumpRamp` and every `LaserGate` in this registry (beam
bounds plus `LaserGate.EmitterReach`, none for a Vertical beam) touching a stretch, plus
`FreeIntervals` / `Nearest` over a lane. Oncoming traffic steers and checks its spawns with it
(`runner-traffic.md`). Built objects only: ask about settled road.

### Forcing the end in (`ForceEndAhead`, the hyperspace jump)

`TrackGenerator.ForceEndAhead(runUpMeters)` brings a finite track's end in NOW. The track is
decided whole at run start, so it first CUTS THE DECIDED ROAD BACK (`CutBackForEnd`): the last knot
kept is the one after the first knot `CutClearance` (200 m — what built objects and 40 m road stamps
reach past their own distance) past the built road, never inside a section. Removing the knots after
it reshapes the segment ENDING at the kept knot (its AutoSmooth tangent changes), so every record on
that segment or past it is dropped, with its claims, keep-outs and pickup marks; the road the ship
can see is untouched. `TrackManager.TruncateKnots` drops the knots, sections and spans past the cut
and clears the end marks. The builder resumes from the kept knot (`knotDistances` tracks every
knot's track distance): heading and grade from its rotation, bank from `GetBankAtDistance`, out of
any sweep. Then the run-up starts at the tip + half a segment, **never inside built road**, plus
what the road there still owes: the bank unwinding (`ceil(|bank|/maxBankStepPerKnot) ×
segmentLength.y + levelLeadDistance`), the end of a section. It sets `endZoneTarget`, `endRunUp` (≥ a
ramp + half a segment) and `targetLength`, and decides the new end at once (`Decide(+∞)`:
`EndZoneStart`, `FinishTrack`, the end ramp records) — the spawners and coins place nothing on the
new tail (their cursors are past it). False (nothing changes) when endless, or the real end zone
already starts before the new one could. Verified: cut 200 m past the built edge, all built knots
unchanged, the new end's ramps and final collider chunk build. `EndRampLaterals()` returns the three ramps' centres
from the SAME layout helper `CreateEndRamps` uses (`EndRampLayout` / `EndRampLateral`), valid before
they exist.

## Feature geometry

### Loops

Mandatory vertical loops the whole track width wide, inserted into the track's **distance**, so
pads, patrol, road stamps and streaming ride them unchanged and the decorator draws the ring's
road chord by chord for free. Since M3 a loop **goes somewhere**: per instance it may corkscrew
sideways (`lateralDriftRange` 0–240 m, side random), carry forward (`forwardCarryRange`
0–300 m), yaw its exit heading (`exitYawRange` 0–30°, side random) and turn more than once
(`turnsRange` 1–1, up to 3) — all on `Loop_Definition.asset`, all eased so both mouths are
tangent to the road. The track continues from the exit: the generator lays an explicit-tangent
exit knot there and the bridge between entry and exit knots is the section's `SplineExtent`.
A failed loop lets go at the top of the FIRST turn and lands on the (displaced) exit. The
cinematic side shot is planted off the entry pose, so a strongly displaced loop may sit partly
out of frame — a `Fighter_CameraSettings` tuning matter.

The entry speed a loop demands is **fixed when it is placed**: `GameSettings` floor 1200 km/h +
18 km/h per 100 m travelled, capped at 2900 (`GameManager.LoopRequiredSpeed`). So the
portal-frame gate at the mouth (green/red against the ship's speed every frame) and the required
km/h never lie. The number is **fixed above the gate as a world-space label**
(`LoopFeature.BuildLabel`, `labelHeight` / `labelSize` on the definition), shown only inside
`labelLeadMeters` (1800 m — 300 m beyond the fog end, so the number leads the gate out of the
haze) and tinted with the gate. It is never a popup riding ahead of the ship.

**A loop is only placed when it is reachable** (`TrackGenerator.LoopReachable`): in an endless
play run, the ship's predicted speed at the spot (at least the CRUISE speed — the track is decided
whole at run start, with the ship standing on the line — minus the passive bleed over the gap)
must clear `LoopRequiredSpeed(spot)` × (1 + `LoopDefinition.gateHeadroom`, 0.1). A refused
loop redraws among the other entries off the **same** roll (`WeightedTable.Pick(…, exclude)`), so seeds
only diverge where a loop was refused; a table with nothing else skips the spot. Edit-mode and
non-endless previews are not gated. So a red gate can only come from speed lost after placement.

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
- Orbs spawn anywhere in the band (a tube claims no footprint) — a purple orb
  under the pipe is the reason to go under. Ramps and loops never start inside one.
- The road is the ordinary road prefab stamped in strips round the band
  (`TrackDecorator.StampTube`, `tubeStripWidth`), with **no barriers on any tube** — the band
  clamp is the fence.
- `ShipState.OnTube` marks it for readers; the pose function and `TrackManager.GetLateralBand`
  do all the work.

## `SpeedPad` + `PadDefinition`

`SpeedPad.Collect(motor)` (from the swept query — there is no `OnTriggerEnter`) calls
`ShipMotor.AddSpeedImpulse(speedDelta)` — positive boost, negative brake, divided by the ship's
`weight` — and raises the static `Collected`. **A boost orb is used up** (deactivated) when
taken; a brake pad stays painted on the road but only bites once. Whatever colliders a pad's
visual carries are only a picture.

- `sizeMultiplier` scales the spawned pad (`BoostPad_Definition` 0.75 → the boost prefabs at 18×,
  ring included) and its pickup volume. Speed-ups are small **hovering orbs (0.3)** on the
  flight line that must be aimed for; speed-downs are large **1.2 pads** that must be dodged.
- **The boost prefabs carry a ring above the pad** (`Indicator`: `indicator-round-d.fbx`, no
  collider) driven by **`BurstSpin`**: a Y-axis billboard — it yaws about the track's up (captured
  at start) to face `Camera.main` every frame, so it is never seen edge on — spinning in its own
  plane in bursts (one fast eased turn, a pause, again). Written in world space in `LateUpdate`, so
  `OrbHover`'s slow spin of the root never leaks into it. `SpeedPad.ApplyColor` tints EVERY renderer, so the ring wears the
  tier colour.
- **The boost QTE** (`GameFlow/BoostQte.cs`, hand-placed in `PF_UI`, bound by `GameManager` when
  `GameSettings.boostQte`): press Boost (`GameAction.ShipBoost`, A / Space) as the ship crosses a
  boost orb and the boost is multiplied by the accuracy. Taking an orb without a press is unchanged.
  Graded in **time** — `(orb.TrackDistance − ship distance) / speed` — so the window feels the
  same at every speed: within `boostQtePerfectSeconds` = PERFECT (the top of
  `boostQteMultiplierBand`, 1.5), falling along `boostQteFalloff` to the bottom (1.1) at
  `boostQteWindowSeconds` either side, beyond = a miss. **An early press is banked** and applied
  by `Collect` in the ONE impulse (the pad asks its static `SpeedPad.BoostTiming` hook, which the
  QTE registers in `OnEnable` — the track never names the QTE — → `SpeedDelta × mult`; `SpeedPad.BoostApplied`
  then resets the feedback), so speed lines, "+N",
  audio and the patrol's `boostShare` all scale; **a late press tops up** with a second impulse of
  `SpeedDelta × (mult − 1)`. One press per orb — a press outside the window locks it as a miss, and
  a window that closes unpressed on a taken orb is a miss too. The target is the nearest boost orb
  ahead within `boostQteShowSeconds`; not while falling, respawning or `DashLocked` (the duel).
  Feedback scales through the static `BoostQte.FeedbackScale` (0 plain … 1 perfect), set only
  around a graded impulse and read inside `PadImpulse`: the warp (`PadEffects` →
  `LensDistortionController.Trigger(BoostQte.WarpScale)`, up to `boostQteWarpAtPerfect`) and the
  rumble (`RunFeedback.OnPadImpulse`, `boostRumble` → `boostQteRumbleAtPerfect`). The picture is
  `BoostQtePrompt` (`runner-hud-screens.md`). The patrol's `Take()` never grades.
- `PadSpawnEntry.pickupScale` scales a tier's pickup volume (prefab colliders + the analytic box), never the picture — Green ships at 2.
- `floatingOrb` makes it a hovering sphere on the flight line, with an `OrbHover` bob/spin/sway
  component added at runtime. `OrbHover` bobs and sways along the **track's** up/right captured
  at spawn, not world axes, so orbs survive loops and tubes. The spin rate is per tier
  (`PadSpawnEntry.spinDegreesPerSecond`, 60 by default); Green's is 0 because its `Boost_v2`
  model sits tilted inside the prefab and wobbles when spun.
- **Tiered boost orbs override the definition's delta and colour per instance** via
  `SetDefinition(def, speedDelta, tint)` — the shared `PadDefinition` asset is never mutated.
  Three rarity tiers: green 1×, blue 2.5×, purple 10× of `GameManager.powerUpSpeedBoost`; the
  higher the multiplier the scarcer the orb and the more it sways. Tier weights/colours/sway live
  on `Spawner_SpeedOrbs` (`SpeedOrbSpawner.tiers`).
- `PadSpawnEntry.lane` (Ground / Air) is the prepared **air lane**: Air entries spawn
  `GameSettings.airLaneHeight` above the flight line along the track's up, reachable only off a
  jump. No table carries one yet.

## `TrackDecorator`

**Open edges**: where `track.IsEdgeOpen` the wall is left off that side. The test scene uses
the full-width `road-straight-barrier` piece (`barrierLateral` 0, both walls in one mesh), so
that stamp is swapped for `oneSidedBarrierPrefab` (wall authored on the LEFT; turned 180° for an
open left edge) or, while that art does not exist, a code-built placeholder wall on the closed
side (`placeholderWallSize`); side-barrier setups just skip the open side. The open edge gets a
low marker strip (`openEdgeMarkerSize`, `openEdgeMaterial`). Code-built boxes lose their collider.

Stamps road-kit meshes (road surface, side barriers) along the spline, streaming-style:
`DecorateUpTo(distance)` advances an internal stamp cursor, `CullBefore(distance)` drops pieces
behind the ship. There is no goal gantry: the end of a finite track is its three end ramps, and
`StampEndMarker` (open-edge material, keyed on its far end) marks the gaps between them.

MPB tints are unreliable with the SRP Batcher — hence the material-override fields.

**Every stamped piece is BENT along the track** (`pieceBlend` = Blended, the default): road slabs, barriers, placeholder
walls, open-edge markers and tube strips. A rigid piece is posed once at its centre, so where the
bank changes (up to ~6° per 40 m) its neighbour sits at a different roll and, ~80 m out on a road
this wide, the outer edges step apart by metres — dark wedges between the pieces on every banked
sweep. `Bend(piece, distance, lateral)` expresses each vertex in the frame of the pose the piece
was stamped at, (x across, y up, z along), and moves it to the pose at `distance + z` keeping x
and y — so neighbours share the same curve and bank and meet exactly. Normals go through the
inverse transpose (the pieces are scaled unevenly). Each piece gets its own mesh copies, owned and
freed by a runtime-added `BentPiece`. ~0.07 ms a stamp (0.02 rigid). **The kit meshes must be
Read/Write enabled** (`road-straight_v1`, `road-straight-barrier`, `road-straight`); a piece whose
mesh is not stays rigid. The end markers (straight run-up) are not bent.
**`pieceBlend` = Not Blended keeps the gaps on purpose**, scaled by two sliders (0–3, shown only
then): each row's bent pose is blended UNCLAMPED toward the rigid one (the stamp pose carried along
its own forward). The turn from the stamp pose is split swing × twist round local Z (`TwistAboutZ`):
`pieceMorph` scales the swing — the curve, yaw and pitch — and the spine position (`LerpUnclamped`),
`pieceZMorph` the twist — the bank, so the edges step up and down. Each: 0 = Blended, 1 = rigid,
above 1 the piece's ends over-rotate and the wedges open wider (2 ≈ double). Both at 1 = the plain
rigid pieces (no mesh copy at all, the 0.02 ms path). Piece centres always sit on the track, and the
road art is a picture only (the ship rides `TrackColliderBuilder`), so it is purely a look. **In edit
mode a change re-stamps the road already drawn** (`OnValidate` → `RestampForBlend` on `delayCall`,
queued once however fast a slider moves, undo included; it compares against `stampedMorph`, the
blend a fresh road was stamped with, and does nothing while `stamped` is empty — after a script
reload the list is gone and re-stamping would double the pieces, so Preview / Generate again); the end markers are kept out of
`Restamp` (`endMarkers` — the generator places them, `StampAt` could not put them back). In play a
change applies to pieces stamped after it.

**The road kit and its neon look** (the test scene): the road stamp is
`03.Prefabs/FiniteRunner/RoadSlab.prefab`, a wrapper round
`02.Art/01.Models/FiniteRunner/OriginalModels/road-straight_v1.fbx` — a road PROFILE: a 110 m flat
centre with banked shoulders rising ~10 m to either side (mesh X is the width, mesh Z its 99 m
length, FBX cm). The wrapper's child is yawed 90° so the width lands on the wrapper's Z (the
decorator's width axis at `roadYaw` 90), rolled 1.7° to level a modelling tilt, and lifted so the
flat centre sits at y 0; `roadScale` (0.403, 0.543, 0.543) puts the flat centre exactly on the 60 m
reference lane and 40 m along the track, height in step with the width. Measured off the mesh, the
crease is at 55.23 mesh units and the top of the bank at ~81.98, 9.44 up — so the shoulder is
**0.4844 × the lane's half width, rising 5.1 m**, which is what `TrackManager.shoulderFraction` /
`shoulderRise` carry and what puts the walls on the bank's lip. The width scales with the track, the
rise does not (`roadScale.y` is not width-scaled), so both defaults hold at any track width. **Tubes keep a flat piece**: `tubeRoadPrefab` / `tubeRoadScale` /
`tubeMaterialOverride` (the old unit `road-straight` at (40, 10, 60)) — a profiled strip would make a
ribbed pipe. Every piece is drawn by `02.Art/04.Shaders/FiniteRunner/NeonRoad.shader`: unlit black
asphalt with an HDR orange edge line on the crease where the shoulder starts (its halo spills both
ways), a dimmer orange line along the top of the bank (`_OuterOffset` / `_OuterStrength`) so the
slope reads against the sky, and blue lane lines — **procedural in mesh space**: the lateral
coordinate is `(positionOS.axis − _MeshCenter) / _MeshHalfWidth` with `_LateralAxis` picking X or
Z, so the material carries the MESH's extents and a kit piece with a different mesh needs its own
material. Nothing varies along the track on purpose (stamps overlap on bends; only a lateral-only
pattern is seamless). Vertical faces can be confined to a light strip under `_WallTop`
(`_WallStrip`), so a wall is a dark face with a neon top line, not an orange plank. Five materials
in `02.Materials/FiniteRunner/`: `NeonRoad_Mat` (the slab: axis X, half width 55.23 = the crease) →
`roadMaterialOverride`; `NeonBarrier_Mat` (the unit channel: axis Z, half 0.5, strip under y 0.08) →
`barrierMaterialOverride`; `NeonEdge_Mat` (all orange, coordinate-free) → `openEdgeMaterial` for the
low marker strips and end markers; `NeonWall_Mat` (all orange, strip under y 0.5) →
`placeholderWallMaterial`; `NeonTube_Mat` (plain asphalt, a faint blue seam per strip, no lanes) →
`tubeMaterialOverride`. The glow is the scene volume profile's **Bloom** (threshold 1, intensity
0.45), added for this — the URP asset is HDR.
