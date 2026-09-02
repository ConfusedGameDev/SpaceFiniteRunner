# Finite Runner — Track Features PRD

**Scope:** the hover-ship runner (`Assets/01.Scripts/Runner/`, namespace `ConfusedGameDev.FiniteRunner`), scene `Assets/05.Scenes/FiniteRunner_Test.unity`.
**Goal:** make the endless track *interesting* — the road stops being a flat ribbon with orbs on it and becomes something the player reads and reacts to at speed.
**Status:** design agreed 2026-09-02. M1 (camera), M2 (jumps) and M3 (loops) implemented 2026-09-02; jumps passed their editor test, loops await theirs.

---

## 0. Summary

Four features, delivered **one at a time**, each played, tested and refined before the next starts:

| # | Feature | One-liner | Status |
|---|---------|-----------|--------|
| 1 | **Jumps** | Optional ramps that throw the ship into a speed-scaled arc | Implemented (§3), in test |
| 2 | **Loops** | Mandatory vertical loops with a minimum entry speed | Implemented (§4), in test |
| 3 | **Cylinder sections** | Stretches where the road curls into a tube the ship can run around and under | Root decisions only (§5) |
| 4 | **Multi-path** | Branching routes with their own challenges and rewards | Deferred; single-spline discipline only (§6) |

Feature 1 also carries a **camera migration**: the runner moves from a camera parented under the ship to the city chase's Cinemachine rig, shared between both games (§7).

Second-level decisions for loops and cylinders are deliberately **not** taken yet — the jump test cycle is expected to change them.

---

## 1. Current state (facts the design builds on)

- **Pose already supports roll.** `TrackManager.GetPose` evaluates the spline's own up vector (`spline.Evaluate(t, out pos, out tangent, out up)`), and the ship, patrol and decorator all take that rotation verbatim. Knots simply never carry rotation today (`TrackGenerator.AddSegment` adds `new BezierKnot(endPosition)`, Y hardcoded to 0). Loops and tubes are a **generator** change more than a motor change. One hazard: `right = cross(up, fwd)` is not `normalizesafe`, so a vertical tangent must never coincide with up.
- **The ship root never leaves the flight line.** Hover height, bob and bank live on the `visual` child so the root's trigger box stays on the line and pads keep working. A jump therefore has to decide whether the root rises (it does — §3.4).
- **Detection is trigger-only.** The ship is a kinematic rigidbody with a trigger `BoxCollider` (5 × 4.6 × 12.3 m); `SpeedPad.OnTriggerEnter` finds the motor with `GetComponentInParent<ShipMotor>()`. No layers or tags. Any new "collision" is a trigger enter that the motor resolves itself.
- **Orbs bob along world +Y** (`OrbHover`), not the track up. They would float sideways off a tube.
- **The generator has one code path.** A yaw-only hop per knot (300–420 m), AutoSmooth tangents, a trailing `SettleMargin` of two segments that nothing is placed on, pads placed by an independent `padCursor` at 150–220 m spacing with a probability table. There is no notion of a section or piece type.
- **The camera is not a rig.** `Main Camera` is parented under the ship root at (0, 9.4, −21.2), pitched ~12° down, FOV 71, with `CameraShaker` rewriting its local pose every `LateUpdate` and a stray URP `FreeCamera` component. It rolls with the track for free and has no damping.
- **Live speeds are large.** Launch 249 m/s, light speed 4000 km/h (≈ 1111 m/s), passive bleed 6 m/s², a green orb ≈ 243 m/s with the current debug multipliers. Speed swings by launch-sized chunks per pickup, so **fixed thresholds are walls early and trivial late**.
- **The city camera rig is car-shaped.** `OrbitCameraRig.SetTarget(CarController)` reads rigidbody speed for the FOV kick and the chassis box for the first-person eye, binds the orbit `LockToTargetWithWorldUp`, and lives in the `PoliceEscape` assembly, which references `Runner` (not the reverse). Only `PoliceEscape` references Cinemachine.

---

## 2. Cross-cutting rules

