---
description: Mission cinemas — CinemaSystem, format library, freezing vs running-world clips, skip gesture, CinemaTrigger
paths:
  - "Assets/01.Scripts/PoliceEscape/Cinema/**"
  - "**/CinemaSystem.cs"
  - "**/CinemaFormat*.cs"
  - "**/CinemaTrigger.cs"
---

# Mission cinemas

`PoliceEscape/Cinema/`, namespace `…PoliceEscape.Cinema`.

A `LevelObjective` can open with a video: its **Cinema** `[ToggleGroup]` (`hasCinema`,
`cinemaClip`, `cinemaFormat`, `cinemaSeconds`, `cinemaPausesGame`) plays the clip the moment the
step activates, **before** its briefing line, with the world frozen under it by default.
`cinemaPausesGame` off lets the game run under the picture.

Assigning the clip auto-fills `cinemaSeconds` from `VideoClip.length` (`[OnValueChanged]`) and the
**Fetch Duration** button re-fetches. **The duration is authoritative**: shorter cuts the clip,
longer holds its last frame — the clip is `Pause()`d on `loopPointReached`, never `Stop()`ped
while visible, so the RenderTexture keeps the frame.

`HasCinema` needs both the toggle and a clip. The inspector list shows `[CINEMA]` through
`EditorLabel`; `Summary` is player-facing (the brief and the map draw it).

## Formats are data

`CinemaFormatLibrary` (`04.Data/Resources/PoliceEscape_CinemaFormats.asset`, created by
`Tools → Police Escape → Create Cinema Format Library` or by the placer, **never overwritten**;
`Load()` falls back to the in-memory defaults) holds a list of `CinemaFormat` rows: `id`,
normalized `viewport` rect (the holder's anchors), `slideFrom` edge + `slideSeconds`,
`backdropAlpha`, `fixedAspect` (1 = a square fitted inside the viewport), `keepClipAspect`
letterboxing, frame colour/padding.

Seeded with FullScreen, RearMirror (top band, slides from the top), SquareLeft and BannerRight.
**A new layout is a new row.** `skipHoldSeconds` lives there too, because it is a property of the
gesture.

The objective's dropdown reads `CinemaFormatLibrary.Ids()` through a member on `LevelObjective`
(an Odin `@` expression cannot see the child namespace) off a **cached** load — an inspector
getter must not allocate.

## `CinemaSystem`

A hand-placed scene-lifetime system under `===SYSTEMS===` (placed by `SceneSystemsPlacer`
unconditionally; `Ensure(scene)` is the find-or-create fallback). **A *disabled* one means cinemas
are off** — the step briefs without one.

On `Awake` it builds its own overlay canvas at **sorting order 22** — above the RPG box 15,
thunder 18 and pause menu 20; below the brief 24 and game over 25 — and **one `Holder_<id>` child
per format** (viewport-anchored root → Panel with the frame and optional `AspectRatioFitter` →
padded Inset → RawImage with the clip-aspect fitter). The holders are real objects while the asset
stays the truth for where they sit.

### Freezing vs running-world

- **A freezing cinema** sets `Time.timeScale = 0` — the cars are rigidbodies stepped from
  FixedUpdate, and the existing `timeScale > 0` gates keep the pause menu, city map and camera
  cycle shut — while the `VideoPlayer` runs on **`DSPTime`** (immune to the clock, unlike
  `GameTime`) and every animation runs unscaled.
- **A non-freezing one** (`pauseGame` false) leaves the clock alone and rides it instead: the clip
  on `GameTime`, countdown and slide on `Time.deltaTime`. A pause menu opened over it halts it
  too, and a `CanvasGroup` on the canvas hides the picture while someone else holds the clock at 0
  (the canvas sorts above the pause menu).

`IsPlaying` says a cinema is up; `IsFrozen` says it owns the freeze (the radio's d-pad gate uses
`IsFrozen`, so the radio still works under a running-world cinema).

### One cinema at a time, newest wins

A `Play` while one is up `End`s the old one at once — **its caller IS called back** (its cinema is
over; a gate or cooldown waiting on it must not stall) — then starts the new one. Only `Cancel`
drops a callback.

### Robustness

The clip is `Prepare()`d before the holder is revealed (a fresh RenderTexture shows black
otherwise; it is also `GL.Clear`ed). `errorReceived` or a **5 s prepare watchdog** end the cinema
rather than leave the game frozen.

Audio is routed `VideoAudioOutputMode.AudioSource` → an `AudioSource` on `GameAudio.Cinema`
(Voice when the mixer lacks the bus; only when `audioTrackCount > 0`; **track setup must precede
`Prepare()`**). A freezing cinema ducks the game's sound like the pause menu —
`GameAudio.SetCinema(true, theme.CinemaAudioFade)` as it starts, released in `End` so the in-game
buses fade back in once it is done, skipped or displaced. A running-world cinema leaves the mix
alone.

**`End()` order matters**: hide, `Stop()`, release the texture, restore `timeScale = 1` (only when
this cinema froze it), **then** invoke the callback — `RpgMessageSystem` types on scaled time, so
the briefing line has to be queued on a running clock.

### Skip

A long press of Enter / A (`MenuNavigator.ConfirmHeld` — **no Space**, it is the car's handbrake
and a menu tap key), drawn as a `Radial360`-filled `UiSprites.Ring` (`UI/UiSprites.cs`, the shared
disc/annulus generator — a filled disc reads as a pie) with the device glyph inside and the
localized `HoldToSkip` caption. It arms only after `MenuTheme.InputGrace` **and** one seen
release, so the press that accepted the brief can't pre-charge it, and drains fast on release.
The dialogue box advances on a TAP of the same chord, so `Play` raises
`RpgMessageSystem.SkipInputSuppressed` and `End` clears it: a line under the picture keeps
playing on its own clock but cannot be fast-forwarded or dismissed until the cinema is over.

## `LevelManager` integration

Gates its loop, impacts and damage on `cinemaOpen` (exactly like `briefOpen`) — raised **only for
a step whose cinema pauses the game**, so a running-world cinema leaves the step live under it.
`cinemaPlaying` is the separate "ours is up" flag `OnDisable` cancels on.

It sets `briefed` **before** starting the cinema so an All-Must-Hold regression never replays it,
and briefs step 0 synchronously from `OnBriefAccepted` so a first-step cinema re-freezes in the
frame the brief unfroze. `OnDisable` cancels a running cinema (`Cancel()` tears down without
calling back — the `MissionBriefScreen` rule), as does the system's own `OnDisable`.

## `CinemaTrigger`

The video twin of `DialogueTrigger`: a hand-placed volume (collider forced to a trigger, same
`DialogueTrigger.IsPlayer` rule) authoring clip / format / duration / `pauseGame` (auto-filled +
Fetch Duration, like the objective) that calls the raw
`CinemaSystem.Play(clip, formatId, seconds, pauseGame, onFinished)`.

`oneShot` destroys it as it fires; otherwise `cooldownSeconds` **starts counting when the cinema
clears** (skip, timeout, or another cinema displacing it — the freeze takes arbitrary real time)
in scaled time. A ready trigger fires even while another cinema is up: the system ends that one
and plays this.

`DialogueTriggerVisualizer` fills both trigger kinds on the same debug channel — orange dialogue,
blue cinema.
