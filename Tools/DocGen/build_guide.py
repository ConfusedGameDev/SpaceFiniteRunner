# -*- coding: utf-8 -*-
"""Builds the handbook: Temp/GuideDoc/handbook.html (artifact body) and Documentation.html (standalone)."""
import os, json, html, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
import content

OUT_DIR = os.path.join(ROOT, "Temp", "DocGen")
REF = json.load(open(os.path.join(OUT_DIR, "tuning_ref.json"), encoding="utf-8"))
E = html.escape

# ------------------------------------------------------------------ data helpers
FIELDS = {}
for s in REF:
    for a in s["assets"]:
        FIELDS[a["asset"]] = {f["name"]: f for f in a["fields"]}
        # toggle-group members are grouped by the switch's field name: use the switch's title
        titles = {f["name"]: f["group"] for f in a["fields"]}
        for f in a["fields"]:
            g = f["group"]
            if g in titles and g != f["name"]:
                f["group"] = titles[g]
            if f["group"].count("(") > f["group"].count(")"):
                f["group"] = f["group"].split(" (")[0]

missing = []
def knob(asset, field, label=None):
    f = FIELDS.get(asset, {}).get(field)
    if not f:
        missing.append((asset, field)); return ""
    unit = f["unit"]
    val = str(f["value"])
    if unit and unit not in ("x",) and not val.endswith(unit):
        val = val + " " + unit
    return '<span class="knob"><span class="k">%s</span><span class="v">%s</span></span>' % (E(label or field), E(val))

def wbr(name):
    """Asset and field names break only at word joints, never mid-word."""
    s = E(name)
    s = re.sub(r"(_)", r"\1<wbr>", s)
    s = re.sub(r"([a-z])([A-Z])", r"\1<wbr>\2", s)
    s = s.replace(".", ".<wbr>")
    return s

def code(name): return "<code>%s</code>" % wbr(name)

