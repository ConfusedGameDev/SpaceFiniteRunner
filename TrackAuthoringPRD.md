# PRD: Authored tracks (bake, edit, save, load and stream the runner track)

| | |
|---|---|
| **Status** | In progress. **M0 landed 2026-09-30. M1 landed 2026-09-30. M2 landed 2026-09-30. M3 landed 2026-09-30. M4 landed 2026-09-30.** Settled with the designer: D1–D3, and on 2026-09-30 the four open questions (D10–D13). D4–D9 are proposals open for review. |
| **Branch** | `feature/trackAuthoring`. |
| **Scope** | Runner game only (`FiniteRunner_Test`, campaign runner levels). The city chase, `SaveData` and the Store are untouched. `Campaign` changes only through `RunnerLevelDefinition`. |
| **Related** | `.claude/rules/runner-track.md`, `runner-hud-screens.md`, `campaign.md`, `ship-standalone.md`; `PatrolDuelPRD.md` (the patrol reads `IsGroundClear`) |

---

## 1. Problem

The runner track is finite now (`RunnerLevelDefinition.trackLengthMeters`, 40 km by default), but it is still built the endless-runner way. `TrackGenerator.StreamTo` decides the road knot by knot, about 1600 m ahead of the ship, from a `TickCount` seed. That causes four problems:

- **Regenerate Track is broken.**
  - The button (`Runner/Editor/TrackGeneratorEditor.cs:24-40`) calls `Generate()` and then snaps the `ShipMotor` transform to `GetPose(0,0)`. Since the M9 physics cutover, `HoverShip.Update` overwrites that pose every frame.
  - It never calls `ShipMotor.Launch()` / `LaunchPhysics` (collider `BuildNow`, launch at 100 m, `body.Reset`). It also never resets the patrol, the clock or the objectives. That full reset is `GameManager.Restart`'s job (`GameManager.cs:1025-1027`).
  - In edit mode it only shows an **endless** preview: no finite end, no run-up, no end ramps, no laser gates (they are play-only, `LaserGateSpawner.cs:44`), and no loop check. The preview never matches a play run, and the next Awake regenerates it with a new seed anyway.
- **A track cannot be authored.**
  - No asset stores a track; the spline exists only on the scene's `SplineContainer`, rebuilt at runtime.
  - Nothing in the runner draws gizmos or handles for the track (`TrackManager`, `TrackGenerator`, `TrackSection`, `TrackGuide`).
  - A level cannot choose anything but its length.
- **A seed does not guarantee a layout.** Collectibles (`TrackGenerator.cs:1188-1202, 1504`) and ramp laterals (`:1308`) draw from the same layout RNG as `AddSegment`. How those draws interleave depends on how many segments each `StreamTo` call appends, which depends on frame timing and ship speed.
- **The minimap cannot show the track.** `ChaseMinimap` is a vertical progress bar. The road beyond the stream horizon does not exist yet, so its shape can't be drawn.

## 2. Goals

1. **G1:** Runtime generation stays available and plays exactly as it does today.
2. **G2:** A designer can generate a whole track in the editor with one click, at its real length and with its real end.
3. **G3:** The designer can shape the track visually in the Scene view: curves, banking, grade, flat sweeps and open straights.
4. **G4:** The designer can place, move, add and delete pickups, orbs, repair orbs, ramps, laser gates and any custom prefab.
5. **G5:** The track can be saved to an asset, assigned per level, and loaded at runtime. The same asset plays the same every time.
6. **G6:** At runtime only a window ahead of and behind the ship is built (objects, colliders, decoration). A full-length track never costs more than today's streamed one.
7. **G7:** The chase minimap draws the real shape of the track.

## 3. Non-goals

- **Changing the runner's rules:** the win and lose conditions, hyperspace, the patrol duel and the hull.
- **Baking decoration or colliders into the asset.** Both stay streamed and derived from the layout.
- **Multi-path track.** It is still unbuilt and still out of scope.
- **A standalone track editor window or a node graph.** Editing happens in the Scene view on the real `TrackManager`.
- **Per-level shape or spawn-set settings for runtime generation.** They could come later; this PRD adds only the per-level asset.
- **The city chase.**

