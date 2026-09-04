---
description: City vehicles — physics backends (built-in vs EVP), car models, brake lights, body damage, traffic identity, air-time slow-mo, scene system placement
paths:
  - "Assets/01.Scripts/PoliceEscape/Vehicles/**"
  - "Assets/01.Scripts/PoliceEscape/Editor/**"
  - "Assets/01.Scripts/PoliceEscape/AI/**"
  - "**/CarController.cs"
  - "**/CarConfig.cs"
  - "**/CarHealth.cs"
  - "**/CarFactory.cs"
  - "**/CarInput.cs"
  - "**/TrafficManager.cs"
  - "**/PatrolManager.cs"
  - "**/SceneSystemsPlacer.cs"
---

# City vehicles

## Physics backends

Every city car can run on either the built-in WheelCollider sim or **Edy's Vehicle Physics 5**
(`Assets/00.Plugins/EVP5`, its own `EVP5` asmdef so game assemblies can reference it), for feel
comparison.

The switch is `04.Data/Resources/PoliceEscape_VehiclePhysics.asset` (Resources-loaded, defaults to
built-in without it) plus a live **VEHICLE PHYSICS choice row** on the debug menu's Car Drive tab
that converts every car on the road via `VehiclePhysicsSettings.ApplyToLiveCars()` — no reload.

**The seam is `CarController.SetBackend`**, called from its own `Start`, so all three construction
paths (player prefab, police prefab, traffic rigs) are covered with no spawn-site changes.

In EVP mode `EvpCarBackend.Install` adds an `EVP.VehicleController` over the **same rigidbody,
WheelColliders and wheel pivots** — the object is briefly deactivated so EVP's `OnEnable` sees a
filled wheel list, and velocities are carried across by hand. `CarController` stops simulating but
**stays as the car's identity**: `FindPlayerCar`, camera, HUD, AI perception and health all keep
reading it.

The backend bridges the unchanged `ICarInput` drivers into EVP's separate throttle/brake inputs
(the same speed-aware forward/reverse rule as EVP's `VehicleStandardInput`) and re-applies the
`CarConfig` mapping **every physics step**, so the debug sliders stay live. Mass / center-of-mass
drop / steer angle / top speed / suspension map across from the shared sections; EVP-only
quantities (drive/brake force, tire friction, anti-roll, aero downforce, rolling resistance) live
in the config's **"EVP (comparison backend)"** section.

### The reference feel is the EVP demo's L200 pickup

(`00.Plugins/EVP5/Prefabs/L200-Red.prefab`.) The config's EVP defaults are its numbers, and
everything with no config knob — curve shapes, slip limits, brake/handbrake modes, balance, all
four driving aids, the parametric center of mass (0.569 / −0.116, so the debug CENTER OF MASS
slider only moves the built-in sim) and the wheel rig (spring 35000, damper 1500, travel 0.3) — is
the fixed `ApplyL200Baseline` / `ApplySuspension` baseline stamped at install.

**Two deliberate departures from the demo truck:**

- The CoM *height* is the config's own `evpCenterOfMassHeight` knob (default −0.45). The L200's
  −0.116 tips over at this game's cornering speeds — **this is the anti-rollover dial**, with
  `evpAntiRoll` and `evpTireFriction` as the supporting pair.
- The skid-mark material falls back to a Sprites/Default equivalent at runtime when the EVP decal
  shader's URP port fails to compile (`UsableMarksMaterial`). **Pink marks mean an unsupported
  shader**; the fallback multiplies the same vertex-alpha fade.

**The player's car also gets the demo's juice in EVP mode** (`InstallEffects`; AI cars stay
silent): a code-built `VehicleAudio` rig (engine loop over simulated RPM/gears, skid on braking and
hard cornering, wind, drags, one-shot impacts — clips referenced off the settings asset, which
points at the EVP demo audio) and `VehicleTireEffects` skid marks, backed by a once-per-scene
**"EVP Ground Effects"** object whose `GroundMaterialManager` has a single catch-all entry (null
physic material — what every city collider reports) pointing at a `TireMarksRenderer` using EVP's
URP-ported skidmarks decal.

**Uninstalling restores the friction curves EVP zeroed** — the built-in sim only rewrites
`stiffness`, never the slip curve under it. That snapshot/restore is what makes the toggle safe
mid-run.

### Authored EVP cars

A car that already carries an `EVP.VehicleController` **reuses it**: no second controller, no L200
baseline/suspension stamp, `ApplyChassis` leaves mass/CoM alone, and `ApplyLiveConfig` pushes ONLY
`maxSteerAngle` — the AI drivers normalise their steer against `config.maxSteerAngle`, so that one
number must match.

`Uninstall` **parks** it (`enabled = false`) instead of destroying it, and
`CarController.SetBackend(false)` calls `EvpCarBackend.ParkAuthoredController` **before** its
idempotence check, so a fresh instance in built-in mode never runs two sims. Built-in mode then
flattens these cars to the `CarConfig` like everyone else — it is the comparison backend.

## Cyberpunk car models

`PoliceEscape/Editor/CyberpunkCarBuilder.cs` + the runtime `Vehicles/CyberpunkCarKit.cs`.

The city's cars wear the Cyberpunk Megapolis kit (`Cyberpunk_Megapolis/Models/Car`, via the pack's
prefabs so the LODGroup bodies and emissive materials come along): `PlayerCar.prefab` (CityTest)
and `TestCar.prefab` (CarTest) the **Quadron**, `TestPoliceCar.prefab` the **Minivan**, and the
traffic pool the **Taxi FBX** (rigged at spawn, no prefab).

