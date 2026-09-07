---
description: Audio — mixer bus layout, GameAudio snapshots and ducking, menu sound slots, the car radio, the runner's music
paths:
  - "**/GameAudio.cs"
  - "**/PoliceEscape/Audio/**"
  - "**/Runner/Audio/**"
  - "**/MusicAssetBuilder.cs"
  - "**/RadioSystem.cs"
  - "**/RadioSettings.cs"
  - "**/RadioAssetBuilder.cs"
  - "**/*.mixer"
  - "Assets/07.Audio/**"
---

# Audio

## Mixer and `GameAudio`

`Assets/04.Data/FiniteRunner/FiniteRunnerMixer.mixer` + the static `GameAudio` facade in
`01.Scripts/UI/`.

**Bus layout**: `Master → Gameplay → (Music / FX / Voice)`, plus `UI`, `PauseMusic`,
`LoadingMusic` and `Cinema` directly under Master.

Route sources through:

| Handle | For |
|---|---|
| `GameAudio.Fx` | world SFX — the EVP car audio uses this |
| `GameAudio.Voice` | `RpgMessageSystem` blips |
| `theme.UiOutput` | menu blips → the UI bus |
| `GameAudio.PauseMusic` | the pause loop |
| `GameAudio.LoadingMusic` | the loading curtain's loop |
| `GameAudio.Cinema` | a cinema clip's own sound, outside the ducked Gameplay bus |

**Menu clips are the `MenuTheme` slots** — `moveClip` (focus/tab change), `confirmClip`,
`backClip`, `adjustClip` (slider/toggle step; falls back to move when empty) and `debriefClip`
(the Mission Complete panel powering on, `01.SFX/FiniteRunner/computerNoise_002`, played once by
`MissionCompleteScreen.Build`) — assigned on `FiniteRunner_MenuTheme.asset`, the four navigation
blips from the Kenney UI pack in `Assets/07.Audio/02.UI`. Every menu
(`MainMenuController`, `PauseMenu`, `GameOverScreen`, `MissionBriefScreen`) plays them through its
own `AudioSource` on `theme.UiOutput`. **Add new menu sounds as theme slots, never as loose clips
on a screen.**

### The four snapshots are the ducks

- `PauseMenu` → `GameAudio.SetPaused(bool, theme.PauseAudioFade)` → **Paused**: the whole Gameplay
  bus mutes while UI stays audible, and an optional `MenuTheme.pauseMusicClip` loop (muted outside
  the Paused snapshot) fades in.
- `LoadingScreen` → `GameAudio.SetLoading(bool, theme.LoadingAudioFade)` → **Loading**: Gameplay
  AND PauseMusic muted, LoadingMusic up.
- `CinemaSystem` → `GameAudio.SetCinema(bool, theme.CinemaAudioFade)` → **Cinema**, for a
  **world-freezing** cinema only: Gameplay and both musics muted, the Cinema bus up so the clip's
  sound plays through the duck. The Cinema bus is up in Gameplay too — a running-world cinema is
  heard — and muted under Paused / Loading.

**`GameAudio` keeps the requests as flags and resolves them together, loading winning over paused
over cinema.** A scene load destroys the leaving scene's `PauseMenu`, whose `OnDestroy` un-pauses
the mix — under the curtain that only clears the pause flag, so the loading duck holds and the next
scene lands on Gameplay when the curtain releases.

**The mixer runs in `UnscaledTime` update mode**: snapshot transitions follow `Time.timeScale` in
the default mode, so the fade would freeze half-done. This way the crossfade plays out at
`timeScale 0`.

**The duck handle is the un-exposed `Gameplay` parent volume on purpose.** Exposed params
(`MasterVolume` / `MusicVolume` / `SFXVolume` / `UIVolume`, driven by `UserSettings` — the SFX
slider pushes `SFXVolume`, `UIVolume` and `VoiceVolume`) leave snapshot control the moment
`SetFloat` touches them, so **user volumes and pause ducking must live on different group volumes.**