## 4. Settled design decisions

- **D1: One track asset per level.** `RunnerLevelDefinition` gets a `track` field (`TrackLayoutAsset`). An empty field means runtime generation, as today.
- **D2: Generate overwrites.** The asset is the source of truth. Generate rolls a fresh layout from the seed and replaces everything, after a confirmation dialog. There is no per-layer merge, with one exception: **Reroll placements** (R5.5) re-rolls only the placements on the current road.
- **D3: The minimap is a top-down view of the whole track,** drawn in the rect the designer placed. Ship and patrol dots move along it, and there are markers for ramps, lasers and the finish.
- **D10: A track asset carries its own countdown.** `TrackLayoutAsset.timeLimitSeconds` (0 = fall back to `GameSettings.timeLimitSeconds`). The countdown is read today in `GameManager.cs:129, 244, 667 and 1008`; all four go through one resolved value instead.
- **D11: No orbs on the minimap.** Speed orbs and repair orbs are never drawn. Only ramps, laser gates and the finish get markers.
- **D12: A debug row forces runtime generation.** It sits on the debug menu's track page, is off by default, has no effect on a level without an asset, and applies on the next restart. Its purpose is to compare an authored track with a generated one.
- **D13: Placements are data-driven, so new power-ups need no code.** Brake pads are retired and not offered. The editor palette is built from a **placement catalog** asset, not a hard-coded list; each entry is a prefab plus its definition (a `PadDefinition`, or a future power-up definition). Adding a power-up later means adding a catalog entry, with no editor or streamer change. (This replaces the brake-pad part of D8.)

## 5. Proposed design decisions (for review)

- **D4: Split *layout* from *materialization*.**
  - **Layout:** a plain serializable `TrackLayout` that describes the whole track.
  - **Materialization:** streams GameObjects, colliders and decoration for the window around the ship, and culls the rest.
  - Today both happen in one pass inside `TrackGenerator`.
- **D5: Runtime generation = an in-memory bake at run start.** The generator runs headless over the full length when the run starts and produces a `TrackLayout`. The streamer then streams it exactly as it streams an asset.
  - Cost: about 120 knots plus a few hundred records, which is trivial.
  - This gives G1, G6 and G7 in both modes, and fixes the determinism problem as a side effect.
- **D6: Distance from the track start stays the authoritative coordinate.** Placements are stored as `(distance, lateral, height)`. Editing the spline moves the road under them: they keep their distance and slide with it. The editor re-validates after each edit.
- **D7: The runtime never writes the asset.** The streamer takes a runtime clone. Hyperspace `ForceEndAhead` truncates the clone. This follows the "gameplay never writes a settings asset" invariant.
- **D8: Placement kinds are an append-only serialized enum:** SpeedOrb, RepairOrb, LaserGate, Ramp, Collectible, Pickup and CustomPrefab. `Pickup` is the generic power-up kind, pointing to a catalog entry (D13). Each kind has one factory that does what the spawners do today: `ForceTriggers`, `PlaceOnTrack` registration and the repair-orb lift. There is no BrakePad kind.
- **D9: The end zone is authored data, not a rule.** `endZoneStart`, `endDistance` and the three end ramps are stored in the layout. The straight walled run-up stays enforced: the editor refuses or warns on edits that bend it.

## 6. Data model

**`TrackLayout`** (`[Serializable]`, new, `Runner/Track/Layout/`) contains:

