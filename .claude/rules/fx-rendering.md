---
description: FX and rendering — distance fog + far glitch, speed lines, weather/rain/thunder, render pipeline asset and quality levels
paths:
  - "Assets/01.Scripts/FX/**"
  - "Assets/01.Scripts/Rendering/**"
  - "Assets/02.Art/04.Shaders/**"
  - "**/DistanceFog*.cs"
  - "**/SpeedLines*.cs"
  - "**/VhsTape*.cs"
  - "**/PsxLook*.cs"
  - "**/CrtScreen*.cs"
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
0/1/2/3 Far/Close/First person/Cinematic — `GameManager` pushes `SpeedLines.CinematicMode` while
`OrbitCameraRig.Cinematic` holds):

- intensity = the smoothed `speedBand` fraction of the reference speed (exponential
  `responseSharpness`, like `SpeedMotionBlur`) + a max-wins `Pulse(strength, seconds)`, × the
  asset's per-mode multiplier (first person 1.3, **cinematic 0 — the lines are off for the
  side-on shot**) × `SetIntensity`'s gameplay scale
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

## VHS tape

`01.Scripts/FX/Vhs/` + `Rendering/VhsTapeFeature.cs` +
`02.Art/04.Shaders/FiniteRunner/VhsTape.shader`.

The finished picture played back off a worn cassette. Every knob is one real fault of the format,
so the asset reads like a deck, not a filter stack: chroma recorded at a fraction of the luma
bandwidth (`chromaBleed` smears the colour sideways, `chromaLag` trails it behind the luma — the
fringe on every hard edge), a little vertical `lumaSoftness`, per-row-pair timing errors plus a slow
sway of the whole frame (`jitter`), a **tracking band** of torn, streaky, colourless rows crawling
down the frame (`tracking` also widens it; `trackingSpeed` / `trackingHeight`), the **head switch**
skewing and filling the bottom rows (`headSwitch` / `headSwitchHeight`), tape `noise` (grain, sparse
white dropout dashes, brightness flicker), CRT `scanlines` (`scanlineCount`, 480 = NTSC), a
washed-out tone curve (`wash`) and a `vignette`. All colour work happens in **YIQ**, the split the
tape itself makes.

Everything random is keyed on a quantised clock (`floor(_Time.y * frameRate)`, like the far glitch
and the speed lines) so the noise **steps at tape rate** instead of shimmering per frame, and the
pause menu freezes the tape. `intensity` both scales every fault and blends the result over the
clean picture, so a low value is a good dub rather than a ghosted one.

ONE Render Graph pass at `AfterRenderingPostProcessing` that, like the fog, **copies the camera
colour and draws back over the active target** (no depth). The installer **appends it AFTER the
`GlitchPost` feature** (`DistanceFogInstaller.InsertAfterPostGlitch`, the mirror of the
before-glitch rule): the tape is the recording medium, so the death glitch, the fog and the speed
lines are all *on* the tape. It draws under the HUD.

The driver is **`VhsTape`** (FX), a hand-placed `[ExecuteAlways]` object beside `DistanceFog`,
`RainSystem` and `SpeedLines` in every scene, writing `VhsTapeSettings`
(`04.Data/Resources/FiniteRunner_VhsTape.asset`, `Load()` falls back to an in-memory default) into
the shared material each frame under the standard contract (`HasDriver`, last-one-standing zeroes
`_Intensity`, `preview` for the Scene view). Its drive is small on purpose: `SetIntensity(0..1)` is
gameplay's ramp, `TrackingPulse(strength, seconds)` is a max-wins burst on the tracking band for a
hit or a story beat, `ClearPulse()` on restart.

Owner wiring: the runner's `GameSettings` "VHS tape" toggle group (`vhsEnabled`, `vhsSettings`) →
`GameManager.Awake` → `VhsTape.Apply`; the city's `CityManager` "VHS tape" group (`vhs`,
`vhsSettings`) → `VhsTape.Apply` after the rain. `Apply` only **finds** the scene object (an error
when missing) and parks it when off.

`Tools → FiniteRunner → Install VHS Tape Feature` (`VhsTapeInstaller`, material at
`02.Materials/FiniteRunner/VhsTape.mat`, never overwriting) also places and wires the `VhsTape`
object in the open scene — run it once per scene. `VhsTapeDebugPage` (nine rows, `MenuTextId`
`Vhs*`) is a pause-menu tab wherever a driver exists.

## PSX look

