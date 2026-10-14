---
description: Runner race mode — RaceDefinition, RaceSystem, Rival, RivalDriver, RivalKillCam, the RacePosition objective, contact, wrecks and bounties, the RACE debug page
paths:
  - "Assets/01.Scripts/Runner/Race/**"
  - "**/RaceContracts.cs"
  - "**/RaceInstaller.cs"
  - "**/LineDriving.cs"
---

# Runner race mode

Design and decisions: `RaceModePRD.md` (D1–D14, R1–R7). Runner only. A **race level** is a
`RunnerLevelDefinition` whose `race` field holds a `RaceDefinition`; empty = the plain runner, and
nothing below runs.

## Pieces

| Type | Where | Role |
|---|---|---|
| `RaceDefinition` | `Runner/Race/` | The tunables: the `rivals` roster (`RivalEntry`: name, prefab, scale, yaw, hover height, contact half length / half width, `paintTint` (alpha = blend), map colour, cruise fraction, `thrustFactor`, aggression, `mistakeRate`, orb appetite, `ramHull`, `bounty`), Grid, Pace, Contact, Handling, Mistakes, the Kill cam toggle group, Fidelity, Debug. Cloned at bind; gameplay reads only the clone. |
| `RaceSystem` | `Runner/Race/`, `PF_RaceSystem` under `===SYSTEMS===` | The field: pool (one `Rival` per entry, built at bind, never instantiated mid-run), grid + launch, the fixed tick (full sim / ghost), gate zones, contact with the ship, wrecks, finishing, standings (`IRaceStandings`), the on-screen readout and ghost wireframes. `Live` = the bound instance. |
| `Rival` | `Runner/Race/` | One pooled shell: a `TrackBody`, a `RivalDriver`, state (`RacerState`: Grid / Racing / Ghost / Finished / Dnf), run form (`RunThrustScale`, `RunPaceScale`, `SurgePhase`, `LaunchDelay`), contact state, wreck info + a short trace ring for the readout, per-rival paint (material copies, `_MainColor` on RealToon else `_BaseColor`). No colliders. |
| `RivalDriver` | `Runner/Race/` | Plain C#: picks the line every tick (see "Driving"). `Mistake` names the hazard a rolled mistake is on. |
| `RivalKillCam` | `Runner/Race/`, beside `RaceSystem` on `PF_RaceSystem` | The kill cam (R7): slow motion + the rig's focus shot on a wreck near the ship. |
| `RaceDebugPage` | `Runner/Race/` | The pause menu's RACE tab (registry order 6, after TRAFFIC's 5), shown only while `RaceSystem.Live` is bound. |
| `LineDriving` / `LineTuning` | `Runner/Simulation/` | The line rules split out of `PatrolDriver` (orbs, ramps, open edges, sweep braking, `CurveSafety`) — the patrol and the rivals feed them from their own assets. Pure functions. |
| `IRaceStandings`, `RacerState` | `Contracts/RaceContracts.cs` | Read-only standings for the HUD, the minimap and the objective: racer count, the player's position, finished / DNF / racing counts, per-rival distance, state, finish place, map colour, `PlayerPositionChanged`. |
| `RaceInstaller` | `Runner/Editor/` | **Tools → FiniteRunner → Install Race Mode** (`Race_Default`, `PF_RaceSystem` + `RivalKillCam`, nested into `PF_Systems`, the race test level) and **Race: Reset Starter Rivals** (the five-car roster). Idempotent. |

## Wiring

- `GameManager` binds the race when `level.race` is set: `race.Bind(motor, track, generator,
  settings, level.race, …)`, subscribes `RivalDestroyed`, and after `cameraDirector.Bind` binds
  the kill cam (`RivalKillCam.Ensure(race).Bind(race, motor, motor, cameraDirector.Rig)`).
  Unbound, both idle — the sandbox (`Sandbox_Race`) boots that way.
- **A race turns things off**: traffic (a warning if the level has both), and the hyperspace jump
  (`hyperspace.Allowed = level.race == null`).
