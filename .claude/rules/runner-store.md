---
description: Store scene — the hub between missions, upgrade definitions, purchase flow and upgrade appliers
paths:
  - "Assets/01.Scripts/Runner/Store/**"
  - "**/StoreSettings.cs"
  - "**/StoreSection.cs"
  - "**/UpgradeDefinition.cs"
  - "**/UpgradeRow.cs"
  - "**/StoreUpgrades.cs"
  - "**/ShipUpgradeApplier.cs"
  - "**/CarUpgradeApplier.cs"
  - "**/StoreSceneBuilder.cs"
  - "**/StoreStage.cs"
  - "**/StoreMediaPanel.cs"
---

# Store

`Runner/Store/`, namespace `ConfusedGameDev.FiniteRunner.Store`. Scene `Store.unity`, built by
`Tools → FiniteRunner → Create Store Scene` (`StoreSceneBuilder`).

## Where it sits in the flow

The hub between missions. Main menu START loads it **by name**
(`MainMenuController.FinishStart`, `StoreSettings.SceneName`, never by build index — only MainMenu
sits at index 0). Its START MISSION row is a `MissionRow` naming the campaign's frontier
(`START MISSION — 2: NAME`), opening a `MissionSession` and loading that world's scene; greyed with
the requirement while gated, COMING SOON once the catalog is exhausted — see `campaign.md`.
`StoreSettings.nextMissionScene` (`CarTest`) is only the fallback when no catalog exists. The
runner's Mission Complete NEXT MISSION returns to it. Back goes to the main menu. All of it through
the loading curtain.

## Layout

Three tabs on the menu framework, cycled with the bumpers / Q-E (`DebugMenu` tab state):

- **CAR** — Speed / Acceleration / Weight / Resistance / Handling
- **SHIP** — Handling / Dash Power / Speed Multiplier / Jump Strength
- **CHARACTER** — Hacking Speed / Hack Value / Strength / Range / Accuracy (saved only, no effect
  yet)

Each tab is a left column of rows over the **`StoreStage`**, a hand-placed turntable the main
camera looks at: one slot per section holding the model instanced in edit mode (Quadron /
`nabucodonosor.fbx` / `PF_ROB`), only the active one visible, yawed by the right stick or a mouse
drag off the rows, idling back into a slow spin. `PrepareInstance` seats a model from its
`StoreModel` entry and strips a LODGroup to LOD0; the Reframe button redoes it.

Rows: the model row (`MenuChoice`, `< QUADRON >`, one entry today — the seam for more), one
**`UpgradeRow`** (`UI/`, ten pips + the next price, `MenuRow.Enabled` greys it when unaffordable,
MAX at 10) per category, then the START MISSION `MissionRow` (refreshed after every purchase, since
a buy can cross a money gate).

The column's X is **pre-measured** from the tab's widest row in any language
(`MenuRow.LabelInsetWidth` + `MenuTextLibrary.MaxWidth` + the row type's `RightReserve`) so plates
stay clear of the left edge and the centre stays open.

The wallet top-right is the shared `MoneyHud` with a `ValueSource`
(`PlayerStats.Balance` = `global.moneyEarned − moneySpent`, so the LOG's lifetime earnings never
shrink), counting down through purchases. The media panel at the right (`StoreMediaPanel`, the
Mission Complete video plate made swappable — one `VideoPlayer` on DSP time, a still, else
NO SIGNAL) shows the piece of the focused row's **next** level.

## Buying

One Confirm press: `PlayerStats.TrySpend(cost)`, `SetUpgradeLevel`, `PlayerProfileStore.Save()` at
once (a purchase is a commit point), meter and wallet punched. **No confirm dialog, no refunds.**

## Data (`Assets/04.Data/Resources/Store/`)

- One `UpgradeDefinition` per category: `id` (an `UpgradeIds` constant), localized `label`,
  default video/still, exactly ten `UpgradeLevel` rows `{cost, multiplier, video, image}`. The
  builder seeds cost `1500 × 1.5^(level−1)` ($1,500 → $57,665) and multiplier `1 + 0.05 × level`,
  **never overwriting an existing asset** — a retune of the curve means editing the fourteen
  authored assets too (the 500 → 1500 change was applied to them by script).
- A `StoreSection` per tab (kind, title, `models`, `categories`).
- `StoreSettings` (the no-catalog fallback scene, the three sections, viewer feel), loaded through
  Resources so gameplay resolves multipliers with no scene reference.

**The profile stores LEVELS, never multipliers** (`PlayerProfile.upgrades`,
`{modelId, categoryId, level}`). `StoreUpgrades.Multiplier(kind | modelId, categoryId)` reads the
level and looks the multiplier up on the definition every time, so a retuned table retunes saved
games. Anything unresolvable reads ×1.

## Applying

- **Ship** — `ShipUpgradeApplier` on a fresh `ShipDefinition` clone: Handling → `lateralSpeed` and
  `handlingResponse`; Dash Power → `dashDistance`; Speed Multiplier → `passiveDeceleration`
  **divided** (a slower bleed); Jump Strength → `ShipDefinition.jumpStrength`, which
  `ShipMotor.TakeOff` multiplies into both the arc length and the lip boost.
- **Car** — `CarUpgradeApplier.Clone` in `CarFactory.Spawn` gives the PLAYER a per-spawn
  `CarConfig` clone (Speed → `topSpeedKmh`; Acceleration → `maxMotorTorque` + `evpDriveForce`;
  Weight → `mass`, heavier; Handling → `maxSteerAngle`, `steerResponse`, `sideStiffness`,
  `evpTireFriction`) so police, traffic and the debug pages keep the asset — **the debug pages
  edit the asset and never see the clone**.
- **Resistance** divides `amount` at the top of `LevelManager.ApplyDamage`, the one player-damage
  entry point.

Every store string is a `MenuTextId` (`Store*`, `Upgrade*`, `StartMission`, `Max`, `HintBuy`…).
Category labels are kept short because the purchase row's reserve is wide.
