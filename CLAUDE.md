# CLAUDE.md

Guidance for Claude Code when working in this repository.

Detailed architecture lives in `.claude/rules/` — those files are **path-scoped** and load
automatically when you touch matching files. Do not paste their content back into this file;
keep this one short.

## Project

Unity 6 (**6000.7.0a3**, URP 17.7) — *SpaceFiniteRunner*. Two games share one project and one
set of systems:

- **Runner** — a hover-ship police-chase endless runner on a spline track.
- **City chase** (PoliceEscape) — a car chase through a procedurally baked cyberpunk city.

A **mission** is a city level plus the escape run after it. The city hands off additively to the
runner; the runner's Mission Complete panel pays the whole mission in full on every completion and
latches it complete. Missions are authored in a **campaign catalog** (worlds → missions, each a
city level + a runner level); the Store's START MISSION always plays the first uncompleted one, and
the main menu's MISSIONS map replays cleared ones. See `campaign.md`.

There is no CLI build or test tooling — all iteration happens through the Unity Editor.
Test scenes: `FiniteRunner_Test` (runner), `CarTest` / `CityTest` (city), `MainMenu`, `Store`.

## Runner game design

- **Objective**: reach Light Speed, then escape off the end of the track, before time runs out
  or you get caught.
- **The track is finite**: a fixed length per level (`RunnerLevelDefinition.trackLengthMeters`,
  0 = `GameSettings.trackLengthMeters`), ending in a straight walled run-up and **three ramps
  side by side over a void**, with gaps between them. The HUD shows the distance left.
- **Win** = BOTH halves: every mandatory objective of the run's `RunnerLevelDefinition` is met
  (today one Reach Speed objective, whose target IS the HUD's "Light Speed" — reaching it ONCE
  latches it, the HUD line turns done) AND the ship leaves the track by one of the end ramps.
  Until the lip everything stays live: countdown, patrol. A standstill
  is never a loss. At the lip the win latches, the
  ship flies on off the ramp (it never lands), MISSION ACCOMPLISHED slams in, the glitch ramps
  to max, then the Mission Complete panel opens.
