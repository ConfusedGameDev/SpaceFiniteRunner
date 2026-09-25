# PRD — The patrol duel: attack runs, the tug of war and the dash finisher

| | |
|---|---|
| **Status** | In progress. All design decisions settled in the grill session on 2026-09-24. **M0–M4 landed; M5 and M6's mechanics landed.** **2026-09-25: the cinematic duel (`PoliceChaseImprovement.md`) landed — D35–D41 below supersede D5, D13, D16, D30, D33 and R3.3.** Outstanding: M5's hands-on tuning pass (OQ1, OQ3–OQ7), duel AUDIO (needs new clips), and the `runner-hud-screens.md` / minimap items that a parallel session owns. Open numbers are listed in §13. |
| **Branch** | `feature/finitePatrolUpdate`. |
| **Scope** | Runner game only (`FiniteRunner_Test`, campaign runner levels). The city chase is untouched, and so are `Campaign` and `SaveData`. |
| **Related** | `SpaceShipUpdatePRD.md` (the M9 physics cutover this builds on), `.claude/rules/runner-ship.md`, `.claude/rules/runner-hud-screens.md`, `.claude/rules/ui-menus.md` |

---

## 1. Problem

The patrol is a thing you flee, and it is not very good at that either. Concretely:

- **It barely arrives.** `Police_PatrolDefinition.asset` ships `rubberBand: 0.95` — the patrol
  *targets five percent slower than the ship* — from `startGap: 548` m, with `ramp: 0.083` m/s²
  and `patrolRedeploySpeedFactor: 1` (a redeployed patrol arrives at exactly the ship's speed and
  never closes). The only thing that ever brings it to the bumper is the slowly-rising speed floor.
  The class doc-comment at `PolicePatrol.cs:11-15` still claims it always closes in; the asset says
  otherwise.
- **When it does arrive, the run just ends.** `UpdateCatch` (`PolicePatrol.cs:405-416`) is an
  abstract proximity test: inside `catchDistance` (10 m) *and* `catchLateral` (18 m), or inside
  `catchDistance` for `sustainedCatchSeconds` (1.5 s), and you are arrested. There is no exchange,
  no counterplay, nothing to read and nothing to answer.
- **There is no interaction surface at all.** The patrol has **no collider** — `AddPart` strips it
  deliberately (`PolicePatrol.cs:580`) because at Light Speed nothing in the runner can be detected
  by contact. It has no health, no damage API, no `Destroyed` event. You cannot bump it, ram it,
  or kill it. Its entire relationship with the ship is distance arithmetic.

We want the patrol to become something you *fight*: it hunts you down, pulls alongside, tries to
shove you off the road, and you can break it and buy yourself a gap — at a price.

## 2. Goals

1. The patrol reliably closes and **commits** to a readable attack run, then backs off. The chase
   has a rhythm of approach → exchange → gap, instead of a permanent tailgater or a distant dot.
2. Being alongside the patrol is a **survivable situation with counterplay**, not a death.
3. The player can **destroy** the patrol with the lateral dash, and the kill is spectacular.
4. Hitting the patrol **from behind** matters and costs something.
5. Grabbing a strong boost orb while hunted is a **deliberate offensive play**.
6. The whole thing escalates: each kill buys a gap and makes the next patrol harder.
7. The encounter never fires where it makes no geometric sense (loops, tubes, ramps, the final
   run-up).

## 3. Non-goals

- **Changing the win condition.** The `RunnerLevelDefinition` objectives plus leaving by an end
  ramp still decide the run. Killing patrols is never an objective.
- **A second patrol.** There is exactly one `PolicePatrol` object, teleported (`GameManager.cs:47`
  is a single serialized reference, never a list). The "destroyed" patrol is the same object
  recycled. Multiple simultaneous patrols are out of scope.
- **Real colliders on the patrol.** All contact stays analytic, per the project invariant.
- **A weapon on the ship.** The dash remains the ship's only offensive verb. No projectiles, no
  shield, no new `GameAction` for firing.
- **Touching the city chase**, the campaign, mission flow, or the Store's upgrade set.
- **Multi-path track.** Still unbuilt, still not part of this work.

## 4. Settled design decisions