Menu items `Player Car Uses Quadron` / `Test Car Uses Quadron` / `Police Car Uses Minivan` rebuild
**only the visual rig** (`Model` / `Wheels` / `WheelPivots` children) through `LoadPrefabContents`,
so root components keep their identity, scene references stay valid and hand-placed extras (the
PF_ROB driver) survive, re-seated by the roof-height change. The police light bar is the one child
*rebuilt* instead (its boxes are sized off the chassis, via the shared
`CarTestSceneBuilder.AddPoliceLightBar`).

`Traffic Uses Taxi` / `Traffic Uses Kenney Vehicles` swap the `TestTrafficSettings` pool. The taxi
entry carries `modelYaw` −90 and `scaleOverride` 1 — the two per-vehicle knobs
`TrafficVehicleDefinition` gained so a real-metre, +X-facing car can share a pool with the ×1.73
Kenney toys (0 = the pool-wide `modelScale`).

**`Rebuild Vehicle Prefabs` / `Create Car Test Scene` rebuild TestCar and TestPoliceCar from the
Kenney models — re-run the Quadron/Minivan items after them.**

### Kit facts

Held once in `CyberpunkCarKit` and absorbed identically by the editor builder and
`VehicleRigBuilder`'s kit path (taken when a model has four `*Wheel*` children and no Kenney wheel
names):

- Real metres (4.8–5.3 m, scale 1). The Kenney builder stretches to 4.4 m.
- Length along X after the FBX X-mirror, **bonnet at +X on every car in the pack**. `ModelYaw`
  −90 turns it to +Z — flip to +90 if one ever drives tail-first.
- Wheels are already axle-pivoted but carry LOD1/2 children (dropped, and pruned from the
  LODGroup) and are classified **by car-space position**, not by the kit's 01–04 names, which
  differ per model.
- The pack's convex body MeshCollider is stripped — the root BoxCollider is the chassis everything
  reads.
- Their floors are only 0.14–0.21 m off the road with a metre of bonnet past the front axle, so a
  box fitted to the shell met bridge ramps before the wheels (raycasts climb anything). **The body
  is lifted `BodyLift` (0.15 m) over the axles and the chassis box's underside is clamped to the
  axle line, so ramps are always met wheels-first.** Traffic taxis get the same treatment at spawn.

