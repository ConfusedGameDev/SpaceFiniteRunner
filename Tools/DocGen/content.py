# -*- coding: utf-8 -*-
"""Hand-written sections of the handbook. HTML fragments; the builder wraps them."""

OVERVIEW = r'''
<section id="overview" class="sec">
  <p class="eyebrow">Overview</p>
  <h2>One mission, two games</h2>
  <p class="lede">SpaceFiniteRunner is two games on one set of systems. A <b>mission</b> is a city level (a car chase through a baked cyberpunk city) followed by an escape run (a hover ship on a finite spline track). This handbook covers where every system is tuned, how the code is put together, and the rules to follow when you change it.</p>

  <div class="facts">
    <div><span>Engine</span><b>Unity 6000.7.0a3 · URP 17.7</b></div>
    <div><span>Code</span><b><code>Assets/01.Scripts/</code></b></div>
    <div><span>Tunables</span><b><code>Assets/04.Data/</code></b></div>
    <div><span>Scenes</span><b><code>Assets/05.Scenes/</code></b></div>
    <div><span>Inspectors</span><b>Odin, runtime attributes only</b></div>
    <div><span>Units</span><b>m/s stored, km/h shown (× 3.6)</b></div>
  </div>

  <h3>How a mission flows</h3>
<pre class="mermaid">
flowchart LR
  MM["MainMenu"] --> ST["Store"]
  ST -- "START MISSION" --> CT["CarTest<br/>city level"]
  CT -- "objectives done:<br/>additive glitch handoff" --> FR["FiniteRunner_Test<br/>escape run"]
  FR -- "Mission Complete<br/>pays the mission" --> ST
  FR -- "last life lost:<br/>GAME OVER" --> ST
  MM -- "MISSIONS map<br/>replay a cleared mission" --> CT
</pre>
  <p class="cap">The Store's START MISSION plays the first uncompleted mission in the campaign catalog. Every scene trip goes through <code>LoadingScreen</code>, except the city-to-runner handoff, which loads the runner additively behind a full glitch and then unloads the city. When the campaign runs out, START MISSION opens <code>ComingSoon</code>.</p>

  <h3>Scenes</h3>
  <div class="rows cols-3">
    <div class="row head"><span>Scene</span><span>Game</span><span>What it is</span></div>
    <div class="row"><span><code>FiniteRunner_Test</code></span><span><i class="tag runner">Runner</i></span><span>Every mission's escape run plays here.</span></div>
    <div class="row"><span><code>CarTest</code></span><span><i class="tag city">City</i></span><span>The real city level: the baked <code>City.prefab</code>, the level flow, police and traffic, every HUD. <code>World_01</code> points at it.</span></div>
    <div class="row"><span><code>CityTest</code></span><span><i class="tag city">City</i></span><span>An old road-generator harness with no baked city. A sandbox, not a level.</span></div>
    <div class="row"><span><code>MainMenu</code> · <code>Store</code> · <code>ComingSoon</code></span><span><i class="tag shared">Shared</i></span><span>Menus, upgrades, and the end of the current campaign.</span></div>
    <div class="row"><span><code>Sandboxes/Sandbox_*</code></span><span><i class="tag shared">Shared</i></span><span>One scene per system, holding that system alone. Used by the smoke test.</span></div>
    <div class="row"><span><code>ShipSandbox</code> · <code>ShipAcceptance</code></span><span><i class="tag runner">Ship</i></span><span>The standalone hover ship on free terrain and on the acceptance course.</span></div>
  </div>

  <h3>The runner in one paragraph</h3>
  <p>Reach the level's target speed (the HUD's "Light Speed"), then leave the track off one of the three end ramps, before the countdown ends or the patrol catches you. The track is finite (33 km by default) and ends in a 1,200 m walled run-up and three ramps over a void. The throttle holds the ship at cruise speed; only boost orbs (green ×1, blue ×2.5, purple ×10, with a timed-press bonus up to ×1.5) push past it. Flat sweeps and open straights have no outer wall, so the ship can fall; it comes back further down the track. A police cruiser hunts you: it holds a standoff, commits to attack runs, takes your controls for a tug of war, and dies to one correctly timed dash press or a laser beam. Hull, lives and repair orbs sit on top.</p>

  <h3>The city in one paragraph</h3>
  <p>A mission brief opens, you accept optional challenges, then the level's objectives play in order (reach speed, escape police, go to a target, survive, chase a car, destroy cars, collect objects, jump). Hard impacts and police hits fill the player's damage meter, shown as screen glitch; three police hits reboot the level. Water splashes and explosive barrels also hurt. Completing the level hands off to the runner, which pays for the whole mission.</p>
</section>
'''

