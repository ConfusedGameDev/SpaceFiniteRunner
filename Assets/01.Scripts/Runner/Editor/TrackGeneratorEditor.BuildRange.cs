using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.EditorTools
{
    /// <summary>
    /// The edit-mode BUILD RANGE: which stretch of the track on show is built
    /// as GameObjects (<see cref="TrackGenerator.PreviewRange"/>, a per-user
    /// preference in <see cref="TrackGizmoSettings.BuildRange"/>). A long track
    /// is decided whole but building all of it makes every edit's rebuild slow,
    /// so the slider picks the stretch and Build applies it — the road and its
    /// records as they are, nothing re-decided. The minimap draws the whole
    /// track top-down in white, the built stretch in red and a pending pick in
    /// orange.
    /// </summary>
    public partial class TrackGeneratorEditor
    {
        const float MinimapHeight = 180f;
        const float MinimapPad = 8f;
        const int MinimapPoints = 800;

        static readonly Color MinimapBack = new(0.1f, 0.1f, 0.12f, 1f);
        static readonly Color MinimapTrack = new(1f, 1f, 1f, 0.9f);
        static readonly Color MinimapPending = new(1f, 0.6f, 0.1f, 0.9f);

        Vector2 pendingRange = new(-1f, -1f); // the slider's pick before Build; x < 0 = not loaded yet
        readonly List<Vector3> minimapLine = new();

        void DrawBuildRange(TrackGenerator generator)
        {
            if (!TrackGizmos.TryGetCentreLine(generator, out List<float> distance, out List<Vector3> centre)) return;
            float length = distance[distance.Count - 1];
            if (pendingRange.x < 0f) pendingRange = TrackGizmoSettings.BuildRange;

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Build Range", EditorStyles.boldLabel);

            // Shown clamped to this track; an untouched pick is kept as asked (a longer track may follow).
            float from = Mathf.Clamp(pendingRange.x, 0f, length);
            float to = Mathf.Clamp(pendingRange.y, from, length);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                from = Mathf.Clamp(EditorGUILayout.FloatField(from, GUILayout.Width(70)), 0f, length);
                EditorGUILayout.MinMaxSlider(ref from, ref to, 0f, length);
                to = Mathf.Clamp(EditorGUILayout.FloatField(to, GUILayout.Width(70)), from, length);
            }
            if (EditorGUI.EndChangeCheck()) pendingRange = new Vector2(Mathf.Round(from), Mathf.Round(to));

            float builtFrom = generator.BuiltFrom, builtTo = generator.SettledDistance;
            EditorGUILayout.LabelField($"Pick {from / 1000f:0.0}–{to / 1000f:0.0} km of {length / 1000f:0.0}",
                                       builtTo > builtFrom ? $"built {builtFrom / 1000f:0.0}–{builtTo / 1000f:0.0} km" : "nothing built");

            bool pending = pendingRange != TrackGizmoSettings.BuildRange;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Whole Track"))
                {
                    pendingRange = new Vector2(0f, Mathf.Ceil(length));
                    ApplyBuildRange(generator);
                }
                using (new EditorGUI.DisabledScope(!pending))
                    if (GUILayout.Button(pending ? "Build Range *" : "Build Range")) ApplyBuildRange(generator);
            }

            Rect rect = GUILayoutUtility.GetRect(10f, MinimapHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                DrawMinimap(rect, distance, centre, builtFrom, builtTo, pending ? new Vector2(from, to) : new Vector2(-1f, -1f));
            EditorGUILayout.HelpBox("White = the whole track · red = built as GameObjects · orange = picked, not built yet. A wide range makes every edit's rebuild slower.", MessageType.None);
        }

        void ApplyBuildRange(TrackGenerator generator)
        {
            TrackGizmoSettings.BuildRange = pendingRange;
            generator.RebuildPreview();
            MarkChanged(generator);
        }

        // Top-down (X, Z), fitted to the rect with the aspect kept, north up.
        void DrawMinimap(Rect rect, List<float> distance, List<Vector3> centre, float builtFrom, float builtTo, Vector2 picked)
        {
            EditorGUI.DrawRect(rect, MinimapBack);
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            foreach (Vector3 p in centre)
            {
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            Vector2 size = Vector2.Max(max - min, Vector2.one);
            Rect inner = new(MinimapPad, MinimapPad, rect.width - 2f * MinimapPad, rect.height - 2f * MinimapPad);
            float scale = Mathf.Min(inner.width / size.x, inner.height / size.y);
            Vector2 offset = new(inner.x + (inner.width - size.x * scale) * 0.5f, inner.y + (inner.height - size.y * scale) * 0.5f);
            Vector3 ToMap(Vector3 p) => new(offset.x + (p.x - min.x) * scale, offset.y + (max.y - p.z) * scale, 0f);

            int stride = Mathf.Max(1, centre.Count / MinimapPoints);
            GUI.BeginClip(rect);
            DrawMinimapSpan(distance, centre, stride, 0f, float.MaxValue, MinimapTrack, 1.5f, ToMap);
            if (picked.y > picked.x) DrawMinimapSpan(distance, centre, stride, picked.x, picked.y, MinimapPending, 5f, ToMap);
            if (builtTo > builtFrom) DrawMinimapSpan(distance, centre, stride, builtFrom, builtTo, TrackGizmos.BuiltColor, 3f, ToMap);

            // The start and the end, so the direction reads.
            Handles.color = Color.green;
            Handles.DrawSolidDisc(ToMap(centre[0]), Vector3.forward, 3.5f);
            Handles.color = new Color(1f, 0.25f, 0.9f, 1f);
            Handles.DrawSolidDisc(ToMap(centre[centre.Count - 1]), Vector3.forward, 3.5f);
            GUI.EndClip();
        }

        void DrawMinimapSpan(List<float> distance, List<Vector3> centre, int stride, float from, float to,
                             Color color, float width, System.Func<Vector3, Vector3> toMap)
        {
            minimapLine.Clear();
            int last = centre.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                if (distance[i] < from) continue;
                if (distance[i] > to) break;
                if (i % stride == 0 || i == last || minimapLine.Count == 0) minimapLine.Add(toMap(centre[i]));
            }
            if (minimapLine.Count < 2) return;
            Handles.color = color;
            Handles.DrawAAPolyLine(width, minimapLine.ToArray());
        }
    }
}