### 2.1 Placement — the generator owns features
- `TrackGenerator` gains a **feature table** next to the pad `spawnTable`: one `FeatureSpawnEntry` per kind with `probability` (auto-rebalanced to 100 % through the same `NormalizeProbabilities` rule), `minSpacing`, an optional visual `prefab` (primitive fallback when empty) and a **definition asset** per kind (`JumpDefinition`, later `LoopDefinition`, `TubeDefinition`) carrying the kind's tunables as Odin sliders.
- Features draw from the **same seeded `rng` stream** as pads, so a non-zero seed reproduces the whole layout, features included.
- **A feature claims its footprint** (its length plus a margin). The pad placer skips any spot inside a claimed footprint — an orb never spawns half inside a ramp.
- **Spacing assumes the worst case.** The generator does not know the ship's speed at placement time, so a jump's *maximum* air distance (fixed by the vertical-speed cap, §3.4) defines the exclusion zone: no other feature within that distance ahead of a ramp. Pads and orbs *may* sit under the arc — the player simply misses them.
- Features are spawned objects: they stream ahead with `aheadDistance` and are culled behind with the rest (`spawned` list). Knots are still never removed.

### 2.2 Ship state
`ShipMotor` gains a `ShipState` enum — `Grounded`, `Airborne` now; `Looping`, `OnTube`, `Falling` reserved for features 2 and 3 — with `StateChanged`, `TookOff` and `Landed` events plus `AirTime` and `AirHeight` readouts. Everything that later wants to know "is the ship flying" (air-lane pickups, camera, HUD, haptics) reads the enum rather than a per-feature flag.

### 2.3 Patrol
The patrol is a **pressure gauge, not a racer**. It keeps following the centre line by distance, oriented to the track pose (so it rolls with a tube and rounds a loop for free), never takes a jump, never checks the loop speed rule and never fails a loop. Its punishment-free run through a loop the player just fell out of is intended: falling costs the player gap.

### 2.4 Tunables & debug
- Every knob is an Odin `[PropertyRange]` / `[MinMaxSlider]` on a ScriptableObject, inline-edited where it is used, never a field on a manager.
- The pause menu's debug pages gain a **Features tab**: per-kind probability + spacing, and the definition sliders. Values persist through a new `FeatureDebugSettings` asset (`Data/Resources/FiniteRunner_FeatureDebug.asset`) in the `TrackDebugSettings` pattern: captured on change, flushed on resume/reload, re-applied in play mode while `applyOnLoad` is on.

### 2.5 Multi-spline discipline (the only multi-path work now)
All spline access goes through `TrackManager`. Concretely: the generator's knot adds (`track.Spline.Spline.Add(...)`) move behind a `TrackManager` API, and no consumer reads the `SplineContainer` directly. No route abstraction, no branch model — that is guessing before features 1–3 exist.

---

## 3. Feature 1 — Jumps

### 3.1 Player-facing behaviour
- A **ramp** sits somewhere across the track. Steer onto it and the ship rides up and launches automatically — **no button**. Steer past it and nothing happens. Taking it or skipping it is pure steering, like aiming for an orb.
- Takeoff gives a **speed boost** and throws the ship into an arc that is **longer and higher the faster you were going**.
- In the air you still steer and dash, at **half authority**, speed keeps bleeding, and everything on the ground (orbs *and* brake pads) passes underneath untouched.
- Hitting the ramp **from the side** is a wall: the ship is stopped laterally, rumbles, glitches and loses a slice of speed.
- Landing: a small shake and rumble, no speed change. A brake pad under the landing spot triggers normally.
- The camera **pulls out to the Far framing** for the arc and eases back to whatever mode you had on landing.

### 3.2 The ramp
| Knob | Default | Where |
|------|---------|-------|
| Width | 0.25 × track width (30 m on the live 120 m track) | `JumpDefinition.widthFraction` |
| Run-up length | 60 m | `JumpDefinition.length` |
| Ramp angle | tuning knob | `JumpDefinition.rampAngle` |
| Per spot | one ramp, never side by side | rule |
| Lateral | random inside the track, ramp fully on the road (same `MaxLateral` rule as pads) | generator |

Visual: optional prefab; fallback a primitive wedge tinted from the entry colour. Colliders forced to triggers like pad prefabs.

