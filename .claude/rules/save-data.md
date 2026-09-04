---
description: Save data — PlayerProfile JSON store, PlayerStats recording API, recorders, and the LOG screen
paths:
  - "Assets/01.Scripts/SaveData/**"
  - "**/PlayerProfile.cs"
  - "**/PlayerProfileStore.cs"
  - "**/PlayerStats.cs"
  - "**/MissionResults.cs"
  - "**/CityStatsRecorder.cs"
  - "**/LogScreenFactory.cs"
  - "**/StatFormat.cs"
---

# Save data and the LOG screen

`01.Scripts/SaveData/`, its own asmdef `ConfusedGameDev.FiniteRunner.SaveData` with **no
game-assembly references** (Odin's runtime attributes are auto-referenced, so its types may carry
`[PropertyRange]`). Referenced by `UI`, `Runner` and `PoliceEscape`.

## The file

**JSON at `Application.persistentDataPath/profile.json`, never a ScriptableObject** — every
`*DebugSettings.Flush` in the project is `#if UNITY_EDITOR`, so an asset write would keep nothing in
a build.

`PlayerProfile` is the `[Serializable]` model, under JsonUtility rules: public fields, nested
`[Serializable]` sections `global` / `lastLevel` / `runner`, `List<CountEntry>` for the per-vehicle
kill counts (there are no dictionaries), `missions` (the campaign's per-mission best-of records, keyed by authored
mission id — what progression gates on), `unlockedIds` (requirement unlocks latched by the
campaign), `completedLevelIds` (a stats ledger nothing gates on), `upgrades` for store levels, and
`version` + `Migrate` for reinterpreting old files. **A new field just loads as its default.**

`PlayerProfileStore` owns the file: lazy load, **atomic write** (`.tmp` swapped in, previous kept
as `.bak`), a file that will not parse quarantined as `profile.corrupt-<stamp>.json` with a fresh
profile started. **No I/O failure ever throws into gameplay.**

**`Invalidate()` runs at `SubsystemRegistration` / `BeforeSceneLoad` and from the Delete Save menu
because domain reload is off** — a cached profile would otherwise be re-saved over the file.

## `PlayerStats` — gameplay records through this only

`RecordDeath(arrested)`, `RecordArrest`, `RecordTotaledCar(police, key, label)`,
`SampleCarSpeed` / `SampleShipSpeed` (max-track, mark dirty only on a record; **car samples above
400 km/h are collision spikes and ignored**), `RecordJump(meters, airSeconds)`,
`RecordLevelCompleted(levelId, name, lastObjective, baseReward, objectives, challenges, rankTable)`
(records the level's rows into `lastLevel` for the Mission Complete panel and **saves at once** —
the city scene is about to unload; **it banks no money**), `RecordMissionCompleted(missionId, total,
rank)` (**the ONE bank** — the runner's panel pays the FULL total on every completion, replay or
panel RETRY included, and latches the campaign `MissionRecord` best-of; `lastLevel.banked` is
vestigial since profile v3), `Mission` / `IsMissionCompleted` / `AnyMissionCompleted`,
`RecordRunStarted` / `RecordRunEnded(escaped, seconds)`, `RecordPad(boost)`, `AddMoney` /
`CompleteBonusObjective`, `TrySpend` / `Balance` / `SetUpgradeLevel`,
`Unlock` / `IsUnlocked` / `IsLevelCompleted`, `RecordCollectible(id)`.

`MissionResults.cs` holds `Rank`, `RankTable`, `ObjectiveResult`, `ChallengeResult`,
`MissionPayout`.

**Writes happen at commit points** — `PauseMenu` Resume/Reload/OnDestroy, level complete, death,
run end, a store purchase — and from the hidden `PlayerProfileBootstrap` (the
`UserSettingsBootstrap` shape: 60 s autosave, flush on pause / focus-loss / quit / scene unload).

That bootstrap also ticks **total play time — unscaled seconds while a scene other than build index
0 is active and `timeScale > 0`**. Menus, game over, brief and cinemas all write timeScale 0; the
loading curtain sets 1 but flags `PlayerStats.SuspendPlayTime`.

## Recorders

**Runner (`GameManager`)** — counts an attempt on the first frame the motor is unpaused
(`ShipMotor.Launch()` fires up to three times per run, so it can't count), records the result in
`EndRun(label, RunOutcome)` (the **typed enum, never the label string**; time-to-light-speed =
`timeLimitSeconds - TimeRemaining`; Caught is also an arrest), and pads in `OnPadCollected`
(`SpeedDelta > 0` = power-up).

**City (`CityStatsRecorder`, `PoliceEscape/Stats/`)** — a hand-placed scene system
(`SceneSystemsPlacer` + the `CityManager.Awake` fallback). It subscribes `CarHealth.Died` in
`OnEnable`/`OnDisable` — **never a static initializer, handlers would stack across play sessions** —
to count totaled cars (police = a `PoliceCarInput` on the car; label from
`VehicleIdentity.Describe`), samples the player car's speed, and measures jumps itself (horizontal
velocity integrated while no wheel is grounded, ≥ 0.25 s air time — **independent of
`AirTimeSlowMo`**, which stands down when its slow-mo is off). It raises `JumpLanded` beside
`RecordJump`.

**`LevelManager`** — records every accepted `RequestReboot` as a death (an arrest when the reason is
full corruption and `lastDamageReason` was a police hit — the city AI has no capture state) and
`Complete()` as a level completed.

## The LOG screen

A pause-menu row (`LogScreenFactory`, UI assembly, reusable by the main menu): one scrollable list
of `StatHeaderRow`s (accent caption + rule, `Focusable => false`, the cursor steps over them)
heading `StatRow`s (`LABEL ……… VALUE`, read-only, value right-aligned in a `MenuChoice`-style
reserved zone).

**Rebuilt from the profile on every pause**, since the vehicle list grows. Values are formatted by
`StatFormat` (`DD:HH:MM:SS`, `KM/H`, `M`, `$N,NNN`, `mm:ss.ff`) and **never localized**; labels are
`MenuTextId`s. It rides the `MenuScreen` viewport (see `ui-menus.md`).

`Tools → FiniteRunner → Save Data → Open Folder / Print Profile / Delete Save` are the developer
handles.
