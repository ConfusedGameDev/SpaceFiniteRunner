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

- **Objective**: escape the police before time runs out or you get caught.
- **Win**: every mandatory objective of the run's `RunnerLevelDefinition` is met (today one
  Reach Speed objective, whose target IS the HUD's "Light Speed"). Ends on the Mission Complete
  panel the frame it is met.
- **Lose**: the countdown hits 0, the patrol catches you, or the ship bleeds to a standstill.
  Ends on the `GameOverScreen` retry panel with a localized reason.
- Neither ending speaks an RPG line and neither prints HUD result text.
- **Speed** is the whole game: one launch impulse, constant passive bleed, no cap. Boost orbs
  (small, 0.3, must be aimed for; green 1× / blue 2.5× / purple 10×) raise it; brake pads
  (large, 1.2, must be dodged) lower it.
- **The patrol** rubber-bands to the ship's speed and takes a 0.7 share of every boost the ship
  collects, so boosts no longer buy the gap. Outrun it far enough and a fresh one cuts in
  behind you at a new, higher floor — coasting can never shake it.
- **Track features**: ramps/jumps (1), vertical loops (2), cylinder sections (3). Multi-path is
  the one feature not yet built.
- Time is the limit, not distance. The track is endless and streamed.
- Story beats are RPG dialogue lines on purple-orb pickups and patrol taunts only.

## Repo map

All game code is in **`Assets/01.Scripts/`**, namespace root `ConfusedGameDev.FiniteRunner`,
split into asmdefs: `Runner`, `PoliceEscape`, `UI`, `FX`, `Cheats`, `Debugging`, `Haptics`,
`Rendering`, `Cameras`, `SaveData`, `Campaign`, plus `Runner/Editor` and `PoliceEscape/Editor`.

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
- The runner is **spline-based, not physics-based**. The ship is kinematic; at Light Speed it
  covers ~36 m per physics step, so detection is analytic, never a moving trigger volume.
- **Distance from the track start is the authoritative coordinate**, not spline `t`.

## Conventions

- **C# parameters are lowerCamelCase** (`void Foo(int myParam)`).
- Uses the **new Input System** (`UnityEngine.InputSystem`) — never the legacy `Input` class.
- Gameplay reads input through the `ControlBindings` table, never the devices directly; menus
  poll devices directly and their chords are not bindable.
- Uses `Unity.Mathematics` alongside `UnityEngine` math in spline code.
- **Designer-facing inspectors use Odin** (`Sirenix.OdinInspector`, runtime attributes only — no
  serializer swap): every tunable is a `[PropertyRange]` slider with a hand-picked range, paired
  values are single `[MinMaxSlider]` bands (`patrolDangerBand` = catch/warn, `patrolRedeployBand` =
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
| `runner-ship.md` | `ShipMotor` sim + jumps/loops/tubes, `GameManager`, `PolicePatrol`, tuning |
| `runner-hud-screens.md` | `RaceHud`, `GameOverScreen`, `MissionCompleteScreen` |
| `runner-store.md` | Store scene, upgrade definitions, appliers |
| `campaign.md` | Mission catalog, `MissionSession`, frontier/unlock rules, MISSIONS map, Coming Soon, build-settings registrar |
| `city-generation.md` | Offline bake, `CityLayout`, road pieces, `RoadGraph`, features, decoration |
| `city-districts-water.md` | Districts, parks, curved avenues, water, shoreline, building sets |
| `city-performance.md` | Static flags, occlusion bake, `CityStreamer`, bounds and wrap |
| `city-level-flow.md` | `LevelManager`, `LevelDefinition`, objectives, challenges, `ObjectiveHud` |
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