`01.Scripts/FX/Psx/` + `Rendering/PsxLookFeature.cs` +
`02.Art/04.Shaders/FiniteRunner/PsxLook.shader`.

The finished picture as a PlayStation-1 console would have put it out. Three faults of that
hardware in ONE pass, all computed per **virtual pixel** ("cell") in *source pixels* of the target
(`_ScreenParams`, never uv — `_BlitTexture_TexelSize` is not filled by `Blitter.BlitTexture`), so the
grid is anchored at pixel (0,0) and cannot drift:

- **Pixelation**: `targetHeight` rows with square cells (`cell = round(res.y / targetHeight)` source
  pixels), nearest sampling. A partial last row/column is overscan, leave it.
- **Wobble**: the PS1 snapped vertices to an integer screen grid and mapped textures affinely. There
  are no vertices in a post pass, so this is a **screen-space stand-in**: the picture is cut into
  `wobbleBlock`-cell blocks keyed on a **half-octave depth band** (`floor(log2(eye) * 2)`), and
  every cell of a (block, band) samples the source with the SAME offset — a whole-pixel jitter
  re-rolled at `jitterRate` (`wobble`, the vertex snap) plus a sub-pixel drift that grows through
  the step and resets at the next (`swim`, the affine crawl). A surface therefore moves as a rigid
  piece: silhouettes dance, interiors hold, which is the PS1 tell; per-pixel hashing would sparkle.
  Seams follow geometry because the band changes where the object does. A **near-only depth guard**
  (the fog's rule) drops the offset when the displaced sample is more than an octave nearer, so a far
  block never smears the ship or the car into itself; the sky is one quad and never moves.
  `wobbleDepthFalloff` calms far surfaces. Without a depth handle the feature writes `_HasDepth 0`
  and the wobble keys on screen blocks alone.
- **Colour**: `colorBits` per channel (5 = the 15-bit framebuffer) quantised in **gamma space** (the
  project renders linear; quantising linear crushes the shadows) under a 4×4 Bayer `dither` computed
  without a table and indexed by the cell — one dot per virtual pixel, locked to the grid.

The clock is quantised (`floor(_Time.y * jitterRate)`) like the far glitch, the speed lines and the
tape, so the pause menu freezes the jitter. `intensity` scales the wobble, the swim and the dither
and blends the result over the clean picture.

ONE Render Graph pass at `AfterRenderingPostProcessing` that copies the camera colour and draws
back over the active target, **and reads depth** (`ConfigureInput(Depth)`; URP's copied
`_CameraDepthTexture` is still bound after post — the copy is scheduled after transparents and
nothing rewrites it before the after-post custom passes). The installer inserts it **right after
`GlitchPost` and ahead of `VhsTape`** (`DistanceFogInstaller.InsertAfterPostGlitch`, which also
keeps a PsxLook feature ahead of anything else inserted "after the glitch", so the two installers
converge in either order): the console shows the death glitch, the tape records the console. It
draws under the HUD (every canvas is Screen Space Overlay).

The driver is **`PsxLook`** (FX), a hand-placed `[ExecuteAlways]` object beside `DistanceFog`,
`RainSystem`, `SpeedLines` and `VhsTape` in every scene, writing `PsxLookSettings`
(`04.Data/Resources/FiniteRunner_PsxLook.asset`, `Load()` falls back to an in-memory default) into
the shared material each frame under the standard contract (`HasDriver`, last-one-standing zeroes
`_Intensity`, `preview` for the Scene view). Its only drive is `SetIntensity(0..1)`.

Owner wiring: the runner's `GameSettings` "PSX look" toggle group (`psxEnabled`, `psxSettings`) →
`GameManager.Awake` → `PsxLook.Apply`; the city's `CityManager` "PSX look" group (`psx`,
`psxSettings`) → `PsxLook.Apply` after the tape. `Apply` only **finds** the scene object (an error
when missing) and parks it when off.

`Tools → FiniteRunner → Install PSX Look Feature` (`PsxLookInstaller`, material at
`02.Materials/FiniteRunner/PsxLook.mat`, never overwriting) also places and wires the `PsxLook`
object in the open scene — run it once per scene. `PsxLookDebugPage` (nine rows, `MenuTextId`
`Psx*`) is a pause-menu tab wherever a driver exists.

## CRT screen

`01.Scripts/FX/Crt/` + `Rendering/CrtScreenFeature.cs` +
`02.Art/04.Shaders/FiniteRunner/CrtScreen.shader`.

