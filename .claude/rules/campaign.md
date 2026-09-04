---
description: Campaign progression — the mission catalog, the mission session, frontier and unlock rules, the Store's START MISSION, the MISSIONS map and the Coming Soon scene
paths:
  - "Assets/01.Scripts/Campaign/**"
  - "Assets/01.Scripts/Runner/Campaign/**"
  - "**/MissionSelectScreenFactory.cs"
  - "**/RequirementText.cs"
  - "**/ComingSoon*.cs"
  - "**/CampaignAssetBuilder.cs"
  - "**/CampaignSceneRegistrar.cs"
  - "**/MissionRow.cs"
---

# Campaign

`01.Scripts/Campaign/`, its own asmdef `ConfusedGameDev.FiniteRunner.Campaign`, referencing **only
`SaveData`** — referenced by `Runner`, `PoliceEscape` and both editor assemblies, never by `UI`.
Everything reads the catalog directly; there is no runtime registry.

## Vocabulary

- **Mission** — one city Level plus its runner Level, cleared, paid and replayed together.
- **Level** — a definition asset with objectives (`LevelDefinition` city, `RunnerLevelDefinition` runner).
- **World** — a city scene plus its ordered Missions. The runner is always one scene
  (`CampaignCatalog.runnerSceneName`) playing each mission's own runner level.

## Data

`MissionDefinition` (`id` — an AUTHORED string that keys the profile, never the asset name;
`displayName`; `CityLevelAsset cityLevel`; `RunnerLevelAsset runnerLevel`; `requirements`),
`WorldDefinition` (`sceneName`, `missions`), `CampaignCatalog` (`worlds`, `runnerSceneName`,
`comingSoonSceneName`; `Resources/Campaign/CampaignCatalog`, loaded with the `StoreSettings.Load()`
static-cache-plus-`ResetStatics` shape).

`CityLevelAsset` / `RunnerLevelAsset` are **empty marker bases** the two game level classes derive
from, so the catalog gets typed inspector slots without this assembly seeing a game assembly. Same
script guid, no field change — existing `.asset` files are untouched.

`UnlockRequirement` is the `LevelObjective` shape: `RequirementType` (append-only) + `[ShowIf]`
knobs. `MinMoney` reads the CURRENT `PlayerStats.Balance`; `MinUpgradeLevel` reads
`PlayerStats.UpgradeLevel(modelId, categoryId)` with the store's raw id strings. "The previous
mission is complete" is implicit in catalog order and never authored.

Seeded by `Tools → FiniteRunner → Create Campaign Assets` (`PoliceEscape/Editor/CampaignAssetBuilder`,
create-or-load, never overwriting): `World_01` on `CarTest`; `Mission_01` = the existing
`TestLevelDefinition` + `FiniteRunner_LevelDefinition`; `Mission_02` = its OWN `Level_02_City` and
`FiniteRunner_Level_02` (Light Speed 7500) so a session that failed to inject the right assets is
visible at once.

## Rules (`CampaignProgress`)

- **Frontier** = the first mission in catalog order not completed (`PlayerStats.IsMissionCompleted`);
  unset once every mission is.
- **Playable** = completed (a replay), or the frontier with its requirements met.
- **Unlocks latch**: the first time a mission's requirements pass, `PlayerStats.Unlock(id)` writes it
  to `unlockedIds` and it never re-locks, even if the balance later drops. A locked mission is
  always shown greyed with the requirement printed (`RequirementText`, Runner — it needs the
  store's labels), never hidden.
- Requirements are evaluated whenever the Store or the MISSIONS map builds its rows.

## The session (`MissionSession`)

Static `Current` + `IsReplay`, set by the Store's START MISSION and the MISSIONS map before the
world scene loads. `LevelManager.Awake` and `GameManager.Awake` **swap their serialized level for
the session's**; the city's handoff loads `runnerSceneName` from the catalog; NEXT MISSION returns
to the Store. **Empty session = direct scene play**: both managers use their serialized assets and
the three old `nextSceneName` / `nextMissionScene` strings, exactly as before — the campaign never
gets in the way of testing a scene. `MainMenuController.Start` clears it (reaching the menu ends
the mission), as does the domain-reload-off `SubsystemRegistration` reset.

## Lifecycle

START → Store (always). START MISSION → `MissionSession.Begin(frontier)` → the world scene →
glitch handoff to the runner → Mission Complete. **The mission latches complete at the rank
slam**, the same call that pays: `PlayerStats.RecordMissionCompleted(missionId, total, rank)` —
the FULL total into the wallet on every completion (replaying is the intended money farm; the
best-of delta bank is gone), and a best-of `MissionRecord` (`bestTotal`, `bestRank`,
`timesCompleted`) that never downgrades. NEXT MISSION → Store; RETRY in place; EXIT → menu.
Missions are **all-or-nothing across a sitting**: quitting before the runner is won loses the
city clear (v1). When the frontier is exhausted, START MISSION leads to the **Coming Soon** scene
(`ComingSoonScreen`, built by `Create Coming Soon Scene`) — the only door to it.

## Screens

- **Store** — the START row is a `MissionRow` labelled `START MISSION — 2: NAME`
  (`MenuTextId.StartMissionTarget`), COMING SOON when done; greyed with the requirement while gated
  (`Enabled` never blocks `Activate`, so `StartMission` refuses the press itself); refreshed after
  every purchase since a buy can cross a money gate.
- **MISSIONS** — a main-menu row **hidden until one mission is complete**, opening
  `MissionSelectScreenFactory`'s campaign map (the LOG recipe: `StatHeaderRow` per reached world,
  a 12-row viewport, rebuilt on every open): completed rows show `S  $12,400` and replay, the
  frontier shows NEXT, locked rows are greyed. Selecting sets the session and loads the world scene
  **directly — the Store is skipped**.
- `MissionRow` (`UI/`) is the `StatRow` shape that DOES confirm; its `RightReserve` is what the
  Store pre-measures the column with.

## Build settings

`Tools → FiniteRunner → Register Campaign Scenes` (`Runner/Editor/CampaignSceneRegistrar`) rewrites
`EditorBuildSettings` from the catalog — MainMenu at index 0, Store, every world scene, the runner,
Coming Soon. Every other trip loads BY NAME, so **run it after adding a world or scene and commit
`ProjectSettings/EditorBuildSettings.asset`** (the list was once committed empty, which broke every
by-name load on a fresh checkout).