# ------------------------------------------------------------------ the tuning map
# (game, system, assets, where you edit it, debug tab, [(asset, field, label)])
G, S, SH, TS = "FiniteRunner_GameSettings", "Fighter_ShipDefinition", "Runner_ShipSettings", "FiniteRunner_TrackShape"
MAP = [
  ("runner", "Run rules", [G], "GameManager › Settings, in <code>PF_Systems</code>", "FALL &amp; RESPAWN",
   [(G, "timeLimitSeconds", "time limit"), (G, "trackLengthMeters", "track length"), (G, "startingLives", "lives"), (G, "laserSpeedLoss", "laser speed loss"), (G, "boostQteWindowSeconds", "QTE window"), (G, "powerUpSpeedBoost", "orb boost")]),
  ("runner", "Run level and target speed", ["FiniteRunner_LevelDefinition"], "GameManager › Level (a campaign mission swaps in its own)", "",
   [("FiniteRunner_LevelDefinition", "levelName", "level"), ("FiniteRunner_LevelDefinition", "trackLengthMeters", "track length (0 = settings)"), ("FiniteRunner_LevelDefinition", "rankTable.sThreshold", "rank S"), (G, "lightSpeedKmh", "fallback Light Speed")]),
  ("runner", "Hull, lives and damage", [G], "GameManager › Settings › Hull and lives", "",
   [(G, "wallSlamDamage", "wall slam"), (G, "wallScrapeDamage", "scrape"), (G, "fallDamage", "fall"), (G, "laserDamage", "laser"), (G, "repairOrbHealFraction", "repair orb"), ("Fighter_ShipDefinition", "maxHull", "max hull")]),
  ("runner", "Ship stats", [S], "HoverShip › Definition, on <code>PF_Ship</code>", "SHIP SPEED · HANDLING · DASH · HOVER",
   [(S, "cruiseSpeed", "cruise"), (S, "thrust", "thrust"), (S, "brakeDecel", "brake"), (S, "lateralSpeed", "lateral"), (S, "gripBase", "grip"), (S, "dashDistance", "dash")]),
  ("runner", "Ship rules and feel", [SH], "HoverShip › Settings, on <code>PF_Ship</code>", "FALL &amp; RESPAWN",
   [(SH, "maxStepMeters", "substep"), (SH, "guideAssist", "guide assist"), (SH, "dashCost", "dash cost"), (SH, "fallGravity", "fall gravity"), (SH, "respawnWaitSeconds", "respawn wait"), (SH, "respawnSpeedPenalty", "respawn speed loss")]),
  ("runner", "Patrol chase", ["Police_PatrolDefinition"], "PolicePatrol › Definition, under <code>===SYSTEMS===</code>", "PATROL · PATROL DRIVER",
   [("Police_PatrolDefinition", "rubberBand", "rubber band"), ("Police_PatrolDefinition", "boostShare", "boost share"), ("Police_PatrolDefinition", "startGap", "start gap"), ("Police_PatrolDefinition", "warnDistance", "warn"), ("Police_PatrolDefinition", "sustainedCatchSeconds", "catch fuse")]),
  ("runner", "Patrol duel", ["Police_PatrolDefinition"], "PolicePatrol › Definition › Duel", "DUEL",
   [("Police_PatrolDefinition", "commitIntervalSeconds", "attack every"), ("Police_PatrolDefinition", "commitFromDistance", "commit from"), ("Police_PatrolDefinition", "duelTimeScale", "duel slow-mo"), ("Police_PatrolDefinition", "finisherWindowSeconds", "kill window"), ("Police_PatrolDefinition", "ramSpeedCost", "ram cost")]),
  ("runner", "Track layout", [TS], "TrackGenerator › Shape, on <code>PF_Track</code>", "CORE SETTINGS · FEATURES",
   [(TS, "trackWidth", "width"), (TS, "straightness", "straightness"), (TS, "featureSpacing", "feature spacing"), (TS, "unbankedSweepChance", "flat sweeps"), (TS, "openStraightChance", "open straights"), (TS, "maxGrade", "max grade")]),
  ("runner", "Track features", ["Jump_Definition", "Loop_Definition", "FullTube_Definition", "LaserGate_Definition"], "The feature table on the track shape points at each definition", "FEATURES",
   [("Jump_Definition", "length", "ramp length"), ("Jump_Definition", "rampAngle", "ramp angle"), ("Loop_Definition", "radius", "loop radius"), ("LaserGate_Definition", "coverageBand", "laser coverage")]),
  ("runner", "Pickups", ["Spawner_SpeedOrbs", "Spawner_RepairOrbs", "Spawner_LaserGates"], "The generator's spawn set (<code>FiniteRunner_TrackSpawnSet</code>)", "CORE SETTINGS · MULTIPLIERS",
   [("Spawner_SpeedOrbs", "spacing", "orbs every"), ("Spawner_RepairOrbs", "spacing", "repair every"), ("Spawner_RepairOrbs", "chance", "repair chance"), ("Spawner_LaserGates", "spacing", "lasers every")]),
  ("runner", "Chase camera", ["Fighter_CameraSettings"], "OrbitCameraRig › Settings, on <code>PF_CameraController</code>", "DUEL (duel framing)",
   [("Fighter_CameraSettings", "distance", "distance"), ("Fighter_CameraSettings", "defaultMode", "default view"), ("Fighter_CameraSettings", "baseFov", "FOV"), ("Fighter_CameraSettings", "maxFovBoost", "speed FOV"), ("Fighter_CameraSettings", "cinematicDistance", "cinematic distance")]),
  ("runner", "Feedback", [G, "LandingShake_Settings"], "GameManager › Settings (Haptics, Loops, Mission complete); one shake asset per event", "",
   [(G, "boostRumble", "boost rumble"), (G, "landingRumble", "landing rumble"), (G, "loopSlowMo", "loop slow-mo"), (G, "winGlitchRampSeconds", "win glitch ramp"), ("LandingShake_Settings", "positionAmplitude", "landing shake")]),
  ("runner", "Screen FX", ["FiniteRunner_RunnerFog", "FiniteRunner_SpeedLines", "Runner_Rain", "Runner_VhsTape", "Runner_PsxLook", "Runner_CrtScreen"], "Each driver in <code>PF_Filters</code> carries its asset; GameSettings only switches them on", "WEATHER · FOG · SPEED LINES · VHS · PSX · CRT",
   [("FiniteRunner_RunnerFog", "fogStart", "fog start"), ("FiniteRunner_RunnerFog", "fogEnd", "fog end"), ("FiniteRunner_RunnerFog", "farClipMargin", "far clip margin"), ("FiniteRunner_SpeedLines", "intensity", "speed lines")]),
  ("runner", "Audio", ["FiniteRunner_Music", "FiniteRunner_Sfx"], "The scene's Music object; GameSettings › Sound effects", "", []),
  ("city", "Car handling", ["TestCarConfig"], "CarController › Config on each car prefab (player, police, traffic share it)", "CAR DRIVE · CAR GRIP · AIR TIME · DAMAGE",
   [("TestCarConfig", "mass", "mass"), ("TestCarConfig", "maxMotorTorque", "torque"), ("TestCarConfig", "topSpeedKmh", "top speed"), ("TestCarConfig", "maxSteerAngle", "steer"), ("TestCarConfig", "evpDriveForce", "EVP drive"), ("TestCarConfig", "spawnSpeedKmh", "spawn speed")]),
  ("city", "Police fleet and chase", ["TestPursuitSettings"], "PatrolManager › Settings, under <code>===SYSTEMS===</code>", "POLICE FLEET · POLICE CHASE",
   [("TestPursuitSettings", "targetPatrolCount", "fleet"), ("TestPursuitSettings", "detectionRange", "detection"), ("TestPursuitSettings", "chaseSpeedKmh", "chase speed"), ("TestPursuitSettings", "searchDuration", "search"), ("TestPursuitSettings", "driving.cornerSpeedKmh", "corner speed")]),
  ("city", "Traffic", ["TestTrafficSettings"], "TrafficManager › Settings, under <code>===SYSTEMS===</code>", "",
   [("TestTrafficSettings", "targetVehicleCount", "vehicles"), ("TestTrafficSettings", "activeRadius", "active radius"), ("TestTrafficSettings", "cruiseSpeedBand", "cruise"), ("TestTrafficSettings", "driving.cornerSpeedKmh", "corner speed")]),
  ("city", "Player health and damage", ["PoliceEscape_VehicleHealth"], "<code>Resources/</code>, Player group (loaded by name)", "DAMAGE",
   [("PoliceEscape_VehicleHealth", "playerPoliceHitDamage", "police hit"), ("PoliceEscape_VehicleHealth", "playerHealPerSecond", "heal"), ("PoliceEscape_VehicleHealth", "playerBlastDamageScale", "blast plating"), ("PoliceEscape_VehicleHealth", "minImpactSpeed", "impact floor")]),
  ("city", "NPC damage and blasts", ["PoliceEscape_VehicleHealth"], "<code>Resources/</code>, Damage, Police, Thresholds and Blast groups", "",
   [("PoliceEscape_VehicleHealth", "damagePerImpactSpeed", "damage per m/s"), ("PoliceEscape_VehicleHealth", "policeToughness", "police toughness"), ("PoliceEscape_VehicleHealth", "blast.radius", "blast radius"), ("PoliceEscape_VehicleHealth", "blast.force", "blast force")]),
  ("city", "Vehicle physics backend", ["PoliceEscape_VehiclePhysics"], "<code>Resources/</code> (loaded by name)", "",
   [("PoliceEscape_VehiclePhysics", "backend", "backend")]),
  ("city", "City level", ["TestLevelDefinition"], "LevelManager › Level (read live; a campaign mission swaps in its own)", "LEVEL",
   [("TestLevelDefinition", "levelName", "level"), ("TestLevelDefinition", "baseReward", "base reward"), ("TestLevelDefinition", "objectives", "objectives"), ("TestLevelDefinition", "optionalChallenges", "challenges")]),
  ("city", "City layout (bake)", ["CityDefinition", "CityTestSettings", "BlockSettings_Downtown", "District_Downtown"], "Tools › Police Escape › City Designer, then rebake", "",
   [("CityTestSettings", "cellSize", "cell"), ("CityTestSettings", "arterialSpacing", "arterial spacing"), ("CityTestSettings", "splashDamage", "splash damage"), ("CityTestSettings", "overpassChance", "overpasses")]),
  ("city", "City HUD and camera", ["TestSpeedometerSettings", "TestMinimapSettings", "TestCityMapSettings", "TestOrbitCameraSettings"], "Each HUD object's Settings; the scene's OrbitCameraRig", "CHASE CAMERA · CAMERA MODES", []),
  ("city", "City screen FX", ["FiniteRunner_DistanceFog", "FiniteRunner_Rain", "FiniteRunner_VhsTape", "FiniteRunner_PsxLook", "FiniteRunner_CrtScreen"], "Each driver in CarTest carries its asset; CityManager only switches them on", "WEATHER · FOG · VHS · PSX · CRT",
   [("FiniteRunner_DistanceFog", "fogStart", "fog start"), ("FiniteRunner_DistanceFog", "fogEnd", "fog end")]),
  ("shared", "Campaign", ["CampaignCatalog", "World_01", "Mission_01"], "<code>Resources/Campaign/</code>; missions list their city and runner level", "", []),
  ("shared", "Store and upgrades", ["StoreSettings", "Section_Ship", "Upgrade_Ship_SpeedMultiplier"], "<code>Resources/Store/</code>", "", [("StoreSettings", "nextMissionScene", "next mission scene")]),
  ("shared", "Menus", ["FiniteRunner_MenuTheme"], "<code>Resources/</code>; fonts, palette, layout, motion, UI audio, menu rumble", "",
   [("FiniteRunner_MenuTheme", "rowWidth", "row width"), ("FiniteRunner_MenuTheme", "screenTransition", "transition"), ("FiniteRunner_MenuTheme", "moveRumble", "move rumble")]),
  ("shared", "Cheats", ["FiniteRunner_Cheats"], "<code>Resources/</code>; codes, console layout and reveal", "", [("FiniteRunner_Cheats", "cheats", "codes")]),
]