| # | Topic | Decision |
|---|---|---|
| D1 | The arrest | The **proximity catch is removed**. A **long-fuse tail-time catch** survives as the punishment for refusing to engage (§5.10). The patrol's normal weapon is the shove, not the arrest. |
| D2 | The shove | A **QTE tug of war** on an abstract bar (D14), initiated when the patrol pulls alongside. |
| D3 | Where it hurts | The shove **pins you into the wall** on walled sections (hull) and **throws you off** on open-edged ones (hull + time). Same move, different consequence. The patrol **times its attack run for open-edged sections** and holds back on walled stretches. |
| D4 | Rear ram cost | A rear ram **costs you speed**, not hull. Speed is the game's currency, so the aggressive option is priced in the resource the run is about. |
| D5 | The finisher | The dash is **disabled for the duration of the tug of war**. On winning, a second prompt appears — **LB or RB, whichever side the patrol is on** — and the dash **passes through** the patrol, leaving an explosion where it was and **teleporting it to a safe distance behind**. The same object is recycled. |
| D6 | Patrol ahead of you | If you brake and it overshoots, it **brakes to re-match**, and while it is ahead it is **both a live obstacle and a live target** — rammable, in your racing line, and able to hit a laser gate or fall off a flat sweep itself. |
| D7 | Supply | **Infinite and escalating** (D22). Coasting can never clear the track. |
| D8 | Approach | An explicit **attack run**: inside `warnDistance` the patrol **overdrives past the ship's speed** to force the alongside position, then falls back to the conservative cruise band after the exchange. The overdrive burst is the telegraph. |
| D9 | Dash economy | A kill **drains the whole dash meter** (both charges), so killing costs the evasive tool for ~1.8 s while a fresh patrol is inbound. |
| D10 | Arming tiers | **Blue and purple orbs** arm the instant-kill window, for ~3 s. Green is far too common (46 % of spawns) and purple alone far too rare (3 %). |
| D11 | Shove damage | The shove deals **no hull of its own**. The damage is whatever the shove puts you into — wall (25) or fall (30). This also avoids a trap: a shove that dealt its own damage would grant 1 s of invulnerability and *shield* you from the wall hit that follows. |
| D12 | Losing the mash | You get **shoved, with D3's consequence**, then the patrol **breaks off into normal chase with a cooldown** before it can commit again. It is never a loss condition and never chains. |
| D13 | Control during the QTE | **Soft assist** (superseded by D30) — see D30. |
| D14 | The bar | An **abstract horizontal bar**, starting at the **centre**. The patrol pushes it toward its own side; you mash to push it back. **Mirrored**: a patrol on your right pushes the bar right and you push left, and vice versa. |
| D15 | The button | **Fixed, not randomized** (superseded by D25) — see D25. |
| D16 | Missing the finisher | The finisher is **missable** with a generous window (~1 s). Miss it and the patrol simply breaks off and drops back: you won, you kept your hull, you just did not get the kill. Pressing the **wrong side** dashes you the wrong way. |
| D17 | Time | The world **slows to 30 %** for the exchange, to put the focus on the tug of war rather than the track. Which clocks scale is D31. |
| D18 | The 3-hit pool | The patrol keeps a **3-point damage pool**, but it **does not kill**. It sets **how hard the tug of war is** — 3/3 is a full-strength push, 1/3 is a pushover. Rear-ramming is *preparation for the exchange*, not a competing kill path. |
| D19 | Armed power-up | An armed window **skips the tug of war entirely**: the patrol pulls alongside and you go straight to the finisher prompt. This is the mechanical expression of "kill it immediately after a power-up". |
| D20 | Arrest vs. the QTE | The tail-time arrest timer is **suspended for the duration of a committed attack run**, and its fuse is **lengthened** (6–8 s) since it now only punishes a patrol tailing you *without* committing. |
| D21 | Forbidden ground | The encounter **mirrors the laser gate rule exactly**: never on or near a ramp, its landing, a loop, a tube, or the final run-up. The patrol holds its attack run until the track allows it. Tubes are the hard case — `Lateral` wraps around the circumference, so there is no edge to be pushed off and the tug of war would have no stake. |
| D22 | Escalation | Each kill raises the **speed floor**, **how soon it commits**, and **the tug of war's push strength** — the last one **capped** so it never becomes unwinnable. Early run you brute-force the mash; late run you soften it up first. |
| D23 | Aborting | **Breaking the alongside geometry by any means aborts the run** — brake behind it, or out-steer it laterally (the ship does 30 m/s to the patrol's 22). It goes on a cooldown. You escape cheaply and get nothing: no kill, no teleport, the problem is still there. |
| D24 | What counts as a ram | **Behind it *and* closing above a relative-speed threshold.** Slower contact is a harmless nudge. Available whenever it is ahead of you (D6), never during an exchange — during one you are alongside, not behind. |
| D25 | The button, settled | **One fixed button, no randomization**: **X / ButtonWest** on gamepad and one key on keyboard (OQ2). The original intent was to randomize across the face buttons, but the pool does not exist — see §7.4. Randomization would also force the player to read a glyph at the exact moment D29 makes the road the thing worth watching. If the encounter feels rote, D22's rising press cadence is the lever. |
| D26 | RPG dialogue | RPG lines are **queued for the duration of an attack run and flushed after**. The patrol taunt fires on the *approach*, before it commits, which is where it belongs dramatically; any purple-orb line pays off *after* the kill. |
| D27 | Rebind protection | The QTE button is added to the **`ControlBindings` reserved set** so it can never be bound to anything else. |
| D28 | Prompt | Superseded by D32. |
| D29 | Losing the bar | The bar bottoming out **triggers a scripted shove on the real ship**: assist releases, the patrol slams you the direction the bar was going, and you are pinned to the wall or thrown off exactly as D3 and D12 describe. **The bar is the contest; the road is still the stake.** Without this, D3's open-edge hunting is decoration. |
| D30 | Assist, settled | **Soft assist, not `SteerOverride`**: guidance keeps you off the edges and centred in the lane, but your steering still adds to it. Hands never fully leave the ship. This also sidesteps the dash-swallowing collision in §7.1, because it never sets the override flag. |
| D31 | Clocks | **The world slows to 30 %. The run countdown keeps running at full rate. The bar runs on unscaled time.** Slow-mo buys *perception*, not mechanical advantage, and the exchange still costs real mission time — which makes declining an attack run (D23) a genuine option when the clock is short. |
| D32 | Bar presentation | **Horizontal bar, low and small, centred**, with the patrol's colour pushing from its actual side and the mash glyph at the end you are pushing toward. Kept small so the road stays in peripheral vision, with push direction reinforced through the **existing rumble channel** rather than bar size. |
| D34 | Lasers kill the patrol | A laser beam **destroys the cruiser outright** — the same explosion and recycle as the finisher's kill, but it charges the player nothing: no dash meter (they never spent one) and **no raised floor**, because a cruiser that drives into a gate by itself is a hazard death like a fall, and falls have never escalated. **This REVERSES R1.5 for laser gates specifically**: a gate must NOT be forbidden ground, or the kill would be unreachable during an exchange. Ramps, landings, loops, tubes and the run-up stay forbidden. It is skill-based rather than random because the driver steers for the ship's own lateral — a patrol not in a run copies the player's dodge, so the only way it eats a beam is the flank offset during a run, which the player aims by positioning themselves a beam's width clear. |
| D33 | Slow-mo through the finisher | Slow-mo **and** assist **continue through the finisher prompt** and release on the connect, with a **hit-stop on the explosion** as the transition back to full speed. One continuous authored beat: contest, prompt, kill, snap back at Light Speed with a fresh gap. The cost is that D16's wrong-side punishment is softened by the assist; accepted. **Amended by D36/D38**: the slow-mo still runs through the prompt; the "assist" is now the control lock. |

### 4.1 The cinematic duel (2026-09-25, from `PoliceChaseImprovement.md`)

| # | Topic | Decision |
|---|---|---|
| D35 | Overshoot | **Braking with the cruiser in its standoff makes it overshoot**: it swerves to a flank, holds the speed it had for `overshootHoldSeconds`, sails past, then drops back behind into its standoff. It is an ordinary chase beat, not an attack run (arrest suspended, cadence untouched). While ahead it is D6's obstacle and ram target. A brake pad's deceleration triggers it too. |
| D36 | Control lock | **Supersedes D13/D30.** The player **stops controlling the ship** for the whole exchange — stick, throttle, brake AND dash — from the bar opening to the kill, the shove or the miss. The ship flies itself (the autopilot generalised with a lateral target). D23's brake-out is therefore gone once the bar is up; out-steering still aborts BEFORE the lock (`Alongside`). |
| D37 | The push | The bar IS the ship's position: bar 0 is where it was caught, bar 1 is `tugPushFraction` of the room to **the edge the cruiser chose**, and it opens at the centre — the cruiser's arrival **shoves the ship halfway out at once**, its force keeps walking it out and every press claws it back, with **sparks grinding between the hulls**. The cruiser is **held on its flank** (never inside the ship's lateral, glued to it for the whole bar), so the pair move as one and a winning press shoves it visibly back. A full bar leaves the ship at the brink; the SHOVE (D29) still finishes it. The bar never drops the ship by itself. The contact is staged as a side swipe: the cruiser leans on the ship nose-in and **slams** it on a real-time cadence (`tugSlamIntervalSeconds`) — both visuals lurch, a burst of sparks jumps up out of the seam and the glow between the cars flares — but the slams are presentation; the bar is a steady push. |
| D38 | The kill | **Supersedes D5 and R3.3.** Winning the bar makes the cruiser **peel out** by `finisherSeparationMeters`; the prompt shows the dash shoulder on its side; **ONE PRESS destroys it**. No dash is performed and **no dash meter is spent** (supersedes D9's price for the finisher — escalation is the cost). |
| D39 | Wrong shoulder | **Supersedes D16.** Pressing the WRONG shoulder **counts as a miss**, exactly like the timeout. One swing. |
| D40 | The miss | **The cruiser brakes hard** (`MissBraking`, its own state) down to `finisherMissBrakeSpeedFactor` of the ship's speed for `finisherMissBrakeSeconds`, visibly falling away, then the ordinary cooldown. Missing still costs the player nothing. |
| D41 | Duel camera | The chase orbit **dollies in** as a committed run closes (0 → 1 over the approach), holds a tighter, lower framing (`duelDistance` / `duelLookHeight` / `duelPitch` on `OrbitCameraSettings`) through the exchange, and eases back out on whatever ends it. Driven per frame, like the RPG queue hold. |
| D42 | The cruiser | The cruiser is the **`PF_PatrolCarModel` prefab** (the Megapolis air car) under the code-built `Visual`, its baked scene transform discarded and its collider stripped; the two code-built light spheres stay. Primitives remain the fallback. |
| D43 | The finisher clock | The prompt's window counts **REAL seconds** (it was scaled — 1 s under the 0.3× clock was 3.3 real seconds); retuned to 1.5. |
| D44 | The hunt | **Amends R1.7/D8.** Once a run is due the cruiser **closes on its standoff from wherever it is** at the run's closing rate (a floor, so boosts still out-run it), and that rate is floored at `minClosingSpeed` (12 m/s) so a run can start against a slow or stopped ship. Found by soak: at 40 m/s the band (95 % of the ship) never brought it back inside the 70 m reach, and below ~100 km/h the reach itself shrank to 11 m. |
| D45 | Lane clearing | A cruiser ahead of the ship that wants to be behind it **keeps to a flank until it is**. The lane clamp otherwise shunts it along the ship's nose for ever (D6's obstacle, taken literally, deadlocked the chase after every overshoot). |

## 5. Functional requirements

### 5.1 The attack run (D8, D21, D23)

- **R1.1** Add an explicit encounter state machine to `PolicePatrol`. Today its mode is implied by
  four booleans (`HasCaught`, `Hold`, `IsGone`, `target.Paused`) plus `ShipState`; the new states
  must be explicit and serialized nowhere: `Cruising → Committing → Alongside → TugOfWar →
  Finisher → BreakingOff → Cooldown`.
- **R1.2** `Cruising` keeps today's behaviour exactly — the `Step` rubber band
  (`PolicePatrol.cs:346-390`), `PatrolDriver` steering, orb seeking, ramp planning and sweep
  braking are all unchanged.
- **R1.3** The patrol enters `Committing` when all of: `SimGap <= warnDistance`, the track ahead
  permits an encounter (R1.5), the cooldown has expired, and the escalation tier's commit interval
  has elapsed. While `Committing` it **overdrives** — `desired` is raised above
  `target.CurrentSpeed` by an `attackRunOverdrive` factor, bypassing the `onTail` clamp at
  `PolicePatrol.cs:359-362`.
- **R1.4** It enters `Alongside` when `SimGap <= alongsideDistance` and it has matched lateral
  within `alongsideLateral`. On entering, it picks its side (left or right of the ship) and holds
  it, steering to maintain the flank rather than to `ship.Lateral` as `PatrolDriver` does today
  (`PatrolDriver.cs:54-57`).
- **R1.5** **Forbidden ground.** An encounter may not be committed to, and an in-progress one must
  break off, when the track within `encounterLookaheadMeters` contains a ramp, a ramp landing, a
  loop, a `TubeSection`, or the final run-up. **Laser gates are deliberately NOT forbidden ground**
  (D34): a beam kills the cruiser, and steering the exchange onto one is the player's move —
  forbidding gates would make that unreachable. This is the same predicate the laser
  gate placer uses; factor it into one shared helper rather than duplicating the rule.
- **R1.6** **Abort.** Any of these returns the patrol to `Cooldown` with no shove and no kill: the
  ship gets fully behind it (D6), the ship out-steers the flank beyond `alongsideLateral` for
  `abortGraceSeconds`, the track ahead becomes forbidden (R1.5), the ship leaves the track, or the
  run ends. `Cooldown` lasts `attackRunCooldownSeconds` and forbids `Committing`.
- **R1.7** **Re-tune for arrival.** `rubberBand` stays conservative (the cruise band is not the
  thing that closes the gap) but `patrolRedeploySpeedFactor` and the floor must be set so the
  patrol reaches `warnDistance` within a reasonable share of a run. The overdrive does the closing;
  the cruise band does not need to be oppressive.

### 5.2 The tug of war (D2, D14, D17, D29, D30, D31, D32)

- **R2.1** Entering `TugOfWar` from `Alongside`: the bar spawns at **centre (0.5)**, the world
  timescale blends to **30 %**, soft assist engages (R2.6), the dash is disabled (R3.1), RPG lines
  begin queueing (R5.8), and the arrest timer suspends (R5.10).
- **R2.2** **The bar.** A single normalized value 0..1, centre 0.5. The patrol pushes it toward its
  own side at `tugPatrolForce` per second, scaled by its remaining damage pool (R4.4) and its
  escalation tier (R6.2). Each registered press moves it back by `tugPressValue`. It is **mirrored**:
  for a patrol on the right, "patrol wins" is 1.0 and "player wins" is 0.0; for a patrol on the
  left, inverted. The bar must never be presented as anything other than "push it away from the
  patrol".
- **R2.3** **The bar runs on unscaled time** (D31). Both the patrol's push and the press value are
  integrated against real seconds, so the 30 % slow-mo grants no mechanical advantage. Follow the
  house rule: timers in the fixed tick, not coroutines, so `Paused` and a menu's `timeScale` freeze
  them — see `ShipHealth.Update` skipping its countdown while `motor.Paused` (`ShipHealth.cs:156`).
- **R2.4** **Player wins** the bar → `Finisher` (§5.3).
- **R2.5** **Player loses** the bar → **the scripted shove** (D29): assist releases, and the patrol
  drives the ship laterally in the bar's direction hard enough to put it into the wall or over the
  edge. Implementation: a lateral impulse on `HoverBody` in the patrol's direction — reuse
  `AddLateralImpulse` (`HoverBody.cs:228`), which already feeds `ShoveVelocity`, the channel a wall
  reads to distinguish a slam from steering (`HoverBody.cs:678`, `:525`). Consequences fall out of
  existing code with **no new damage source**: a walled section produces a `WallHit` →
  `wallSlamDamage` (25), an open edge produces a fall → `fallDamage` (30) plus the respawn and the
  lost time. The patrol then goes to `BreakingOff` → `Cooldown` (D12).
- **R2.6** **Soft assist** (D30): while `TugOfWar` or `Finisher`, a steering contribution is added
  that keeps the ship clear of open edges and biased toward lane centre, and the player's own
  steering input **still applies on top**. It must **not** set `ControlOverride`, `Autopilot` or
  `SteerOverride` — see §7.1.
- **R2.7** **Throttle and brake stay live** throughout, so braking to slide behind the patrol
  remains a legitimate way to abort the exchange (R1.6, D6).
- **R2.8** **The HUD bar** (D32): horizontal, low, small, centred; the patrol's colour pushes in
  from its real side; the mash glyph sits at the end the player is pushing toward. Pulses at the
  cadence needed to win. Hidden while `motor.Paused`. Follow `DashPromptController` for the overlay
  canvas, pulse and pause behaviour.
- **R2.9** Push direction is reinforced by the **rumble** channel (`HapticsSystem`), directionally
  if the device supports it, so the player can feel which way they are losing without reading the
  bar.

### 5.3 The finisher (D5, D16, D33)

- **R3.1** The dash is **disabled** from `TugOfWar` onward: `TryDash` (`HoverShip.cs:363-384`) must
  reject while the encounter owns it. Do this through an explicit, named gate, **not** by setting
  the override flags (§7.1).
- **R3.2** On winning the bar, the state becomes `Finisher`: the prompt shows **LB or RB matching
  the patrol's actual side**, for `finisherWindowSeconds` (~1 s). Slow-mo and assist continue (D33).
- **R3.3** **Correct side pressed** → the ship dashes through the patrol. Because the dash is an
  impulse of ~360 m/s over `dashDistance` (20.7 m) and the patrol's threat band is `catchLateral`
  (18 m), the existing dash already spans the geometry; no special-case movement is needed.
  On the connect: spawn the explosion at the patrol's pose, hit-stop, release slow-mo and assist,
  and recycle the patrol (R3.5).
- **R3.4** **Wrong side pressed** → the ship dashes away from the patrol, normally, with all the
  usual consequences (a wall slam if there is a wall, a fall if there is an open edge — softened by
  the assist, which is accepted per D33). The encounter goes to `BreakingOff`.
- **R3.5** **Window expires with no press** → the patrol breaks off and drops back. No kill, no
  damage, no penalty (D16).
- **R3.6** **Recycling the kill.** The "destroyed" patrol is the existing single object: play the
  explosion, hide the visual, then `Redeploy`-style teleport to a safe distance behind the ship
  with the escalation applied (R6.1). Reuse the existing `Redeploy` path
  (`PolicePatrol.cs:426-441`) — it already resets `tailTimer`, `warnCooldown` and `warned`,
  increments `PatrolNumber`, re-poses and fires `Redeployed`. The teleport must never be visible:
  hide the visual for the explosion and the reposition, and blank the minimap icon during it
  (`ChaseMinimap.cs:193-194` already does this for `IsGone` and is the natural hook).
- **R3.7** **A kill drains the whole dash meter** (D9): set `dashMeter` to 0 rather than
  subtracting `dashCost`. At `dashRechargeSeconds` 1.8 the player is without a dash for the fresh
  patrol's approach.

### 5.4 Rear ramming and the damage pool (D4, D18, D24)

- **R4.1** **Analytic contact only.** A rear ram is detected in track space: the ship is behind the
  patrol (`SimGap < 0` by the existing sign convention), longitudinally within `ramContactDistance`,
  laterally within `ramContactLateral`, **and** closing at more than `ramClosingSpeedThreshold`.
  Slower contact is a no-op. No collider is added to the patrol, and nothing is detected by a
  trigger.
- **R4.2** A ram **costs the ship speed**: subtract `ramSpeedCost` from forward speed. No hull
  damage to the player (D4, D11).
- **R4.3** A ram removes **one point** from the patrol's 3-point pool, with feedback: rumble,
  a hit spark, and a visible reaction on the patrol's visual.
- **R4.4** The pool **does not kill at zero** (D18). It scales `tugPatrolForce` (R2.2): full pool =
  full-strength push, one point left = a pushover. The pool refills when the patrol is recycled
  (R3.6) and is **not** reset by an abort (D23 — evasion must not undo ramming work).
- **R4.5** Ramming is only possible while the patrol is ahead of the ship, which per D6 means after
  the player has braked past it or it has overshot. It is **never** available during `TugOfWar` or
  `Finisher`.
- **R4.6** **The patrol ahead is an obstacle** (D6): while it is ahead it occupies its lateral lane,
  it brakes to re-match the ship's speed rather than resetting far ahead, and it remains subject to
  its own hazards — it can slide off a flat sweep, which triggers the existing
  `OnLeftTrack` → `Redeploy(raiseFloor: false)` path (`PolicePatrol.cs:266`), and **a laser beam
  destroys it outright** (D34).

### 5.5 The armed window (D10, D19)

- **R5.1** Add a timed **armed** state to the ship. There is no power-up state of any kind today —
  pickups are instantaneous speed impulses — so this is new. Model it on
  `ShipHealth.invulnerableLeft` (`ShipHealth.cs:36`): a float ticked down in the fixed tick,
  frozen while `motor.Paused`, exposed as a bool.
- **R5.2** It is armed by collecting a **Blue** or **Purple** `SpeedPad` (tier names from
  `TrackGenerator.spawnTable`), for `armedWindowSeconds` (~3 s). Green and Brake do not arm it.
  Hook `SpeedPad.Collected` / the existing `GameManager.OnPadCollected` (`GameManager.cs:855-862`),
  which already filters by tier name for the purple RPG line.
- **R5.3** While armed, an encounter reaching `Alongside` **skips `TugOfWar` entirely** and goes
  straight to `Finisher` (D19). The slow-mo and assist still engage, so the beat still reads.
- **R5.4** The armed state needs a visible tell on the ship — it is a large reward and the player
  must know they have it. A glow or trail treatment, not a HUD icon.

### 5.6 Failure, hull and lives (D11, D12)

- **R6.1** **No new damage source is added to `ShipHealth`.** Every consequence routes through an
  existing one: the scripted shove produces a `WallHit` (`wallSlamDamage` 25) or a fall
  (`fallDamage` 30, which is already `forced: true` and pierces the blink). `ApplyDamage` stays
  private; no new public wrapper is needed.
- **R6.2** Losing the tug of war is **never** a loss condition and never takes a life directly. It
  can of course take the last of the hull, in which case the existing `Destroyed` →
  `BeginFail(RunOutcome.Destroyed)` path applies unchanged.
- **R6.3** The patrol may not commit while the ship is `OffTrack`, `Respawning` or `Falling`, nor
  while `GameManager.IsEnding` or `RunOver`. The existing `SetHold` path
  (`PolicePatrol.cs:141-156`) already covers the respawn case and must also clear any encounter
  state.

### 5.7 Escalation (D7, D22)

- **R7.1** On every recycle (R3.6), raise the escalation tier. The tier scales three things:
  the rubber-band **speed floor** (today's only escalation — `minSpeed` in `Redeploy`), the
  **commit interval** (how soon after arriving it starts an attack run), and **`tugPatrolForce`**.
- **R7.2** `tugPatrolForce` is **capped** at `tugForceMaxScale` so the bar is always winnable from
  centre with a full pool. Escalation compresses the player's recovery time; it must never make the
  mash mathematically impossible.
- **R7.3** A redeploy from the patrol *falling off* (`OnLeftTrack`) does **not** raise the tier,
  matching today's `raiseFloor: false`. Only a kill and an outrun do.
- **R7.4** The tier resets on `Launch` (a fresh run), alongside `PatrolNumber`.

### 5.8 Slow-mo and clocks (D17, D31, D33)

- **R8.1** The world blends to **30 %** on entering `TugOfWar` (or `Finisher` when armed) and blends
  back on the finisher connect, the shove, or a break-off. Use `LoopSlowMo`
  (`Runner/Ship/LoopSlowMo.cs`) as the model — it is a timed, blend-in/blend-out world-clock window
  with the pause and menu re-arm contract already solved. Two slow-mo owners must not fight: if a
  loop and an encounter overlap, one wins deterministically.
- **R8.2** **The run countdown keeps running at full rate** (D31). This is consistent with the
  existing rule that the countdown never stops, including through a fall.
- **R8.3** **The bar is on unscaled time** (R2.3).
- **R8.4** The finisher's hit-stop is a brief deeper dip before the release (D33).
- **R8.5** Verify the substepped ship at 30 % time: it covers ~11 m per physics step instead of
  ~36, which changes substep counts and therefore the sweep/grip and pickup paths. No regressions
  in `ShipPickupSweeper` or the grip test are acceptable.

### 5.9 Input and the prompt (D25, D27, D32)

- **R9.1** **The mash button is fixed**: `PadControl.ButtonWest` (X) on gamepad — the only genuinely
  unused face button in the project — and one key on keyboard (OQ2).
- **R9.2** It is **non-bindable**, which is a deliberate exception to the project rule that all
  gameplay input routes through `ControlBindings`. The precedent is explicit and documented:
  gamepad A carries no binding default *precisely so* it can be read raw over live gameplay for
  RPG dialogue advance (`ControlBindings.cs:52-54`, `:126-129`). The mouse for camera pan and touch
  steering are the other two. Document this as the fourth.
- **R9.3** Add the QTE controls to the **reserved set** (`ControlBindings.cs:130-139`, `IsReserved`
  at `:174`/`:177`) so a player can never bind them to a ship or camera action and be asked to mash
  a button that also steers.
- **R9.4** **The finisher uses the real dash bindings** — `GameAction.ShipDashLeft` /
  `ShipDashRight` — so a player who has rebound the dash sees their own binding. Read it through
  `ControlBindings.PadFor` / `KeyFor` and render with `ControlGlyphSet`, exactly as
  `DashPromptController.RefreshBinding` does (`DashPromptController.cs:165-174`), and subscribe to
  `ControlBindings.Changed` so a rebind updates it live.
- **R9.5** **Device detection** must use the presence rule `Gamepad.current != null` as
  `DashPromptController.RefreshDevice` does (`:192-200`), polled each frame for hot-plug.
  `InputPromptBinder.Poll()` is **not** an option: it is only called from the main menu, Store,
  Coming Soon and cinema screens, so its `Device` value is stale during a run.
- **R9.6** Add `MenuTextId` entries for the mash prompt and the finisher prompt, translated in all
  four languages (EN/ES/JA/FR). There is no existing mash/QTE id.

### 5.10 The arrest (D1, D20)

- **R10.1** **Remove the proximity catch.** The `|across| <= catchLateral` branch of `UpdateCatch`
  (`PolicePatrol.cs:405-416`) goes. `catchLateral` becomes unused by the catch and should be either
  deleted or repurposed as `alongsideLateral` (R1.4).
- **R10.2** **Keep the tail-time catch**, with its fuse lengthened from 1.5 s to
  `sustainedCatchSeconds` in the 6–8 s range (OQ3).
- **R10.3** **Suspend the timer** while the patrol is in `Committing`, `Alongside`, `TugOfWar`,
  `Finisher` or `BreakingOff`. Without this, every exchange auto-arrests the player before the mash
  can be completed — see §7.2.
- **R10.4** `RunOutcome.Caught`, `PlayerStats.RecordArrest()` and `MenuTextId.LoseCaught` are
  unchanged.

## 6. Tunables

All new patrol knobs go on **`PatrolDefinition`** (Odin `[PropertyRange]` sliders with hand-picked
ranges, paired values as `[MinMaxSlider]` bands unpacked by accessor properties, per the project
convention). Encounter-wide and HUD knobs go on **`GameSettings`** in a new `[ToggleGroup]`
alongside the existing hull and dash groups.

### 6.1 New `PatrolDefinition` fields

| Field | Meaning | Suggested start |
|---|---|---|
| `attackRunOverdrive` | ×ship speed while `Committing` | 1.15 |
| `alongsideDistance` | longitudinal gap that begins the exchange | 12 m |
| `alongsideLateral` | lateral band that counts as alongside (repurposed `catchLateral`) | 18 m |
| `encounterLookaheadMeters` | how far ahead the forbidden-ground test reads | 300 m |
| `attackRunCooldownSeconds` | after an abort, loss or break-off | 8 s |
| `commitIntervalSeconds` | minimum between attack runs, scaled down by tier | 12 s → 6 s |
| `abortGraceSeconds` | how long the ship must hold outside the flank to abort | 0.4 s |
| `tugPatrolForce` | bar units per second at full pool, tier 0 | OQ1 |
| `tugPressValue` | bar units returned per press | OQ1 |
| `tugForceMaxScale` | escalation cap on the push | 1.8 |
| `damagePoolMax` | rear-ram hits it takes | 3 |
| `ramContactDistance` | longitudinal window for a rear ram | 8 m |
| `ramContactLateral` | lateral window for a rear ram | 6 m |
| `ramClosingSpeedThreshold` | minimum closing speed for a ram to register | OQ6 |
| `killTeleportGap` | the "safe distance" a killed patrol reappears at | OQ9 |

### 6.2 New `GameSettings` fields

| Field | Meaning | Suggested start |
|---|---|---|
| `patrolDuelEnabled` | master toggle for the whole feature | true |
| `duelTimeScale` | world timescale during an exchange | 0.30 |
| `duelTimeBlendSeconds` | blend in/out | 0.15 |
| `duelHitStopSeconds` | the finisher's deeper dip | 0.12 |
| `finisherWindowSeconds` | how long the LB/RB prompt lives | 1.0 |
| `ramSpeedCost` | speed a rear ram costs the ship | OQ6 |
| `armedWindowSeconds` | power-up arming duration | 3.0 |
| `duelAssistStrength` | soft-assist steering weight | OQ4 |
| `duelBarColor`, bar size/position knobs | HUD | — |

### 6.3 Existing values this work changes

| Asset | Field | From | To |
|---|---|---|---|
| `Police_PatrolDefinition` | `sustainedCatchSeconds` | 1.5 (C# default, unserialized) | 6–8 (OQ3) |
| `Police_PatrolDefinition` | `catchLateral` | 18 (unserialized) | repurposed as `alongsideLateral` |
| `FiniteRunner_GameSettings` | `patrolRedeploySpeedFactor` | 1.0 | > 1.0 (R1.7) |

### 6.4 Debug menu

`DebugMenuFactory.BuildPatrolTab` (`DebugMenuFactory.cs:431-461`) and `BuildPatrolDriverTab`
(`:469-499`) already have a row per `PatrolDefinition` field. Every new field needs a `MenuTextId`
and an `AddPatrolStat` row. **Critically:** `PatrolDebugSettings.ApplyTo` (`:105-130`) uses a
`-1 = never captured` sentinel for fields added after the original eight. Any new field must follow
the sentinel rule or it will **silently overwrite authored asset values**. A new duel tab is
probably warranted rather than growing the patrol tab further.

## 7. Collisions with existing code that must be resolved

These are the traps found while speccing. Each one will silently break the feature if missed.

### 7.1 `SteerOverride` swallows the dash

`HoverShip.cs:352` discards the dash request entirely while `ControlOverride`, `Autopilot` or
`SteerOverride` is set — that is how the tube return and autopilot stay hands-off. The finisher
**is** a dash. So the assist in D30 must be a *steering contribution*, not an override flag, or the
finisher will never fire. This is the main reason D30 chose soft assist over a full override.

### 7.2 The arrest timer fires mid-QTE

`sustainedCatchSeconds` is 1.5 s and the tail-time branch of `UpdateCatch` ignores lateral
position entirely. The exchange keeps the patrol inside `catchDistance` for several seconds by
design. Without R10.3's suspension, **every single tug of war ends in an automatic arrest before
the player can finish it.** This must land in the same milestone as the QTE.

### 7.3 RPG dialogue collides with the encounter

Purple-orb pickups fire a "PILOT" line (`GameManager.OnPadCollected`, gated on
`TierName == settings.messageOrbTierName` = `"Purple"`), and patrol taunts fire lines on the
proximity warn. Both land inside an attack run, and both want **gamepad A** via
`RpgMessageSystem.AdvancePressed` → `MenuNavigator.DialogueAdvancePressed`
(`RpgMessageSystem.cs:406`, `MenuNavigator.cs:73-86`). D19 makes it worse: a purple orb
*simultaneously* arms the instant kill and puts a text box on screen. R5.8/D26's queue-and-flush
resolves it. **A is also why the mash button cannot be A.**

### 7.4 The gamepad button pool does not exist

The premise that ABXY are free is two-thirds wrong:

| Button | Status |
|---|---|
| **A** / ButtonSouth | **Read raw over live gameplay** — RPG dialogue advance, hold-to-skip, `TuningScreen` launch. Carries no binding default *because* of this, so it looks free in the table and is not. |
| **B** / ButtonEast | `CarHandbrake` default, plus the menus' hard-wired Back (`MenuNavigator.cs:118`). |
| **X** / ButtonWest | **Genuinely unused.** The only free face button. |
| **Y** / ButtonNorth | `CarRespawn` default. |

Also free: `DpadDown`, `LeftStickPress` (L3). `Start` is unbound but reserved. This is why D25
dropped randomization. Keyboard by contrast has roughly nineteen free keys — an asymmetry that
would make a randomized pool behave differently per device.

### 7.5 Stale documentation to correct

Found while speccing; fix when touching these files.

- `CLAUDE.md:156` documents a `patrolDangerBand` field that **does not exist** — it is
  `catchDistance` + `warnDistance` on `PatrolDefinition`.
- `PolicePatrol.cs:11-15`'s class doc claims the patrol "always closes in without boost orbs"; the
  asset's `rubberBand: 0.95` means it does not.
- `.claude/rules/runner-ship.md` says `patrolRedeploySpeedFactor` makes a fresh patrol "close in
  until the next boost"; the asset has it at exactly 1.0.
- `ShipMotor.cs:155`'s comment says the dash meter "starts each run empty"; `HoverShip.Launch`
  sets it to 1 (`HoverShip.cs:216`).

## 8. Invariants that must survive

- **No collider on the patrol, ever.** All ship↔patrol contact is analytic track-space math. Nothing
  is detected by a trigger or a moving volume.
- **Never mutate a ScriptableObject at runtime.** `PolicePatrol` already clones
  (`runtimeDef = Instantiate(definition)`, `:179-180`); the new fields are read off the clone.
- **Timers in the fixed tick, not coroutines**, so `Paused` and a menu's `timeScale` freeze them.
- **One patrol object.** No `Instantiate`, no list. The kill is an explosion plus the existing
  teleport.
- **Speeds in m/s**, UI converts with `* 3.6f`.
- **All player-facing strings are `MenuTextId` entries** translated in four languages; stat values
  go through `StatFormat` and are never localized.
- **Serialized enums are append-only** — including any new encounter-state enum that ends up
  serialized, and `GameAction` if it is touched at all.
- **`[SerializeField]` on anything baked into the `PolicePatrol` prefab.** A plain private field
  deserializes as zero and silently kills the behaviour.
- **The countdown never stops.**

## 9. Milestones

| # | Scope | Done when |
|---|---|---|
| **M0** ✅ | Attack run: the encounter state machine, overdrive commit, forbidden ground (R1.5), abort and cooldown (R1.6), arrival re-tune (R1.7), arrest suspension and fuse (R10.1-R10.3). **No QTE** — the patrol parks alongside, waits, then shoves via R2.5. | The patrol reliably finds you, commits visibly, parks on a flank, shoves you into a wall or off an edge, breaks off, and comes back. It never commits on a ramp, loop, tube or the run-up. You can shake it by braking or out-steering. |
| **M1** ✅ | The tug of war: bar model (R2.2), unscaled-time integration (R2.3), slow-mo clocks (§5.8), soft assist (R2.6), fixed button and prompt (§5.9), win/lose resolution (R2.4, R2.5), HUD bar (R2.8), rumble (R2.9). | You can win or lose the bar. Winning does nothing yet. Losing shoves you. The countdown still costs full time. Mashing is no easier for the slow-mo. |
| **M2** ✅ | The finisher: dash gate (R3.1), prompt (R3.2), pass-through and explosion (R3.3), wrong side (R3.4), expiry (R3.5), recycle teleport (R3.6), meter drain (R3.7), hit-stop (R8.4). | Winning the bar and pressing the right shoulder kills the patrol spectacularly and a fresh one arrives behind you. The teleport is never visible. Your dash meter is empty. |
| **M3** ✅ | Rear ramming: analytic contact (R4.1), laser kill (D34), speed cost (R4.2), damage pool (R4.3, R4.4), the patrol-as-obstacle behaviour (R4.6). | Braking past the patrol and ramming it costs you speed, takes a point, and visibly weakens its push in the next exchange. |
| **M4** | The armed window: ship armed state (R5.1), blue/purple arming (R5.2), mash skip (R5.3), visual tell (R5.4). | Grabbing a blue or purple orb while hunted takes you straight to the finisher prompt. |
| **M5** ◑ | Escalation and balance: tier scaling (R7.1), the cap (R7.2), a full tuning pass on OQ1, OQ3–OQ7. | A long run gets genuinely harder, the bar is always winnable from centre with a full pool, and killing is worth it but never free. |
| **M6** ◑ | Polish and docs: RPG queue (R5.8/D26), reserved bindings (R9.3), `MenuTextId` entries in four languages (R9.6), debug menu rows and the sentinel rule (§6.4), audio, minimap, §11 documentation, §7.5 stale-doc fixes. | Nothing in §7 is outstanding and the rules files describe what the code does. |

## 10. Verification (every milestone)

- `FiniteRunner_Test` plays start to finish with the patrol enabled and with
  `patrolDuelEnabled` **off** (the feature must be cleanly disableable).
- Hull off (`hullEnabled` false) and hull on both work.
- The `CityTest` → runner additive handoff still works, and the runner's shared-material drivers
  still restore `_Intensity` on disable.
- Pause during every encounter state, and during the slow-mo blend, resumes correctly.
- The loop slow-mo and an encounter overlapping does not leave the timescale stuck.
- A fall, a respawn and a relaunch during an encounter leaves no encounter state behind.
- The win at the end-ramp lip still beats a same-frame encounter (`GameManager.Update` early-outs
  on `IsEnding` before the patrol poll).
- Rebinding the dash updates the finisher prompt live; unplugging the gamepad mid-encounter swaps
  the glyphs to keys.
- Domain reload is off: re-enter play twice without a recompile and confirm no stale static state,
  no double subscription, and the tier reset works.

## 11. Documentation to update (M6)

- `.claude/rules/runner-ship.md`: the encounter state machine, the attack run, the tug of war, the
  finisher, the damage pool, escalation, and the arrest change. Fix the stale redeploy claim (§7.5).
- `.claude/rules/runner-hud-screens.md`: the duel bar and the two prompts.
- `.claude/rules/ui-menus.md`: the non-bindable QTE button as the fourth documented exception, and
  the new reserved entries.
- `.claude/rules/shared-systems.md`: if the rumble channel gains a directional mode.
- `.claude/rules/debug-visualizers.md`: the duel debug tab.
- `CLAUDE.md` "Runner game design": rewrite the patrol paragraph — it currently says the patrol
  "catches by being on your tail AND close across the track", which D1 removes. Also fix the
  `patrolDangerBand` line (§7.5).

## 12. Risks

| Risk | Mitigation |
|---|---|
| **Three accommodations stack.** Slow-mo at 30 %, soft assist *and* an abstract bar all reduce the same difficulty, and together the exchange may feel like a minigame the runner pauses for rather than something happening at speed. | D31 keeps the countdown at full rate and the bar on unscaled time, so neither time nor difficulty is actually refunded. Tune `duelTimeScale` **up** toward 1 before adding any further help, and treat the assist strength as the second dial. If the encounter still feels detached at M1, cut the assist first — it is the least load-bearing of the three. |
| **The bar pulls the player's eyes off the road while the real stake is positional** (D29). | D32 keeps the bar small and low with the road in peripheral vision, and R2.9 puts push direction in the rumble channel so it can be felt rather than read. |
| **The patrol is currently tuned never to arrive** (`rubberBand` 0.95, factor 1.0). Re-tuning risks swinging to oppressive. | R1.7 keeps the cruise band conservative and puts all the closing in the overdrive burst, so "how often does this happen" is one number (`commitIntervalSeconds`) rather than an emergent property of the whole chase. |
| **`PatrolDebugSettings` silently overwrites authored values** for any new field that skips the `-1` sentinel. | §6.4 calls it out explicitly; verify by editing the asset, entering play, and confirming the value survives. |
| **Only one genuinely free gamepad button**, and reserving it shrinks the player's rebinding room. | X is unbound by default, so reserving it costs nothing today. If a second control is ever needed, `DpadDown` and L3 are the remaining candidates. |
| **Slow-mo changes the substep count** — ~11 m per step instead of ~36 — which touches grip, sweeps and the pickup sweeper. | R8.5 makes this an explicit verification item at M1, before anything is built on top. |
| **The kill's teleport could be visible**, breaking the illusion that a new patrol arrived. | R3.6 hides the visual across the explosion and the reposition and blanks the minimap icon, reusing the `IsGone` hook. |
| **Two slow-mo owners** (a vertical loop and an encounter) fighting over the timescale. | R8.1 requires a deterministic winner; D21 already forbids encounters on loops, which removes the common case. |

## 13. Open questions

Each has a working default, so no milestone is blocked. Confirm before the milestone that needs it.

- **OQ1 — Bar feel (M1).** `tugPatrolForce` and `tugPressValue`. Default: a full-pool tier-0 patrol
  drives the bar from centre to its edge in **2.5 s**, and one press returns **8 %** of the bar, so
  winning from centre is roughly **6–8 presses**. Needs hands on a pad.
- **OQ2 — The keyboard mash key (M1).** Default: **Space** — it holds only a Car-section default
  (`CarHandbrake`) and is never read during a runner run, and it is the natural mash key. `X` is the
  alternative and pairs symmetrically with gamepad X.
- **OQ3 — The arrest fuse (M0).** `sustainedCatchSeconds`. Default: **7 s**. It now only punishes a
  patrol tailing you while *not* committing, so it should feel like a long grace.
- **OQ4 — Assist strength (M1).** `duelAssistStrength`. Default: enough to keep the ship off an
  open edge but not enough to hold a lane against player input. Start at 0.5 of full steer authority.
- **OQ5 — Commit cadence (M5).** `commitIntervalSeconds` at tier 0 and at high tiers. Default:
  **12 s → 6 s**. This is the single number that decides how much of the run is duelling.
- **OQ6 — Ram numbers (M3).** `ramClosingSpeedThreshold` and `ramSpeedCost`. Default: threshold
  **15 m/s** of closing speed; cost **8 %** of current forward speed, so the price scales with how
  fast you are going.
- **OQ7 — Armed window (M4).** `armedWindowSeconds`. Default: **3 s**. Long enough to reach a patrol
  you can already see, short enough that it is not a standing state.
- **OQ8 — The explosion (M2).** `ExplosionVfx.SpawnFireball` already exists for the ship's own death
  (`GameManager.ExplodeShip`, `:519-533`) with textures on `GameSettings`. Reuse it with a different
  tint and scale, or author a dedicated patrol explosion?
- **OQ9 — Kill teleport distance (M2).** `killTeleportGap`. Default: reuse
  `patrolRedeployBand.x` (691.75 m in the asset), which is already inside `minimapRangeMeters`
  (1012) so the player can see the replacement arrive. A shorter, more threatening gap is the
  alternative.
- **OQ10 — Audio under slow-mo (M1).** Does the mixer pitch down with the timescale, or does the
  duel get its own `GameAudio` snapshot? A pitched-down engine is the cheap, expected read; a
  snapshot is more controllable. Default: snapshot, ducking the music and lifting the siren.