| Field | Contents |
|---|---|
| Header | `formatVersion`, `seed`, `length`, `width`, `timeLimitSeconds` (D10), `endZoneStart`, `endDistance`, end-ramp layout (gap, side gap) |
| `knots` | `List<TrackKnot>`: position, rotation (whose up vector carries grade + bank) and tangent mode/tangent. This mirrors the three `TrackManager.AppendKnot` overloads. |
| `flatSweeps`, `openStretches` | Distance spans, the same as `TrackManager.FlatSweep` / `OpenStretch`. |
| `sections` | `List<SectionRecord>`: feature definition, start distance and the rolled params. This needs `TrackFeatureDefinition.CreateSection(track, start, ref rng)` split into `Roll(ref rng)` and `Build(track, start, params)`. |
| `placements` | `List<TrackPlacement>`: kind, distance, lateral, height, tier/variant, a catalog entry or prefab/definition override, and per-kind params (laser pattern, rotor, sway, spin, scale). |

**`TrackPlacementCatalog : ScriptableObject`** (D13) lists the placeable power-ups: display name, gizmo colour, prefab, definition, lane (Ground/Air), default sway/spin/scale. It is seeded with the green/blue/purple speed orbs and the repair orb. The editor palette and the streamer's `Pickup` factory both read it.

**`TrackLayoutAsset : ScriptableObject`** holds one `TrackLayout`, plus the provenance needed to regenerate it: the shape asset, the spawn set, the seed and the target length.

## 7. Requirements

### R1: Runtime generation (G1)
- R1.1 A level with no `track` asset generates at run start, as today. Same shape asset, same spawn set, same feature and spawner rules, same exclusion rules.
- R1.2 A non-zero seed always gives the same track, whatever the frame rate or ship speed. Collectibles and ramp laterals get their own hashed RNG streams, the same pattern as `TrackSpawner.cs:71`.
- R1.3 Regenerate Track works in play mode. It routes through `GameManager.Restart` (`RegenerateForRun` + `motor.Launch()` + `patrol.Launch()`): no fall, no hitch, no stale patrol.
- R1.4 `TrackGuide`'s curvature cache is cleared on `Regenerated` (`TrackGuide.cs:180-192`).

### R2: Generate in the editor (G2)
- R2.1 **Generate Track** builds the full-length layout in edit mode with the inputs a play run uses:
  - the level's target length and run-up
  - a baseline jump strength
  - an assumed speed for loop reachability
  - laser gates included
- R2.2 The editor exposes seed (0 = random, and the rolled seed is stored), shape asset, spawn set and target level.
- R2.3 Regenerating a hand-edited asset asks for confirmation (D2).
- R2.4 **What the editor shows is what plays.** Found in M0 testing: today play regenerates in `Awake` with a fresh seed, a finite length the preview lacks, and ramp landing room sized by the PLAYER's Store jump upgrade, so the edit-mode track never matches play. From M2:
  - a level with an asset plays exactly the asset;
  - ramp landing room is sized by a FIXED jump strength (the strongest upgrade), never the player's, so one seed gives one road for every player;
  - the edit-mode preview uses the same length, run-up and end as play.

### R3: Save and load (G5)
- R3.1 Save / Save As writes a `TrackLayoutAsset` under `04.Data/FiniteRunner/Tracks/`.
- R3.2 `RunnerLevelDefinition.track` is exposed through `ITrackRunRules` (`Contracts/TrackContracts.cs`) and resolved in `GameManager.ResolveRunData`, so a campaign `MissionSession` level wins.
- R3.3 A loaded asset overrides `trackLengthMeters`. The inspector shows the asset's length.
- R3.4 Hyperspace still works on a loaded track. It truncates the runtime clone and brings the end ramps in about 2 s ahead.
- R3.5 The countdown comes from the loaded asset's `timeLimitSeconds`, else from `GameSettings.timeLimitSeconds` (D10). The HUD, the run-ended stats (`PlayerStats.RecordRunEnded`) and retries all use the resolved value.
- R3.6 A debug row, **FORCE RUNTIME TRACK**, ignores the level's asset and generates at runtime on the next restart (D12). It is a runtime debug toggle, not an asset write.

### R4: Visual editing of the road (G3)
- R4.1 **Scene preview** of the edited layout:
  - road edges and walls
  - open edges and flat sweeps in distinct colours
  - loop and tube bounds
  - end zone and end ramps
  - distance ticks every 500 m with labels
  - placement icons in each spawner's existing `color`
  - Drawing is limited to a window around the Scene camera so a 40 km track stays responsive.