- The patrol never reads the standings (D14) and never touches a rival.
- `RaceSystem.ResetForRun` (retry, regenerate) parks every rival and re-lays the grid.

## Grid, launch, pace

- The grid is laid behind / around the ship's start (`gridColumns`, row / lane spacing,
  `playerGridSlot`). Each run **rolls a form** per rival (`System.Random`): thrust and pace off the
  authored values by up to `formVariance`, a surge phase (`surgeAmount` over `surgePeriod`), a
  launch reaction inside `launchReaction`, a home lane pulled toward a random one by `laneSpread`.
  Without it the field moved as an evenly spread pack.
- Lights out is the tick the ship first moves: each rival then waits its `LaunchDelay` and gets
  the ship's launch speed. Thrust = `thrustShare` × the ship's base thrust (read at lights out;
  `thrustShare` applies live) × form × surge × the rubber band. `Pace` (the cap) is
  `cruiseFraction` × Light Speed × the rubber band; over pace bleeds at `overPaceBleed`. Orbs give
  a rival `orbBoostShare` of the boost.
- **The rubber band scales the THRUST as well as the cap** (`rubberBand` at full strength
  `rubberRange` from the ship: ahead eases off, behind pushes). The caps sit at 64–82 % of Light
  Speed and the field rarely reaches them, so acceleration is what orders the race — a band on the
  cap alone left a runaway leader uncatchable. The field's thrust stays BELOW the ship's
  (`thrustShare` 0.8): rivals drive clean and lose nothing to beams, walls or braking, so the
  player's edge has to be raw acceleration plus the orbs (rivals get only `orbBoostShare` 0.4).

- **The lead governor** (`leadGovernor`, on by default): a rival AHEAD of the ship has its pace
  capped at the ship's smoothed speed (`leadSpeedSmoothing`) × a factor falling from `leadCapNear`
  (1.05, right ahead) to `leadCapFar` (0.9, `rubberRange` ahead or more), never below the launch
  speed. Rivals lose nothing to hazards while every hit costs the player, so without it a lead
  built on one bad stretch was never caught; with it a player holding their speed always reels the
  leaders in. It is the RACE page's LEAD GOVERNOR / LEADER SPEED CAP rows.

## Two fidelities (D10)

