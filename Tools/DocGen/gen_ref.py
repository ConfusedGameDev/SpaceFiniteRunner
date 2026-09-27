"""Builds tuning_ref.json: every designer-facing field of the project's settings assets,
with its inspector group, tooltip, range and the value the shipped asset holds."""
import os, re, json, glob

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ASSETS = os.path.join(ROOT, "Assets")
OUT_DIR = os.path.join(ROOT, "Temp", "DocGen")
os.makedirs(OUT_DIR, exist_ok=True)
OUT = os.path.join(OUT_DIR, "tuning_ref.json")

# ------------------------------------------------------------------ guid maps
guid_to_path = {}
for base in [""]:
    for meta in glob.glob(os.path.join(ASSETS, base, "**", "*.meta"), recursive=True):
        try:
            with open(meta, encoding="utf-8", errors="ignore") as f:
                for line in f:
                    if line.startswith("guid:"):
                        guid_to_path[line.split()[1].strip()] = meta[:-5]
                        break
        except OSError:
            pass

# ------------------------------------------------------------------ C# parsing
SCRIPTS = os.path.join(ASSETS, "01.Scripts")
sources = {}
for p in glob.glob(os.path.join(SCRIPTS, "**", "*.cs"), recursive=True):
    sources[p] = open(p, encoding="utf-8", errors="ignore").read()

enums = {}
for src in sources.values():
    for m in re.finditer(r"\benum\s+(\w+)\s*(?::\s*\w+)?\s*\{([^}]*)\}", src):
        members, val = [], -1
        for part in m.group(2).split(","):
            part = re.sub(r"//.*", "", part).strip()
            part = re.sub(r"\[[^\]]*\]", "", part).strip()
            if not part: continue
            if "=" in part:
                name, v = [x.strip() for x in part.split("=", 1)]
                try: val = int(v, 0)
                except ValueError: val += 1
            else:
                name, val = part, val + 1
            members.append((val, name))
        enums[m.group(1)] = dict(members)

def class_body(src, cls):
    m = re.search(r"\b(?:class|struct)\s+%s\b[^{]*\{" % re.escape(cls), src)
    if not m: return None
    i, depth = m.end(), 1
    while i < len(src) and depth:
        if src[i] == "{": depth += 1
        elif src[i] == "}": depth -= 1
        i += 1
    return src[m.end():i - 1]

def find_class(cls, depth=0):
    """The class body, with every project base class's body prepended (base fields first)."""
    for p, src in sources.items():
        body = class_body(src, cls)
        if body is None: continue
        m = re.search(r"\b(?:class|struct)\s+%s\b\s*(?:<[^>]*>)?\s*:\s*([\w.]+)" % re.escape(cls), src)
        if m and depth < 4:
            base = m.group(1).split(".")[-1].split("<")[0]
            if base not in ("ScriptableObject", "MonoBehaviour", "Object"):
                _, base_body = find_class(base, depth + 1)
                if base_body: body = base_body + "\n" + body
        return p, body
    return None, None

STR = r'"((?:[^"\\]|\\.)*)"'
def join_tooltip(attr_text):
    parts = re.findall(STR, attr_text)
    return " ".join(p.replace('\\"', '"') for p in parts)

FIELD = re.compile(r"^\s*(?:public|\[SerializeField[^\]]*\]\s*(?:private\s+|protected\s+)?|protected\s+\[SerializeField\])\s*([\w<>\[\],.\s]+?)\s+(\w+)\s*(=\s*[^;]+)?;")