ARCHITECTURE = r'''
<section id="architecture" class="sec">
  <p class="eyebrow">Architecture</p>
  <h2>How it is built</h2>

  <h3>Assemblies</h3>
  <p>All game code is in <code>Assets/01.Scripts/</code>, one folder per assembly (namespace root <code>ConfusedGameDev.FiniteRunner</code>). References only point downward. The city depends on the runner and never the reverse, which is why shared screens and pickups live in the Runner assembly. <code>Contracts</code> holds interfaces only and references nothing: it is the seam systems talk through instead of naming each other.</p>
<pre class="mermaid">
flowchart BT
  SD["SaveData"]
  CON["Contracts<br/>interfaces only"]
  CAMP["Campaign"] --> SD
  UI["UI"] --> SD
  FX["FX"] --> UI
  FX --> REN["Rendering"]
  CAM["Cameras"] --> UI
  CAM --> FX
  CHT["Cheats"] --> UI
  CHT --> HAP["Haptics"]
  SHIP["Ship"] --> CAM
  RUN["Runner"] --> SHIP
  RUN --> CON
  RUN --> CAMP
  RUN --> CHT
  PE["PoliceEscape"] --> RUN
  PE --> DBG["Debugging"]
  PE --> CON
</pre>
  <p class="cap">Simplified: every game assembly also references <code>UI</code>, <code>FX</code> and <code>Haptics</code>. <code>PoliceEscape</code> also uses EVP5 (vehicle physics) and Cinemachine. Editor assemblies (<code>Runner.Editor</code>, <code>Ship.Editor</code>, <code>PoliceEscape.Editor</code>) sit beside their game assembly.</p>

  <div class="rows cols-2 narrow-first">
    <div class="row head"><span>Assembly</span><span>Holds</span></div>
    <div class="row"><span><code>Runner</code></span><span>The runner game: <code>GameFlow</code> (GameManager, RunFeedback, RunCameraDirector, PolicePatrol and the duel, settings assets), <code>Track</code> (TrackManager, TrackGenerator, features, spawners, pickups), <code>Ship</code> (ShipMotor, the runner's ship adapter), <code>HUD</code>, <code>Screens</code> (menus, pause, debug pages), <code>Store</code>, <code>Collectibles</code>, <code>Simulation</code> (TrackBody).</span></div>
    <div class="row"><span><code>Ship</code></span><span>The standalone hover ship: <code>HoverShip</code>, <code>HoverBody</code>, guides, recovery, pickups, input. Flies any collider surface; knows nothing about the track.</span></div>
    <div class="row"><span><code>PoliceEscape</code></span><span>The city game: <code>City</code> (CityManager, CityRoot, streamer, wrap, water), <code>AI</code> (police and traffic), <code>Vehicles</code> (CarController, backends, CarFactory, PlayerCars), <code>UI</code> (minimap, speedometer, map, city debug pages), <code>LevelManager</code>, <code>PlayerHealthMeter</code>, triggers, cinemas.</span></div>
    <div class="row"><span><code>Contracts</code></span><span>Interfaces only: <code>IStreamFocus</code>, <code>IShipPerformance</code>, <code>ITrackRunRules</code>, <code>IRepairable</code>, <code>IChaseTarget</code>, <code>IControlTakeover</code>, plus <code>ContractLookup</code>.</span></div>
    <div class="row"><span><code>UI</code></span><span>The menu framework, control bindings, <code>LoadingScreen</code>, the debug-page registry (<code>DebugPages</code>, <code>SettingsDebugPage</code>) and <code>DebugAssetEdits</code>.</span></div>
    <div class="row"><span><code>FX</code> · <code>Rendering</code></span><span>Full-screen drivers (DistanceFog, SpeedLines, VhsTape, PsxLook, CrtScreen, RainSystem, GlitchController) and their renderer features.</span></div>
    <div class="row"><span><code>Cameras</code></span><span><code>OrbitCameraRig</code>, <code>CameraRigInstaller</code>, <code>CameraShake</code>.</span></div>
    <div class="row"><span><code>Campaign</code> · <code>SaveData</code></span><span>The mission catalog and session; the player profile. The only type-sharing seams between the two games.</span></div>
    <div class="row"><span><code>Cheats</code> · <code>Haptics</code> · <code>Debugging</code></span><span>Cheat codes and console; gamepad rumble; editor-only debug overlays.</span></div>
  </div>

  <h3>The runner</h3>
  <p><code>GameManager</code> owns the run's rules (win, lose, countdown, objectives, lives, hull, endings, retry) and is the composition root: its <code>Awake</code> configures the hand-placed ship components, initialises the patrol, and binds its two presentation siblings on the same object, <code>RunFeedback</code> (rumble, shake, glitch, speed lines, story lines) and <code>RunCameraDirector</code> (jump, loop, fall and ending shots, the duel dolly). The track and the patrol never see the ship's motor: they talk through Contracts.</p>
<pre class="mermaid">
flowchart LR
  GM["GameManager<br/>rules + composition root"] -- "Bind" --> RF["RunFeedback"]
  GM -- "Bind" --> RC["RunCameraDirector"]
  GM -- "Init: rules, track" --> PP["PolicePatrol"]
  GM -- "configures" --> SM["ShipMotor"]
  SM --> HS["HoverShip<br/>(Ship assembly)"]
  HS -. "IShipGuide" .-> TGU["TrackGuide"]
  TG["TrackGenerator"] -. "IStreamFocus<br/>IShipPerformance" .-> SM
  TG -. "ITrackRunRules" .-> GM
  PP -. "IChaseTarget<br/>IControlTakeover" .-> SM
  HUD["RaceHud · ChaseMinimap"] -. "IRunState<br/>IChaseTarget" .-> GM
</pre>
  <p class="cap">Solid arrows: wiring by the composition root. Dotted arrows: interfaces.</p>

  <div class="cards">
    <div class="card"><h4><code>HoverShip</code> + <code>HoverBody</code></h4><p>A kinematic, cast-based surface follower, never a dynamic rigidbody. At Light Speed the ship covers about 36 m per physics step, so each tick is split into substeps of at most 4 m, the ground is read with probe rays and walls with hull sweeps. Nothing is detected by a moving trigger.</p></div>
    <div class="card"><h4><code>ShipMotor</code></h4><p>The runner's face of the ship. Mirrors the ship into track coordinates (distance from the start is the authoritative coordinate, not spline <i>t</i>) and adds the runner's rules: loop gate and drop, ramp lips and side hits, tube return, the track's end, laser sweeps.</p></div>
    <div class="card"><h4><code>TrackManager</code> + <code>TrackGenerator</code></h4><p>The manager owns the spline. The generator streams road, colliders (<code>TrackColliderBuilder</code>), features and pickups ahead of the ship, builds the end ramps, and culls behind. Layout comes from one shape asset; spawners are cloned per run.</p></div>
    <div class="card"><h4><code>PolicePatrol</code></h4><p>Drives the same <code>TrackBody</code> physics as the ship through <code>PatrolDriver</code>, and runs the duel's state machine (<code>PatrolEncounter</code>): Cruising, Committing, Alongside, TugOfWar, Finisher, BreakingOff, Cooldown, plus Overshooting and MissBraking.</p></div>
    <div class="card"><h4>HUD and screens</h4><p><code>RaceHud</code>, <code>ChaseMinimap</code>, <code>DuelBarHud</code>, <code>BoostQte</code>, <code>MissionAccomplishedBanner</code>, <code>MissionCompleteScreen</code>, <code>GameOverScreen</code>, <code>PauseMenu</code>. All hand-placed in <code>PF_UI</code> and bound at run start.</p></div>
  </div>

  <h4 class="sub">Runner scene headers</h4>
  <div class="rows cols-2 narrow-first">
    <div class="row head"><span>Header</span><span>Holds (hand-placed, each a nested <code>PF_</code> prefab)</span></div>
    <div class="row"><span><code>===SYSTEMS===</code></span><span>GameManager (+ RunFeedback, RunCameraDirector), CollectibleManager, EventSystem, FloatingText, RpgMessage, Haptics, CheatManager, Music, the patrol</span></div>
    <div class="row"><span><code>===PLAYER===</code></span><span>Ship: HoverShip, ShipMotor, ShipRecovery, ShipPickupSweeper, SteeringInput, and the run components (LoopSlowMo, ShipArmed, RespawnBlink, ShipAudio, BarrelRollTrail, ShipHealth, DuelSlowMo, DashGhostTrail)</span></div>
    <div class="row"><span><code>===ENV===</code></span><span>Track: TrackManager, TrackGenerator, TrackDecorator, TrackColliderBuilder, TrackGuide</span></div>
    <div class="row"><span><code>===CAMERAS===</code></span><span>The orbit rig (carrying its own camera settings), main, cinematic and first-person cameras</span></div>
    <div class="row"><span><code>===UI===</code></span><span>RaceHUD, ChaseMinimap, DashPrompt, DuelBarHud, BoostQte, MoneyHud, PauseMenu</span></div>
    <div class="row"><span><code>===LIGHTING===</code></span><span>Volume, lights, and <code>Filters</code>: fog, glitch, speed lines, rain, VHS, PSX, CRT</span></div>
  </div>

  <h4 class="sub">A run, from boot to retry</h4>
  <ol class="steps">
    <li><code>TrackGenerator.Awake</code> clones the shape and spawner assets and builds the first stretch.</li>
    <li><code>GameManager.Awake</code> builds the run's ship definition (a clone of the asset with the Store upgrades multiplied in), configures the ship components, deals the lives, initialises the patrol (a clone of its definition), binds RunFeedback and RunCameraDirector, and switches the full-screen drivers and music on or off.</li>
    <li><code>ShipMotor.Start</code> launches the ship from the track's start pose.</li>
    <li>Every tick the ship steps, the motor mirrors it and applies the runner's rules, the patrol steps its body and the duel, the GameManager ticks the countdown and objectives, and the generator streams ahead and culls behind.</li>
    <li>Win: the ship leaves an end ramp with every objective met. The banner slams in, the glitch ramps to max, and the Mission Complete panel pays the mission.</li>
    <li>Lose: a life is taken, the MISSION FAILED banner plays, then the retry panel. RETRY restarts in place: the track regenerates and ship and patrol relaunch. The last life forfeits the mission and returns to the Store.</li>
  </ol>

  <h3>The city</h3>
  <p>The city is generated offline into <code>03.Prefabs/PoliceEscape/City.prefab</code>; play mode generates nothing. <code>LevelManager</code> runs the objectives and the retry, and gates the <code>PlayerHealthMeter</code> beside it. Each placed system owns its own settings asset; <code>CityManager</code> fronts the baked city and nothing else.</p>
<pre class="mermaid">
flowchart LR
  LM["LevelManager<br/>objectives, retry"] -- "gates, answers Depleted" --> HM["PlayerHealthMeter"]
  LM -- "resets on retry" --> PM["PatrolManager"]
  LM -- "resets on retry" --> TM["TrafficManager"]
  LM -- "respawns" --> PCS["PlayerCarSpawner"]
  PCS --> CF["CarFactory"] --> CAR["Player car<br/>CarInput registers itself"]
  CAR --> REG["PlayerCars.Current"]
  PM -. "who is the player" .-> REG
  TM -. "who is the player" .-> REG
  PM -. "road graph" .-> CM["CityManager<br/>+ CityRoot"]
  TM -. "road graph" .-> CM
  PDR["Blasts · water"] --> HM
  HM -- "SetFloor" --> GC["GlitchController"]
</pre>

  <div class="cards">
    <div class="card"><h4><code>CarController</code> + backends</h4><p>One car simulation for everyone, driven by an <code>ICarInput</code>: <code>CarInput</code> (player), <code>PoliceCarInput</code>, <code>TrafficCarInput</code>. Built-in WheelColliders or EVP5 (<code>EvpCarBackend</code>), chosen on <code>PoliceEscape_VehiclePhysics</code>.</p></div>
    <div class="card"><h4><code>PlayerCars</code></h4><p>The one answer to "which car is the player's". Every <code>CarInput</code> registers itself; <code>PlayerCars.Current</code> is the newest active player car, <code>All()</code> a snapshot.</p></div>
    <div class="card"><h4><code>PatrolManager</code> + <code>PoliceCarInput</code></h4><p>Keeps the police fleet alive around the player. Each cruiser runs Patrol, Chase and Search, rams inside 1.5 cells, and sounds its siren only in Chase.</p></div>
    <div class="card"><h4><code>TrafficManager</code> + <code>TrafficCarInput</code></h4><p>Civilians inside an active radius: wander, queue, work stops. A civilian can become a Chase Car objective's quarry. Police and traffic share one driving profile type (<code>AiDrivingProfile</code>).</p></div>
    <div class="card"><h4><code>PlayerHealthMeter</code></h4><p>Owns the player's damage (0 to 1), heals it, and shows it on the glitch as a floor. Impacts, police hits, blasts and splashes land here; at full it fires <code>Depleted</code> and the level reboots.</p></div>
    <div class="card"><h4>City generation</h4><p><code>CityDefinition</code> (grid, seed, blocks) + <code>CityGenerationSettings</code> (city-wide) + <code>BlockSettings</code> (interior). <code>CityBaker</code> builds roads, features, buildings, props, parks and shorelines. At runtime <code>CityRoot</code> rebuilds the <code>RoadGraph</code>, <code>CityStreamer</code> switches blocks by distance and <code>CityWrap</code> wraps the player across the edge.</p></div>
  </div>

  <h3>Shared systems</h3>
  <div class="cards">
    <div class="card"><h4>Campaign</h4><p><code>CampaignCatalog</code> lists worlds, each world lists missions, each mission is a city level plus a runner level. The frontier is the first uncompleted mission. <code>MissionSession</code> swaps the session's level assets into <code>LevelManager</code> and <code>GameManager</code>.</p></div>
    <div class="card"><h4>Menus and the debug menu</h4><p>Code-built menus with plates that auto-fit their text in four languages. The pause menu's DEBUG row builds the runner's own tabs, then every page registered in <code>DebugPages</code> (the city's car tabs, the shared FX pages).</p></div>
    <div class="card"><h4>Cameras</h4><p><code>OrbitCameraRig</code> follows any <code>ICameraTarget</code>: far, close and first-person views, look-back, a planted cinematic shot, duel framing, speed FOV. The rig placed in each scene carries its own settings asset. Its far clip follows its own scene's distance fog.</p></div>
    <div class="card"><h4>Screen FX</h4><p>Hand-placed drivers write shared materials: <code>DistanceFog</code>, <code>SpeedLines</code>, <code>VhsTape</code>, <code>PsxLook</code>, <code>CrtScreen</code>, <code>RainSystem</code>, <code>GlitchController</code>. Each scene's driver carries that scene's own asset. The player's VIDEO page scales PSX, VHS and CRT.</p></div>
    <div class="card"><h4>Audio</h4><p>One mixer: Master, Gameplay (Music, FX, Voice), UI, pause and loading buses. <code>GameAudio</code> snapshots duck for pause, loading and cinemas.</p></div>
    <div class="card"><h4>Haptics, cheats, save data</h4><p><code>HapticsSystem</code> plays one-shot pulses and the chase rumble on unscaled time. <code>CheatManager</code> reads codes from <code>FiniteRunner_Cheats</code>. <code>profile.json</code> is written only through <code>PlayerStats</code>.</p></div>
  </div>
</section>
'''