def tuning_map():
    out = ['<div class="tmap" role="table" aria-label="Where to tune each system">',
           '<div class="tmap-row head" role="row"><span role="columnheader">System</span><span role="columnheader">Asset</span><span role="columnheader">Where you edit it</span><span role="columnheader">Key knobs (shipped values)</span></div>']
    for game, system, assets, where, tab, knobs in MAP:
        assets_html = "".join("<code>%s</code>" % wbr(a) for a in assets)
        tab_html = '<span class="dbg">Debug: %s</span>' % tab if tab else ""
        knobs_html = "".join(knob(*k) for k in knobs) or '<span class="none">See the knob reference below.</span>'
        out.append('<div class="tmap-row %s" role="row">'
                   '<span class="c-sys" role="cell" data-l="System"><i class="dot"></i>%s</span>'
                   '<span class="c-asset" role="cell" data-l="Asset">%s</span>'
                   '<span class="c-where" role="cell" data-l="Where you edit it">%s%s</span>'
                   '<span class="c-knobs" role="cell" data-l="Key knobs">%s</span></div>'
                   % (game, E(system), assets_html, where, tab_html, knobs_html))
    out.append("</div>")
    return "\n".join(out)

# ------------------------------------------------------------------ the knob reference
GAME_LABEL = {"runner": "Runner", "city": "City", "shared": "Shared"}
def reference():
    out = []
    for game in ("runner", "city", "shared"):
        out.append('<h3 class="refgame %s">%s</h3>' % (game, GAME_LABEL[game]))
        for s in [x for x in REF if x["game"] == game]:
            n = sum(len(a["fields"]) for a in s["assets"])
            out.append('<details class="refsys %s"><summary><span class="t">%s</span><span class="n">%d knobs · %s</span></summary>' %
                       (game, E(s["system"]), n, ", ".join(E(a["asset"]) for a in s["assets"])))
            for a in s["assets"]:
                out.append('<div class="refasset"><div class="refhead"><code>%s</code><span>%s · <code class="path">%s</code></span></div>' %
                           (wbr(a["asset"]), E(a["class"] or ""), wbr(a["path"])))
                out.append('<div class="fields">')
                last = None
                for f in a["fields"]:
                    g = f["group"] or "General"
                    if g != last:
                        out.append('<div class="fgroup">%s</div>' % E(g))
                        last = g
                    rng = ""
                    if f.get("range"):
                        rng = "%s – %s" % (f["range"][0].rstrip("f"), f["range"][1].rstrip("f"))
                    unit = (" " + f["unit"]) if f["unit"] else ""
                    tip = f["tooltip"] or ""
                    out.append('<div class="field" data-s="%s">'
                               '<span class="fn">%s</span><span class="fv">%s<small>%s</small></span>'
                               '<span class="fr">%s</span><span class="ft">%s</span></div>'
                               % (E((f["name"] + " " + g + " " + tip + " " + a["asset"]).lower()), wbr(f["name"]),
                                  E(str(f["value"])), E(unit), E(rng), E(tip)))
                out.append("</div></div>")
            out.append("</details>")
    return "\n".join(out)

