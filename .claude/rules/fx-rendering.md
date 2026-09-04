---
description: FX and rendering — distance fog + far glitch, speed lines, weather/rain/thunder, render pipeline asset and quality levels
paths:
  - "Assets/01.Scripts/FX/**"
  - "Assets/01.Scripts/Rendering/**"
  - "Assets/02.Art/04.Shaders/**"
  - "**/DistanceFog*.cs"
  - "**/SpeedLines*.cs"
  - "**/RainSystem.cs"
  - "**/RainSettings.cs"
  - "**/GlitchController.cs"
  - "**/RendererFeatureAudit.cs"
---

# FX and rendering

## The shared contract every full-screen driver keeps

1. A hand-placed `[ExecuteAlways]` driver object writes its settings asset into the **shared
   material** every frame.
2. `OnDisable` zeroes `_Intensity`, and **only the last instance standing cleans the material** —
   the additive city→runner handoff has two drivers alive at once.
3. The renderer feature **self-gates** on `HasDriver` + that `_Intensity`, so scenes without a
   driver pay nothing, and skips render-texture cameras (the minimap) and preview/reflection
   cameras.

Without the `HasDriver` guard, an edit-mode preview that saved `_Intensity 1` into the shared asset
made a driverless scene render the previous scene's fog unconfigured (the runner showed the city's
purple at 120 m).

## Distance fog + far glitch

`01.Scripts/FX/Atmosphere/` + `Rendering/DistanceFogFeature.cs` +
`02.Art/04.Shaders/InfiniteCity/DistanceFog.shader`.

**ONE depth-based full-screen Render Graph pass at `BeforeRenderingPostProcessing`** — fog is scene
light, so bloom/tonemap must see it, and the `GlitchPost` feature after post corrupts a fogged
picture.

Both effects key on the same radial distance:

- an exp² fog over `fogStart..fogEnd` blending a near→far colour ramp (`skyFogAmount` for the far
  plane, optional height falloff)
- from `glitchStart` the picture tears, drops macroblocks to the far fog colour and splits
  channels — the far city dissolves into signal noise before the haze, which hides
  `CityStreamer`'s pop-in and reads cyberpunk

Displaced samples are **depth-guarded**: a torn row never smears a surface nearer than
`glitchStart`. The feature requests depth via `ConfigureInput` (the pipeline asset keeps depth
off).

The scene side is a hand-placed `[ExecuteAlways]` **`DistanceFog`** object (created by
`CarTestSceneBuilder`; `preview` shows it in the Scene view) writing `DistanceFogSettings`
(`04.Data/Resources/FiniteRunner_DistanceFog.asset`, `Load()` falls back to an in-memory default).
`SetIntensity(0..1)` is gameplay's ramp.

**The far-clip clamp lives on the Cinemachine lens** — `OrbitCameraRig` reads
`DistanceFog.Instance.FarClipPlane` (= `fogEnd + farClipMargin`) and restores the authored default
when the fog is off — because the brain pushes lens clip planes onto the camera every frame, so a
`Camera.main` write would be overwritten.

`RainSystem.atmosphere`'s legacy `RenderSettings.fog` is independent and stacks.

`Tools → Police Escape → Install Distance Fog Feature` (`DistanceFogInstaller`) creates the
material + settings asset (never overwriting) and **inserts** the feature before the `GlitchPost`
full-screen feature on every renderer asset. `DistanceFogDebugPage` (nine slider rows, `MenuTextId`
`Fog*` / `FarGlitch*`) is added by `PauseMenu` wherever a `DistanceFog` exists, editing the asset
directly and flushing at the menu's commit points.

## Speed lines

`01.Scripts/FX/SpeedLines/` + `Rendering/SpeedLinesFeature.cs` +
`02.Art/04.Shaders/FiniteRunner/SpeedLines.shader`.

Manga 集中線 over the picture as the ship nears Light Speed: hard-edged white wedges from the
screen edges pointing at the ship, tips inward, the middle clear, re-randomised `flickerRate` times
a second (quantised `_Time`, so pause freezes them). Procedural in the shader — a coarse and a fine
layer; density, width and the clear radius scale with intensity, **the line counts stay fixed so
cells never drift between frames**.