The wheel rig itself is `CarTestSceneBuilder.BuildWheelFromMesh`; the Kenney `BuildModelWheel`
resolves a name and defers to it.

## Brake lights (`Vehicles/BrakeLights.cs`)

Every city car (player, police, traffic) lights its tail lights while braking or reversing by
driving the **HDR emission intensity** of its kit material. The Cyberpunk cars carry one material
each whose emission MAP paints the tail lights (headlights and badge included, so they go dark
together), and the component rewrites `_EmissionColor = chroma × 2^EV`, keeping the map's tint and
swapping the exposure between `CarConfig.brakeLightIdleIntensity` (−10 EV, dark) and
`brakeLightBrakingIntensity` (5 EV) over `brakeLightFadeSeconds`. The three knobs sit in the
config's "Brake lights" group.

`CarController.Start` adds it (`BrakeLights.Ensure`) the way it installs the backend, so all three
construction paths are covered with no spawn-site wiring. It reads `CarController.RearLightsOn` —
throttle against travel, handbrake, reverse throttle or rolling backwards past `ReverseLightSpeed`
(0.5 m/s), **never the coast brake** — written by the built-in drive step and by `EvpCarBackend` in
EVP mode.

Only materials with `_EMISSION` on **and** an emission map qualify, so the code-built police bar
and the Kenney toys are untouched. It instances through `Renderer.materials` (the fleet shares the
assets; per-instance materials keep SRP batching where an MPB would not) and **never destroys
them**: they are the same instances `CarHealth` chars, and the wreck keeps rendering after
CarHealth strips this component — `OnDisable` just drops the lights back to idle.

## Body damage (`Vehicles/CarDeformation.cs`) — EVP backend only

Cosmetic crumpling for every city car (player, police, traffic, authored EVP demo cars included)
through EVP5's `VehicleDamage`. The gameplay damage (`LevelManager.ApplyDamage`, `CarHealth`) is
untouched, and built-in mode shows no dents.

`VehicleDamage` `[RequireComponent]`s the `VehicleController` and reads its `onImpact`, so
`EvpCarBackend.Install` adds a `CarDeformation` **inside the inactive window** — `VehicleDamage`
snapshots its arrays in `OnEnable`, which `AddComponent` fires at once on an active object; the
installer cycles the object inactive itself when called live — and `Uninstall` detaches it before
the controller.

**The wiring is derived, never authored:**

