---
description: Systems both games share — floating text, RPG message box, haptics, collectibles and the money HUD
paths:
  - "Assets/01.Scripts/Runner/Collectibles/**"
  - "Assets/01.Scripts/Haptics/**"
  - "**/FloatingTextSystem.cs"
  - "**/FloatingWorldText.cs"
  - "**/RpgMessageSystem.cs"
  - "**/HapticsSystem.cs"
  - "**/Collectible.cs"
  - "**/CollectibleManager.cs"
  - "**/MoneyHud.cs"
---

# Shared systems

These live in the `Runner` assembly (or their own) because **the city references Runner, never the
reverse** — both games use them.

## `FloatingTextSystem`

Singleton entry point for floating gameplay texts:
`FloatingTextSystem.Instance.DisplayText(text, color, duration = 1f)`, with an overload taking lead
distance + character size. Auto-created on first use; spawns texts **ahead of the ship** so they
stay readable at speed.

The lead offset for boost popups is `GameSettings.boostTextLeadMeters`; the "+N" popup size is
`RaceHud.boostTextSize` (0.6, half the other floating texts). Backed by **`FloatingWorldText`**, the
code-built rising/fading billboard component.

## `RpgMessageSystem`

Singleton RPG dialogue box — portrait + speaker name + typewriter text at the bottom of the screen.
Auto-created like `FloatingTextSystem`; **pre-place one in the scene** to wire its public
UnityEvents (`onMessageStarted` / `onTypingFinished` / `onMessageFinished`) or assign a portrait
sprite / type sound.

`ShowMessage(speaker, text, hold, accent, avatar, playTypeSound, onFinished)` queues a line: one
message at a time (duplicates dropped, **their `onFinished` still runs so callers can't stall**),
typewriter reveal with an optional per-character blip (a placeholder beep is generated when no clip
is assigned), then a hold for its duration before hiding and firing `onFinished`.

**It runs on scaled time**, so messages freeze with the pause menu — and any caller waiting on
`onFinished` must not freeze the world under it.

`ClearMessages()` drops pending lines **without** firing their `onFinished`. `GameManager.EndRun`
and `Restart()` both call it, so a story line can neither sit frozen under a panel nor land on the
next run.

The portrait shows the speaker's initial until real art is assigned. `PlaceholderBlip()` is the
shared rising-pitch blip the Mission Complete typewriter also uses.

Triggered by: purple-orb pickups (via the static `SpeedPad.Collected` event + `SpeedPad.TierName`),
the patrol's warnings and redeploys, the radio's now-playing line, and the city's briefings and
completion lines. **Neither runner ending speaks** — win and loss raise their panels directly.

`HudSuppressed` is the flag other HUD elements hide under.

## `HapticsSystem`

Singleton gamepad rumble, auto-created like `FloatingTextSystem`.

- `Pulse(low, high, duration)` for one-shots.
- `SetChaseIntensity(0..1)` is a continuous channel the patrol refreshes each frame while close;
  **it self-fades when the calls stop**, so stale rumble can't persist.

Wired: boost/brake pulses via `GameManager.OnPadImpulse`, proximity rumble in `PolicePatrol`, long
rumble on getting caught, ramp wall hits and loop falls. Motors reset on disable/quit. It runs on
**unscaled** time so rumble decays while paused.

## Collectibles (`Runner/Collectibles/`, namespace `…FiniteRunner.Collectibles`)

A pickup the player drives or flies through.

**`Collectible`** carries a string `id` (what it is; many share one id), a **`CollectibleKind`**
(`Item = 0` counted only, `Money = 1` — append-only) with, for Money, a `[MinMaxSlider]`
`valueRange` (default $1–5) rolled once in `Awake` unless a spawner called `SetValue(int)`, a
trigger volume (every collider on it is forced to a trigger, a sphere added when there is none) and
a `mesh` slot whose object spins around a chosen local axis (`SpinAxis` X/Y/Z, default Y — X or Z
for a flat disc) and hovers over its authored local position (spin / amplitude / frequency knobs,
random phase so a row never bobs in unison). `Configure(id, kind, axis, mesh)` sets a code-built one
up after `AddComponent`.

**The player is any collider whose attached rigidbody or parent chain carries the `ICollector`
marker** — `ShipMotor` and the city's `CarInput` implement it — so the pickup knows neither vehicle.

Collecting plays the optional `pickupClip` through a throwaway source on `GameAudio.Fx`, raises the
static `Collectible.Collected` event and destroys the object. **It records nothing itself.**

### `CollectibleManager` is the one recorder

A hand-placed scene-lifetime system in BOTH scenes (a root object in `FiniteRunner_Test`, under
`===SYSTEMS===` in `CarTest`). **`Instance` only finds it** and logs an error once when a scene has
none — it never creates.

It subscribes `Collected` in `OnEnable`/`OnDisable`, calls `PlayerStats.RecordCollectible(id)` for
every pickup (the LOG's COLLECTIBLES section lists a total and one row per id) and switches on the
kind — Money adds the value to `RunMoney`, banks it at once through `PlayerStats.AddMoney` (a game
over keeps it; the Mission Complete panel pays the mission's own reward separately) and raises
`MoneyChanged(runTotal, delta)`. **A new kind is one more case.**

`ResetRun()` zeroes the run counters — `GameManager.Restart` calls it.

### `MoneyHud` (`Runner/HUD/`)

Hand-placed beside the manager in both scenes: the top-right counter on its own overlay canvas at
sorting 10 (the city gauges' recipe), one legacy-font `$1,234` label (`StatFormat.Money`) that
counts up toward `RunMoney` with a scale punch per pickup and hides under
`RpgMessageSystem.HudSuppressed`. The Store uses it with a `ValueSource` for the wallet instead.

`Tools → FiniteRunner → Place Scene Systems` (`RunnerSceneSystemsPlacer`) and the city's
`SceneSystemsPlacer` place both objects when missing.

### Authoring

Hand-place collectibles as root objects, or under the city prefab's `AdditionalItems` socket
(rebake-proof). **GameObject → Police Escape → Collectible** drops a ready one (trigger sphere +
placeholder cube in the mesh slot).

The runner streams money through the `TrackGenerator`'s "Collectibles" toggle group — see
`runner-track.md`; `CreateCollectible` builds a gold cylinder coin under a root with the long box
trigger (or instantiates the prefab), `Configure`s it as Money, `SetValue`s the roll and adds it to
`spawned` for culling. `RaceHud` answers `MoneyChanged` with a gold `+$N` floating text. **The city
spawns no money yet** — it only hosts the manager and the HUD.