ONE Render Graph raster pass at `AfterRenderingPostProcessing` that **blends over
`activeColorTexture` with no colour copy and no depth** (`Blend SrcAlpha OneMinusSrcAlpha`, the
source-less `Blitter.BlitTexture(cmd, scaleBias, material, pass)` — Blit.hlsl's `Vert` never reads
`_BlitTexture`), with `requiresIntermediateTexture` kept on so the pass never targets the backbuffer
and `texcoord` is the y-up viewport space the focus is written in.

**Inserted before the `GlitchPost` feature** — same event, so list order is the tie-break
(`DistanceFogInstaller.InsertBeforePostGlitch` is the shared rule) — so the death glitch corrupts
the lines. It draws under the HUD.

The driver is **`SpeedLines`** (in FX — it cannot see `Cameras`, so it takes a focus `Transform`, a
`Func<float>` km/h reader, the reference speed its band is a fraction of, and a camera-mode index
0/1/2 Far/Close/First person):

- intensity = the smoothed `speedBand` fraction of the reference speed (exponential
  `responseSharpness`, like `SpeedMotionBlur`) + a max-wins `Pulse(strength, seconds)`, × the
  asset's per-mode multiplier (first person 1.3) × `SetIntensity`'s gameplay scale
- focus = the ship's smoothed viewport position; the screen centre in first person, or behind the
  camera

`SpeedLinesSettings` (`04.Data/Resources/FiniteRunner_SpeedLines.asset`) is generic — it also
carries the material so a driver created at play time finds it — and is re-read every frame.

**The driver is a hand-placed scene object, never spawned** (the project rule for every
scene-lifetime system — they must be tunable before play). The runner scene carries a `SpeedLines`
root object beside its `DistanceFog`, `RainSystem`, `CollectibleManager` and `MoneyHud` with the
material and asset wired; `SpeedLines.Apply(enabled, settings)` only **finds** it (an error when
missing) and parks it when off.

Runner wiring: `GameSettings` "Speed lines" toggle group (`speedLinesSettings`,
`boostPulseStrength` / `boostPulseSeconds`); `GameManager.Awake` → `SpeedLines.Apply` +
`SetTarget(motor, km/h, lightSpeedKmh)`; `Update` pushes `cameraRig.Mode`; `OnPadImpulse` pulses on
boosts scaled by tier (`rawMagnitude / powerUpSpeedBoost`); `Restart` clears the pulse.

`Tools → FiniteRunner → Install Speed Lines Feature` (`SpeedLinesInstaller`, material at
`02.Materials/FiniteRunner/SpeedLines.mat`, never overwriting) also places and wires the
`SpeedLines` object in the open scene. `SpeedLinesDebugPage` (nine rows, `MenuTextId`
`SpeedLines*`) is a pause-menu tab wherever a driver exists.

## Render pipeline asset and quality levels

The game renders through `Assets/04.Data/URP Asset.asset` → `URP Asset_Renderer.asset`, feature
order **GlitchSilhouette → DistanceFog → SpeedLines → GlitchPost**.

**Both quality levels in `ProjectSettings/QualitySettings.asset` point at that asset explicitly —
keep it that way.** A quality level's Render Pipeline Asset overrides the GraphicsSettings default,
and the URP 3D-sample template's `PC_RPAsset` / `Mobile_RPAsset` GUIDs (`4b83569d…` / `5e6cbd92…`)
the levels used to carry are shared by every asset-store pack built from that template. Importing
Cyberpunk Megapolis revived the dangling PC reference onto the pack's `CP_High.asset` (deferred,
SSAO, decals, its own global volume profile), and the full-screen glitch silently stopped rendering
because that renderer never had the GlitchPost feature — the fog kept working only because its
installer had stamped every renderer in the project.

**Guards:**

- `DistanceFogInstaller` / `GlitchSilhouetteInstaller` only touch renderers under
  `Assets/04.Data/` (`DistanceFogInstaller.IsProjectRendererAsset`).