- **meshes** = every active body `MeshFilter` not under a wheel visual (all body LODs — the
  LODGroup culls, it doesn't deactivate)
- **nodes** = the wheel MESHES under the visual pivots (EVP writes the pivots' world pose every
  frame, so only the child can hold a bend — it wobbles like a bent rim)
- **colliders** = none (the root box is the chassis; this also sidesteps EVP's per-impact
  collider-mesh leak)
- `enableRepairKey` always off — R is respawn.

Knobs are the `CarConfig` **"Damage (EVP)"** group (`evpDamage`, `evpDamageWheels`, min speed,
multiplier, radius, max displacement, vertex fracture, wheel bend, repair rate — L200 demo
defaults) pushed by `EvpCarBackend.ApplyDamageConfig` every physics step, authored cars too. The
master toggle installs/detaches live and the wheel toggle re-installs (the node list is sized at
enable), both resetting current dents. The debug menu's **BODY DAMAGE** tab
(`CityDebugMenuFactory.BuildDamageTab`) edits them.

**Two EVP quirks handled in `Detach(keepDents)`:** it never unsubscribes from `onImpact` (the
delegate is removed by hand, or a parked authored controller keeps deforming through a dead
component), and its `OnDisable` restores the meshes — right for the backend toggle, wrong for a
kill. So `CarHealth.BecomeWreck` calls `Detach(keepDents: true)` **first** (arrays emptied before
the destroy, and queued ahead of the controller it requires — the drivers' ordering rule).

**`CarDeformation` also owns the mesh copies.** `VehicleDamage` deforms `MeshFilter.mesh` (a
per-car copy Unity never frees with the GameObject), so a backend toggle puts the shared asset back
and destroys the copy, a despawn destroys the copies in `OnDestroy`, and `Detach(keepDents: true)`
returns them for `CarHealth` to destroy with the wreck.

Radius/displacement are in the mesh's local units (Kenney toys ×1.73), and `RecalculateNormals`
flattens the kit's smoothing on a dented mesh — the demo's artifact.

**Read/Write is required on the car FBXs for builds** (the editor reads any mesh): the Cyberpunk
car models and `02.Art/01.Models/InfiniteCity/Vehicles` are flipped to `isReadable: 1`. An
unreadable mesh is skipped with a warning in a build instead of throwing inside EVP.

`CarRespawner.Respawn` calls `Repair()` (progressive); a level reboot reloads the scene.

## EVP traffic vehicles and vehicle identity

`Editor/EvpTrafficCarBuilder.cs`, `Vehicles/VehicleIdentity.cs`.

The seven EVP5 demo cars (`00.Plugins/EVP5/Prefabs/Vehicle/` — Bus-Green, L200-Blue/Green/Red,
Sport Coupe-Blue/-Red, Sport Coupe Drift-Blue; `Vehicle Original/` is the untouched backup) drive
as civilians **on their own authored physics**.

**Tools → Police Escape → Build EVP Traffic Prefabs** converts them IN PLACE (idempotent):

- Strips the demo input (`VehicleStandardInput` / `VehicleRandomInput` would overwrite the AI's
  inputs every physics step), audio, damage/tire/visual add-ons, the `DriverFrontPivot` ragdoll (a
  second rigidbody) and the body mesh colliders.
- Adds the root chassis `BoxCollider` (body bounds minus wheels/calipers, underside clamped to the
  axle line — the `SwapModel` rule), a `CarController` wired to the EVP wheel colliders and wheel
  transforms (**classified by car-space position, never by array order**) and a `TrafficCarInput`,
  and stamps the identity.
- The `VehicleController` and its wheel list stay.

**Traffic Adds EVP Vehicles** appends them to `TestTrafficSettings.asset` (existing entries stay;
bus weight 0.4) and names any model entry still without an identity.

`TrafficVehicleDefinition` has **two sources** — `prefab` (a finished NPC prefab, instantiated at
the spawn pose like the police car: `TrafficManager` requires `CarController` + `TrafficCarInput`
on its root and renames it `<prefab>-npc`) or `model` (rigged by `VehicleRigBuilder`) — plus
`kind` / `paint` / `color`.

**Identity**: `VehicleIdentity { VehicleKind kind; VehiclePaint paint; Color color }` lives on
`CarController.identity` (Odin "Identity" group; `Kind` / `Paint` / `PaintColor` getters, `IsSet`
= kind ≠ Unknown), surfaced as `Identity` on `TrafficCarInput` and `PoliceCarInput` and appended to
the `CarAiVisualizer` label. Prefab cars author it; `TrafficManager` stamps a model entry's
identity at spawn and overrides a prefab's only when the entry's `kind` is set.
`CarTestSceneBuilder.TryIdentifyModel` maps pool model names (Kenney toys, `CP_Taxi` /
`CP_Quadron` / `CP_Minivan`) to kinds — used by the Kenney fill, `Traffic Uses Taxi` and
`SwapModel` (police → Minivan). **Only the taxis get a named paint; the rest stay `Unknown` rather
than a guess.**

The editor asmdef references `EVP5`.

## Air-time slow-mo (`Vehicles/AirTimeSlowMo.cs`, player only)

Once every wheel of the PLAYER's car has been off the ground for `CarConfig.airSlowMoDelay`
(0.5 s) the world clock drops to `airSlowMoScale` (0.35):

- The right stick / arrow keys **pitch** (forward = nose down) and **roll** the airborne car by
  steering the rigidbody's local angular velocity toward `airControlRate` (**SIM °/s — what the
  player sees is rate × scale, so the landing never inherits a hidden spin**). Yaw is left to the
  physics; a neutral stick applies nothing.
- The left stick Y / W-S slide the clock inside the `airSlowMoMinScale`–`airSlowMoMaxScale` band
  (released = the default).
- Landing blends back to 1 over `airSlowMoBlendOut`.

`CarController.Start` adds it the way it adds `BrakeLights`, but **only when the driver is a
`CarInput`** (the project's player marker), so a cruiser off a ramp never slows the game.

**The clock has other owners** — every menu writes `timeScale` 0 on open and exactly 1 on close and
none touch `fixedDeltaTime` — so the component follows one rule: **it only enters when the clock
reads exactly 1**, remembers the value it last wrote, and cancels silently (restoring only the
fixed step) the moment the clock reads anything else, never writing again until it re-enters. A
pause mid-jump freezes cleanly and slow-mo re-arms after the resume if the car is still flying.

`fixedDeltaTime` is scaled with the clock (smooth physics at 0.35) and restored on every exit,
including `OnDisable` / destruction. `OrbitCameraRig.ReadPan` returns nothing from the stick and
arrows while `AirTimeSlowMo.IsActive` (the mouse keeps panning).

Knobs live in `CarConfig`'s "Air time (player)" group (toggle, delay, scale, min/max, blend
in/out, control rate/response) and on the debug menu's **AIR TIME** tab (a `MenuToggle` row via
`AddCarToggle` plus sliders — all live).