The hidden `UserSettingsBootstrap` object re-pushes the exposed params one frame after every scene
load, because the mixer applies its start snapshot on its first audio update and silently
overwrites any `SetFloat` made before it.

`PauseMenu.OnDestroy` restores the Gameplay snapshot if it dies paused — the mixer outlives scenes.

## Car radio

`PoliceEscape/Audio/`, namespace `…PoliceEscape.Audio`.

`RadioSystem` is a hand-placed scene-lifetime system under `===SYSTEMS===` (placed as `Radio` by
`SceneSystemsPlacer`; **Tools → Police Escape → Place Scene Systems** adds it to an existing scene)
playing the `RadioSettings` playlist (`04.Data/Resources/PoliceEscape_Radio.asset`, `Load()` falls
back to a silent default; created and seeded by **Tools → Police Escape → Create Radio Settings** /
`RadioAssetBuilder.CreateOrLoad`) through `GameAudio.Music`, so the pause snapshot ducks it for
free.

**Two song sources:**

- The bundled `songs` list — the asset's **Fetch Songs** button scans `sourceFolder`
  (`Assets/07.Audio/03.Music/InGame`) for clips.
- With `useStreamingAssets` on, every `.mp3` / `.ogg` / `.wav` in
  `StreamingAssets/<streamingFolder>` (`Radio`) at play time, loaded one by one via
  `UnityWebRequestMultimedia` (kept compressed in memory) and appended as each lands, skipping
  files that share a bundled song's name. **Copy Songs To StreamingAssets** seeds that folder so a
  build ships loose, replaceable files players can add to.

Every song start posts `nowPlayingFormat` ("Now Playing: {0}") on the `RpgMessageSystem` as
`speakerName`. A finished song hands to the next; the end of the list wraps to the first.

**Controls are read only over live gameplay** (`timeScale > 0`, no main menu / cinema / loading
curtain — the pause menu spends the d-pad on sliders):

- pad right / key **6** — next
- pad left / key **5** — previous
- long press left / 5 (`longPressSeconds`) — radio OFF (says `radioOffText`)
- long press right / 6 — radio back ON

A long press fires while held and **swallows its release**, so a power switch never doubles as a
skip. The d-pad gate uses `CinemaSystem.IsFrozen`, so the radio still works under a running-world
cinema.

**Nothing cuts hard.** Every transition rides one volume fade (`fadeSeconds`, unscaled time): a
song change fades out, swaps at silence and fades in; off fades out then pauses in place; on
resumes and fades in. A request mid-fade replaces what happens at silence, so mashing skip lands on
the last song asked for.

Streamed clips are destroyed with the system; bundled clips are assets and never touched.

## Runner music

`Runner/Audio/`, namespace `…FiniteRunner.Audio`. One track, one big loop.

`RunnerMusic` is a hand-placed scene-lifetime system under the runner scene's `===SYSTEMS===`
(placed as `Music` by **Tools → FiniteRunner → Place Scene Systems**, which parents new systems
under that header when the scene has one). `GameManager.Awake` calls
`RunnerMusic.Apply(settings.musicEnabled, settings.musicSettings)` after the CRT screen: it only
**finds** the object (an error naming the placer when missing) and parks it when the
`GameSettings` "Music" toggle group is off — never spawned. Its knobs all live on `MusicSettings`
(`04.Data/Resources/FiniteRunner_Music.asset`, created and seeded with the clip by **Tools →
FiniteRunner → Create Music Settings** / `MusicAssetBuilder.CreateOrLoad`; `Load()` falls back to a
silent in-memory default): `clip`, `volume`, `randomStart`, `fadeInSeconds`, `fadeOutSeconds`.