- `RendererFeatureAudit` (`Rendering/`) warns from `GlitchController.Awake` /
  `DistanceFog.OnEnable` (play mode) when no renderer of the active pipeline asset carries a
  feature driving their material — one warning naming the pipeline asset and quality level.

If a pack's look is wanted (SSAO, a volume profile), **port those features onto
`URP Asset_Renderer`; never point a quality level at a pack's pipeline asset.**

## Weather

`01.Scripts/FX/Weather/`, namespace `…FiniteRunner.FX`.

`RainSystem` builds its particle systems from code and re-applies its `RainSettings` asset every
frame (no runtime clone, so the inline inspector and the debug page tune a live downpour).

**It is a camera-sized volume, not a world storm**: a box of `areaRadius` rides with `Camera.main`,
pushed along the flat view direction by `leadDistance`, simulating in world space so turning the
camera never drags the drops. Because the two games move at wildly different speeds, drops carry a
share of the camera's own motion (`followSpeed`, **horizontal only** — inheriting vertical motion
would cancel the fall) and the stretched-billboard streak is capped in metres (`maxStreakLength`):
world-static rain is right at a car's pace and gone between two frames at the ship's.

### Thunder

Every `strikeInterval ÷ thunderFrequency` — a band, re-rolled at each strike so the storm never
falls into a rhythm, over a single rate dial; **the roll is divided rather than the band's ends**,
so turning the storm up makes strikes closer together rather than more evenly spaced — it washes
the screen white from its own overlay canvas at **sorting order 18**: above the HUD and the story
messages, below the pause menu, because lightning washes the world and its readouts but never a
menu the player is reading.

The envelope is `flashFlickers` sharp pops under one falling curve — a single smooth fade reads as
a camera flash; the stutter is what makes it lightning.

The strike fires **`onThunderStrike`** (a scene-wirable `UnityEvent` on the component, twinned with
a static `RainSystem.ThunderStruck` for listeners that spawn later) on the same frame as the flash,
which is where the thunderclap sound hangs. `RainSystem.Strike()` is public so a story beat can
call for thunder on cue, and is the inspector's Test Strike button. Events never fire out of the
editor preview.

### Other rules

- Ground splashes are a **collision sub-emitter** (a child `ParticleSystem`, as the API requires) —
  that toggle is also the on/off for particle collision, the expensive half.
- `atmosphere` drives the scene's fog and ambient light, captured on enable and **restored on
  disable** — the same contract `GlitchController` keeps with its shared material.
- Drop/splash sprites come off the Kenney particle pack (`02.Art/05.Particles/GeneralParticles`).
  **Drops want a *horizontal* streak** (`Rotated/trace_01_rotated`), because stretching maps the
  sprite's X to the direction of travel. With no texture assigned it generates its own.
- The shipped asset is `04.Data/Resources/FiniteRunner_Rain.asset` and `RainSettings.Load()` falls
  back to an in-memory default, so rain never fails for want of wiring.
- It is **`[ExecuteAlways]` and lives in the scene as a hand-placed `RainSystem` object** (in all
  three scenes, re-created by the scene builders). With `preview` on it hand-steps the simulation
  around the *scene view* camera, so the whole asset is tunable before pressing play.
- Everything it builds is flagged `DontSaveInEditor` and re-adopted rather than duplicated on a
  recompile, so the scene file only ever holds the one component, and **the volume follows the
  camera by moving that child, never the root** — an editor preview must not leave the scene
  permanently dirty.
- `RainSystem.Apply(enabled, override)` is what the scene owners call on boot
  (`GameSettings.rainEnabled` in the runner, `CityManager.rain` in the city): the hand-placed
  object always wins, and switching the weather off *parks* it rather than ignoring it.
  `SetIntensity(0..1)` is gameplay's ramp on top of the asset's `intensity`.
- `RainDebugPage` lives here with the system, not in either game's factory, because both scenes
  spawn the same `RainSystem`. It is added whenever the scene is raining: eight downpour rows,
  where the two min-max bands (fall speed, drop size) collapse to one row each that *slides* the
  band and keeps its spread. It edits the asset directly and flushes at the menu's commit points.