- R4.2 **Knot handles:** position (moves the curve and the grade/pendant), a bank disc around the tangent, tangent handles on spot knots, insert between knots, delete.
- R4.3 **Spans:** drag the ends of flat sweeps and open stretches along the track; add and remove them.
- R4.4 **Sections:** edit a loop's or tube's rolled params in an Odin inspector, and move its start distance.
- R4.5 Every edit is undoable and rebuilds the spline live. An optional **Preview geometry** toggle materializes colliders and decoration in the editor.

### R5: Placement editing (G4)
- R5.1 Placement handles are constrained to the road: drag along distance and lateral, plus height where relevant. The projection uses the `TrackGuide.TryProject` approach, Newton steps on `GetPoseAtDistance`.
- R5.2 **Palette:** add any **catalog power-up** (D13), a laser gate (pattern), a ramp, a collectible, or a **custom prefab** at the clicked road point. Also delete, duplicate, and a list view with click-to-frame. Brake pads are not offered.
- R5.3 Custom prefabs and catalog power-ups stream and cull like every other placement. A new catalog entry shows up in the palette with no code change.
- R5.4 **Validation warnings**, reusing today's rules:
  - `TrackSpawnContext.KeepOutUntil`: lasers near ramps, loops, tubes or the run-up
  - `NearPickup` overlap
  - items outside `GetLateralBand`
  - a ramp landing inside the end zone
- R5.5 **Reroll placements** re-rolls only the placements, from the spawn set, on the current road, after a confirmation.

### R6: Streaming (G6)
- R6.1 All knots are pushed into `TrackManager` at load. The spline is cheap at this size.
- R6.2 Placements are instantiated inside `[ship − behindDistance, ship + aheadDistance]` and culled behind, reusing the `spawned` / `CullBehind` bookkeeping.
- R6.3 `TrackColliderBuilder`, `TrackDecorator`, `TrackGuide`, `PolicePatrol` (`IsGroundClear`) and `HyperspaceJump` (`ForceEndAhead`, `EndRampLaterals`) read the streamer through a `Contracts` interface, not through `TrackGenerator`.
- R6.4 No hitch at load. The object count inside the window matches today's.

### R7: Minimap (G7, D3)
- R7.1 A new `TrackMinimapGraphic : MaskableGraphic` draws the layout's simplified XZ polyline in `OnPopulateMesh`, fitted to the designer's rect with the aspect ratio kept. Code never lays out the HUD.
- R7.2 The ship sits at `DisplayDistance` and the patrol at `DisplayDistance − GapToShip`, both mapped onto the polyline. `HiddenFromMap` is honoured.
- R7.3 Ramps, lasers and the finish get markers; orbs never do (D11). The driven part of the line is tinted. A hyperspace truncation redraws the end.
- R7.4 Colours, line width and sprites go in `ChaseMinimapSettings`. The distance texts stay.

## 8. Milestones