Teleports need no hook: a respawn lands within a fraction of a second, and a pacman wrap keeps the
car airborne so the slow-mo correctly continues.

## Scene-lifetime systems are hand-placed (`PoliceEscape/Editor/SceneSystemsPlacer.cs`)

The city chase's managers and overlays that live as long as the scene sit in the scene **before
play** under a `===SYSTEMS===` header, wired from the `CityManager`'s fields: `PatrolManager`,
`TrafficManager`, `Minimap`, `Speedometer`, `CityMap`, `SpeedMotionBlur`, `OrbitCameraRig` + its
`FirstPersonCamera` sibling, `CinemaSystem`, `Radio` (`RadioSystem`), `StatsRecorder`,
`CollectibleManager` + `MoneyHud`, `EventSystem`.

**Everything spawned at play is parented under a header too** (`SceneHierarchy`, runtime):
`===SYSTEMS===` for the mission brief and the EVP ground effects, `===PLAYER===` for the car,
`===NPC===` → `==Police==` / `==TrafficNPC==` for the fleets. Find-or-create, and **every header is
forced back to the origin on each fetch**, because the spawners set world poses and then parent —
a nudged header would offset every spawn, wrap and graph lookup under it.

Both scene builders run the placer, and **Tools → Police Escape → Place Scene Systems** adds
whatever an open scene is missing (idempotent, creates all the headers when absent).

`CityManager.Awake` keeps its find-or-create fallback for old scenes and counts an *inactive*
placed system as present — **disabling one is how it is switched off**.

`OrbitCameraRig.Build` adopts a pre-placed sibling named `OrbitCameraRig.FirstPersonName` (adding
the Cinemachine components it lacks) and only destroys a first-person object it created itself.

Per-run objects stay runtime-spawned on purpose (but land under the headers above): the player car
and NPCs (spawn cells come from the live road graph), `MissionBriefScreen` (a modal that destroys
itself on accept) and `EVP Ground Effects` (backend-conditional; its `TireMarksRenderer` builds its
mesh in `OnEnable`, so an edit-mode copy would bake runtime state into the scene).
