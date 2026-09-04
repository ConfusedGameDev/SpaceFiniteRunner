# Mission Progression Plan

Formalizes `MissionProgress.MD` into an implementable design. All decisions below were settled in review; nothing here is a silent assumption.

## 1. Terminology

- **Mission** — one city Level plus its matching runner Level, cleared together. The unit of progression, payment, and replay.
- **Level** — a definition asset with objectives (`LevelDefinition` for the city, `RunnerLevelDefinition` for the runner).
- **World** — a scene with a city prefab plus its ordered list of Missions. Completing every Mission of a World unlocks the next World. The finite runner is always the same World (`FiniteRunner_Test`) playing different Levels.

## 2. Data model — new `Campaign` assembly

A new bottom-level asmdef `ConfusedGameDev.FiniteRunner.Campaign` (the SaveData shape: no game-assembly references; referenced by `UI`, `Runner`, `PoliceEscape`). Everything reads the catalog directly — no runtime registry.

**Marker base classes** (in Campaign): `CityLevelAsset : ScriptableObject` and `RunnerLevelAsset : ScriptableObject`, both empty. `LevelDefinition` and `RunnerLevelDefinition` are re-based onto them (serialization-safe — same `m_Script` guid, no field changes). This gives the catalog typed, inspector-safe slots without Campaign seeing the game assemblies.

**Assets** (Odin style: `[InlineEditor]`, `[PropertyRange]`, append-only enums):

- **`MissionDefinition`** — one asset per mission:
  - `id` (explicit authored string, e.g. `"m1_downtown"` — stable against asset renames; keys the profile)
  - localized display name (`MenuTextId` or string per current house pattern for designer text)
  - `CityLevelAsset cityLevel`, `RunnerLevelAsset runnerLevel`
  - `List<UnlockRequirement> requirements` — enum-typed, append-only (`LevelObjective` pattern): `MinUpgradeLevel { modelId/categoryId, level }`, `MinMoney { amount }`. "Previous mission complete" is always implicit and never authored.
- **`WorldDefinition`** — city scene name + ordered `List<MissionDefinition>`.
- **`CampaignCatalog`** — ordered `List<WorldDefinition>` + the runner scene name + the Coming Soon scene name. Lives in `Resources` (`Campaign/CampaignCatalog`), loaded with the `StoreSettings.Load()` pattern (static cache, invalidated for domain-reload-off).

**Frontier** = the first mission in catalog order that is not completed; it is *playable* when its requirements are met (or latched).

## 3. Mission session — how a scene knows what to run

A static `MissionSession` (Campaign assembly): `Current` (the selected `MissionDefinition`) plus `IsReplay`. Set by the Store's START MISSION and by mission select; cleared on exit to main menu.

- `LevelManager.Awake` (city): if `MissionSession.Current != null`, use `Current.cityLevel` instead of the serialized `level` field.
- `GameManager.Awake` (runner): same, with `Current.runnerLevel`.
- **Direct scene play (editor)**: session empty → both managers use their serialized assets exactly as today. The three existing scene-name strings (`StoreSettings.nextMissionScene`, `LevelDefinition.nextSceneName`, `RunnerLevelDefinition.nextSceneName`) become **fallbacks for direct play only**; the catalog drives all real routing.

## 4. Mission lifecycle

1. Main menu **START** → Store (always, even after full completion).
2. Store **START MISSION** → sets `MissionSession.Current` to the frontier mission → loads its World's scene through the loading curtain.
3. City level cleared → additive glitch handoff to the runner scene (`LevelManager.TransitionToNextScene` reads the runner scene from the catalog when a session is live). City clear persists for the session via the existing `lastLevel` handoff record.
4. Runner lost → `GameOverScreen` retry panel, RETRY in place (city clear persists). Quitting to main menu before winning the runner loses the city clear — **missions are all-or-nothing across a sitting** (accepted v1 limitation; no half-mission persistence).
5. Runner won → Mission Complete panel. **The mission latches complete at the rank slam** — the exact moment payment already happens.
6. **NEXT MISSION** → Store (always). **RETRY** → in-place runner restart. **EXIT** → main menu.
7. When the frontier is exhausted (all authored missions complete), the Store's START MISSION leads to the **Coming Soon** scene. That is the only door to it; START keeps going to the Store.

## 5. Payment — full payout, every completion

- Every rank slam pays its **complete total** into the wallet (`base + Σ rewards × Π multipliers`), first clear or fiftieth. **The best-of-delta banking in `PlayerStats.RecordMissionResult` is deleted** — replaying (and panel-RETRYing) a mission is the intended money farm for upgrades and money-gated unlocks; one rule, no exploit distinction.
- The per-mission **record stays best-of**: best total and best rank never downgrade; they are what mission select displays.
- Run-collected coins keep banking as collected (`CollectibleManager` unchanged).

## 6. Profile schema (version 3)

- New `List<MissionRecord> missions` — `{ missionId, completed, bestTotal, bestRank (stored per RankTable convention), timesCompleted }`.
- `unlockedIds` gains its first writer: requirement unlocks **latch** here the first time they are met (see §7).
- `lastLevel` stays exactly as-is: the transient city→runner handoff slot for the Mission Complete panel. Its `banked` flag and delta fields become vestigial once delta banking is removed; keep the fields (JsonUtility default-loads them), stop reading them.
- `completedLevelIds` keeps being written (harmless stats ledger) but nothing gates on it.
- **No migration** of existing saves: `"TestLevelDefinition"` in `completedLevelIds` is dev data; campaign progression starts fresh. `Migrate` bumps version 2 → 3 by null-filling the new list only.
- All gameplay writes go through `PlayerStats` (`RecordMissionCompleted(missionId, total, rank)` alongside the existing calls); saves at the same commit points.