total_knobs = sum(len(a["fields"]) for s in REF for a in s["assets"])
total_assets = sum(len(s["assets"]) for s in REF)

TUNING = r'''
<section id="tuning" class="sec">
  <p class="eyebrow">Tuning</p>
  <h2>Where to tune it</h2>
  <p class="lede">One row per system: the asset that holds its knobs, where you reach it in the editor, the live debug tab if there is one, and a few key values as shipped. Every tunable lives on a ScriptableObject and is drawn inline on the component that uses it, so you can balance without leaving the scene.</p>
  ''' + tuning_map() + r'''
  <div class="note"><b>Live tuning</b> Pause the game (Esc or Start) and open DEBUG. Every slider writes its asset and is saved when you resume. Assets that gameplay clones (the ship, the patrol, the track) are updated on the live clone at the same time, so the change is felt at once. Undo a tuning change with git: it is an ordinary asset edit.</div>
</section>
'''

REFERENCE = r'''
<section id="reference" class="sec">
  <p class="eyebrow">Knob reference</p>
  <h2>Every knob, as shipped</h2>
  <p class="lede">%d knobs across %d assets, read from the settings classes (inspector group, tooltip, slider range) and the shipped asset files (value). A field the asset has never saved shows the code default it runs on.</p>
  <div class="search">
    <label for="knob-search">Find a knob</label>
    <input id="knob-search" type="search" placeholder="e.g. respawn, fog, corner speed" autocomplete="off">
    <button type="button" id="expand-all">Expand all</button>
    <span id="search-count" aria-live="polite"></span>
  </div>
  %s
</section>
''' % (total_knobs, total_assets, reference())