The finished picture shown on a curved glass tube — the display the PSX console plugs into and the
VHS deck plays out to, so it is **the last pass of the chain**. Every knob is one physical trait of
a tube, all in ONE pass:

- **Glass**: a barrel warp (`curvature`, each axis bowed by the square of the other) whose visible
  area is the rounded barrel silhouette of a tube, black beyond it, with rounded corners
  (`cornerRadius`, aspect-corrected so they are circular) and dimmer corners (`vignette`). The
  scanlines and the grille bend *with* the picture.
- **Beam**: each phosphor dot **bleeds** sideways into its neighbours (`bleed`, px at the centre,
  five taps) and the beam loses focus toward the edges, so the bleed grows to 2.5× there; the three
  guns never land on the same spot, so red and blue drift away from green toward the edges
  (`convergence`, px at the corners, ~nothing at the centre); bright areas glow through the glass
  (`glow`, a thresholded four-tap halation). This edge-keyed growth is what makes the picture go
  soft and smeary at the borders — the "edges morph" read.
- **Phosphors**: `scanlines` darken the gaps between `scanlineCount` rows, bright rows blooming
  over the gaps; the aperture grille (`mask`) is R/G/B stripes `maskScale` screen pixels wide,
  locked to the target's pixel columns (`_ScreenParams`, never uv) with the lost light put back.
- **Refresh**: the whole frame breathes at `refreshRate` (`flicker`) on a quantised clock
  (`floor(_Time.y * refreshRate)`) like the far glitch, the speed lines, the tape and the console,
  so the pause menu freezes it.

`intensity` scales every trait, **the warp included** (a low dial is a flatter tube, not a ghosted
one), and blends the result over the clean picture.

ONE Render Graph pass at `AfterRenderingPostProcessing` that copies the camera colour and draws
back over the active target (no depth). The installer **appends it at the very END of the feature
list** — after `PsxLook` and `VhsTape` — and `DistanceFogInstaller.Insert` keeps it there: a
`CrtScreenFeature` always appends, and nothing inserted "after the glitch" ever lands behind an
existing one, so the installers converge in any order. It draws under the HUD.

The driver is **`CrtScreen`** (FX), a hand-placed `[ExecuteAlways]` object beside `DistanceFog`,
`RainSystem`, `SpeedLines`, `VhsTape` and `PsxLook` in every scene, writing `CrtScreenSettings`
(`04.Data/Resources/FiniteRunner_CrtScreen.asset`, `Load()` falls back to an in-memory default)
into the shared material each frame under the standard contract (`HasDriver`, last-one-standing
zeroes `_Intensity`, `preview` for the Scene view). Its only drive is `SetIntensity(0..1)`.

Owner wiring: the runner's `GameSettings` "CRT screen" toggle group (`crtEnabled`, `crtSettings`)
→ `GameManager.Awake` → `CrtScreen.Apply`; the city's `CityManager` "CRT screen" group (`crt`,
`crtSettings`) → `CrtScreen.Apply` after the console. `Apply` only **finds** the scene object (an
error when missing) and parks it when off.

`Tools → FiniteRunner → Install CRT Screen Feature` (`CrtScreenInstaller`, material at
`02.Materials/FiniteRunner/CrtScreen.mat`, never overwriting) also places and wires the
`CrtScreen` object in the open scene — run it once per scene. `CrtScreenDebugPage` (nine rows,
`MenuTextId` `Crt*`) is a pause-menu tab wherever a driver exists.

## The player's filter dials (SETTINGS → VIDEO)

The three retro filters — PSX look, VHS tape, CRT screen — each carry a **player dial** in
`UserSettings` (`PsxFilter` / `VhsFilter` / `CrtFilter`, 0..1, PlayerPrefs `settings.filter.*`,
default 1), set from the VIDEO page under SETTINGS in both menus (`MenuScreenFactory.BuildVideo`,
`MenuTextId` `Video` / `Filter*`). Each driver multiplies the dial into its effective intensity
**in play mode only, re-read every frame** (`intensity × intensityScale × UserSettings.XFilter`):
no event, so a slider drag in the pause menu shows through the menu live, and 0 switches a filter
off for that player without touching the designer's asset (the debug pages) or the scene owner's
on/off. The fog, the speed lines and the death glitch are gameplay signals, not filters, and have
no dial.

## Render pipeline asset and quality levels

The game renders through `Assets/04.Data/URP Asset.asset` → `URP Asset_Renderer.asset`, feature
order **GlitchSilhouette → DistanceFog → SpeedLines → GlitchPost → PsxLook → VhsTape → CrtScreen**.

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