def parse_fields(body):
    """Top-level serialized fields of a class body, in order, with their attributes."""
    fields, attrs, depth = [], [], 0
    group = None
    raw = body.split("\n")
    lines, k = [], 0
    while k < len(raw):
        ln = raw[k]
        if re.match(r"^\s*(?:public\s+)?[\w<>\[\],.]+\s+\w+\s*=\s*$", ln):
            # a multi-line initialiser: keep the declaration, skip its body up to ';'
            depth_i = 0
            k += 1
            while k < len(raw):
                depth_i += raw[k].count("{") - raw[k].count("}")
                if ";" in raw[k] and depth_i <= 0: break
                k += 1
            lines.append(ln.rstrip().rstrip("=").rstrip() + ";")
            k += 1
            continue
        lines.append(ln)
        k += 1
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped = line.strip()
        if depth == 0 and stripped.startswith("["):
            # gather a (possibly multi-line) attribute block
            block = stripped
            while block.count("[") > block.count("]") and i + 1 < len(lines):
                i += 1; block += " " + lines[i].strip()
            # an attribute may be followed by the field on the same line
            after = block[block.rfind("]") + 1:].strip() if block.endswith(";") or ";" in block[block.rfind("]"):] else ""
            attrs.append(block[: block.rfind("]") + 1] if after else block)
            if after and ("public" in after or "SerializeField" in block):
                line = after if after.startswith(("public", "[")) else "public " + after
                stripped = line.strip()
            else:
                i += 1
                continue
        if depth == 0:
            m = FIELD.match(line if "SerializeField" not in "".join(attrs[-1:]) or line.strip().startswith(("public", "[")) else "public " + line.strip())
            if m is None and attrs and "SerializeField" in attrs[-1] and re.match(r"^\s*(?:private\s+)?[\w<>\[\],.]+\s+\w+\s*(=|;)", line):
                m = FIELD.match("public " + re.sub(r"^\s*private\s+", "", line))
            if m and "static" not in line and "const" not in line and "=>" not in line and "(" not in (m.group(1) or ""):
                a = " ".join(attrs)
                if "NonSerialized" not in a and "HideInInspector" not in a:
                    g = re.search(r'(?:TitleGroup|FoldoutGroup|BoxGroup|TabGroup)\(\s*' + STR, a)
                    tg = re.search(r'ToggleGroup\(\s*' + STR + r'(?:\s*,\s*' + STR + r')?', a)
                    if g: group = g.group(1).split("/")[0]
                    elif tg: group = (tg.group(2) or tg.group(1))
                    hdr = re.search(r'Header\(\s*' + STR, a)
                    if hdr: group = hdr.group(1)
                    tip = re.search(r"Tooltip\((.*?)\)\s*\]", a)
                    rng = re.search(r"(?:PropertyRange|Range)\(\s*([-\d.eE]+)f?\s*,\s*([-\d.eE]+)f?", a)
                    mm = re.search(r"MinMaxSlider\(\s*([-\d.eE]+)f?\s*,\s*([-\d.eE]+)f?", a)
                    unit = re.search(r'SuffixLabel\(\s*' + STR, a)
                    fields.append({
                        "name": m.group(2), "type": re.sub(r"\s+", "", m.group(1)),
                        "group": group or "",
                        "tooltip": join_tooltip(tip.group(1)) if tip else "",
                        "range": [rng.group(1), rng.group(2)] if rng else ([mm.group(1), mm.group(2)] if mm else None),
                        "unit": unit.group(1) if unit else "",
                        "default": (m.group(3) or "")[1:].strip(),
                    })
            attrs = []
        depth += line.count("{") - line.count("}")
        if depth < 0: depth = 0
        i += 1
    return fields

# ------------------------------------------------------------------ Unity YAML
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_yaml import parse_yaml

def ref_name(v):
    m = re.search(r"guid:\s*([0-9a-f]{32})", v)
    if m:
        p = guid_to_path.get(m.group(1))
        return os.path.splitext(os.path.basename(p))[0] if p else "(asset)"
    if re.search(r"fileID:\s*0\b", v): return "none"
    return v

def num(s):
    try:
        f = float(s)
        return ("%g" % f)
    except ValueError:
        return s

def fmt(value, ftype):
    t = ftype
    if isinstance(value, list):
        if not value: return "empty"
        simple = [x for x in value if isinstance(x, str)]
        if len(simple) == len(value):
            vals = [ref_name(x) if "fileID" in x else num(x) for x in value]
            s = ", ".join(vals[:6])
            return s + (" … (%d)" % len(vals) if len(vals) > 6 else "")
        return "%d entr%s" % (len(value), "y" if len(value) == 1 else "ies")
    if isinstance(value, dict):
        return None
    v = value
    if v.startswith("{") and "fileID" in v: return ref_name(v)
    m = re.match(r"^\{r:\s*([-\d.eE]+),\s*g:\s*([-\d.eE]+),\s*b:\s*([-\d.eE]+),\s*a:\s*([-\d.eE]+)\}$", v)
    if m:
        r, g, b, a = [float(x) for x in m.groups()]
        return "#%02X%02X%02X%s" % (round(min(r,1)*255), round(min(g,1)*255), round(min(b,1)*255), "" if a >= 0.999 else " α%.2g" % a)
    m = re.match(r"^\{x:\s*([^,]+),\s*y:\s*([^,}]+)(?:,\s*z:\s*([^,}]+))?(?:,\s*w:\s*([^}]+))?\}$", v)
    if m:
        parts = [num(x.strip()) for x in m.groups() if x is not None]
        if t == "Vector2" and len(parts) == 2: return parts[0] + " – " + parts[1]
        return " · ".join(parts)
    if t == "bool": return "on" if v.strip() == "1" else "off"
    base = t.split(".")[-1]
    if base in enums:
        try: return enums[base].get(int(v), v)
        except ValueError: return v
    if t == "string":
        v = v.strip("'\"")
        v = re.sub(r"\\u([0-9a-fA-F]{4})", lambda m: chr(int(m.group(1), 16)), v)
        return v or "—"
    return num(v)