## 7. Unlock rules

- A mission is **reachable** when it is the frontier (all prior missions complete) and its `requirements` pass.
- Requirements are evaluated whenever the Store or mission select builds its rows, and after every mission completes. `MinMoney` checks **current spendable balance** (`PlayerStats.Balance`) — buying upgrades can drop you below a threshold you haven't reached yet.
- **Unlocks latch**: the first time a mission's requirements pass, its id is written to `unlockedIds` and it never re-locks, even if the balance later falls below the threshold.
- A frontier mission with unmet requirements: the Store's START MISSION row is **disabled (greyed) with the requirement printed** (`REQUIRES: $5,000` / `REQUIRES: SPEED LV 3`); same treatment on locked mission-select rows. Never hidden.

## 8. Store changes (`StoreScreen`)

- START MISSION row shows its target: `START MISSION — 2: <NAME>` (one localized format string).
- Row resolves the frontier from the catalog + profile each time the screen builds; disabled-with-requirement when gated; leads to Coming Soon when the campaign is done.
- `StoreSettings.nextMissionScene` is no longer read on the real path (kept as the editor-direct-play fallback / removed from the flow).

## 9. Mission select — a campaign map in the main menu

- New top-level main-menu row **MISSIONS** (the 4-touch `MainMenuController` pattern: `MenuTextId` + screen field + `BuildX()` before `BuildMain()` + `TearDown` reset; plus `SetFooterFor`).
- **Hidden until at least one mission is complete.**
- Scope is a **campaign map**, not a bare replay list: all missions of reached worlds, one row each —
  - *Completed*: selectable, shows best rank/total (`MISSION 1 — S  $12,400`).
  - *Frontier*: selectable (identical to Store START MISSION).
  - *Locked*: greyed, requirement text printed.
- Selecting a playable mission sets `MissionSession.Current` (+ `IsReplay` when already completed) and **loads the city scene directly through the loading curtain — the Store is skipped** on replays. Replays change nothing about the frontier; their NEXT MISSION still returns to the Store.
- Uses the `MenuScreen` viewport (`SetViewport`) with `StatHeaderRow` per world, the LOG screen recipe.

## 10. Coming Soon scene + build settings

- New minimal scene `ComingSoon.unity` built by an editor tool (menu-theme backdrop, localized COMING SOON text, EXIT TO MAIN MENU row through the curtain).
- New **`Tools → FiniteRunner → Register Campaign Scenes`**: syncs `EditorBuildSettings` from the catalog — MainMenu (index 0), Store, every world scene, the runner scene, ComingSoon. Run it and **commit the populated list** (it is committed empty today, which breaks every by-name load and `LoadMainMenu()` on a fresh checkout — a pre-existing landmine this fixes).

## 11. Initial authoring

- **Mission 1**: the existing pair — `TestLevelDefinition` + `FiniteRunner_LevelDefinition` — wrapped in `Mission_01` inside `World_01` (scene `CarTest`). No requirements.
- **Mission 2**: same world; a new small city objective list (e.g. a DestroyCars step) **and its own new `RunnerLevelDefinition`** with one knob changed (e.g. higher Light Speed target or shorter timer) — identical runner assets would hide an injection bug. **Auto-unlocks** on Mission 1 complete (requirements exercised later via a one-line inspector edit, not gated into the test loop).
- An editor tool seeds the catalog/world/mission assets (never overwriting), the `StoreSceneBuilder` pattern.

## 12. Key code seams (verified against current source)

| Seam | Today | Becomes |
|---|---|---|
| `StoreScreen.StartMission()` (`StoreScreen.cs:393–399`) | loads `settings.nextMissionScene` ("CarTest") | resolves frontier from catalog, sets session, loads its world scene; disabled+requirement when gated; Coming Soon when done |
| `LevelManager.level` (`LevelManager.cs:101`) | serialized asset | session override in Awake, serialized fallback |
| `LevelManager.TransitionToNextScene` (`LevelManager.cs:900–908`) | `level.nextSceneName` | catalog's runner scene when session live; field as fallback |
| `GameManager.level` (`GameManager.cs:56`) | serialized asset | session override in Awake, serialized fallback |
| Mission Complete `onNext` (`GameManager.cs:324`) | `LoadingScreen.Load(level.nextSceneName)` | `LoadingScreen.Load(Store)` always |
| `PlayerStats.RecordMissionResult` (`PlayerStats.cs:175–191`) | best-of delta bank | full payout + best-of record per mission id |
| `PlayerStats.IsLevelCompleted` / `Unlock` / `IsUnlocked` | zero call sites | first consumers: frontier resolution + unlock latching |
| `MainMenuController.Build()` / `BuildMain()` | 5 rows | + MISSIONS row (conditional) + missions screen |
| `EditorBuildSettings` | committed empty | populated by Register Campaign Scenes, committed |

## 13. Out of scope (v1)

- Persisting a half-completed mission (city cleared, runner pending) across app quits.
- More than one authored World (structure supports it; Coming Soon stands in for World 2).
- Mission-specific store inventory, difficulty scaling on replays, per-mission leaderboards.