The source is `loop = true` on `GameAudio.Music`, so **pause is the snapshot's job** — the Paused
/ Loading / Cinema ducks hide it with no pause detection in the system, and it keeps running
silently under the duck (a big loop resumes wherever it is). The clip's importer is
CompressedInMemory + preload so the seek is instant.

**Every play is a fresh play** (`Play()`, the system's own `Start` and `GameManager.Restart`):
`source.time` is set to a random point (the last second excluded so a start never lands on the
seam) **before** `Play()`, then the level fades in. The fades are the radio's idiom — one `level`
scalar × `settings.volume` on `source.volume`, `MoveTowards` on **unscaled** time, an `atSilence`
booking — and never `SetFloat` an exposed param (`MusicVolume` stays the player's slider).

- **Win**: `FinishWin`, once the ship is grounded, books `FadeOut(winGlitchRampSeconds +
  winGlitchHoldSeconds)` so the music reaches silence on the frame the Mission Complete panel
  opens — sound washes out with the picture.
- **Lose**: `EndRun` books `FadeOut()` at the asset's `fadeOutSeconds`, under the retry panel.
- A fade-out that reaches silence **pauses** the source; `Play()` restarts it either way, and a
  RETRY pressed mid-fade just turns the music around onto a new point.

## Runner sound effects

`Runner/Audio/ShipAudio.cs` + `RunnerSfxSettings` (`04.Data/Resources/FiniteRunner_Sfx.asset`,
created and seeded with the clips by **Tools → FiniteRunner → Create Sound Effects Settings** /
`SfxAssetBuilder.CreateOrLoad`; `Load()` falls back to a silent default). The ship's own sound:
the power-up pickup, the engine loop and the jump takeoff, every clip "Empty = silent", bands
unpacked by accessors.

`ShipAudio` **rides the ship like `LoopSlowMo`**: `GameManager.Awake` calls
`ShipAudio.Ensure(motor).Configure(settings, LightSpeedKmh)` while the `GameSettings` "Sound
effects" toggle group is on (off = no component). It reads the settings live and builds three 2D
child sources (`Engine` loop, `Pickups`, `Jumps`) on **`GameAudio.Fx`** — the ducks and the SFX
slider come free, and nothing touches an exposed mixer param.

- **Pickups hook `SpeedPad.Collected`, never `ShipMotor.PadImpulse`** — a ramp takeoff raises
  the impulse too and would double up with the jump. Boost orbs play `powerUpClip` at a pitch
  from `powerUpPitchBand` on a log scale of the tier (`SpeedDelta / powerUpSpeedBoost`: green 0,
  blue 0.4, purple 1). Brake pads play `brakeClip` only when one is assigned (empty by default).
- **Jumps hook `ShipMotor.TookOff`** (raised in `TakeOff()` only; tubes and loops never fire it).
- **Dashes hook `ShipMotor.DashPerformed` and `BarrelRollStarted`.** An airborne dash raises
  both the same frame, so `dashClip` plays only on the track (skipped while `Airborne` unless a
  roll is already spinning) and `barrelRollClip` plays for the roll.
- **The engine is a fraction of Light Speed**, smoothed by `engineResponse` on real time:
  `volume = lerp(engineVolumeBand)`, `pitch = lerp(enginePitchBand)`, both by the smoothed
  fraction (clamped — the ship may pass Light Speed after the win latch). It is **gated on the
  sim** (`!motor.Paused && !HasStopped`, faded over `engineFadeSeconds`): `EndRun` pauses the
  motor, so both endings fade the engine out with no manager hook, and `Restart` brings it back;
  at 0 the source is stopped so nothing plays inaudibly under a panel. With `engineFollowsClock`
  the pitch is multiplied by `Time.timeScale` while the clock runs, so the loop slow-mo drags the
  engine down with the picture (Unity never ties pitch to the clock); a stopped clock leaves it.
- `FiniteRunnerEngine.ogg` is imported with preload on so the loop is resident at launch.
