using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Store;

namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// The refactor's safety net: writes every tuning value the game actually
    /// runs on to <c>Temp/TuningDump/&lt;stamp&gt;/</c>, one pretty JSON file
    /// per object, so two dumps taken before and after a change diff line by
    /// line. Two kinds of entry: <b>effective</b> — the objects a run really
    /// flies on, resolved exactly as the runtime resolves them (ship
    /// definition: clone → store upgrades; ship settings and patrol: the
    /// clone of the asset) — and <b>raw</b>
    /// — every ScriptableObject under <c>Assets/04.Data</c> as authored.
    /// Nothing is played, nothing is saved: every clone is destroyed.
    /// </summary>
    public static class TuningDump
    {
        const string Root = "Temp/TuningDump";

        [MenuItem("Tools/Refactor/Dump Effective Tuning")]
        public static void Dump()
        {
            string dir = Path.Combine(Root, System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);
            int count = 0;

            // Effective: what a run flies on.
            foreach (var def in Find<ShipDefinition>())
            {
                var run = ShipUpgradeApplier.BuildRunDefinition(def);
                count += Write(dir, "effective", "ShipDefinition", def, run);
                Object.DestroyImmediate(run);
            }

            foreach (var settings in Find<ShipSettings>())
            {
                var clone = Object.Instantiate(settings);
                count += Write(dir, "effective", "ShipSettings", settings, clone);
                Object.DestroyImmediate(clone);
            }

            foreach (var def in Find<PatrolDefinition>())
            {
                var run = Object.Instantiate(def);
                count += Write(dir, "effective", "PatrolDefinition", def, run);
                Object.DestroyImmediate(run);
            }

            // Raw: every authored asset.
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/04.Data" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var so in AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableObject>())
                    count += Write(dir, "raw", so.GetType().Name, so, so);
            }

            Debug.Log($"[TuningDump] {count} objects written to {Path.GetFullPath(dir)}");
        }

        [MenuItem("Tools/Refactor/Compare Last Two Tuning Dumps")]
        public static void CompareLastTwo()
        {
            var dumps = Directory.Exists(Root) ? Directory.GetDirectories(Root).OrderBy(d => d).ToArray() : new string[0];
            if (dumps.Length < 2) { Debug.LogWarning("[TuningDump] Need two dumps to compare."); return; }
            string before = dumps[dumps.Length - 2], after = dumps[dumps.Length - 1];

            var report = new List<string>();
            var files = Relative(before).Union(Relative(after)).OrderBy(f => f);
            foreach (string file in files)
            {
                string a = Path.Combine(before, file), b = Path.Combine(after, file);
                if (!File.Exists(a)) { report.Add($"+ {file} (new)"); continue; }
                if (!File.Exists(b)) { report.Add($"- {file} (gone)"); continue; }
                var la = File.ReadAllLines(a);
                var lb = File.ReadAllLines(b);
                var changed = la.Except(lb).Select(l => "    was " + l.Trim())
                    .Concat(lb.Except(la).Select(l => "    now " + l.Trim())).ToList();
                if (changed.Count > 0) { report.Add($"~ {file}"); report.AddRange(changed); }
            }

            string summary = report.Count == 0
                ? "no differences"
                : string.Join("\n", report);
            string outFile = Path.Combine(after, "_diff.txt");
            File.WriteAllText(outFile, summary);
            Debug.Log($"[TuningDump] {Path.GetFileName(before)} → {Path.GetFileName(after)}: " +
                      (report.Count == 0 ? "no differences" : $"differences written to {Path.GetFullPath(outFile)}\n{summary}"));
        }

        static IEnumerable<T> Find<T>() where T : Object =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null);

        static int Write(string dir, string kind, string type, Object source, Object value)
        {
            string folder = Path.Combine(dir, kind, type);
            Directory.CreateDirectory(folder);
            string name = source.name;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            File.WriteAllText(Path.Combine(folder, name + ".json"), EditorJsonUtility.ToJson(value, true));
            return 1;
        }

        static IEnumerable<string> Relative(string dir) =>
            Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
                .Select(f => f.Substring(dir.Length + 1));
    }
}
