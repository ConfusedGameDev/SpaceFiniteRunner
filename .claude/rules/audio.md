---
description: Audio — mixer bus layout, GameAudio snapshots and ducking, menu sound slots, the car radio
paths:
  - "**/GameAudio.cs"
  - "**/PoliceEscape/Audio/**"
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

**Menu clips are the four `MenuTheme` slots** — `moveClip` (focus/tab change), `confirmClip`,
`backClip`, `adjustClip` (slider/toggle step; falls back to move when empty) — assigned on
`FiniteRunner_MenuTheme.asset` from the Kenney UI pack in `Assets/07.Audio/02.UI`. Every menu
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