| # | Milestone | Covers | Done when |
|---|---|---|---|
| **M0** | Quick fixes: the Regenerate button in play, determinism, curvature cache, a null guard in `Generate()` | R1.2–R1.4 | Regenerate restarts cleanly in play. Two plays with the same seed produce identical knot and placement dumps. |
| **M1** | Layout / materialization split: `TrackLayoutBuilder` + `TrackStreamer`, spawners emit records, contract seam, runtime = in-memory bake | D4, D5, R1.1, R6 | A full run plays as before (orbs, lasers, ramps, end ramps, hyperspace, fall/respawn, patrol duel). The sandbox smoke test and the independence validator pass. |
| **M2** | `TrackLayoutAsset`, editor Generate + Save, per-level field, loader, hyperspace truncation, per-track countdown, FORCE RUNTIME TRACK debug row | D1, D2, D7, D10, D12, R2, R3 | The track generated in the editor is the track that plays. A baked asset plays identically every time, both in `FiniteRunner_Test` and from a campaign mission. Levels without an asset still generate. |
| **M3** | Read-only Scene preview and gizmos | R4.1 | A 40 km asset displays fully and stays responsive. The gizmos match the play road. |
| **M4** | Road editing: knots, bank, grade, spans, sections, undo | R4.2–R4.5, D9 | A reshaped curve and bank are saved and ridden by the physics ship. |
| **M5** | Placement catalog, placement editing, palette, custom prefabs, validation, reroll | D8, D13, R5 | Hand-placed items and custom prefabs play and stream in a baked track. |
| **M6** | Top-down minimap | D3, D11, R7 | The whole track is visible from the first frame, and the dots follow the real positions. |
| **M7** | Docs and hygiene: `runner-track.md`, `runner-hud-screens.md`, `campaign.md`, the `CLAUDE.md` repo map, a sandbox entry, the validator accept list | n/a | The rules files describe the new flow, and the validators are clean. |

M0 can ship on its own. M1 is the risky one; every later milestone depends on it.

**M1 as built (differences from the plan above):**
- The builder and the streamer are two passes inside `TrackGenerator` (`Decide`, `DecidePlacementsUpTo`, `BuildUpTo`, `Build`), not two new components. That keeps every prefab and scene reference as it is. M2's asset loader will fill the same records.
- **R6.3 (a Contracts interface for the streamer) is deferred.** The collider builder, patrol, hyperspace and GameManager still read `TrackGenerator`. Its serialized reference can't be an interface, and M2 loads assets inside the generator too, so there is nothing to separate yet.
- The data model so far is `TrackPlacement` records (kind, distance, lateral, height, variant, data). Knots, spans and sections still live in `TrackManager`; M2 serializes them next to the records.
- Hyperspace now cuts the decided road back (`CutBackForEnd`, `TrackManager.TruncateKnots`) and lays a new end, since there is no unbuilt road left to redirect.
- `TrackGuide`'s no-hint search now looks where it last found the ship, falling back to a whole-track scan, instead of the newest stretch (which is now the finish line).
- Debug edits to spawn spacing or tier chances apply on the next generate, not mid-run.

**M2 as built:**
- `TrackLayout` saves the knots as they were handed in (not as AutoSmooth left them), so a load is exact. Loops and tubes are saved as replayable records (the random state they were rolled from).
- Inspector buttons: Generate Track, Save Track As…, Set as Current Track, Clear Current Track, Preview Saved Track, and the old endless preview.
- FORCE RUNTIME TRACK is a 0/1 debug slider (the menu has no toggle row type).
- Laser gates are now decided in edit mode (built in play only), so saved tracks include them.
- Edit-mode previews are flagged never to be saved into the scene, and the buttons don't dirty the scene.
- Verified: the saved track loads identically; an editor bake equals runtime generation on the same seed; hyperspace works on a saved track and leaves the asset untouched; a restart reloads it; the countdown comes from the asset.

**M3 as built:**
- The Scene-view drawing reads the live track, not the asset directly: it shows whatever the generator holds (a Generate Track bake, a Preview Saved Track, or the run in play). That covers "preview the edited asset", and M4's handles will edit the same live track.
- Drawing is a `[DrawGizmo]` (no EditorTool yet; M4 brings the tool for handles). Switches live in the generator's inspector and are stored per user.
- Cache rebuild: about 40 ms for 70 km, once per track change.