Inside the window round the ship (`fullSimBand`: metres behind / ahead, the ahead edge capped at
the generator's `SettledDistance`) a rival runs the full `TrackBody` sim and is drawn. Outside it
it is a **ghost**: distance, accelerating at `Thrust × ghostPaceFactor` toward
`Pace × ghostPaceFactor` (both — a ghost meets no hazard, so with full thrust it out-ran the
drawn field), hidden, unable to crash. A ghost is seated
back only on plain road (no section, no ramp span), on the nearest lateral clear of obstacles AND
of known gate zones for its hazard look-ahead; a racer is ghosted only when grounded off any ramp.
A ghost crossing the end finishes.

## Driving (`RivalDriver.Drive`)

The line, in order: racing line (or the nearest end ramp once inside the hazard look-ahead of
`EndZoneStart` — a gap if the end mistake rolls) → tube wrap → `SeekOrb` → `PlanRamps` → `PassShip`
→ `KeepOffOpenEdges` → `ShaveEdge` (the edge mistake) → **`AvoidGates` last**. Gates come from
`RaceSystem`'s gate zones, built from the generator's DECIDED placements (`TrackPlacementKind.
LaserGate`), so they are known kilometres ahead of the built road. A vertical beam's width is its
model's, measured off built gates (`verticalBeamHalfWidth` is the floor). `AvoidGates`: the NEXT
gate picks the side — the free road nearest the rival's current lateral — the line is blocked if
it is off that side or the path to it crosses a zone, and within the side it prefers a lane that
also clears the later gates; `gateMargin` keeps the target inside the free interval. Mistakes
(overspeed on a sweep, an edge shave, a missed end ramp) are hashed per hazard with a per-run
salt, so a rival makes or skips a given one consistently through a run.

## Contact, wrecks, bounty (R4, R5)

All analytic, in track space — no colliders. `ShipContact`: side by side, both are shoved apart
(`sideShoveMeters`; the ship through `IControlTakeover.AddLateralShove`); the ship hitting a
rival's tail at `ramClosingThreshold` costs it `ramSpeedCost` and takes a point of the rival's
`ramHull` (at 0: `Rammed`); a rival into the ship's tail is held clear. `ContactSuspended` turns
it all off (the kill cam). Rival ↔ rival keeps `rivalSeparation` across.

`DestroyRival(rival, cause)` — `RivalWreck { Laser, Fell, MissedRamp, Rammed }` — marks DNF,
explodes it with the `GameSettings` explosion look and raises `RivalDestroyed(rival, cause,
byPlayer)`. **byPlayer** = Rammed, or a contact with the ship within `creditWindow`. GameManager
pays `bounty` through `CollectibleManager.AwardMoney` (the `+$N` floater) — held while a kill cam
plays and paid on the cut back. A rival that wrecks on its own pays nothing.

## Standings and the objective

Finish order is lip order (D8): a rival's place is fixed when it leaves an end ramp; the ship's by
`FixPlayerFinish()` from its own lip, inside that tick. The live position of everyone still racing
is by distance. `RunnerObjectiveType.RacePosition` with `RaceGoalMode { Win, TopX, Exactly }`
reads it through `ObjectiveContext` (`IRunState.ObjectiveState`); an impossible goal fails early
(`RunOutcome.RaceLost`, LoseRaceBeaten / LoseRacePosition), and a wrong place at the lip loses
the same way. The HUD's POS readout and the minimap's rival dots read `IRaceStandings`. The Mission
Complete panel shows a RACE section (place, rivals wrecked, bounties) — display only.

## Kill cam (`RivalKillCam`)

GameManager asks `TryPlay(rival)` on every wreck (anyone's). It plays when the wreck was **in
view** (full sim), within `killCamRange` of the ship, the clock reads 1 with no Dodge / Loop / Duel
slow-mo, the cooldown is over, the ship is steady and short of the final run-up, and the rig has
no cinematic or focus shot. Then: the clock eases to `killCamTimeScale` over the blend-in, the rig
cuts to `OrbitCameraRig.SetFocusShot` planted at `killCamOffset` (side away from the ship, up,
ahead, in the track frame), the ship holds its lateral on autopilot, contact is suspended; after
`killCamSeconds` of real time it cuts back and the clock eases home. Clock ownership is
`DodgeSlowMo`'s protocol: anyone else writing the clock ends it at once. While `Active`,
GameManager shields the hull, skips the catch and freezes the countdown; `Ended` pays the held
bounty and grants `killCamGrace` of invulnerability. `Cancel()` on fail, lip and restart. The
patrol is NOT paused (its timers ride the slowed clock).

## Debug

- `debugReadout` prints the standings and every rival's state, gap, speed, pace, thrust, hull,
  mistake, wreck note (cause, lateral, line, a 2 s trace) and full-sim vs ghost averages; the
  wreck is also logged as `[Race] … wrecked`. `debugGhostWireframes` draws each ghost as a
  wireframe box in its map colour (GL, `OnRenderObject`, depth test off).
- The RACE page: actions WRECK NEAREST RIVAL (in view first, credited to the player — the kill
  cam's and the bounty's test), WRECK ALL RIVALS, ALL RIVALS FINISH; sliders for thrust, rubber
  band, ghost pace, orb boost, form variance, surge, lane spread, ram speed cost, side shove, kill
  credit window, the kill cam's switch / slow motion / hold / range; the two debug toggles. Rows
  write the asset (`DebugAssetEdits.Touch`) and mirror the clone; form variance and lane spread
  are rolled at the grid, so they bite on the next run. Actions are queued and run on the next
  race tick — from the pause menu, after it closes, so a wreck can still play the kill cam.
- Odin buttons on `RaceSystem`: Finish Leader, Kill Nearest, DNF All, Finish All.

## Known gaps

R4.2's forward kick to the ship on a rear ram (the takeover API has none) and R4.3's weighted side
shove (both cars move the same) are not built.