CSS = r'''
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Chakra+Petch:wght@500;600;700&family=IBM+Plex+Sans:ital,wght@0,400;0,500;0,600;1,400&family=IBM+Plex+Mono:wght@400;500&display=swap">
<style>
:root{
  --bg:#F3F4F1; --surface:#FFFFFF; --sunk:#ECEEEA; --ink:#15171C; --muted:#586070; --faint:#8A919C; --line:#D9DCD6;
  --runner:#2451C6; --runner-bg:#E8EEFB; --city:#0B7A6F; --city-bg:#E2F3F0; --shared:#9A5A00; --shared-bg:#FBEFDC;
  --code-bg:#EEF0EC; --focus:#2451C6;
  --display:"Chakra Petch", "Segoe UI", system-ui, sans-serif;
  --body:"IBM Plex Sans", "Segoe UI", system-ui, -apple-system, sans-serif;
  --mono:"IBM Plex Mono", ui-monospace, "Cascadia Mono", Consolas, monospace;
}
@media (prefers-color-scheme: dark){
  :root:not([data-theme="light"]){
    color-scheme:dark;
    --bg:#0F1115; --surface:#171A1F; --sunk:#1D2127; --ink:#E6E8EC; --muted:#A0A8B5; --faint:#6F7784; --line:#2B3039;
    --runner:#8FAEFF; --runner-bg:#1A2440; --city:#56D6C3; --city-bg:#12302C; --shared:#F2B660; --shared-bg:#33260F;
    --code-bg:#20252C; --focus:#8FAEFF;
  }
}
:root[data-theme="dark"]{
  color-scheme:dark;
  --bg:#0F1115; --surface:#171A1F; --sunk:#1D2127; --ink:#E6E8EC; --muted:#A0A8B5; --faint:#6F7784; --line:#2B3039;
  --runner:#8FAEFF; --runner-bg:#1A2440; --city:#56D6C3; --city-bg:#12302C; --shared:#F2B660; --shared-bg:#33260F;
  --code-bg:#20252C; --focus:#8FAEFF;
}
*{box-sizing:border-box}
html{scroll-behavior:smooth; scroll-padding-top:64px}
@media (prefers-reduced-motion: reduce){html{scroll-behavior:auto}}
body{margin:0; background:var(--bg); color:var(--ink); font:15px/1.6 var(--body); -webkit-text-size-adjust:100%}
a{color:var(--runner)}
:focus-visible{outline:2px solid var(--focus); outline-offset:2px}
code{font:0.86em/1.4 var(--mono); background:var(--code-bg); padding:0.08em 0.36em; border-radius:4px; overflow-wrap:normal; word-break:normal}
.wrap{max-width:1180px; margin:0 auto; padding-inline:clamp(16px, 4vw, 40px)}

/* header */
.top{background:var(--surface); border-bottom:1px solid var(--line)}
.top .wrap{padding-block:clamp(28px, 5vw, 48px) 24px}
.brand{font:600 12px/1 var(--mono); letter-spacing:.14em; text-transform:uppercase; color:var(--muted)}
h1{font:700 clamp(30px, 5vw, 46px)/1.08 var(--display); letter-spacing:.01em; margin:.35em 0 .3em; text-wrap:balance}
.top p{max-width:68ch; color:var(--muted); margin:0}
.legend{display:flex; flex-wrap:wrap; gap:8px 16px; margin-top:18px; font-size:13px; color:var(--muted)}
.legend span{display:inline-flex; align-items:center; gap:6px}

/* nav */
nav.toc{position:sticky; top:env(safe-area-inset-top, 0px); z-index:5; background:color-mix(in srgb, var(--bg) 92%, transparent); backdrop-filter:blur(6px); border-bottom:1px solid var(--line)}
nav.toc .wrap{display:flex; gap:4px; overflow-x:auto; padding-block:8px; scrollbar-width:thin}
nav.toc a{flex:none; font:500 13px/1 var(--body); color:var(--ink); text-decoration:none; padding:8px 12px; border-radius:6px}
nav.toc a:hover{background:var(--sunk)}

/* sections */
.sec{padding-block:clamp(36px, 6vw, 64px); border-bottom:1px solid var(--line)}
.sec:last-child{border-bottom:0}
.eyebrow{font:600 12px/1 var(--mono); letter-spacing:.12em; text-transform:uppercase; color:var(--muted); margin:0 0 10px}
h2{font:600 clamp(24px, 3.4vw, 32px)/1.15 var(--display); margin:0 0 14px; text-wrap:balance}
h3{font:600 20px/1.25 var(--display); margin:40px 0 12px; text-wrap:balance}
h4{font:600 15px/1.35 var(--body); margin:0 0 6px}
h4.sub{font:600 16px/1.3 var(--display); margin:28px 0 10px}
p{margin:0 0 12px; max-width:72ch}
.lede{font-size:16.5px; color:var(--ink); max-width:70ch}
.cap{font-size:13.5px; color:var(--muted); margin-top:8px}
ol.steps{padding-left:1.3em; max-width:78ch} ol.steps li{margin-bottom:6px}

/* tags */
.tag{display:inline-block; font:600 11px/1 var(--mono); letter-spacing:.04em; text-transform:uppercase; font-style:normal; padding:4px 7px; border-radius:4px; white-space:nowrap}
.tag.runner{color:var(--runner); background:var(--runner-bg)}
.tag.city{color:var(--city); background:var(--city-bg)}
.tag.shared{color:var(--shared); background:var(--shared-bg)}

/* facts */
.facts{display:grid; grid-template-columns:repeat(auto-fit, minmax(210px, 1fr)); gap:1px; background:var(--line); border:1px solid var(--line); border-radius:8px; overflow:hidden; margin:20px 0 8px}
.facts div{background:var(--surface); padding:12px 14px; display:flex; flex-direction:column; gap:4px}
.facts span{font:600 11px/1 var(--mono); letter-spacing:.08em; text-transform:uppercase; color:var(--muted)}
.facts b{font-weight:500}

/* generic rows (responsive tables) */
.rows{border:1px solid var(--line); border-radius:8px; background:var(--surface); overflow:hidden; margin:12px 0 8px}
.rows .row{display:grid; gap:4px 18px; padding:11px 14px; border-top:1px solid var(--line); align-items:start}
.rows .row:first-child{border-top:0}
.rows .row.head{background:var(--sunk); font:600 11.5px/1.3 var(--mono); letter-spacing:.07em; text-transform:uppercase; color:var(--muted)}
.rows.cols-3 .row{grid-template-columns:minmax(0, 15rem) minmax(0, 9rem) minmax(0, 1fr)}
.rows.cols-2 .row{grid-template-columns:minmax(0, 17rem) minmax(0, 1fr)}
@media (max-width: 760px){
  .rows .row, .rows.cols-3 .row, .rows.cols-2 .row{grid-template-columns:minmax(0, 1fr)}
  .rows .row.head{display:none}
  .rows .row > span:first-child{font-weight:600}
}

/* cards */
.cards{display:grid; grid-template-columns:repeat(auto-fill, minmax(290px, 1fr)); gap:12px; margin:14px 0}
.card{background:var(--surface); border:1px solid var(--line); border-radius:8px; padding:14px 16px}
.card p{font-size:14px; margin:0; color:var(--ink)}

/* mermaid */
pre.mermaid{background:var(--surface); border:1px solid var(--line); border-radius:8px; padding:16px; margin:14px 0 0; overflow-x:auto; text-align:center; font:13px/1.4 var(--mono)}

/* the tuning map */
.tmap{border:1px solid var(--line); border-radius:10px; background:var(--surface); margin:18px 0 12px}
.tmap-row{display:grid; grid-template-columns:minmax(0, 11rem) minmax(0, 14.5rem) minmax(0, 13.5rem) minmax(0, 1fr); gap:6px 18px; padding:14px 16px; border-top:1px solid var(--line); align-items:start}
.tmap-row:first-child{border-top:0}
.tmap-row.head{background:var(--sunk); font:600 11.5px/1.3 var(--mono); letter-spacing:.07em; text-transform:uppercase; color:var(--muted); border-radius:10px 10px 0 0; position:sticky; top:calc(env(safe-area-inset-top, 0px) + 49px); z-index:2}
.c-sys{font-weight:600; display:flex; gap:8px; align-items:baseline}
.dot{flex:none; width:8px; height:8px; border-radius:2px; transform:translateY(-1px); background:var(--muted)}
.tmap-row.runner .dot{background:var(--runner)} .tmap-row.city .dot{background:var(--city)} .tmap-row.shared .dot{background:var(--shared)}
.c-asset{display:flex; flex-direction:column; align-items:flex-start; gap:5px}
.c-asset code{font-size:12.5px; line-height:1.35}
.c-where{font-size:13.5px; color:var(--muted)}
.c-where code{font-size:12px}
.dbg{display:block; margin-top:6px; font:600 11px/1.4 var(--mono); letter-spacing:.03em; color:var(--ink)}
.c-knobs{display:flex; flex-wrap:wrap; gap:6px}
.knob{display:inline-flex; align-items:baseline; gap:6px; border:1px solid var(--line); border-radius:6px; padding:3px 8px; font-size:13px; background:var(--bg)}
.knob .k{color:var(--muted)}
.knob .v{font:500 12.5px/1.3 var(--mono); font-variant-numeric:tabular-nums; color:var(--ink)}
.none{font-size:13px; color:var(--faint)}
@media (max-width: 900px){
  .tmap-row{grid-template-columns:minmax(0, 1fr); gap:8px; padding:16px}
  .tmap-row.head{display:none}
  .tmap-row > span[data-l]:not(.c-sys)::before{content:attr(data-l); display:block; font:600 10.5px/1.2 var(--mono); letter-spacing:.08em; text-transform:uppercase; color:var(--faint); margin-bottom:4px}
  .c-asset{flex-direction:row; flex-wrap:wrap}
  .c-sys{font-size:16px}
}
.note{background:var(--surface); border:1px solid var(--line); border-left:3px solid var(--runner); border-radius:0 8px 8px 0; padding:12px 16px; font-size:14px; max-width:none}
.note b{display:block; margin-bottom:2px}

/* rules and recipes */
.rules{display:grid; grid-template-columns:repeat(auto-fill, minmax(320px, 1fr)); gap:12px; margin-top:12px}
.rules > div{background:var(--surface); border:1px solid var(--line); border-radius:8px; padding:13px 16px; font-size:14px}
.rules b{display:block; margin-bottom:4px; font-size:14.5px}
.recipes details{background:var(--surface); border:1px solid var(--line); border-radius:8px; margin-bottom:8px}
.recipes summary{cursor:pointer; padding:12px 16px; font-weight:600; list-style-position:outside}
.recipes ol{margin:0; padding:0 18px 14px 36px; font-size:14px} .recipes li{margin-bottom:6px}

/* knob reference */
.search{display:flex; flex-wrap:wrap; align-items:center; gap:8px 12px; margin:18px 0 8px; position:sticky; top:calc(env(safe-area-inset-top, 0px) + 49px); z-index:3; background:var(--bg); padding-block:10px}
.search label{font:600 12px/1 var(--mono); letter-spacing:.08em; text-transform:uppercase; color:var(--muted)}
.search input{flex:1 1 240px; min-width:0; font:15px var(--body); color:var(--ink); background:var(--surface); border:1px solid var(--line); border-radius:6px; padding:9px 12px}
.search button{font:500 13px var(--body); color:var(--ink); background:var(--surface); border:1px solid var(--line); border-radius:6px; padding:9px 12px; cursor:pointer}
#search-count{font-size:13px; color:var(--muted)}
.refgame{margin-top:32px}
.refgame.runner{color:var(--runner)} .refgame.city{color:var(--city)} .refgame.shared{color:var(--shared)}
.refsys{background:var(--surface); border:1px solid var(--line); border-radius:8px; margin-bottom:8px}
.refsys > summary{cursor:pointer; padding:12px 16px; display:flex; flex-wrap:wrap; gap:4px 14px; align-items:baseline}
.refsys > summary .t{font-weight:600}
.refsys > summary .n{font-size:12.5px; color:var(--muted)}
.refasset{border-top:1px solid var(--line)}
.refhead{display:flex; flex-wrap:wrap; gap:4px 12px; align-items:baseline; padding:12px 16px 6px}
.refhead > code{font-size:13px; font-weight:500}
.refhead span{font-size:12.5px; color:var(--muted)}
.refhead code.path{font-size:11.5px}
.fields{padding:0 16px 12px}
.fgroup{font:600 11px/1 var(--mono); letter-spacing:.08em; text-transform:uppercase; color:var(--muted); padding:14px 0 6px; border-bottom:1px solid var(--line)}
.field{display:grid; grid-template-columns:minmax(0, 15rem) minmax(0, 11rem) minmax(0, 6.5rem) minmax(0, 1fr); gap:3px 16px; padding:7px 0; border-bottom:1px solid var(--line); font-size:13.5px}
.fn{font:500 12.5px/1.4 var(--mono); color:var(--ink)}
.fv{font:500 12.5px/1.4 var(--mono); font-variant-numeric:tabular-nums; overflow-wrap:anywhere}
.fv small{font:12px var(--body); color:var(--muted)}
.fr{font:12px/1.4 var(--mono); color:var(--faint)}
.ft{color:var(--muted); font-size:13px}
@media (max-width: 760px){
  .field{grid-template-columns:minmax(0, 1fr) minmax(0, auto); gap:2px 12px}
  .fn{grid-column:1} .fv{grid-column:2; text-align:right}
  .fr{grid-column:1 / -1} .fr:empty{display:none}
  .ft{grid-column:1 / -1}
}
.field.hide, .fgroup.hide, .refasset.hide, .refsys.hide{display:none}
footer{padding-block:28px 48px; font-size:13px; color:var(--muted)}
</style>
'''