**M4 as built:**
- Edits go into the saved track asset (with Undo), not the scene. `ApplyEdit` reloads the asset and recomputes what moved: knot distances, length, run-up, end ramps. Every other placement keeps its distance and slides with the road.
- Knots: select, move (up/down = grade), bank (disc and slider, with the road's actual bank shown next to it), insert after, delete. Spans: drag the ends, or add/remove them on the selected segment.
- **Deferred:**
  - R4.4, editing a loop's or tube's rolled parameters (none in the shipped shape).
  - Tangent handles on feature knots.
  - Knot editing on tracks with loops or tubes (locked: moving road before a section would shift it).
- The final run-up and the first knot are locked (D9).
- Added after the first check:
  - Live drag: the reshaped stretch rebuilds while dragging.
  - Explicit saving: snapshot on Edit Track, Save / Revert, and a Save / Discard / Keep Editing prompt on Stop Editing.
  - Undo verified, including redo and discard.
  - Save Track As… copies a saved or edited track.

**Milestone gate (applies to every milestone, M0–M7):**
1. When a milestone's code is done, work stops. Nothing is committed yet.
2. The implementer passes the checks in §12, then hands over a short test script: what changed, and what to try in play mode.
3. A human checks the milestone in the Unity Editor, **in play mode**, against its "Done when" line.
4. Only after the human explicitly authorizes it are the milestone's changes committed, as one commit per milestone.
5. Work on the next milestone starts only after that commit.

If the human check fails, the fixes go into the same milestone, the milestone is checked again, and it still is not committed until it is authorized.

## 9. Affected code

- `Runner/Track/TrackGenerator.cs`: split into builder + streamer
- `Runner/Track/TrackManager.cs`: bulk knot load; the pose API is reused unchanged
- `Runner/Track/Spawning/TrackSpawnContext.cs`, `TrackSpawner.cs`, `SpeedOrbSpawner.cs`, `RepairOrbSpawner.cs`, `LaserGateSpawner.cs`
- `Runner/Track/Features/TrackFeatureDefinition.cs`, `LoopDefinition.cs`, `TubeDefinition.cs`, `JumpDefinition.cs`
- `Runner/Track/TrackColliderBuilder.cs`, `TrackDecorator.cs`, `TrackGuide.cs`, `HyperspaceJump.cs`
- `Contracts/TrackContracts.cs`
- `Runner/GameFlow/RunnerLevelDefinition.cs`, `GameManager.cs`
- `Runner/Editor/TrackGeneratorEditor.cs`, and the new `Runner/Editor/TrackLayoutEditor.cs`
- `Runner/HUD/ChaseMinimap.cs`, `ChaseMinimapSettings.cs`, and the new `TrackMinimapGraphic.cs`

## 10. Risks

- **The M1 refactor touches the hottest code in the runner.** It lands behind the runtime path first and is checked for identical behaviour before any asset work starts.
- **Play-only inputs:** loop reachability uses the live speed, and ramp exclusion uses the Store's jump strength. A bake has to assume values for both. Loops are at 0 % in the shipped shape, so the impact is low today. The ramp exclusion should assume the strongest upgrade so landings never clip a laser.
- **Laser gates are play-only today.** Their decision pass has to work in edit mode.
- **Nested-prefab scene edits.** The preview writes to `PF_Track` inside `PF_Env`. Edits must go to the asset, never as prefab overrides.

## 11. Open questions

None at the moment. The four earlier questions were answered on 2026-09-30:
- OQ1 → **D10**: per-track countdown
- OQ2 → **D11**: no orbs on the minimap
- OQ3 → **D12**: debug row to force runtime generation
- OQ4 → **D13**: no brake pads; a data-driven catalog for future power-ups

## 12. Verification

These automated checks come before the human play-mode check and the authorization to commit (the milestone gate in §8). They never replace them.

In the Unity Editor via Unity MCP, at every milestone:
- Compile clean (`Unity_GetConsoleLogs`).
- Play `FiniteRunner_Test` through a ramp win, a hyperspace win, a fall / clock / catch loss, and a retry.
- Run **Tools → Refactor → Validate System Independence** and **Run Sandbox Smoke Test** (`Temp/SandboxSmoke.txt`).

Per milestone:
- **Determinism (M0/M2):** diff the knot and placement dumps across two plays.
- **Visuals (M3–M6):** `Unity_SceneView_CaptureMultiAngleSceneView` and `Unity_Camera_Capture` to compare the gizmos with the road and the minimap with the track.
- **Performance:** profile a baked 40 km track against today's streamed one.