- **Lose**: the countdown hits 0, the patrol catches you, or it reaches the end of the track without the win — an objective still open (ramp or not), or
  through a gap between the ramps — and drops into the void — or the **hull reaches 0 and the ship
  explodes**. EVERY loss slams a MISSION FAILED banner in (the win banner's animation), then the
  `GameOverScreen` retry panel: MISSION FAILED, the localized reason, RETRY? YES / NO (NO = main
  menu).
- **Hull and lives** (`GameSettings.hullEnabled`): the HUD's life bar sits under the speed wedge
  with a ×N lives count at its right end. Hard wall hits (a dash slam, a ramp's side),
  plain wall contact, laser beams and falling off the track take hull points; every hit blinks the ship
  invulnerable for a moment. **Repair orbs** (a green cross in a translucent red sphere, as
  frequent as green boost orbs) give back 15 % of the hull, never past the max; they are always
  collected, full hull or not, and the patrol never takes them.
  **Every failed run costs a life** — a fresh set (`startingLives`) each time the runner is
  entered, kept across retries. The run that takes the last one is **GAME OVER**: the banner says
  so, the panel has no retry (PRESS ANY BUTTON → the Store), and the mission is forfeited — the
  wallet goes back to what it was when the mission started and the city clear is dropped, so
  START MISSION replays the city. Cleared missions and bought upgrades are kept.
- Neither ending speaks an RPG line and neither prints HUD result text.
- **Speed** is the whole game: one launch impulse, then the throttle (W / RT) holds the ship up
  to its cruise speed and the brake (S / LT) slows it. Only boost orbs (small, 0.3, must be aimed
  for; green 1× / blue 2.5× / purple 10×) push past cruise, where a passive bleed pulls the speed
  back down to it; laser beams take 10 % of it. No cap. (Brake pads are retired: the
  `Spawner_BrakePads` asset is kept out of the track's spawn set — see `runner-track.md`.)
- **The patrol** drives the same physics as the ship: it rubber-bands to the ship's speed and
  takes a share (`boostShare`) of every boost the ship collects, steers for the ship, goes after
  boost orbs of its own (which it uses up), rounds ramps or jumps them, brakes for flat sweeps,
  and can fall off — a fall just drops a fresh one in behind you, except at the END of the
  track, which takes every patrol that reaches it for good. Outrun it far enough and a fresh one
  cuts in behind you at a new, higher floor — coasting can never shake it.
- **The patrol HUNTS you** (`PatrolDuelPRD.md`). It holds a standoff behind you, then on a cadence
  commits to an **attack run**: an overdrive burst onto one of your flanks, chosen so YOU are between
  it and an open edge, while the chase camera dollies in. Alongside, **it takes the ship's controls**
  (stick, throttle, brake and dash all locked for the exchange) and a **tug of war** opens — a bar it
  pushes and you mash back, sparks grinding between the hulls, your ship walked toward the edge as
  the bar goes its way — and losing it is a shove into the wall or off the road. Winning it makes the
  cruiser peel out a little and opens a **kill prompt** on the shoulder it is on: ONE PRESS of that
  dash shoulder destroys it (no dash, no meter cost); the wrong shoulder or no press is a miss, and
  it **brakes hard and falls away** before coming back. It never commits on a ramp, a loop, a tube or
  the final run-up, and out-steering the flank before the lock aborts the run for free. **Brake with
  it in its standoff and it overshoots**: it swerves to a flank, sails past on held speed, then drops
  back — while ahead it is an obstacle you can **ram from behind**, costing you speed and weakening
  its next push. A **laser beam destroys it**, which is the one hazard you can aim it at. A blue or
  purple orb leaves you **armed**: the next exchange skips the bar and goes straight to the kill.
  Each kill or outrun **escalates** the next cruiser. The old proximity arrest is gone: only sitting
  on your tail WITHOUT committing, for a long fuse, still arrests you.
- **Curves**: banked sweeps always hold the ship. A share of sweeps is authored FLAT, with no
  wall on the outer edge: taken too fast the ship loses grip and slides outward — brake first.
  Some straight runs have no walls at all: drift or dash too close to the side and the ship drops.
  Over the edge it falls, then comes back further down the track at 85% of the speed it fell
  with, blinking and untouchable for 3 s with the patrol frozen (`respawnRollingStart`: ON flies
  that window under control, OFF stands still through it and relaunches after), and the
  countdown never stopped — falling costs time, not the run.
- **Track features**: ramps/jumps (1), vertical loops (2), cylinder sections (3). Multi-path is
  the one feature not yet built.
- **Laser gates**: emitter pairs firing a beam across 20–30 % of the road — single horizontal,
  single vertical, three stacked, or a flat spinning rotor — steered round, never jumped. A beam
  costs a fall's worth of hull and 10 % of the speed at once (`laserSpeedLoss`) with a heavy rumble. Never on or near a ramp, its landing, a loop,
  a tube or the final run-up.
- Time AND distance are the limits. The track is streamed ahead of the ship but finite.
- Story beats are RPG dialogue lines on purple-orb pickups and patrol taunts only.

## Repo map

All game code is in **`Assets/01.Scripts/`**, namespace root `ConfusedGameDev.FiniteRunner`,
split into asmdefs: `Runner`, `PoliceEscape`, `Ship`, `UI`, `FX`, `Cheats`, `Debugging`, `Haptics`,
`Rendering`, `Cameras`, `SaveData`, `Campaign`, plus `Runner/Editor`, `Ship/Editor` and
`PoliceEscape/Editor`. `Ship` (`Assets/01.Scripts/Ship/`) is the standalone hover ship — a prefab
that flies any collider surface, with an optional guide spline; `Runner` references it and puts the
runner's rules on top. See `ship-standalone.md`.

| Path | Holds |
|---|---|
| `Assets/01.Scripts/` | all game code |
| `Assets/02.Art/` | models, materials, shaders, particles |
| `Assets/03.Prefabs/PoliceEscape/` | city + vehicle prefabs, `City.prefab` |
| `Assets/04.Data/` | ScriptableObjects; city assets under `InfiniteCity/`, Resources-loaded ones under `Resources/` |
| `Assets/05.Scenes/` | `CarTest`, `CityTest`, `FiniteRunner_Test`, `MainMenu`, `Store` |
| `Assets/07.Audio/` | music, UI and SFX clips |
| `Assets/00.Plugins/EVP5/` | Edy's Vehicle Physics 5 (own `EVP5` asmdef) |
| `Assets/Plugins/Sirenix/` | Odin Inspector |

**Assembly direction**: `UI` is the lowest assembly every game assembly references.
`PoliceEscape` references `Runner`, **never the reverse** — that is why shared screens and
pickups live under `Runner/`. `Cheats` references `UI`, never the reverse. `SaveData` and
`Campaign` (which references only `SaveData`) sit below everything and are the only type-sharing
seams between the two games.

Everything else in `Assets/` is inherited from Unity's URP 3D Sample template and should not be
modified: `Assets/Scenes/` (Cockpit, Garden, Oasis, Terminal) and `Assets/SharedAssets/`.

## Project-wide invariants

These hold everywhere. Break one and something else quietly stops working.

- **Never mutate a ScriptableObject asset at runtime.** Gameplay takes a runtime *clone*
  (`ShipDefinition`, `PatrolDefinition`, feature definitions); the debug menu edits the clone.
  The exceptions are deliberate and documented per system (the city's settings assets and
  `LevelDefinition` are read live, so their debug pages edit the assets themselves).
- **Tunables live in ScriptableObjects** (the runner's in `FiniteRunner/Data/` — `GameSettings`,
  `ShipDefinition`, `PadDefinition`, `CameraShakeSettings`), not as fields on managers. Add new
  knobs to the settings asset, not to the component that reads it.
- **Scene-lifetime systems are hand-placed**, under `===SYSTEMS===`, so they are tunable before
  play; code only ever find-or-parks them. Per-run objects are runtime-spawned, under runtime
  headers (`===PLAYER===`, `===NPC===`) that are forced back to the origin on every fetch.
  Every scene-root header sits at the origin; the runner scene is `===SYSTEMS===`, `===PLAYER===`,
  `===ENV===`, `===CAMERAS===`, `===UI===`, `===LIGHTING===` (full-screen filters under its
  `Filters`). Each header and each object directly under it is a nested `PF_` prefab in
  `03.Prefabs/FiniteRunner/`; the instances keep their scene names, since headers and the rig's
  sibling cameras are found by name.
- **Auto-created singletons** (`FloatingTextSystem`, `RpgMessageSystem`, `HapticsSystem`,
  `CheatManager`, `DebugManager`) follow one rule: a hand-placed instance always wins, because
  that is the copy carrying someone's inspector wiring.
- **A shared material written by a driver is restored on disable** (`_Intensity` zeroed), and
  only the last instance standing cleans it — the additive city→runner handoff has two drivers
  alive at once. Every full-screen feature also self-gates on that `_Intensity`.
- **Domain reload is off.** Static state, cached profiles and event subscriptions survive play
  sessions: subscribe in `OnEnable`/`OnDisable`, never in a static initializer, and re-`Boot()`
  anything cached.
- **Every scene trip goes through `LoadingScreen`** — except the city→runner completion
  handoff, which is its own additive transition.
- Speeds are stored in **m/s**; UI converts with `* 3.6f`.
- **The ship is a kinematic, cast-based surface follower, never a dynamic rigidbody.** At Light
  Speed it covers ~36 m per physics step, so every tick is split into ≤ 4 m substeps, the world is
  read through PhysX QUERIES (sphere casts, probe rays, box casts along the substep path), and
  nothing is ever detected by a moving trigger volume. The runner's track is colliders streamed
  from the same pose function the old track-space ship rode (`TrackColliderBuilder`) plus a guide
  (`TrackGuide`); `ShipMotor` in physics mode mirrors the ship back into track coordinates for
  everything that reads them. Laser gates stay analytic (no colliders).
- **Distance from the track start is the authoritative coordinate**, not spline `t`.

## Conventions

- **C# parameters are lowerCamelCase** (`void Foo(int myParam)`).
- Uses the **new Input System** (`UnityEngine.InputSystem`) — never the legacy `Input` class.
- Gameplay reads input through the `ControlBindings` table, never the devices directly; menus
  poll devices directly and their chords are not bindable.
- Uses `Unity.Mathematics` alongside `UnityEngine` math in spline code.
- **Designer-facing inspectors use Odin** (`Sirenix.OdinInspector`, runtime attributes only — no
  serializer swap): every tunable is a `[PropertyRange]` slider with a hand-picked range, paired
  values are single `[MinMaxSlider]` bands (`patrolRedeployBand` =
  drop-in/trigger) unpacked by accessor properties so gameplay never touches `.x`/`.y`, optional
  blocks are `[ToggleGroup]`s, and settings assets are `[InlineEditor]`-ed into the components that
  use them (`GameManager.settings`, `ShipMotor.definition`) so balancing happens without leaving
  the scene. Keep that style.
- **Menu plates auto-fit their texts** across all four languages — never hardcode a plate width.
  A new row type with right-side widgets overrides `ReservedRightWidth` and `SetWidth`.
- All player-facing strings are `MenuTextId` entries in `MenuTextLibrary`, translated in all
  four languages. Stat values are formatted by `StatFormat` and never localized.
- Enums that are serialized are **append-only**.
- Scripts carry XML doc summaries explaining the class's role and the design rule it enforces.
- Editor-only code goes in an `Editor/` subfolder (namespace `FiniteRunner.EditorTools`).
- Fields on anything baked into a prefab must be `[SerializeField]` — a plain private field
  deserializes as zero and silently kills the behaviour.

## Rules index

Loaded automatically by path. Listed here so you know what exists.

| Rule | Covers |
|---|---|
| `runner-track.md` | `TrackManager`, sections, `TrackGenerator` streaming, features, pads/orbs, decorator |
| `runner-ship.md` | `ShipMotor` (physics mode + the legacy track-space sim), `GameManager`, `PolicePatrol`, tuning |
| `ship-standalone.md` | The `Ship` assembly: `HoverBody`, guides, recovery, pickups, prefab rig, the physics runner |
| `runner-hud-screens.md` | `RaceHud`, `GameOverScreen`, `MissionCompleteScreen` |
| `runner-store.md` | Store scene, upgrade definitions, appliers |
| `campaign.md` | Mission catalog, `MissionSession`, frontier/unlock rules, MISSIONS map, Coming Soon, build-settings registrar |
| `city-generation.md` | Offline bake, `CityLayout`, road pieces, `RoadGraph`, features, decoration |
| `city-districts-water.md` | Districts, parks, curved avenues, water, shoreline, building sets |
| `city-performance.md` | Static flags, occlusion bake, `CityStreamer`, bounds and wrap |
| `city-level-flow.md` | `LevelManager`, `LevelDefinition`, objectives, challenges, `ObjectiveHud`, `PlayerSpawnPoint` |
| `city-cinemas.md` | `CinemaSystem`, formats, triggers |
| `vehicles.md` | Physics backends, EVP, car models, brake lights, damage, traffic, air-time |
| `cameras.md` | `OrbitCameraRig`, view modes, look-back, camera shake |
| `fx-rendering.md` | Distance fog + far glitch, speed lines, weather, render pipeline assets |
| `audio.md` | Mixer bus layout, `GameAudio` snapshots, car radio |
| `ui-menus.md` | Menu framework, control bindings, `LoadingScreen`, `PauseMenu`, debug menu |
| `shared-systems.md` | Floating text, RPG messages, haptics, collectibles, money HUD |
| `save-data.md` | `PlayerProfile`, `PlayerStats`, the LOG screen |
| `cheats.md` | Cheat codes and the cheats page |
| `debug-visualizers.md` | `DebugManager`, road graph and AI overlays |

## Known drift

Resolve these against the code when you next touch them:

- The old doc claimed core runner scripts live in `Assets/99.Test/Jorge/FiniteRunner/Scripts/`
  and the test scene at `Assets/99.Test/Jorge/FiniteRunner/Scenes/FiniteRunner_Test.unity`, while
  also stating `Assets/99.Test/` holds no code and scenes live in `Assets/05.Scenes/`. This file
  assumes `Assets/01.Scripts/` and `Assets/05.Scenes/`. Confirm, and correct the rule files if the
  scripts actually sit elsewhere.
- The old doc stated the win condition twice, once as the `RunnerLevelDefinition` objectives and
  once as "speed reaches `lightSpeedKmh`". The objectives version is treated as current here.