JS = r'''
<script>
(function(){
  var input = document.getElementById('knob-search');
  var count = document.getElementById('search-count');
  var expand = document.getElementById('expand-all');
  var systems = Array.prototype.slice.call(document.querySelectorAll('.refsys'));
  function apply(){
    var q = (input.value || '').trim().toLowerCase();
    var terms = q ? q.split(/\s+/) : [];
    var shown = 0;
    systems.forEach(function(sys){
      var sysHits = 0;
      sys.querySelectorAll('.refasset').forEach(function(asset){
        var assetHits = 0, group = null, groupHits = 0;
        function closeGroup(){ if (group) group.classList.toggle('hide', terms.length > 0 && groupHits === 0); }
        Array.prototype.forEach.call(asset.querySelector('.fields').children, function(el){
          if (el.classList.contains('fgroup')){ closeGroup(); group = el; groupHits = 0; return; }
          var s = el.getAttribute('data-s') || '';
          var ok = terms.every(function(t){ return s.indexOf(t) !== -1; });
          el.classList.toggle('hide', !ok);
          if (ok){ assetHits++; groupHits++; }
        });
        closeGroup();
        asset.classList.toggle('hide', terms.length > 0 && assetHits === 0);
        sysHits += assetHits;
      });
      sys.classList.toggle('hide', terms.length > 0 && sysHits === 0);
      if (terms.length) sys.open = sysHits > 0;
      shown += sysHits;
    });
    count.textContent = terms.length ? (shown + ' match' + (shown === 1 ? '' : 'es')) : '';
  }
  input.addEventListener('input', apply);
  expand.addEventListener('click', function(){
    var open = expand.getAttribute('data-open') !== '1';
    systems.forEach(function(s){ if (!s.classList.contains('hide')) s.open = open; });
    expand.setAttribute('data-open', open ? '1' : '0');
    expand.textContent = open ? 'Collapse all' : 'Expand all';
  });
})();
</script>
'''

