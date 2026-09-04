---
description: City performance — static flags, occlusion bake, CityStreamer block activity culling and its invariants
paths:
  - "**/CityStreamer.cs"
  - "**/CityStaticFlags.cs"
  - "**/CityOcclusionBaker.cs"
  - "**/CityRoot.cs"
  - "**/CityBlock.cs"
  - "**/CityBounds.cs"
  - "**/CityBaker.cs"
---

# City performance

The answer to "64 blocks loaded all the time". Three independent layers.

## Static flags are a bake product

`CityStaticFlags` (editor). `CityBaker.BakeInto` flags every block it builds when
`CityGenerationSettings.staticFlags` (Performance group) is on:

- `Roads` / `Buildings` / `Shoreline` / `Nature` / `SeaFloor` subtrees → Occluder + Occludee
- `WaterSurface` → Occludee-only (it is transparent)

**The recursion stops at the first `Rigidbody`**, so decoration props, barrels and nature props
stay dynamic — a flagged mover is culled against its bake-time position.

Only objects owning a `Renderer` take flags, and only when they differ, so the pass is idempotent.
`Tools → Police Escape → Apply City Static Flags` flags an existing prefab in place without a
rebake.

**`staticBatching` is a separate knob, OFF by default.** The SRP batcher already batches; static
batching duplicates ~23k instances' vertex data and fights the culling.

## Occlusion culling

`Tools → Police Escape → Bake Occlusion Culling` (`CityOcclusionBaker`) runs
`StaticOcclusionCulling.Compute()` with kit-sized parameters (smallest occluder 8 m, hole 1.5 m,
backface 100) on the **saved, city-bearing scene** with every block active (edit mode).

**Occlusion data is a scene artifact keyed to bake-time renderers — re-run it after every city
bake or block rebuild.**

## `CityStreamer` — block activity culling

Runtime, added to the city root by `CityRoot.Awake` when `streamBlocks` is on. It is activity
culling **inside the single baked prefab**, not loading.

Every `Block_x_y` object stays alive: `RebuildGraph` / `StraightSpawns` / `CityMapModel` use
active-only `GetComponentsInChildren`, and the ground slab, sea floor and splash trigger colliders
hang off it. Only its baked **content roots** toggle (`CityBlock.StreamedRoots`, recorded by
`CityBlockBuilder` at bake; pre-streamer prefabs fall back to "every direct child except the
splash zone").

Membership is the **torus rectangle distance** from the player to each block — the diagonal
neighbour joins near a corner, and the pacman seam is one more route — with `streamEnterDistance`
/ `streamExitDistance` hysteresis on `CityRoot` (500 / 650 m against 886 m blocks → 4–6 blocks
loaded). Ticked on `CityRoot.Update`'s 1 s cadence right after `CityBounds`.

Toggles are **time-sliced** `activationsPerFrame` roots per frame, activations nearest-first,
because a `Buildings` root drops dozens of non-convex mesh colliders into PhysX at once.

Disabling the component restores every block immediately.

### Invariants (warned once on the first tick)

- `streamEnterDistance` ≥ the **police reach** (`max(despawnDistance, spawn max + 50)`) and the
  **traffic reach** (`activeRadius + despawnPadding`) — or NPCs drive on switched-off ramp/deck
  colliders. Spawns are already restricted to `CityBounds`' allowed set ⊂ the ring.
- `streamEnterDistance` ≥ `DistanceFogSettings.fogEnd` — or blocks pop in ahead of the fog.

`IsCellClear` reads "clear" in an unloaded block — only the debug Create Car button can land
there.
