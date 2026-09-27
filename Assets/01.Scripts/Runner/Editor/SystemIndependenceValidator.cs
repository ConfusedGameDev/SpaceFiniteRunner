using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// The independence check (refactor Step 11). Scans the game code for a
    /// scene search — <c>FindAnyObjectByType&lt;T&gt;</c>, <c>FindFirstObjectByType</c>,
    /// <c>FindObjectsByType</c>, <c>FindObjectOfType</c> — whose <c>T</c> belongs to
    /// ANOTHER system than the calling file. A system is a namespace under
    /// <c>ConfusedGameDev.FiniteRunner</c> (the city's split one level deeper:
    /// <c>PoliceEscape.AI</c>, <c>PoliceEscape.City</c>, …). Such a search is a
    /// hidden dependency: the caller silently needs the other system in the
    /// scene. Every one that exists today is listed in <see cref="Accepted"/>
    /// with the reason it is acceptable — a composition root, a debug tool, a
    /// scene service the caller idles without, a documented fallback. A NEW
    /// cross-system search fails the check: hand the reference in (a contract,
    /// a Bind call, a registry) or add it here with a reason a reviewer can
    /// argue with. Editor-only files are skipped (builders and tools may reach
    /// anywhere). Run from Tools → Refactor → Validate System Independence.
    /// </summary>
    public static class SystemIndependenceValidator
    {
        const string ScriptsRoot = "Assets/01.Scripts";
        const string RootNamespace = "ConfusedGameDev.FiniteRunner";

        const string CompositionRoot = "Composition root: it wires or resets the systems of its scene.";
        const string DebugTool = "Debug tool: editor-only overlay or developer page discovering what it inspects.";
        const string CityService = "Scene service: the city's road network (CityManager); the caller idles, or falls back, without it.";
        const string LevelService = "Scene service: the city's level flow (LevelManager); the caller uses defaults without it.";
        const string HealthService = "Scene service: the player's health meter; the caller falls back to a glitch pulse without it.";
        const string Fallback = "Documented fallback for an unwired reference; the composition root hands it in.";
        const string RunnerAdapter = "The runner's ship adapter (ShipMotor): binding the ship to the track is its whole job.";

        /// <summary>(calling file under 01.Scripts, searched type) → why it is acceptable.</summary>
        static readonly Dictionary<(string file, string type), string> Accepted = new()
        {
            // composition roots
            { ("PoliceEscape/LevelManager.cs", "PatrolManager"), CompositionRoot },
            { ("PoliceEscape/LevelManager.cs", "TrafficManager"), CompositionRoot },
            { ("PoliceEscape/LevelManager.cs", "PlayerCarSpawner"), CompositionRoot },
            { ("PoliceEscape/LevelManager.cs", "PoliceCarInput"), CompositionRoot },
            { ("PoliceEscape/LevelManager.cs", "TrafficCarInput"), CompositionRoot },
            { ("Runner/Screens/PauseMenu.cs", "TrackGenerator"), CompositionRoot },
            // debug tools
            { ("PoliceEscape/UI/CityDebugMenuFactory.cs", "TrackGenerator"), DebugTool },
            { ("PoliceEscape/UI/CityDebugMenuFactory.cs", "LevelManager"), DebugTool },
            { ("PoliceEscape/UI/CityDebugMenuFactory.cs", "PlayerCarSpawner"), DebugTool },
            { ("PoliceEscape/UI/CityDebugMenuFactory.cs", "OrbitCameraRig"), DebugTool },
            { ("PoliceEscape/UI/CityDebugMenuFactory.cs", "PatrolManager"), DebugTool },
            { ("PoliceEscape/UI/CityDebugMenuFactory.cs", "PoliceCarInput"), DebugTool },
            { ("PoliceEscape/UI/CityDebugMenuFactory.cs", "CarController"), DebugTool },
            { ("PoliceEscape/Debugging/CarAiVisualizer.cs", "PoliceCarInput"), DebugTool },
            { ("PoliceEscape/Debugging/CarAiVisualizer.cs", "TrafficCarInput"), DebugTool },
            { ("PoliceEscape/Debugging/AiDebugInstaller.cs", "CityManager"), DebugTool },
            { ("PoliceEscape/Debugging/RoadGraphVisualizer.cs", "CityManager"), DebugTool },
            { ("PoliceEscape/Debugging/RoadGraphVisualizer.cs", "CarController"), DebugTool },
            { ("PoliceEscape/City/CityStreamer.cs", "PatrolManager"), DebugTool + " (a one-time load-distance warning)" },
            { ("PoliceEscape/City/CityStreamer.cs", "TrafficManager"), DebugTool + " (a one-time load-distance warning)" },
            // the city's road network
            { ("PoliceEscape/AI/PatrolManager.cs", "CityManager"), CityService },
            { ("PoliceEscape/AI/TrafficManager.cs", "CityManager"), CityService },
            { ("PoliceEscape/AI/PoliceCarInput.cs", "CityManager"), CityService },
            { ("PoliceEscape/TargetObject.cs", "CityManager"), CityService },
            { ("PoliceEscape/UI/Map/CityMapScreen.cs", "CityManager"), CityService },
            { ("PoliceEscape/UI/Map/ObjectiveGps.cs", "CityManager"), CityService },
            { ("PoliceEscape/Vehicles/CarRespawner.cs", "CityManager"), CityService },
            { ("PoliceEscape/Vehicles/PlayerCarSpawner.cs", "CityManager"), CityService },
            // the city's level flow
            { ("PoliceEscape/Audio/PoliceSiren.cs", "LevelManager"), LevelService },
            { ("PoliceEscape/UI/Map/CityMapScreen.cs", "LevelManager"), LevelService },
            { ("PoliceEscape/UI/Map/ObjectiveGps.cs", "LevelManager"), LevelService },
            { ("PoliceEscape/Vehicles/CarRespawner.cs", "LevelManager"), LevelService },
            { ("PoliceEscape/Vehicles/PlayerCarSpawner.cs", "LevelManager"), LevelService },
            { ("PoliceEscape/UI/Minimap.cs", "PoliceCarInput"), "The radar draws the police it finds; with none it draws none." },
            // the player's health
            { ("PoliceEscape/City/WaterSplashZone.cs", "PlayerHealthMeter"), HealthService },
            { ("PoliceEscape/Vehicles/PlayerDamageReceiver.cs", "PlayerHealthMeter"), HealthService },
            { ("PoliceEscape/UI/Speedometer.cs", "PlayerHealthMeter"), HealthService.Replace("a glitch pulse", "a full life ring") },
            // fallbacks and the runner's adapter
            { ("Runner/GameFlow/PolicePatrol.cs", "TrackManager"), Fallback },
            { ("Runner/GameFlow/PolicePatrol.cs", "TrackGenerator"), Fallback },
            { ("Runner/CameraFX/PadEffects.cs", "ShipMotor"), Fallback },
            { ("Runner/Ship/ShipMotor.Physics.cs", "TrackGuide"), RunnerAdapter },
            { ("Runner/Ship/ShipMotor.Physics.cs", "TrackColliderBuilder"), RunnerAdapter },
        };

        static readonly Regex FindCall = new(@"Find(?:Any|First)?Object(?:s)?(?:OfType|ByType)<([\w.]+)>", RegexOptions.Compiled);
        static readonly Regex NamespaceDecl = new(@"^namespace\s+([\w.]+)", RegexOptions.Multiline | RegexOptions.Compiled);
        static readonly Regex TypeDecl = new(@"\b(?:class|interface|struct|enum)\s+(\w+)", RegexOptions.Compiled);

        public readonly struct Finding
        {
            public readonly string file, type, callerSystem, typeSystem;
            public readonly int line;
            public readonly string reason; // null = not accepted

            public Finding(string file, int line, string type, string callerSystem, string typeSystem, string reason)
            {
                this.file = file; this.line = line; this.type = type;
                this.callerSystem = callerSystem; this.typeSystem = typeSystem; this.reason = reason;
            }
        }

        [MenuItem("Tools/Refactor/Validate System Independence")]
        public static void RunFromMenu()
        {
            List<Finding> findings = Scan();
            var violations = findings.Where(f => f.reason == null).ToList();
            var report = new StringBuilder();
            report.AppendLine($"System independence: {findings.Count} cross-system scene searches, {findings.Count - violations.Count} accepted, {violations.Count} NEW.");
            foreach (var group in findings.Where(f => f.reason != null).GroupBy(f => f.reason))
            {
                report.AppendLine($"  [{group.Count()}] {group.Key}");
                foreach (var f in group) report.AppendLine($"      {f.file}:{f.line}  {f.callerSystem} → {f.type} ({f.typeSystem})");
            }
            if (violations.Count == 0) Debug.Log(report.ToString());
            else
            {
                foreach (var f in violations)
                    Debug.LogError($"System independence: {f.file}:{f.line} — {f.callerSystem} searches the scene for {f.type} ({f.typeSystem}). Hand the reference in, or accept it in SystemIndependenceValidator with a reason.");
                Debug.LogWarning(report.ToString());
            }
            // Accepted entries whose search is gone are stale: say so, so the list shrinks with the code.
            var live = new HashSet<(string, string)>(findings.Select(f => (f.file, f.type)));
            foreach (var key in Accepted.Keys.Where(k => !live.Contains(k)))
                Debug.LogWarning($"System independence: the accepted search {key.file} → {key.type} no longer exists — remove it from the list.");
        }

        /// <summary>Every cross-system scene search in the game code, with its acceptance reason (null when new).</summary>
        public static List<Finding> Scan()
        {
            var files = Directory.GetFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories)
                                 .Select(p => p.Replace('\\', '/')).ToList();
            var namespaceOf = new Dictionary<string, string>();
            var typeNamespace = new Dictionary<string, string>();
            var sources = new Dictionary<string, string>();
            foreach (string path in files)
            {
                string source = File.ReadAllText(path);
                sources[path] = source;
                Match ns = NamespaceDecl.Match(source);
                namespaceOf[path] = ns.Success ? ns.Groups[1].Value : string.Empty;
                foreach (Match t in TypeDecl.Matches(source))
                    if (!typeNamespace.ContainsKey(t.Groups[1].Value)) typeNamespace[t.Groups[1].Value] = namespaceOf[path];
            }

            var findings = new List<Finding>();
            foreach (string path in files)
            {
                if (path.Contains("/Editor/")) continue;
                string source = sources[path];
                string callerSystem = SystemOf(namespaceOf[path]);
                foreach (Match m in FindCall.Matches(source))
                {
                    string type = m.Groups[1].Value.Split('.').Last();
                    if (!typeNamespace.TryGetValue(type, out string typeNs)) continue; // an engine type
                    string typeSystem = SystemOf(typeNs);
                    if (typeSystem == callerSystem) continue;
                    string file = path.Substring(ScriptsRoot.Length + 1);
                    int line = source.Take(m.Index).Count(ch => ch == '\n') + 1;
                    Accepted.TryGetValue((file, type), out string reason);
                    findings.Add(new Finding(file, line, type, callerSystem, typeSystem, reason));
                }
            }
            return findings;
        }

        /// <summary>A namespace's system: the first segment under the root, the city's one level deeper.</summary>
        static string SystemOf(string ns)
        {
            string rest = ns.StartsWith(RootNamespace) ? ns.Substring(RootNamespace.Length).TrimStart('.') : ns;
            string[] parts = rest.Split('.');
            if (parts[0] == "PoliceEscape" && parts.Length > 1) return "PoliceEscape." + parts[1];
            return string.IsNullOrEmpty(parts[0]) ? "(root)" : parts[0];
        }
    }
}