HEADER = r'''
<header class="top"><div class="wrap">
  <div class="brand">SpaceFiniteRunner · Unity 6 · URP</div>
  <h1>Finite Runner Handbook</h1>
  <p>How to tune every system of the runner and the city chase, how the code is put together, and the rules and recipes for changing it.</p>
  <div class="legend"><span><i class="tag runner">Runner</i> the hover-ship escape run</span><span><i class="tag city">City</i> the car chase</span><span><i class="tag shared">Shared</i> both games</span></div>
</div></header>
<nav class="toc" aria-label="Sections"><div class="wrap">
  <a href="#overview">Overview</a><a href="#tuning">Where to tune it</a><a href="#architecture">Architecture</a><a href="#coding">Coding reference</a><a href="#reference">Knob reference</a>
</div></nav>
'''

FOOTER = '<footer class="wrap">Generated from the project: the settings classes in <code>Assets/01.Scripts</code> and the assets in <code>Assets/04.Data</code>. Rebuild with <code>python Tools/DocGen/gen_ref.py</code> then <code>python Tools/DocGen/build_guide.py</code>.</footer>'

body = ('<title>Finite Runner Handbook</title>\n' + CSS + HEADER + '<main class="wrap">' +
        content.OVERVIEW + TUNING + content.ARCHITECTURE + content.CODING + REFERENCE + '</main>' + FOOTER + JS)

artifact_path = os.path.join(OUT_DIR, "handbook.html")
open(artifact_path, "w", encoding="utf-8").write(body)

head = ('<!DOCTYPE html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n'
        '<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">\n'
        '<script src="https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"></script>\n'
        '<script>document.addEventListener("DOMContentLoaded",function(){if(window.mermaid){var dark=window.matchMedia&&window.matchMedia("(prefers-color-scheme: dark)").matches;mermaid.initialize({startOnLoad:true,theme:dark?"dark":"neutral"});}});</script>\n')
i = body.index('</style>') + len('</style>')
doc = head + body[:i] + '\n</head>\n<body>\n' + body[i:] + '\n</body>\n</html>\n'
open(os.path.join(ROOT, "Documentation.html"), "w", encoding="utf-8").write(doc)
print("built", len(body), "bytes;", total_knobs, "knobs;", "missing knobs:", missing)