### 3.3 Entering the ramp and side hits
- **Front entry** (the ship's trigger box enters the ramp's entry volume across its front edge): the ship is **committed** — lateral is clamped to the ramp width (side rails) for the run-up, the root follows the slope, and takeoff fires at the lip. At live speeds the run-up lasts a quarter second or less; an abort window that short would only trigger by accident.
- **Side hit** (the box enters a ramp *side* volume): reuse the dash wall-hit path — `WallHit` event, lateral velocity zeroed, ship held outside the ramp width until it is past, the existing glitch (`dashWallGlitchStrength`) and rumble — plus a **speed loss as a fraction of current speed**, default 15 % (`JumpDefinition.sideHitSpeedLoss`). A fraction, because speed grows all run and a fixed number stops mattering.

### 3.4 The arc (root rises)

> **Implementation note (M2):** the arc is authored in *track distance*, not time. A time-domain ballistic arc with a vertical-speed cap bounds the flight *time* but not its *length* (length = speed × time, and speed is unbounded), so the generator could never space features safely. Instead: air distance = `clamp(takeoffSpeed × airDistancePerSpeed, airDistanceRange)`; the ship leaves the lip tangent to the ramp and follows `y(s) = h0 + tan(angle)·s − a·s²` with `a` chosen so it lands at exactly that distance. Faster still means higher and longer, and `airDistanceRange.y` is the exclusion the generator keeps clear ahead of every ramp. The `launchFactor` / `gravity` / `maxVerticalSpeed` knobs below are superseded by `airDistancePerSpeed` / `airDistanceRange`.
- Takeoff boost: `GameSettings.powerUpSpeedBoost × FeatureSpawnEntry.multiplier` (default 1.0 — a green orb's worth), delivered through `ShipMotor.AddSpeedImpulse`, so the "+N" floating text, pad camera shake and pad rumble come for free and the boost is weight-scaled like every other impulse.
- Vertical launch speed `v_up = takeoffSpeed × sin(rampAngle) × launchFactor`, **capped** at `JumpDefinition.maxVerticalSpeed`. A fake gravity `JumpDefinition.gravity` pulls it back. Faster = higher *and* longer, the cap keeps a purple-orb run out of orbit and fixes the maximum air distance the generator spaces by (§2.1).
- **The root rises.** Height is added along the track's up vector on the root, not the visual, so the trigger box leaves the ground lane and ground orbs/brake pads are physically missed rather than ignored by a flag. This is what makes a future air lane trivial: air pickups are ordinary pads placed at height.
- Landing is when height returns to 0 along the arc: state → `Grounded`, `Landed` fires, impulse shake + rumble, the root is back on the flight line.
- Airborne steering: `lateralSpeed` and dash both at **50 %** (`JumpDefinition.airControlFactor`). Lateral stays clamped to the track width. The visual pitches with the arc's vertical velocity for readability.

### 3.5 Air lane (prepared, not populated)
- `PadSpawnEntry` gains `lane` (`Ground` / `Air`). Air entries are placed at `GameSettings.airLaneHeight` above the flight line; they exist in the table at **0 %** for now.
- `OrbHover` bob and sway switch from world axes to the track pose's up/right so orbs survive loops and tubes later.

### 3.6 Feedback summary
| Moment | Text | Camera | Rumble | Glitch |
|--------|------|--------|--------|--------|
| Takeoff | "+N" (via `PadImpulse`) | blend to Far, pad shake | pad pulse | — |
| Side hit | — | wall shake | wall pulse | wall glitch |
| Landing | — | landing impulse | short pulse | — |

### 3.7 Acceptance
- With the jump entry at 100 % and spacing at minimum, every ramp reads at launch speed and at 3000 km/h; taking one from the centre never clips a side rail.
- Ground orbs under the arc are not collected; a brake pad at the landing point is.
- Seeded runs place ramps identically.
- No ramp footprint overlaps another feature or the settle margin; no orb spawns inside a footprint.
- Pausing mid-air freezes the arc and resumes it; `Restart()` mid-air lands the ship on the line.
- Camera returns to the pre-jump mode on landing; Tab does nothing while airborne.

---

## 4. Feature 2 — Loops

> **Second-level decisions (agreed 2026-09-02, implemented):** a loop is a **distance-insert section** (`LoopSection`) with its own pose function — the spline stays flat, so every distance-based consumer rides it unchanged and the decorator stamps the ring's road for free. Pure vertical circle across the full track width, radius 100 m (40–250). Verdict taken **at the gate** against a speed fixed when the loop is placed (floor 1200 km/h + 18 km/h per 100 m, cap 2900), so the gate's colour is a promise. A fail rides to the top and **falls visibly** straight down onto the exit under a fake gravity (120 m/s²), losing 40% of speed; distance is parked at the exit so the patrol gains the whole fall. Telegraph: portal-frame gate at the mouth tinted green/red every frame, plus a floating alert at `loopAlertLeadMeters` (400 m). No pads inside a loop; the road is the ordinary road prefab in chords; a prefab slot on the entry scales by the radius.

- **Mandatory.** The track *is* the loop; there is no bypass lane. The loop is what sells the speed.
- **Speed requirement** = a floor plus a ramp with distance travelled, capped well under light speed, all on `GameSettings` (`loopSpeedFloor`, `loopSpeedPerMeter`, `loopSpeedCap`). Never a fixed number alone.
- **Telegraphed** by a colour-changing gate ring at the loop mouth (green = fast enough, red = not) and a floating alert in the patrol-warning style ("LOOP x M — NEED n"). No new HUD element.
- **Failure:** entering too slow drops the ship from the top of the loop — rumble + glitch — and it reappears on the track *after* the loop with a heavy speed penalty and the seconds lost. Not a game over; the game already has three loss paths. The patrol does not slow during the punishment.
- **Patrol** runs the perfect loop every time on the path, no speed check.
- **Deferred until jumps ship:** loop radius/size, whether the loop is a spline section or a placed piece with its own pose function, exact fall-out staging (how long the ship is off the track, where it lands, penalty fraction), whether orbs/pads sit inside a loop, ring visuals.

---

## 5. Feature 3 — Cylinder sections (root decisions)

- The road runs on the **outside** of a cylinder; full 360° is possible, so the ship can hang under the track. This is the "running through the code, not on a road" feel.
- **Sections, not the whole track.** The generator decides where a tube stretch starts and ends; flat road curls into the tube and uncurls out of it (transition length is a section knob).
- **Steering stays bounded.** On a tube, lateral becomes an **angle** around the tube. Steering and dash move the ship within a **per-section angular band** (e.g. ±60° for a gentle curl, ±180° for all-the-way-under) around a **per-section centre offset** (0 = top; an inverted stretch is a tuning choice, not a special case). Never endless spinning. The existing `WallHit` at the band edge carries over.
- **Camera** follows the ship's up vector with a short roll-only lag (§7).
- **Patrol** rides the centre line, rolled with the pose.
- **Deferred until loops ship:** tube radius vs track width, how the tube's up vector is authored (knot rotation vs a lateral→angle mapping in `TrackManager`), road/barrier meshes on a tube, orb placement around the tube, whether ramps can appear on tube sections.

---

## 6. Feature 4 — Multi-path (deferred)

Branching routes where the player picks a direction, each branch with its own challenges and rewards. Still WIP as a design; **not to be designed before 1–3 are complete**. The only work now is §2.5 — keep every consumer behind `TrackManager` so a distance→(spline, t) route layer can be introduced later without touching them.

---

## 7. Camera migration (delivered with Feature 1)

### 7.1 Shared rig
- `OrbitCameraRig` + `OrbitCameraSettings` move out of `PoliceEscape` into a new shared assembly (`ConfusedGameDev.FiniteRunner.CameraRig`, referencing Cinemachine, UI, FX) so `Runner` can use them without referencing the city.
- The rig follows an **`ICameraTarget`** (transform, speed in km/h, eye anchor) implemented by `CarController` (rigidbody velocity, chassis box) and `ShipMotor` (`CurrentSpeed × 3.6`, authored eye offset). `SetTarget(ICameraTarget)` replaces `SetTarget(CarController)`.
- The city's air-time pan mute (`AirTimeSlowMo.IsActive`) becomes a hook the city sets on the rig; the rig itself no longer references city types.
- The rig's `MainMenuController.IsOpen` gate and `MenuNavigator.CameraCyclePressed` stay (both are in assemblies the rig can reference).

### 7.2 Settings
- New knob **`upBinding`**: `WorldUp` (the car keeps `LockToTargetWithWorldUp`) or `TargetUp` (the ship: the orbit follows the target's full rotation so it rolls through loops and under tubes), with a **roll-only lag** (`rollLagSeconds`) on the `TargetUp` path — position stays rigid so speed reads the same.
- Ship asset `Assets/04.Data/FiniteRunner/Fighter_CameraSettings.asset`, inline-edited on `GameManager` like the other settings. **Far** seeded from today's camera (9.4 m up, 21.2 m back, ~12° down, FOV 71), **Close** derived at roughly half, FOV kick scaled to the ship's band (the cap does the work at 4000 km/h), deoccluder **off** for the ship, first-person eye from a hand-authored `firstPersonEyeOffset` (the box-derived eye would sit 2.3 m above the hull).
- All three modes (Far / Close / First person on Tab / Back) stay on the ship.

### 7.3 Jumps
Takeoff blends into Far over `modeBlendSeconds`; landing blends back to the mode the player had (no-op if it was Far). **Tab is locked while airborne** so the forced Far cannot be undone mid-arc.

### 7.4 Scene changes
- `Main Camera` is unparented from the ship and gets a `CinemachineBrain`; the URP `FreeCamera` component is removed.
- `CameraShaker` / `ShakeOnPad` are **replaced** by Cinemachine impulse (an impulse source fired on pad hits, side hits and landings; listeners on the vcams). The existing `CameraShakeSettings` assets remain the amplitude/duration source.
- `SceneSystemsPlacer` (or the runner's equivalent) places the rig and its `FirstPersonCamera` sibling under `===SYSTEMS===` in the runner scene.

---

## 8. Milestones

Each milestone ends in a playable build and a tuning pass before the next begins.

**M1 — Camera migration.** Shared assembly, `ICameraTarget`, up-binding knob + roll lag, ship settings asset, impulse shake, scene rewire. *Done when:* the runner plays exactly as today on the new rig (Far), all three modes cycle, pad shake works, the city scene is unchanged in feel.

**M2 — Jumps** *(done 2026-09-02; detection is analytic on distance/lateral rather than trigger volumes, see §3.4 note)*. `ShipState`, feature table + `JumpDefinition` + footprint rule, ramp spawn with side/entry volumes, ballistic arc on the root, half-authority air control, side-hit penalty, landing feedback, forced Far, Features debug tab + `FeatureDebugSettings`, air-lane fields, orb hover on track axes, knot adds behind `TrackManager`. *Done when:* §3.7 passes.

**M2.5 — Test & refine jumps.** Play sessions; retune ramp size, arc, cap, penalties; revisit §4/§5 deferred decisions with what was learned.

**M3 — Loops** *(done 2026-09-02, see §4 note)*.

**M4 — Cylinder sections.** Second-level design round first (§5 deferred list), then implementation.

**M5 — Multi-path.** Design from scratch on top of 1–3.

---

## 9. New assets and files (expected)

| Kind | Path |
|------|------|
| Definition | `Assets/04.Data/FiniteRunner/Jump_Definition.asset` (`JumpDefinition`) |
| Camera settings | `Assets/04.Data/FiniteRunner/Fighter_CameraSettings.asset` |
| Debug persistence | `Assets/04.Data/Resources/FiniteRunner_FeatureDebug.asset` (`FeatureDebugSettings`) |
| Code | `Runner/Track/Features/` — `FeatureSpawnEntry`, `JumpDefinition`, `JumpRamp` (the spawned component), footprint bookkeeping in `TrackGenerator` |
| Code | `Runner/Ship/ShipMotor.cs` — `ShipState`, airborne integration, `ICameraTarget` |
| Code | `CameraRig/` (new asmdef) — `OrbitCameraRig`, `OrbitCameraSettings`, `ICameraTarget`, impulse shake |
| Settings | `GameSettings` — `airLaneHeight`; loop speed knobs arrive with M3 |

---

## 10. Open questions (parked, by design)

- Loop: size, section-vs-piece, fall-out staging, contents, ring art.
- Cylinder: radius, up-vector authoring, meshes on a tube, orbs on a tube, ramps on tubes.
- Multi-path: everything.
- Air lane: which orb tiers live there and at what odds (needs jumps in players' hands first).