CODING = r'''
<section id="coding" class="sec">
  <p class="eyebrow">Coding reference</p>
  <h2>Rules, patterns and recipes</h2>

  <h3>Rules that hold everywhere</h3>
  <p>Break one of these and something else quietly stops working.</p>
  <div class="rules">
    <div><b>Gameplay never writes a settings asset.</b> Where gameplay changes values it works on a runtime clone (the ship definition with Store upgrades, the patrol definition, track shape, spawners, feature definitions). A debug-menu row writes the asset, marks it with <code>DebugAssetEdits.Touch</code>, then mirrors the value onto the live clone. What the inspector shows is what runs.</div>
    <div><b>Tunables live in ScriptableObjects</b>, not as fields on managers. Add a knob to the settings asset, not to the component that reads it. Draw it with Odin: a <code>[PropertyRange]</code> slider with a hand-picked range, a <code>[MinMaxSlider]</code> for a paired band (unpacked by accessor properties), <code>[ToggleGroup]</code> for optional blocks, and inline the asset into the component that uses it.</div>
    <div><b>Scene-lifetime systems are hand-placed</b> under <code>===SYSTEMS===</code> (or their scene header), so they can be tuned before play. Code finds or parks them; it never creates them at play time. Per-run objects (cars, NPCs, the mission brief) are spawned under runtime headers that are forced back to the origin.</div>
    <div><b>Systems stay independent.</b> A system dropped alone into an empty scene must boot and then idle, hide or fall back, never throw. Systems reach each other through Contracts, <code>Bind</code> calls from a composition root, registries, or events, not by searching the scene for another system's type.</div>
    <div><b>Domain reload is off.</b> Static state, cached assets and event subscriptions survive between play sessions. Subscribe in <code>OnEnable</code> / <code>OnDisable</code>, never in a static initialiser, and reset statics in a <code>[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]</code>.</div>
    <div><b>A shared material written by a driver is restored on disable</b> (<code>_Intensity</code> zeroed), and only the last instance standing cleans it: the additive handoff has two drivers alive at once.</div>
    <div><b>The ship is kinematic.</b> Never a dynamic rigidbody, never detected by a moving trigger. Laser gates stay analytic (no colliders).</div>
    <div><b>Every scene trip goes through <code>LoadingScreen</code></b>, except the city-to-runner handoff.</div>
    <div><b>Serialized enums are append-only.</b> Fields on anything baked into a prefab are <code>[SerializeField]</code>: a plain private field deserialises as zero.</div>
    <div><b>Player-facing strings</b> are <code>MenuTextId</code> entries in <code>MenuTextLibrary</code>, translated in all four languages. Menu plates auto-fit; never hardcode a plate width. Gameplay reads input through <code>ControlBindings</code>, never the devices directly.</div>
  </div>

  <h3>How systems reach each other</h3>
  <div class="rows cols-3">
    <div class="row head"><span>Mechanism</span><span>Where</span><span>Use it for</span></div>
    <div class="row"><span>Contracts interfaces</span><span><code>Contracts/TrackContracts.cs</code>, <code>ChaseContracts.cs</code></span><span>Cross-system reads and verbs: the track reads the ship through <code>IStreamFocus</code> / <code>IShipPerformance</code> and the run through <code>ITrackRunRules</code>; the patrol hunts through <code>IChaseTarget</code> and takes controls through <code>IControlTakeover</code>; repair orbs heal an <code>IRepairable</code>.</span></div>
    <div class="row"><span>Game-level interfaces</span><span><code>IRunState</code> (Runner), <code>ICameraTarget</code> (Cameras), <code>IShipGuide</code> (Ship), <code>ICarInput</code>, <code>IDamageable</code> (city)</span><span>Views of a system inside one game: the HUD reads the run through <code>IRunState</code>, the camera follows any <code>ICameraTarget</code>, blasts damage any <code>IDamageable</code>.</span></div>
    <div class="row"><span>Composition roots</span><span><code>GameManager.Awake</code>, <code>LevelManager</code></span><span>Wiring: the root finds the placed components and calls their <code>Init</code> / <code>Bind</code> / <code>Configure</code>. A sibling component is fetched with its static <code>Ensure(host)</code>, which returns the placed one and adds it only if missing.</span></div>
    <div class="row"><span>Registries</span><span><code>PlayerCars</code>, <code>DebugPages</code>, <code>SpeedPad.BoostTiming</code>, <code>DistanceFog.For(scene)</code></span><span>"Which one is it" questions without a scene search: the player car, the debug pages on offer, the boost-timing hook, the fog of a given scene.</span></div>
    <div class="row"><span><code>ContractLookup.Find&lt;T&gt;()</code></span><span><code>Contracts/ContractLookup.cs</code></span><span>Discovery by interface when nothing handed the reference in (a HUD dropped into a scene on its own).</span></div>
    <div class="row"><span>Events</span><span>ship, patrol, pickups, health</span><span>Reactions: <code>ShipMotor.Landed</code>, <code>PolicePatrol.Redeployed</code>, <code>SpeedPad.Collected</code>, <code>PlayerHealthMeter.Depleted</code>. Subscribe in <code>OnEnable</code> or <code>Bind</code>, unsubscribe in <code>OnDisable</code> or <code>OnDestroy</code>.</span></div>
  </div>

  <h3>Recipes</h3>
  <div class="recipes">
    <details open><summary>Add a tunable</summary>
      <ol>
        <li>Add the field to the system's settings asset class, in the right <code>[TitleGroup]</code>, with a <code>[Tooltip]</code> and a <code>[PropertyRange]</code> (or <code>[MinMaxSlider]</code> plus accessor properties for a band).</li>
        <li>Give it the value the game runs today as its initialiser, so existing assets keep playing the same.</li>
        <li>Read it where it is used. If gameplay works on a clone, read the clone.</li>
        <li>For live tuning, add a debug row (below). Speeds are m/s; show km/h with × 3.6.</li>
      </ol></details>
    <details><summary>Add a debug page for a system's asset</summary>
      <ol>
        <li>If the system re-reads its asset every frame, write a page on <code>SettingsDebugPage&lt;T&gt;</code>: <code>new SettingsDebugPage&lt;T&gt;(name, parent, theme, title, asset, refreshers, tabIndex, tabCount)</code>, then one <code>.Slider(label, min, max, step, format, get, set)</code> per row.</li>
        <li>Register it from the system's own assembly: <code>[RuntimeInitializeOnLoadMethod(AfterAssembliesLoaded)] static void Register() =&gt; DebugPages.Register(nameof(MyPage), order, () =&gt; DebugPages.Single(Discover(), Build));</code>. <code>Discover</code> returns null when the scene has nothing to tune.</li>
        <li>If gameplay works on a clone, build the rows yourself: write the asset, call <code>DebugAssetEdits.Touch(asset)</code>, then mirror onto the clone.</li>
        <li>Add every label as a <code>MenuTextId</code> in all four languages. The pause menu saves edits at resume and reload.</li>
      </ol></details>
    <details><summary>Add a new system</summary>
      <ol>
        <li>Give it one settings asset and one prefab. Place the prefab in the scenes that use it (it is never spawned at play time).</li>
        <li>Take what it needs from others through an interface, a <code>Bind</code> from the composition root, a registry, or an event. With nothing bound it should idle.</li>
        <li>Add an entry to <code>SystemSandboxes.Systems</code>, run <b>Build System Sandboxes</b> and <b>Run Sandbox Smoke Test</b>: it must pass alone.</li>
        <li>Run <b>Validate System Independence</b>. A new cross-system scene search fails it: hand the reference in, or add it to the accept list with a reason a reviewer can argue with.</li>
      </ol></details>
    <details><summary>Add a rumble, shake or glitch beat</summary>
      <ol>
        <li>A rumble is a <code>Vector3</code> (low motor, high motor, seconds) on the settings asset of its event, played with <code>HapticsSystem.Instance.Pulse(value)</code>.</li>
        <li>A camera shake is its own <code>CameraShakeSettings</code> asset, referenced from the event's settings.</li>
        <li>A glitch burst is <code>GlitchController.Instance.Pulse(strength)</code>. A sequence that must hold the picture calls <code>Hold(owner, level)</code> and later <code>Release(owner)</code>. A value another system owns (the city's damage) is shown with <code>SetFloor(owner, level)</code>, which neither suspends the fade nor writes the base.</li>
        <li>In the runner, reactions go in <code>RunFeedback</code> and shots in <code>RunCameraDirector</code>, never back into <code>GameManager</code>.</li>
      </ol></details>
    <details><summary>Read the player car, damage the player</summary>
      <ol>
        <li>The player car is <code>PlayerCars.Current</code> (null while none exists). Never scan the scene for it.</li>
        <li>Player damage goes through <code>PlayerHealthMeter.ApplyDamage(amount, reason)</code>, or <code>ApplyBlast</code> / <code>ApplySplash</code>. The Store's resistance upgrade divides it there.</li>
        <li>Blasts use <code>Blast.Apply(origin, BlastProfile, ignore)</code>, which damages every <code>IDamageable</code> in range and throws loose bodies.</li>
      </ol></details>
    <details><summary>Check a tuning change did what you meant</summary>
      <ol>
        <li>Run <b>Tools → Refactor → Dump Effective Tuning</b> in play mode before and after the change: it writes the values a run really uses (clones included) to <code>Temp/TuningDump</code>.</li>
        <li>Run <b>Compare Last Two Tuning Dumps</b> to list every value that moved.</li>
      </ol></details>
  </div>

  <h3>Editor tools</h3>
  <div class="rows cols-2 narrow-first">
    <div class="row head"><span>Menu</span><span>What it does</span></div>
    <div class="row"><span><code>Tools/Refactor/Validate System Independence</code></span><span>Lists every cross-system scene search; fails on one that is not on the reasoned accept list.</span></div>
    <div class="row"><span><code>Tools/Refactor/Build System Sandboxes</code> · <code>Run Sandbox Smoke Test</code></span><span>One scene per system in <code>05.Scenes/Sandboxes/</code>; plays each alone for 4 s and writes <code>Temp/SandboxSmoke.txt</code>.</span></div>
    <div class="row"><span><code>Tools/Refactor/Dump Effective Tuning</code> · <code>Compare Last Two Tuning Dumps</code></span><span>The values a run really uses, and what changed between two dumps.</span></div>
    <div class="row"><span><code>Tools/FiniteRunner/Place Scene Systems</code></span><span>Places whatever runner scene system the open scene is missing.</span></div>
    <div class="row"><span><code>Tools/FiniteRunner/Install … Feature</code></span><span>Speed lines, VHS tape, PSX look, CRT screen: creates the material and asset and inserts the renderer feature.</span></div>
    <div class="row"><span><code>Tools/FiniteRunner/Create Runner Level Definition</code> · <code>Create Campaign Assets</code> · <code>Register Campaign Scenes</code></span><span>Level and campaign authoring.</span></div>
    <div class="row"><span><code>Tools/FiniteRunner/Ship/…</code></span><span>Ship layers, HoverShip prefabs, the acceptance and physics-runner scenes.</span></div>
    <div class="row"><span><code>Tools/Police Escape/City Designer</code></span><span>Edit the city definition and rebake <code>City.prefab</code>.</span></div>
    <div class="row"><span><code>Tools/Police Escape/Place Scene Systems</code></span><span>Places the city's scene systems (fleets, HUD, camera rig, cinema, radio, stats), each wired with its own asset.</span></div>
    <div class="row"><span><code>Tools/Police Escape/Create Car Test Scene</code> · <code>Create City Test Scene</code></span><span>Rebuild the city scenes from their builders.</span></div>
    <div class="row"><span><code>Tools/Police Escape/Apply City Static Flags</code> · <code>Bake Occlusion Culling</code></span><span>City performance passes after a rebake.</span></div>
  </div>

  <h3>Debug menu tabs</h3>
  <p>Pause (Esc or Start), then DEBUG. Bumpers or Q / E cycle tabs. Every slider writes its asset; edits are saved on resume and reload. Track and ship edits ask to reload the scene when you back out.</p>
  <div class="rows cols-3">
    <div class="row head"><span>Scene</span><span>Tab</span><span>Edits</span></div>
    <div class="row"><span><i class="tag runner">Runner</i></span><span>CORE SETTINGS · MULTIPLIERS · FEATURES</span><span>Track shape, track length, spawner spacing, orb tiers and boosts, the feature table and jump definition.</span></div>
    <div class="row"><span><i class="tag runner">Runner</i></span><span>SHIP SPEED · HANDLING · DASH · HOVER</span><span>The ship definition (the run's clone is rebuilt with the Store upgrades).</span></div>
    <div class="row"><span><i class="tag runner">Runner</i></span><span>FALL &amp; RESPAWN</span><span>Fall and respawn rules on GameSettings and the ship settings.</span></div>
    <div class="row"><span><i class="tag runner">Runner</i></span><span>PATROL · PATROL DRIVER · DUEL</span><span>The patrol definition and its Duel group; the duel camera rows edit the rig's camera settings.</span></div>
    <div class="row"><span><i class="tag city">City</i></span><span>CAR DRIVE · CAR GRIP · AIR TIME · DAMAGE</span><span><code>TestCarConfig</code>; the player's upgraded clone is rebuilt live.</span></div>
    <div class="row"><span><i class="tag city">City</i></span><span>CHASE CAMERA · CAMERA MODES</span><span>The scene rig's camera settings.</span></div>
    <div class="row"><span><i class="tag city">City</i></span><span>POLICE FLEET · POLICE CHASE · LEVEL</span><span>Pursuit settings; the level's objectives with live status.</span></div>
    <div class="row"><span><i class="tag shared">Both</i></span><span>WEATHER · DISTANCE FOG · SPEED LINES · VHS · PSX · CRT</span><span>The scene's FX assets, wherever the driver exists.</span></div>
  </div>

  <h3>Interfaces</h3>
  <div class="rows cols-3">
    <div class="row head"><span>Interface</span><span>File</span><span>Role</span></div>
    <div class="row"><span><code>IStreamFocus</code> · <code>IShipPerformance</code> · <code>ITrackRunRules</code></span><span><code>Contracts/TrackContracts.cs</code></span><span>What the track needs: where to stream, the ship's speed model, the run's track rules.</span></div>
    <div class="row"><span><code>IRepairable</code></span><span><code>Contracts/TrackContracts.cs</code></span><span>What a repair orb heals.</span></div>
    <div class="row"><span><code>IChaseTarget</code> · <code>IControlTakeover</code></span><span><code>Contracts/ChaseContracts.cs</code></span><span>What the patrol hunts, and the verbs it may use on the ship during a duel.</span></div>
    <div class="row"><span><code>IRunState</code></span><span><code>Runner/GameFlow/IRunState.cs</code></span><span>A view of the run: clock, lives, hull, objectives, distance left, endings.</span></div>
    <div class="row"><span><code>IRunnerShip</code></span><span><code>Runner/Ship/IRunnerShip.cs</code></span><span>The runner's view of its ship.</span></div>
    <div class="row"><span><code>IShip</code> · <code>IShipGuide</code> · <code>IShipPickup</code></span><span><code>Ship/</code></span><span>The standalone ship, its optional guide spline, what it can pick up.</span></div>
    <div class="row"><span><code>ISteeringInput</code> · <code>IThrottleInput</code> · <code>IDashInput</code></span><span><code>Ship/SteeringInput.cs</code></span><span>Ship input, swappable (a VR implementation can replace it).</span></div>
    <div class="row"><span><code>ICameraTarget</code></span><span><code>Cameras/ICameraTarget.cs</code></span><span>Anything the orbit rig can follow.</span></div>
    <div class="row"><span><code>ITrackPickup</code> · <code>ICollector</code> · <code>IRunConsumable</code></span><span><code>Runner/</code></span><span>Analytic pickups on the track, who collects them, run-scoped consumables.</span></div>
    <div class="row"><span><code>ICarInput</code> · <code>IDamageable</code></span><span><code>PoliceEscape/</code></span><span>Who drives a car; what a blast can hurt.</span></div>
    <div class="row"><span><code>IDebugTabs</code></span><span><code>UI/DebugMenuHooks.cs</code></span><span>A batch of debug tabs a registered provider builds.</span></div>
    <div class="row"><span><code>IWeightedEntry</code></span><span><code>Runner/Track/Spawning/WeightedTable.cs</code></span><span>A row of a probability table (orb tiers, features).</span></div>
  </div>
</section>
'''