def fmt_default(val, ftype):
    """A field the asset never saved runs on its C# initialiser: show that in the saved values' format."""
    val = (val or "").strip()
    if not val: return "—"
    if re.match(r"^-?[\d.]+f?$", val): return num(val.rstrip("f"))
    if val in ("true", "false"): return "on" if val == "true" else "off"
    m = re.match(r"^new(?:\s+\w+)?\((.*)\)$", val)
    if m:
        args = [a.strip().rstrip("f") for a in m.group(1).split(",") if a.strip()]
        if not args: return "empty"
        try:
            nums = [float(a) for a in args]
        except ValueError:
            return val
        if ftype == "Color":
            r, g, b = nums[:3]; a = nums[3] if len(nums) > 3 else 1
            return "#%02X%02X%02X%s" % (round(min(r,1)*255), round(min(g,1)*255), round(min(b,1)*255), "" if a >= 0.999 else " α%.2g" % a)
        if ftype == "Vector2" and len(nums) == 2: return "%g – %g" % tuple(nums)
        return " · ".join("%g" % x for x in nums)
    m = re.match(r"^(?:\w+\.)?(\w+)\.(\w+)$", val)
    if m and m.group(1) in enums: return m.group(2)
    if val.startswith('"'): return val.strip('"') or "—"
    return val

def asset_fields(path):
    text = open(path, encoding="utf-8", errors="ignore").read()
    data = parse_yaml(text)
    sm = re.search(r"m_Script:\s*\{[^}]*guid:\s*([0-9a-f]{32})", text)
    script = guid_to_path.get(sm.group(1)) if sm else None
    cls = os.path.splitext(os.path.basename(script))[0] if script else None
    if not cls: return None, []
    src_path, body = find_class(cls)
    if body is None: return cls, []
    rows = []
    for f in parse_fields(body):
        if f["name"] not in data:
            shown = fmt_default(f["default"], f["type"])
        else:
            raw = data[f["name"]]
            shown = fmt(raw, f["type"])
            if shown is None and isinstance(raw, dict) and "m_Curve" in raw:
                keys = raw.get("m_Curve")
                shown = "curve (%d keys)" % (len(keys) if isinstance(keys, list) else 0)
            elif shown is None and isinstance(raw, dict) and "m_Bits" in raw:
                bits = int(raw.get("m_Bits", "0") or 0)
                shown = "layers: " + (", ".join(str(b) for b in range(32) if bits >> b & 1) or "none")
            if shown is None and isinstance(raw, dict):
                # a nested profile: expand one level
                sub_cls = f["type"].split(".")[-1]
                _, sub_body = find_class(sub_cls)
                if sub_body:
                    for sf in parse_fields(sub_body):
                        if sf["name"] in raw:
                            rows.append(dict(sf, name=f["name"] + "." + sf["name"], group=f["group"] or sf["group"],
                                             value=fmt(raw[sf["name"]], sf["type"])))
                    continue
                shown = "(%d fields)" % len(raw)
        rows.append(dict(f, value=shown))
    return cls, rows

