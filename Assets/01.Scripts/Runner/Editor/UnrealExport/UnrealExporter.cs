using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.EditorTools.UnrealExport
{
    /// <summary>
    /// The Unity half of the Unreal port's data pipeline (UnrealPortPRD M0), and
    /// the only thing the port adds to this repo. Writes three things into the
    /// Unreal repo's <c>Import</c> folder:
    /// <list type="bullet">
    /// <item><b>Data/&lt;Type&gt;/&lt;Asset&gt;.json</b> — every runner ScriptableObject
    /// under <c>Assets/04.Data</c>, read through <see cref="SerializedObject"/>
    /// (never by parsing YAML), object references written as guid + path.</item>
    /// <item><b>Schema/&lt;Type&gt;.json</b> — each exported class's serialized fields
    /// with their tooltip, range, group and unit, so the Unreal DataAsset classes
    /// are written from the same source the inspector shows.</item>
    /// <item><b>AssetManifest.csv</b> — the guid trace of the runner scenes and data:
    /// every non-script asset they depend on. Re-exporting keeps the Action,
    /// UEPath and Notes columns a person filled in.</item>
    /// </list>
    /// Values are written exactly as Unity stores them (metres, Unity axes); the
    /// Unreal importer converts. Nothing in the project is changed.
    /// Menu: Tools ▸ Unreal Export. Batch: <c>-executeMethod
    /// ConfusedGameDev.FiniteRunner.EditorTools.UnrealExport.UnrealExporter.ExportAll
    /// [-unrealExportDir &lt;path&gt;]</c>.
    /// </summary>
    public static class UnrealExporter
    {
        const string DataRoot = "Assets/04.Data";
        const string DefaultOutput = "../SpaceFiniteRunnerUE/SpaceFiniteRunnerUE/Import";
        /// <summary>No runner asset comes near this; past it the walk has gone wrong, so stop instead of filling memory.</summary>
        const int MaxJsonLength = 64 * 1024 * 1024;

        /// <summary>Data that belongs to the city chase, the campaign or the Store (PRD §3): never exported.</summary>
        static readonly string[] excludedData =
        {
            "Assets/04.Data/InfiniteCity/", "Assets/04.Data/CityDefinition/", "Assets/04.Data/Customize/",
            "Assets/04.Data/Resources/Store/", "Assets/04.Data/Resources/Campaign/",
            "Assets/04.Data/Resources/PoliceEscape_", "Assets/04.Data/Campaign/World_",
            "Assets/04.Data/Campaign/Mission_", "Assets/04.Data/Campaign/Level_", "Assets/04.Data/URP Asset",
        };

        /// <summary>Runner features the port leaves behind (PRD §3): cheats, retro filters, rain, the patrol cam, brake pads, Store music.</summary>
        static readonly HashSet<string> excludedTypes = new()
        {
            "CheatDefinition", "CheatGlyphSet", "CrtScreenSettings", "PsxLookSettings", "VhsTapeSettings",
            "RainSettings", "PatrolCamSettings", "BrakePadSpawner",
        };

        static readonly HashSet<string> excludedNames = new() { "FiniteRunner_StoreMusic", "BrakePad_Definition" };

        static readonly string[] runnerScenes =
        {
            "Assets/05.Scenes/FiniteRunner_Test.unity", "Assets/05.Scenes/MainMenu.unity", "Assets/05.Scenes/LevelSelect.unity",
        };

        static readonly HashSet<string> scriptExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".cs", ".dll", ".asmdef", ".asmref", ".rsp", ".xml", ".json", ".txt", ".md", ".pdf", ".uxml", ".uss", ".tss" };

        static readonly HashSet<string> portExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".fbx", ".obj", ".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr", ".hdr", ".ttf", ".otf", ".ogg", ".mp3", ".wav", ".mp4" };

        static readonly HashSet<string> unityObjectFields = new()
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "m_Enabled", "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier",
        };

        [MenuItem("Tools/Unreal Export/Export All")]
        public static void ExportAll()
        {
            string output = ResolveOutput();
            var assets = CollectData();
            int data = ExportData(output, assets);
            int schema = ExportSchema(output, assets);
            int rows = ExportManifest(output, assets);
            Debug.Log($"[UnrealExport] {data} data assets, {schema} schema files, {rows} manifest rows written to {output}");
        }

        static string ResolveOutput()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-unrealExportDir");
            string output = at >= 0 && at + 1 < args.Length ? args[at + 1] : DefaultOutput;
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            return output;
        }

        static bool IsExcluded(string path) => excludedData.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        static List<ScriptableObject> CollectData()
        {
            var found = new List<ScriptableObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { DataRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsExcluded(path)) continue;
                if (AssetDatabase.LoadMainAssetAtPath(path) is ScriptableObject main
                    && !excludedTypes.Contains(main.GetType().Name) && !excludedNames.Contains(main.name))
                    found.Add(main);
            }
            return found.OrderBy(AssetDatabase.GetAssetPath, StringComparer.Ordinal).ToList();
        }

        // ------------------------------------------------------------------ data
        static int ExportData(string output, List<ScriptableObject> assets)
        {
            string root = Path.Combine(output, "Data");
            if (Directory.Exists(root)) Directory.Delete(root, true);

            foreach (var asset in assets)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                var json = new JsonWriter();
                json.BeginObject();
                json.Name("unityType").Value(asset.GetType().Name);
                json.Name("fullType").Value(asset.GetType().FullName);
                json.Name("name").Value(asset.name);
                json.Name("guid").Value(AssetDatabase.AssetPathToGUID(path));
                json.Name("path").Value(path);
                json.Name("fields").BeginObject();

                using (var serialized = new SerializedObject(asset))
                {
                    var property = serialized.GetIterator();
                    for (bool more = property.Next(true); more; more = property.Next(false))
                    {
                        if (unityObjectFields.Contains(property.name)) continue;
                        json.Name(property.name);
                        WriteProperty(json, property.Copy());
                    }
                }

                json.EndObject();
                json.EndObject();

                string dir = Path.Combine(root, asset.GetType().Name);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, asset.name + ".json"), json.ToString(), new UTF8Encoding(false));
            }
            return assets.Count;
        }

        static void WriteProperty(JsonWriter json, SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.ArraySize:
                    json.Value(property.longValue);
                    return;
                case SerializedPropertyType.Boolean:
                    json.Value(property.boolValue);
                    return;
                case SerializedPropertyType.Float:
                    if (property.numericType == SerializedPropertyNumericType.Double) json.Value(property.doubleValue);
                    else json.Value(property.floatValue);
                    return;
                case SerializedPropertyType.String:
                    json.Value(property.stringValue);
                    return;
                case SerializedPropertyType.Enum:
                {
                    int index = property.enumValueIndex;
                    string[] names = property.enumNames;
                    json.BeginObject();
                    json.Name("$enum").Value(index >= 0 && index < names.Length ? names[index] : "");
                    json.Name("value").Value((long)property.intValue);
                    json.EndObject();
                    return;
                }
                case SerializedPropertyType.Color:
                {
                    Color c = property.colorValue;
                    json.BeginObject();
                    json.Name("r").Value(c.r); json.Name("g").Value(c.g); json.Name("b").Value(c.b); json.Name("a").Value(c.a);
                    json.EndObject();
                    return;
                }
                case SerializedPropertyType.Vector2: WriteVector(json, property.vector2Value.x, property.vector2Value.y); return;
                case SerializedPropertyType.Vector3: WriteVector(json, property.vector3Value.x, property.vector3Value.y, property.vector3Value.z); return;
                case SerializedPropertyType.Vector4: WriteVector(json, property.vector4Value.x, property.vector4Value.y, property.vector4Value.z, property.vector4Value.w); return;
                case SerializedPropertyType.Quaternion: WriteVector(json, property.quaternionValue.x, property.quaternionValue.y, property.quaternionValue.z, property.quaternionValue.w); return;
                case SerializedPropertyType.Vector2Int: WriteVector(json, property.vector2IntValue.x, property.vector2IntValue.y); return;
                case SerializedPropertyType.Vector3Int: WriteVector(json, property.vector3IntValue.x, property.vector3IntValue.y, property.vector3IntValue.z); return;
                case SerializedPropertyType.Rect:
                {
                    Rect r = property.rectValue;
                    json.BeginObject();
                    json.Name("x").Value(r.x); json.Name("y").Value(r.y); json.Name("width").Value(r.width); json.Name("height").Value(r.height);
                    json.EndObject();
                    return;
                }
                case SerializedPropertyType.AnimationCurve:
                    WriteCurve(json, property.animationCurveValue);
                    return;
                case SerializedPropertyType.Gradient:
                    WriteGradient(json, property.gradientValue);
                    return;
                case SerializedPropertyType.ObjectReference:
                    WriteReference(json, property.objectReferenceValue);
                    return;
                case SerializedPropertyType.Hash128:
                    json.Value(property.hash128Value.ToString());
                    return;
                case SerializedPropertyType.ManagedReference:
                case SerializedPropertyType.Generic:
                    if (json.Length > MaxJsonLength)
                        throw new InvalidOperationException($"Export of '{property.serializedObject.targetObject.name}' passed {MaxJsonLength} characters at '{property.propertyPath}'.");
                    if (property.isArray)
                    {
                        json.BeginArray();
                        for (int i = 0; i < property.arraySize; i++) WriteProperty(json, property.GetArrayElementAtIndex(i));
                        json.EndArray();
                        return;
                    }
                    WriteChildren(json, property);
                    return;
                default:
                    json.Null();
                    return;
            }
        }

        static void WriteChildren(JsonWriter json, SerializedProperty property)
        {
            json.BeginObject();
            if (property.propertyType == SerializedPropertyType.ManagedReference)
                json.Name("$type").Value(property.managedReferenceFullTypename);

            // Children are exactly the properties one level deeper, up to the first one that is not.
            var child = property.Copy();
            int depth = property.depth;
            for (bool more = child.Next(true); more && child.depth > depth; more = child.Next(false))
            {
                if (child.depth != depth + 1) continue;
                json.Name(child.name);
                WriteProperty(json, child.Copy());
            }
            json.EndObject();
        }

        static void WriteVector(JsonWriter json, params float[] components)
        {
            const string names = "xyzw";
            json.BeginObject();
            for (int i = 0; i < components.Length; i++) json.Name(names[i].ToString()).Value(components[i]);
            json.EndObject();
        }

        static void WriteCurve(JsonWriter json, AnimationCurve curve)
        {
            json.BeginObject();
            json.Name("$curve").BeginArray();
            foreach (Keyframe key in curve.keys)
            {
                json.BeginObject();
                json.Name("time").Value(key.time); json.Name("value").Value(key.value);
                json.Name("inTangent").Value(key.inTangent); json.Name("outTangent").Value(key.outTangent);
                json.Name("inWeight").Value(key.inWeight); json.Name("outWeight").Value(key.outWeight);
                json.Name("weightedMode").Value((long)key.weightedMode);
                json.EndObject();
            }
            json.EndArray();
            json.Name("preWrapMode").Value(curve.preWrapMode.ToString());
            json.Name("postWrapMode").Value(curve.postWrapMode.ToString());
            json.EndObject();
        }

        static void WriteGradient(JsonWriter json, Gradient gradient)
        {
            json.BeginObject();
            json.Name("$gradient").BeginArray();
            foreach (GradientColorKey key in gradient.colorKeys)
            {
                json.BeginObject();
                json.Name("time").Value(key.time);
                json.Name("r").Value(key.color.r); json.Name("g").Value(key.color.g); json.Name("b").Value(key.color.b);
                json.EndObject();
            }
            json.EndArray();
            json.Name("alpha").BeginArray();
            foreach (GradientAlphaKey key in gradient.alphaKeys)
            {
                json.BeginObject();
                json.Name("time").Value(key.time); json.Name("a").Value(key.alpha);
                json.EndObject();
            }
            json.EndArray();
            json.Name("mode").Value(gradient.mode.ToString());
            json.EndObject();
        }

        static void WriteReference(JsonWriter json, UnityEngine.Object target)
        {
            if (target == null) { json.Null(); return; }

            string path = AssetDatabase.GetAssetPath(target);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string guid, out long fileId);
            json.BeginObject();
            json.Name("$ref").BeginObject();
            json.Name("guid").Value(guid ?? "");
            json.Name("fileId").Value(fileId);
            json.Name("path").Value(path);
            json.Name("name").Value(target.name);
            json.Name("type").Value(target.GetType().Name);
            json.Name("main").Value(AssetDatabase.IsMainAsset(target));
            json.EndObject();
            json.EndObject();
        }

        // ---------------------------------------------------------------- schema
        static int ExportSchema(string output, List<ScriptableObject> assets)
        {
            string root = Path.Combine(output, "Schema");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);

            var roots = assets.Select(a => a.GetType()).Distinct().OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
            foreach (Type type in roots)
            {
                var nested = new List<Type>();
                var json = new JsonWriter();
                json.BeginObject();
                json.Name("unityType").Value(type.Name);
                json.Name("fullType").Value(type.FullName);
                json.Name("baseType").Value(type.BaseType != null ? type.BaseType.Name : "");
                json.Name("script").Value(ScriptPath(type));
                json.Name("fields");
                WriteFields(json, type, nested);

                json.Name("types").BeginObject();
                for (int i = 0; i < nested.Count; i++) // WriteFields appends while we walk
                {
                    Type t = nested[i];
                    json.Name(t.Name).BeginObject();
                    json.Name("fullType").Value(t.FullName);
                    if (t.IsEnum)
                    {
                        json.Name("enum").BeginArray();
                        foreach (object value in Enum.GetValues(t))
                        {
                            json.BeginObject();
                            json.Name("name").Value(value.ToString());
                            json.Name("value").Value(Convert.ToInt64(value));
                            json.EndObject();
                        }
                        json.EndArray();
                    }
                    else
                    {
                        json.Name("fields");
                        WriteFields(json, t, nested);
                    }
                    json.EndObject();
                }
                json.EndObject();
                json.EndObject();
                File.WriteAllText(Path.Combine(root, type.Name + ".json"), json.ToString(), new UTF8Encoding(false));
            }
            return roots.Count;
        }

        static string ScriptPath(Type type)
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:MonoScript {type.Name}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type) return path;
            }
            return "";
        }

        static void WriteFields(JsonWriter json, Type type, List<Type> nested)
        {
            json.BeginArray();
            foreach (FieldInfo field in SerializedFields(type))
            {
                Type element = ElementType(field.FieldType);
                json.BeginObject();
                json.Name("name").Value(field.Name);
                json.Name("type").Value(TypeName(field.FieldType));
                if (field.DeclaringType != type) json.Name("declaredIn").Value(field.DeclaringType.Name);

                foreach (object attribute in field.GetCustomAttributes(true))
                {
                    Type a = attribute.GetType();
                    switch (a.Name)
                    {
                        case "TooltipAttribute": json.Name("tooltip").Value(((TooltipAttribute)attribute).tooltip); break;
                        case "RangeAttribute":
                            json.Name("min").Value(((RangeAttribute)attribute).min);
                            json.Name("max").Value(((RangeAttribute)attribute).max);
                            break;
                        case "MinAttribute": json.Name("min").Value(((MinAttribute)attribute).min); break;
                        case "PropertyRangeAttribute":
                            json.Name("min").Value(Member(attribute, "Min"));
                            json.Name("max").Value(Member(attribute, "Max"));
                            break;
                        case "MinMaxSliderAttribute":
                            json.Name("band").Value(true);
                            json.Name("min").Value(Member(attribute, "MinValue"));
                            json.Name("max").Value(Member(attribute, "MaxValue"));
                            break;
                        case "SuffixLabelAttribute": json.Name("unit").Value(Convert.ToString(Member(attribute, "Label"), CultureInfo.InvariantCulture)); break;
                        case "TitleGroupAttribute":
                        case "ToggleGroupAttribute":
                        case "FoldoutGroupAttribute":
                        case "BoxGroupAttribute":
                            json.Name("group").Value(Convert.ToString(Member(attribute, "GroupID"), CultureInfo.InvariantCulture));
                            if (a.Name == "ToggleGroupAttribute") json.Name("toggleGroup").Value(true);
                            break;
                        case "HideInInspector": json.Name("hidden").Value(true); break;
                        case "TextAreaAttribute": json.Name("multiline").Value(true); break;
                    }
                }
                json.EndObject();

                if (IsNestedData(element) && !nested.Contains(element)) nested.Add(element);
            }
            json.EndArray();
        }

        static object Member(object target, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type type = target.GetType();
            return type.GetField(name, flags)?.GetValue(target) ?? type.GetProperty(name, flags)?.GetValue(target);
        }

        /// <summary>Base-class fields first, each class in declaration order — the order the inspector draws.</summary>
        static IEnumerable<FieldInfo> SerializedFields(Type type)
        {
            var chain = new List<Type>();
            for (Type t = type; t != null && t != typeof(ScriptableObject) && t != typeof(MonoBehaviour) && t != typeof(object) && t != typeof(ValueType); t = t.BaseType)
                chain.Add(t);
            chain.Reverse();

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (Type t in chain)
                foreach (FieldInfo field in t.GetFields(flags).OrderBy(f => f.MetadataToken))
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsDefined(typeof(NonSerializedAttribute), true)) continue;
                    if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), true) && !field.IsDefined(typeof(SerializeReference), true)) continue;
                    yield return field;
                }
        }

        static Type ElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
            return type;
        }

        static bool IsNestedData(Type type)
        {
            if (type.IsEnum) return true;
            if (type.IsPrimitive || type == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(type)) return false;
            if (type.Namespace != null && (type.Namespace.StartsWith("UnityEngine") || type.Namespace.StartsWith("Unity.") || type.Namespace.StartsWith("System"))) return false;
            return type.IsDefined(typeof(SerializableAttribute), false);
        }

        static string TypeName(Type type)
        {
            if (type.IsArray) return TypeName(type.GetElementType()) + "[]";
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return TypeName(type.GetGenericArguments()[0]) + "[]";
            return type.Name;
        }

        // -------------------------------------------------------------- manifest
        const string ManifestHeader = "Guid,UnityPath,AssetType,Ext,SizeBytes,Action,UEPath,Notes";

        static int ExportManifest(string output, List<ScriptableObject> assets)
        {
            string file = Path.Combine(output, "AssetManifest.csv");
            var kept = ReadManifest(file);

            var roots = runnerScenes.Where(File.Exists).Concat(assets.Select(AssetDatabase.GetAssetPath)).Distinct().ToArray();
            var dataPaths = new HashSet<string>(assets.Select(AssetDatabase.GetAssetPath));
            var paths = AssetDatabase.GetDependencies(roots, true)
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !scriptExtensions.Contains(Path.GetExtension(p)))
                .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

            var csv = new StringBuilder();
            csv.Append(ManifestHeader).Append('\n');
            foreach (string path in paths)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                string ext = Path.GetExtension(path).ToLowerInvariant();
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                long size = File.Exists(path) ? new FileInfo(path).Length : 0;

                string action = dataPaths.Contains(path) ? "Data"
                    : path.Contains("/EVP5/") ? "Not ported"
                    : portExtensions.Contains(ext) ? "Port"
                    : "Rebuild";
                string uePath = "", notes = "";
                if (kept.TryGetValue(guid, out string[] previous))
                {
                    if (previous[0].Length > 0) action = previous[0];
                    uePath = previous[1];
                    notes = previous[2];
                }

                csv.Append(guid).Append(',').Append(Csv(path)).Append(',').Append(type != null ? type.Name : "").Append(',')
                    .Append(ext).Append(',').Append(size.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(Csv(action)).Append(',').Append(Csv(uePath)).Append(',').Append(Csv(notes)).Append('\n');
            }
            File.WriteAllText(file, csv.ToString(), new UTF8Encoding(false));
            return paths.Count;
        }

        /// <summary>guid → (Action, UEPath, Notes) of the manifest already on disk, so hand edits survive a re-export.</summary>
        static Dictionary<string, string[]> ReadManifest(string file)
        {
            var kept = new Dictionary<string, string[]>();
            if (!File.Exists(file)) return kept;
            foreach (string line in File.ReadAllLines(file).Skip(1))
            {
                var cells = SplitCsv(line);
                if (cells.Count >= 8) kept[cells[0]] = new[] { cells[5], cells[6], cells[7] };
            }
            return kept;
        }

        static string Csv(string value) =>
            value.IndexOfAny(new[] { ',', '"', '\n' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";

        static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
                else cell.Append(c);
            }
            cells.Add(cell.ToString());
            return cells;
        }

        // ------------------------------------------------------------------ json
        /// <summary>A small indented JSON writer: floats round-trip, keys keep the order they were written in.</summary>
        sealed class JsonWriter
        {
            readonly StringBuilder text = new();
            readonly Stack<bool> hasItems = new();
            bool afterName;

            public int Length => text.Length;

            public override string ToString() => text.Append('\n').ToString();

            void Separate()
            {
                if (afterName) { afterName = false; return; }
                if (hasItems.Count == 0) return;
                if (hasItems.Pop()) text.Append(',');
                hasItems.Push(true);
                text.Append('\n').Append(' ', hasItems.Count * 2);
            }

            void Close(char bracket)
            {
                bool any = hasItems.Pop();
                if (any) text.Append('\n').Append(' ', hasItems.Count * 2);
                text.Append(bracket);
            }

            public JsonWriter BeginObject() { Separate(); text.Append('{'); hasItems.Push(false); return this; }
            public JsonWriter EndObject() { Close('}'); return this; }
            public JsonWriter BeginArray() { Separate(); text.Append('['); hasItems.Push(false); return this; }
            public JsonWriter EndArray() { Close(']'); return this; }

            public JsonWriter Name(string name)
            {
                Separate();
                Quote(name);
                text.Append(": ");
                afterName = true;
                return this;
            }

            public JsonWriter Null() { Separate(); text.Append("null"); return this; }
            public JsonWriter Value(bool value) { Separate(); text.Append(value ? "true" : "false"); return this; }
            public JsonWriter Value(long value) { Separate(); text.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }
            public JsonWriter Value(string value) { Separate(); if (value == null) text.Append("null"); else Quote(value); return this; }

            public JsonWriter Value(float value)
            {
                Separate();
                text.Append(float.IsNaN(value) || float.IsInfinity(value) ? "0" : value.ToString("R", CultureInfo.InvariantCulture));
                return this;
            }

            public JsonWriter Value(double value)
            {
                Separate();
                text.Append(double.IsNaN(value) || double.IsInfinity(value) ? "0" : value.ToString("R", CultureInfo.InvariantCulture));
                return this;
            }

            /// <summary>An attribute value read by reflection: a number, a bool or text.</summary>
            public JsonWriter Value(object value)
            {
                switch (value)
                {
                    case null: return Null();
                    case bool b: return Value(b);
                    case string s: return Value(s);
                    case float f: return Value(f);
                    case double d: return Value(d);
                    case IConvertible c when value.GetType().IsPrimitive: return Value(c.ToDouble(CultureInfo.InvariantCulture));
                    default: return Value(value.ToString());
                }
            }

            void Quote(string value)
            {
                text.Append('"');
                foreach (char c in value)
                {
                    switch (c)
                    {
                        case '"': text.Append("\\\""); break;
                        case '\\': text.Append("\\\\"); break;
                        case '\n': text.Append("\\n"); break;
                        case '\r': text.Append("\\r"); break;
                        case '\t': text.Append("\\t"); break;
                        default:
                            if (c < 0x20) text.Append("\\u").Append(((int)c).ToString("x4"));
                            else text.Append(c);
                            break;
                    }
                }
                text.Append('"');
            }
        }
    }
}
