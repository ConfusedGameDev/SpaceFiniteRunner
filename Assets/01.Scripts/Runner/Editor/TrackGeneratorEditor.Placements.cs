using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;
using ConfusedGameDev.FiniteRunner.Track.Features;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// Placement editing on a saved track (TrackAuthoringPRD M5) — the
    /// Placements tab of Edit Track. Every change is a record in the asset
    /// (<see cref="TrackLayout.placements"/>), undoable, saved with the rest
    /// of the edit (Save / Stop Editing).
    /// <list type="bullet">
    /// <item><b>Select</b> a placement by clicking its dot; drag it along and
    /// across the road (projected back to distance and lateral, clamped to the
    /// lane). The inspector edits its numbers and variant, duplicates or
    /// deletes it, and lists the ones nearest the Scene camera.</item>
    /// <item><b>Place</b>: pick from the palette — the catalog's power-ups
    /// (<see cref="TrackPlacementCatalog"/>), a laser gate, a ramp, a row of
    /// coins or any prefab — then click the road.</item>
    /// <item><b>Check</b>: the generator's own rules, as warnings — a laser
    /// gate near a ramp, a feature or the run-up, pickups on top of each
    /// other, anything off the lane, a ramp whose landing reaches the run-up,
    /// anything in the run-up or past the end.</item>
    /// <item><b>Reroll Placements</b> decides the orbs, repair orbs, gates and
    /// coins afresh on the road as it stands.</item>
    /// </list>
    /// A moved or added ramp moves its keep-out with it (the ground gates and
    /// the patrol's duel keep off).
    /// </summary>
    public partial class TrackGeneratorEditor
    {
        const float GateClearance = 150f; // the laser gate spawner's clearance
        static readonly string[] TabNames = { "Road", "Placements" };
        static readonly Color PlacementDotColor = new(1f, 1f, 1f, 0.8f);
        static readonly Color WarningColor = new(1f, 0.2f, 0.2f, 1f);

        int editTab;
        int selectedPlacement = -1;
        int paletteIndex;
        bool placing;
        GameObject customPrefab;
        Vector2 listScroll;
        readonly List<(int index, string message)> warnings = new();
        bool warningsStale = true;

        // ------------------------------------------------------------ palette
        // What the palette offers, rebuilt per draw (cheap): the catalog, then
        // the four gate variants, every ramp entry of the feature table, coins, a prefab.
        struct PaletteItem
        {
            public string label;
            public System.Func<float, float, TrackPlacement> make;
        }

        List<PaletteItem> Palette(TrackGenerator generator)
        {
            var items = new List<PaletteItem>();
            TrackPlacementCatalog catalog = generator.Catalog;
            if (catalog != null)
                for (int i = 0; i < catalog.entries.Count; i++)
                {
                    int index = i;
                    items.Add(new PaletteItem { label = "Power-up/" + catalog.entries[i].displayName, make = (d, l) => catalog.RecordFor(index, d, l) });
                }
            foreach (LaserGateVariant variant in System.Enum.GetValues(typeof(LaserGateVariant)))
            {
                var v = variant;
                items.Add(new PaletteItem
                {
                    label = "Laser gate/" + v,
                    make = (d, l) =>
                    {
                        generator.Track.GetLateralBand(d, out float min, out float max);
                        return new TrackPlacement(TrackPlacementKind.LaserGate, d, l, (int)v, new Vector4((max - min) * 0.25f, 60f, 0f, 0f));
                    },
                });
            }
            var table = generator.FeatureTable;
            if (table != null)
                for (int i = 0; i < table.Length; i++)
                {
                    if (!(table[i]?.definition is JumpDefinition)) continue;
                    int index = i;
                    items.Add(new PaletteItem { label = "Ramp/" + table[i].name, make = (d, l) => new TrackPlacement(TrackPlacementKind.Ramp, d, l, index) });
                }
            items.Add(new PaletteItem { label = "Coin", make = (d, l) => new TrackPlacement(TrackPlacementKind.Collectible, d, l, 0, new Vector4(3f, 0f, 0f, 0f)) });
            items.Add(new PaletteItem { label = "Custom prefab", make = null }); // needs the prefab field
            return items;
        }

        // -------------------------------------------------------------- panel
        void DrawEditTabs()
        {
            int tab = GUILayout.Toolbar(editTab, TabNames);
            if (tab != editTab)
            {
                editTab = tab;
                placing = false;
                SceneView.RepaintAll();
            }
        }

        void DrawPlacementPanel(TrackGenerator generator, TrackLayout layout)
        {
            // Palette and placing.
            List<PaletteItem> palette = Palette(generator);
            paletteIndex = Mathf.Clamp(paletteIndex, 0, palette.Count - 1);
            var labels = new string[palette.Count];
            for (int i = 0; i < palette.Count; i++) labels[i] = palette[i].label;
            paletteIndex = EditorGUILayout.Popup("Place", paletteIndex, labels);
            bool custom = palette[paletteIndex].make == null;
            if (custom) customPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab", customPrefab, typeof(GameObject), false);
            using (new EditorGUI.DisabledScope(custom && customPrefab == null))
            {
                bool on = GUILayout.Toggle(placing, placing ? "Placing — click the road (Esc stops)" : "Click in Scene to Place", "Button", GUILayout.Height(24));
                if (on != placing) { placing = on; SceneView.RepaintAll(); }
            }
            if (generator.Catalog == null)
                EditorGUILayout.HelpBox($"No placement catalog: wire one on the generator or put one at Resources/{TrackPlacementCatalog.ResourcePath}.", MessageType.Warning);

            // The selected one.
            if (selectedPlacement >= layout.placements.Count) selectedPlacement = -1;
            if (selectedPlacement >= 0) DrawSelectedPlacement(generator, layout);

            // Checks.
            if (warningsStale) { Validate(generator, layout); warningsStale = false; }
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(warnings.Count == 0 ? "Checks: all clear" : $"Checks: {warnings.Count} warning(s)", EditorStyles.boldLabel);
            for (int w = 0; w < Mathf.Min(warnings.Count, 12); w++)
            {
                var (index, message) = warnings[w];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{layout.placements[index].distance / 1000f:0.00} km", message, EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("Go", GUILayout.Width(36))) SelectPlacement(generator, layout, index);
                }
            }
            if (warnings.Count > 12) EditorGUILayout.LabelField($"… and {warnings.Count - 12} more");

            // Nearest to the Scene camera.
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Near the Scene camera", EditorStyles.boldLabel);
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.Height(140));
            foreach (int index in NearestPlacements(generator, layout, 25))
            {
                TrackPlacement p = layout.placements[index];
                using (new EditorGUILayout.HorizontalScope())
                {
                    string label = $"{p.distance / 1000f:0.000} km  {Describe(generator, p)}";
                    if (GUILayout.Toggle(index == selectedPlacement, label, "Button") && index != selectedPlacement) SelectPlacement(generator, layout, index);
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Reroll Placements (orbs, repair orbs, gates, coins)")
                && EditorUtility.DisplayDialog("Reroll placements?",
                    "Decide every speed orb, repair orb, laser gate and coin afresh on this road? Ramps, power-ups from the catalog and custom prefabs stay. Undo brings the old ones back.",
                    "Reroll", "Cancel"))
            {
                Undo.RecordObject(working, "Reroll Placements");
                generator.RerollPlacements(working);
                EditorUtility.SetDirty(working);
                selectedPlacement = -1;
                warningsStale = true;
                SceneView.RepaintAll();
            }
        }

        void DrawSelectedPlacement(TrackGenerator generator, TrackLayout layout)
        {
            int i = selectedPlacement;
            TrackPlacement p = layout.placements[i];
            bool fixedPlacement = p.kind == TrackPlacementKind.EndRamp || p.kind == TrackPlacementKind.Loop;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Selected: {Describe(generator, p)}", EditorStyles.boldLabel);
            if (fixedPlacement)
            {
                EditorGUILayout.HelpBox("End ramps follow the end of the road, and a loop is its section: neither is moved here.", MessageType.None);
                return;
            }

            EditorGUI.BeginChangeCheck();
            float distance = EditorGUILayout.FloatField("Distance (m)", p.distance);
            generator.Track.GetLateralBand(distance, out float min, out float max);
            float lateral = EditorGUILayout.Slider("Lateral (m)", p.lateral, min, max);
            float height = p.kind == TrackPlacementKind.CustomPrefab ? EditorGUILayout.FloatField("Height (m)", p.height) : p.height;
            int variant = DrawVariant(generator, p);
            Vector4 data = p.data;
            if (p.kind == TrackPlacementKind.LaserGate)
            {
                data.x = EditorGUILayout.Slider("Beam length (m)", data.x, 5f, max - min);
                if ((LaserGateVariant)variant == LaserGateVariant.Rotor) data.y = EditorGUILayout.Slider("Rotor speed (°/s)", data.y, -360f, 360f);
                data.w = EditorGUILayout.Toggle("Wave", data.w > 0.5f) ? 1f : 0f;
            }
            if (p.kind == TrackPlacementKind.Collectible) data.x = EditorGUILayout.IntSlider("Value ($)", Mathf.RoundToInt(data.x), 1, 100);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(working, "Edit Placement");
                var moved = new TrackPlacement(p.kind, Mathf.Clamp(distance, 0f, layout.endDistance), lateral, variant, data, height);
                SetPlacement(generator, layout, i, moved);
                Commit(generator, true);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Frame")) FramePlacement(generator, p);
                if (GUILayout.Button("Duplicate (+30 m)"))
                {
                    Undo.RecordObject(working, "Duplicate Placement");
                    var copy = p;
                    copy.distance = Mathf.Min(p.distance + 30f, layout.endDistance);
                    AddPlacement(generator, layout, copy);
                    Commit(generator, true);
                }
                if (GUILayout.Button("Delete"))
                {
                    Undo.RecordObject(working, "Delete Placement");
                    RemovePlacement(generator, layout, i);
                    Commit(generator, true);
                }
            }
        }

        // The variant control for a kind: an orb tier, a gate variant, a ramp entry, a catalog entry.
        int DrawVariant(TrackGenerator generator, TrackPlacement p)
        {
            switch (p.kind)
            {
                case TrackPlacementKind.SpeedOrb:
                {
                    var orbs = generator.GetSpawner<SpeedOrbSpawner>();
                    if (orbs?.Tiers == null) return p.variant;
                    var names = new string[orbs.Tiers.Length];
                    for (int t = 0; t < names.Length; t++) names[t] = orbs.Tiers[t].name;
                    return EditorGUILayout.Popup("Tier", p.variant, names);
                }
                case TrackPlacementKind.LaserGate:
                    return (int)(LaserGateVariant)EditorGUILayout.EnumPopup("Gate", (LaserGateVariant)p.variant);
                case TrackPlacementKind.Pickup:
                {
                    var catalog = generator.Catalog;
                    if (catalog == null) return p.variant;
                    var names = new string[catalog.entries.Count];
                    for (int c = 0; c < names.Length; c++) names[c] = catalog.entries[c].displayName;
                    return EditorGUILayout.Popup("Power-up", p.variant, names);
                }
                default:
                    return p.variant;
            }
        }

        // ------------------------------------------------------------- scene
        void PlacementSceneGUI(TrackGenerator generator, TrackLayout layout, Vector3 eye, float radiusSqr)
        {
            TrackManager track = generator.Track;
            Event e = Event.current;

            if (placing)
            {
                // Clicks go to placing, not to selecting things in the scene.
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { placing = false; e.Use(); Repaint(); }
                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt
                    && PickRoad(track, HandleUtility.GUIPointToWorldRay(e.mousePosition), eye, radiusSqr, out float d, out float lateral))
                {
                    PlaceAt(generator, layout, d, lateral);
                    e.Use();
                }
            }

            // A dot per placement near the camera; the selected one gets a handle.
            if (warningsStale) { Validate(generator, layout); warningsStale = false; }
            var flagged = new HashSet<int>();
            foreach (var w in warnings) flagged.Add(w.index);

            for (int i = 0; i < layout.placements.Count; i++)
            {
                TrackPlacement p = layout.placements[i];
                Vector3 world = PlacementWorld(track, p);
                if ((world - eye).sqrMagnitude > radiusSqr && i != selectedPlacement) continue;
                float size = HandleUtility.GetHandleSize(world) * 0.1f;
                Handles.color = i == selectedPlacement ? SelectedColor : flagged.Contains(i) ? WarningColor : PlacementDotColor;
                if (!placing && Handles.Button(world, Quaternion.identity, size, size * 1.3f, Handles.DotHandleCap))
                {
                    selectedPlacement = i;
                    Repaint();
                }
                if (flagged.Contains(i)) Handles.DrawWireDisc(world, Vector3.up, size * 5f);
            }

            if (placing || selectedPlacement < 0 || selectedPlacement >= layout.placements.Count) return;
            TrackPlacement selected = layout.placements[selectedPlacement];
            if (selected.kind == TrackPlacementKind.EndRamp || selected.kind == TrackPlacementKind.Loop) return;

            Vector3 at = PlacementWorld(track, selected);
            EditorGUI.BeginChangeCheck();
            Vector3 dragged = Handles.FreeMoveHandle(at, HandleUtility.GetHandleSize(at) * 0.18f, Vector3.zero, Handles.CircleHandleCap);
            if (!EditorGUI.EndChangeCheck()) return;

            float newDistance = ProjectDistance(track, dragged, selected.distance);
            track.GetPoseAtDistance(newDistance, 0f, out Vector3 centre, out Quaternion rotation);
            track.GetLateralBand(newDistance, out float min, out float max);
            float newLateral = Mathf.Clamp(Vector3.Dot(dragged - centre, rotation * Vector3.right), min, max);

            Undo.RecordObject(working, "Move Placement");
            float oldDistance = selected.distance;
            selected.distance = newDistance;
            selected.lateral = newLateral;
            SetPlacement(generator, layout, selectedPlacement, selected);
            EditorUtility.SetDirty(working);
            generator.ApplyEditLive(working, Mathf.Min(oldDistance, newDistance) - 60f, Mathf.Max(oldDistance, newDistance) + 60f);
            warningsStale = true;
            rebuildPending = true;
            SceneView.RepaintAll();
        }

        void PlaceAt(TrackGenerator generator, TrackLayout layout, float distance, float lateral)
        {
            List<PaletteItem> palette = Palette(generator);
            PaletteItem item = palette[Mathf.Clamp(paletteIndex, 0, palette.Count - 1)];
            TrackPlacement record;
            if (item.make != null) record = item.make(distance, lateral);
            else
            {
                if (customPrefab == null) return;
                Undo.RecordObject(working, "Place Prefab");
                int index = working.customPrefabs.IndexOf(customPrefab);
                if (index < 0) { working.customPrefabs.Add(customPrefab); index = working.customPrefabs.Count - 1; }
                record = new TrackPlacement(TrackPlacementKind.CustomPrefab, distance, lateral, index);
            }
            Undo.RecordObject(working, "Place " + item.label);
            AddPlacement(generator, layout, record);
            selectedPlacement = layout.placements.Count - 1;
            Commit(generator, true);
        }

        // -------------------------------------------------------- record edits
        // Every record change goes through these three, so a ramp's keep-out
        // (the ground gates and the patrol duel keep off) moves with it.
        void AddPlacement(TrackGenerator generator, TrackLayout layout, TrackPlacement p)
        {
            layout.placements.Add(p);
            if (p.kind == TrackPlacementKind.Ramp) layout.keepOuts.Add(generator.RampKeepOut(p.variant, p.distance));
            warningsStale = true;
        }

        void SetPlacement(TrackGenerator generator, TrackLayout layout, int i, TrackPlacement p)
        {
            TrackPlacement old = layout.placements[i];
            if (old.kind == TrackPlacementKind.Ramp) RemoveKeepOutAt(layout, old.distance);
            layout.placements[i] = p;
            if (p.kind == TrackPlacementKind.Ramp) layout.keepOuts.Add(generator.RampKeepOut(p.variant, p.distance));
            warningsStale = true;
        }

        void RemovePlacement(TrackGenerator generator, TrackLayout layout, int i)
        {
            if (layout.placements[i].kind == TrackPlacementKind.Ramp) RemoveKeepOutAt(layout, layout.placements[i].distance);
            layout.placements.RemoveAt(i);
            selectedPlacement = -1;
            warningsStale = true;
        }

        static void RemoveKeepOutAt(TrackLayout layout, float start)
        {
            int nearest = -1;
            float best = 1f;
            for (int k = 0; k < layout.keepOuts.Count; k++)
            {
                float gap = Mathf.Abs(layout.keepOuts[k].x - start);
                if (gap < best) { best = gap; nearest = k; }
            }
            if (nearest >= 0) layout.keepOuts.RemoveAt(nearest);
        }

        // ------------------------------------------------------------- checks
        // The generator's own placement rules, as warnings on the records.
        void Validate(TrackGenerator generator, TrackLayout layout)
        {
            warnings.Clear();
            TrackManager track = generator.Track;
            if (track == null) return;
            float zone = layout.endZoneStart >= 0f ? layout.endZoneStart : float.MaxValue;
            float padLength = generator.PadLength;

            var pickups = new List<int>();
            for (int i = 0; i < layout.placements.Count; i++)
            {
                TrackPlacement p = layout.placements[i];
                if (p.kind == TrackPlacementKind.EndRamp) continue;

                if (p.distance > layout.endDistance) { warnings.Add((i, "past the end of the road")); continue; }
                if (p.distance >= zone) warnings.Add((i, "in the final run-up (it stays clear)"));

                track.GetLateralBand(p.distance, out float min, out float max);
                if (p.kind != TrackPlacementKind.Loop && (p.lateral < min - 0.5f || p.lateral > max + 0.5f)) warnings.Add((i, "off the lane"));

                switch (p.kind)
                {
                    case TrackPlacementKind.LaserGate:
                        foreach (Vector2 keepOut in layout.keepOuts)
                            if (p.distance + GateClearance > keepOut.x && p.distance - GateClearance < keepOut.y)
                            {
                                warnings.Add((i, keepOut.y > zone ? "laser gate near the final run-up" : "laser gate within 150 m of a ramp, its landing or a feature"));
                                break;
                            }
                        break;
                    case TrackPlacementKind.Ramp:
                        if (generator.RampKeepOut(p.variant, p.distance).y > zone) warnings.Add((i, "ramp landing reaches the final run-up"));
                        break;
                    case TrackPlacementKind.SpeedOrb:
                    case TrackPlacementKind.RepairOrb:
                    case TrackPlacementKind.Pickup:
                    case TrackPlacementKind.BrakePad:
                        pickups.Add(i);
                        break;
                }
            }

            pickups.Sort((a, b) => layout.placements[a].distance.CompareTo(layout.placements[b].distance));
            for (int n = 1; n < pickups.Count; n++)
                if (layout.placements[pickups[n]].distance - layout.placements[pickups[n - 1]].distance < padLength)
                    warnings.Add((pickups[n], $"within {padLength:0} m of another pickup"));
        }

        // ------------------------------------------------------------ helpers
        static Vector3 PlacementWorld(TrackManager track, TrackPlacement p)
        {
            track.GetFrameAtDistance(p.distance, out Vector3 centre, out _, out Vector3 up, out Vector3 right);
            return centre + right * p.lateral + up * (p.height + 3f);
        }

        // The road point under the mouse: the nearest hit of the ray on the road's
        // own plane, sampled every 20 m round the camera, inside the road's width.
        static bool PickRoad(TrackManager track, Ray ray, Vector3 eye, float radiusSqr, out float distance, out float lateral)
        {
            distance = lateral = 0f;
            float bestRay = float.MaxValue;
            for (float d = 0f; d <= track.Length; d += 20f)
            {
                track.GetFrameAtDistance(d, out Vector3 centre, out Vector3 forward, out Vector3 up, out Vector3 right);
                if ((centre - eye).sqrMagnitude > radiusSqr) continue;
                var plane = new Plane(up, centre);
                if (!plane.Raycast(ray, out float enter)) continue;
                Vector3 hit = ray.GetPoint(enter);
                float along = Vector3.Dot(hit - centre, forward);
                float across = Vector3.Dot(hit - centre, right);
                if (Mathf.Abs(along) > 10f || Mathf.Abs(across) > track.RoadHalfWidth || enter >= bestRay) continue;
                bestRay = enter;
                distance = Mathf.Clamp(d + along, 0f, track.Length);
                track.GetLateralBand(distance, out float min, out float max);
                lateral = Mathf.Clamp(across, min, max);
            }
            return bestRay < float.MaxValue;
        }

        List<int> NearestPlacements(TrackGenerator generator, TrackLayout layout, int count)
        {
            var view = SceneView.lastActiveSceneView;
            Vector3 eye = view != null && view.camera != null ? view.camera.transform.position : Vector3.zero;
            var order = new List<(float, int)>();
            for (int i = 0; i < layout.placements.Count; i++)
                order.Add(((PlacementWorld(generator.Track, layout.placements[i]) - eye).sqrMagnitude, i));
            order.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            var nearest = new List<int>();
            for (int i = 0; i < Mathf.Min(count, order.Count); i++) nearest.Add(order[i].Item2);
            nearest.Sort((a, b) => layout.placements[a].distance.CompareTo(layout.placements[b].distance));
            return nearest;
        }

        void SelectPlacement(TrackGenerator generator, TrackLayout layout, int index)
        {
            selectedPlacement = index;
            FramePlacement(generator, layout.placements[index]);
            Repaint();
        }

        static void FramePlacement(TrackGenerator generator, TrackPlacement p)
        {
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(PlacementWorld(generator.Track, p), Vector3.one * 150f), false);
        }

        string Describe(TrackGenerator generator, TrackPlacement p)
        {
            switch (p.kind)
            {
                case TrackPlacementKind.SpeedOrb:
                    var orbs = generator.GetSpawner<SpeedOrbSpawner>();
                    return orbs?.Tiers != null && p.variant >= 0 && p.variant < orbs.Tiers.Length ? orbs.Tiers[p.variant].name + " orb" : "Speed orb";
                case TrackPlacementKind.LaserGate: return ((LaserGateVariant)p.variant) + " laser gate";
                case TrackPlacementKind.Ramp: return "Ramp";
                case TrackPlacementKind.Collectible: return $"Coin (${Mathf.RoundToInt(p.data.x)})";
                case TrackPlacementKind.Pickup:
                    var catalog = generator.Catalog;
                    return catalog != null && p.variant >= 0 && p.variant < catalog.entries.Count ? catalog.entries[p.variant].displayName : "Power-up";
                case TrackPlacementKind.CustomPrefab:
                    return working != null && p.variant >= 0 && p.variant < working.customPrefabs.Count && working.customPrefabs[p.variant] != null
                        ? working.customPrefabs[p.variant].name : "Custom prefab";
                default: return p.kind.ToString();
            }
        }
    }
}