# ------------------------------------------------------------------ the curated set
D = "04.Data/"
SETS = [
  ("runner", "Run rules", ["FiniteRunner/FiniteRunner_GameSettings"]),
  ("runner", "Run level", ["FiniteRunner/FiniteRunner_LevelDefinition"]),
  ("runner", "Ship stats", ["FiniteRunner/Fighter_ShipDefinition"]),
  ("runner", "Ship rules and feel", ["Ship/Runner_ShipSettings"]),
  ("runner", "Patrol and duel", ["FiniteRunner/Police_PatrolDefinition", "FiniteRunner/Police_PatrolVisual"]),
  ("runner", "Track layout", ["FiniteRunner/FiniteRunner_TrackShape", "FiniteRunner/FiniteRunner_TrackSpawnSet", "Resources/FiniteRunner_EndRamp"]),
  ("runner", "Track features", ["FiniteRunner/Jump_Definition", "FiniteRunner/Loop_Definition", "FiniteRunner/FullTube_Definition", "FiniteRunner/LaserGate_Definition"]),
  ("runner", "Pickups", ["FiniteRunner/Spawnables/Spawner_SpeedOrbs", "FiniteRunner/Spawnables/Spawner_RepairOrbs", "FiniteRunner/Spawnables/Spawner_LaserGates", "FiniteRunner/BoostPad_Definition"]),
  ("runner", "Chase camera", ["FiniteRunner/Fighter_CameraSettings"]),
  ("runner", "Camera shakes", ["FiniteRunner/LandingShake_Settings", "FiniteRunner/WallHitShake_Settings", "FiniteRunner/LaserHitShake_Settings", "FiniteRunner/ScrapeShake_Settings", "FiniteRunner/ExplosionShake_Settings", "FiniteRunner/WinBannerShake_Settings", "FiniteRunner/BoostShake_Settings"]),
  ("runner", "Runner HUD", ["FiniteRunner/ChaseMinimap_Style", "FiniteRunner/RpgMessage_Style"]),
  ("runner", "Runner screen FX", ["FiniteRunner/FiniteRunner_RunnerFog", "Resources/FiniteRunner_SpeedLines", "FiniteRunner/Runner_Rain", "FiniteRunner/Runner_VhsTape", "FiniteRunner/Runner_PsxLook", "FiniteRunner/Runner_CrtScreen"]),
  ("runner", "Runner audio", ["Resources/FiniteRunner_Music", "Resources/FiniteRunner_Sfx"]),
  ("city", "Car handling", ["InfiniteCity/TestCarConfig"]),
  ("city", "Police fleet and chase", ["InfiniteCity/TestPursuitSettings"]),
  ("city", "Traffic", ["InfiniteCity/TestTrafficSettings"]),
  ("city", "Damage, health and blasts", ["Resources/PoliceEscape_VehicleHealth"]),
  ("city", "Vehicle physics backend", ["Resources/PoliceEscape_VehiclePhysics"]),
  ("city", "City level", ["InfiniteCity/TestLevelDefinition"]),
  ("city", "City layout (bake)", ["InfiniteCity/CityDefinition", "InfiniteCity/CityTestSettings", "InfiniteCity/Districts/BlockSettings_Downtown", "InfiniteCity/Districts/District_Downtown"]),
  ("city", "City HUD and camera", ["InfiniteCity/TestSpeedometerSettings", "InfiniteCity/TestMinimapSettings", "InfiniteCity/TestCityMapSettings", "InfiniteCity/TestOrbitCameraSettings"]),
  ("city", "City screen FX", ["Resources/FiniteRunner_DistanceFog", "Resources/FiniteRunner_Rain", "Resources/FiniteRunner_VhsTape", "Resources/FiniteRunner_PsxLook", "Resources/FiniteRunner_CrtScreen"]),
  ("city", "Cinemas and radio", ["Resources/PoliceEscape_CinemaFormats", "Resources/PoliceEscape_Radio"]),
  ("shared", "Campaign", ["Resources/Campaign/CampaignCatalog", "Campaign/World_01", "Campaign/Mission_01"]),
  ("shared", "Store and upgrades", ["Resources/Store/StoreSettings", "Resources/Store/Section_Ship", "Resources/Store/Upgrade_Ship_SpeedMultiplier", "Resources/Store/Upgrade_Car_Resistance"]),
  ("shared", "Menus", ["Resources/FiniteRunner_MenuTheme", "Resources/FiniteRunner_MenuMusic", "Resources/FiniteRunner_StoreMusic"]),
  ("shared", "Cheats", ["Resources/FiniteRunner_Cheats"]),
  ("shared", "Standalone ship sandbox", ["Ship/HoverShip_Settings"]),
]

out = []
for game, system, names in SETS:
    assets = []
    for n in names:
        path = os.path.join(ASSETS, D + n + ".asset")
        if not os.path.exists(path):
            print("MISSING", n); continue
        cls, rows = asset_fields(path)
        assets.append({"asset": os.path.basename(n), "path": "Assets/" + D + n + ".asset", "class": cls, "fields": rows})
        print("%-40s %-28s %3d fields" % (os.path.basename(n), cls, len(rows)))
    out.append({"game": game, "system": system, "assets": assets})
json.dump(out, open(OUT, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
print("total fields", sum(len(a["fields"]) for s in out for a in s["assets"]))
