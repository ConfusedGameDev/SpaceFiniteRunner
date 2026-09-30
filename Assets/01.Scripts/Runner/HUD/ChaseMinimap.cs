using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.Contracts;
using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Ship;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// The chase minimap. <b>The track map</b> (TrackAuthoringPRD M6) draws
    /// the WHOLE track from above inside the hand-placed <c>TrackMap</c> rect
    /// — the run knows its full shape from the first frame, the track being
    /// decided whole at run start — turned so the start is at the bottom and
    /// the finish at the top, fitted with its aspect kept (<see cref="TrackMapLine"/>).
    /// The ship and the patrol are dots at their real distances along it
    /// (the patrol at the ship's distance minus its gap, hidden with
    /// <c>HiddenFromMap</c>), the flown stretch is tinted, and ramps, laser
    /// gates and the finish are marked — never orbs (D11). The track is
    /// re-sampled whenever it changes (a restart, the hyperspace jump's new
    /// end: the track's revision, length or end moved).
    /// <para>The older <b>strip</b>: a vertical strip that IS
    /// the track — the ship diamond starts at the bottom and climbs to the
    /// top as it nears the end, the distance left reads above the strip and
    /// the patrol gap in meters below it. The patrol icon hangs under the
    /// ship on its own zoomed scale (the full minimap range = chaseSpan
    /// pixels; at track scale the gap would be a pixel or two), and is drawn
    /// only when it is inside that range AND there is strip left under the
    /// ship to draw it on. On an endless track the ship stays pinned at the
    /// top. Spawned with or without a patrol.
    /// <para><b>The layout is the designer's.</b> The strip, icons and labels
    /// are prefab children wired below; at runtime the GameManager's Spawn
    /// only binds them. Code moves the icons along the strip's height and sets
    /// their size and colours from the settings asset — nothing else: the
    /// strip's rect, the labels' place and fonts and the icons' sideways
    /// offset are left as authored. <b>Rebuild UI</b> creates any unwired
    /// part with a default look and re-applies the style; editing the style
    /// asset does the same live.</para>
    /// </summary>
    public class ChaseMinimap : MonoBehaviour
    {
        [Tooltip("All minimap look tunables live on this asset — add new knobs there, not here.")]
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        [OnValueChanged(nameof(RebuildUI))]
        ChaseMinimapSettings style;

        [Title("Parts")]
        [Tooltip("The strip: bottom = the track's start, top = its end.")]
        [SerializeField] RectTransform bar;
        [Tooltip("Child of the strip, bottom-anchored: moved up it by the run.")]
        [SerializeField] RectTransform shipIcon;
        [Tooltip("Child of the strip, bottom-anchored, with an Image: hangs under the ship.")]
        [SerializeField] RectTransform policeIcon;
        [Tooltip("Distance to the end of the track.")]
        [SerializeField] Text endText;
        [Tooltip("Patrol gap.")]
        [SerializeField] Text distanceText;

        [Title("Track map")]
        [Tooltip("The rect the whole track is drawn in (place and size it by hand; an Image on it is the backdrop).")]
        [SerializeField] RectTransform mapArea;
        [Tooltip("Child of the map rect, stretched to fill it: draws the track line.")]
        [SerializeField] TrackMapLine mapLine;
        [Tooltip("Child of the map rect, centre-anchored: the ship's dot.")]
        [SerializeField] RectTransform mapShipIcon;
        [Tooltip("Child of the map rect, centre-anchored, with an Image: the patrol's dot.")]
        [SerializeField] RectTransform mapPoliceIcon;

        IChaseTarget motor;   // the ship, by contract
        PolicePatrol patrol;     // null on a chase-less run: the map still shows the track
        IRunState gameManager; // the run, by contract
        float rangeMeters; // gap beyond which the patrol is off the map; at it the icon hangs a full chaseSpan under the ship
        float warnMeters;  // gap below which the readout turns red

        Image policeImage;
        Image mapPoliceImage;
        TrackGenerator generator;   // the track the map draws; null = no map
        int mapRevision = -1;       // the track as last sampled: re-sampled when any of these move
        float mapLength = -1f, mapEnd = -2f, mapStep = 1f;
        readonly List<Vector2> mapPoints = new();       // offsets from the map rect's centre, every mapStep metres
        readonly List<RectTransform> mapMarkers = new(); // pooled: ramps, gates, the finish
        int shownEnd = -1; // what the two labels currently read, so the strings
        int shownGap = -1; // are rebuilt only when the number changes
        float blinkTimer;
        bool blinkState;

        /// <summary>The ship icon part (editor sync reads its rotation back into the style).</summary>
        public RectTransform ShipIcon => shipIcon;
        /// <summary>The police icon part (editor sync reads its rotation back into the style).</summary>
        public RectTransform PoliceIcon => policeIcon;
        /// <summary>The assigned style asset, or null.</summary>
        public ChaseMinimapSettings StyleAsset => style;

        ChaseMinimapSettings Style => style != null ? style : style = ScriptableObject.CreateInstance<ChaseMinimapSettings>();

        public static ChaseMinimap Spawn(IChaseTarget motor, PolicePatrol patrol, IRunState gameManager, float rangeMeters, float warnMeters,
                                         TrackGenerator track = null)
        {
            var map = FindFirstObjectByType<ChaseMinimap>();
            if (map == null) map = new GameObject("ChaseMinimap").AddComponent<ChaseMinimap>();
            map.generator = track;
            map.mapRevision = -1;
            map.motor = motor;
            map.patrol = patrol;
            map.gameManager = gameManager;
            map.rangeMeters = Mathf.Max(1f, rangeMeters);
            map.warnMeters = warnMeters;
            map.Bind();
            return map;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Rebuilds every live minimap on <paramref name="changed"/> — scene
        /// instances and the open prefab stage, never the prefab asset itself.
        /// Called by the settings asset whenever it is edited.
        /// </summary>
        public static void RebuildAllUsing(ChaseMinimapSettings changed)
        {
            foreach (var map in Resources.FindObjectsOfTypeAll<ChaseMinimap>())
                if (map.style == changed && !UnityEditor.EditorUtility.IsPersistent(map))
                    map.RebuildUI();
        }
#endif

        void Awake()
        {
            // Scene-placed instance whose Spawn has not come (or never will —
            // a scene with no GameManager): hide it so no dead gauge lingers.
            if (motor == null) SetShown(false);
        }

        void SetShown(bool shown)
        {
            var canvas = GetComponent<Canvas>();
            if (canvas != null) canvas.enabled = shown;
        }

        // Runtime: adopt the authored parts (building only what a bare,
        // prefab-less map lacks) and apply the settings' sizes and colours.
        void Bind()
        {
            RebuildUI();
            SetShown(true);
            policeIcon.gameObject.SetActive(false);
            mapPoliceIcon.gameObject.SetActive(false);
            shownEnd = shownGap = -1;
        }

        /// <summary>
        /// Creates any unwired part with the default look, then applies the
        /// style asset to the icons: sizes, colours and sprites. Runs on Spawn,
        /// from this button, and live whenever the style asset is edited or
        /// swapped. The strip's rect, the labels and the icons' sideways offset
        /// are never touched.
        /// </summary>
        [Button("Rebuild UI", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildUI()
        {
            BuildMissingParts();
            ApplyStyle();
        }

        void ApplyStyle()
        {
            var s = Style;
            shipIcon.sizeDelta = Vector2.one * s.ShipIconEdge;
            policeIcon.sizeDelta = Vector2.one * s.PoliceIconEdge;
            var shipImage = shipIcon.GetComponent<Image>();
            if (shipImage != null)
            {
                shipImage.color = s.shipColor;
                ApplySprite(shipImage, s.shipSprite, 45f, s.shipSpriteRotation);
            }
            policeImage = policeIcon.GetComponent<Image>();
            if (policeImage != null)
            {
                policeImage.color = s.policeRed;
                ApplySprite(policeImage, s.policeSprite, 0f, s.policeSpriteRotation);
            }
            // The track map: its own icons styled like the strip's, the backdrop, the line.
            mapShipIcon.sizeDelta = Vector2.one * s.ShipIconEdge;
            mapPoliceIcon.sizeDelta = Vector2.one * s.PoliceIconEdge;
            var mapShipImage = mapShipIcon.GetComponent<Image>();
            if (mapShipImage != null)
            {
                mapShipImage.color = s.shipColor;
                ApplySprite(mapShipImage, s.shipSprite, 45f, s.shipSpriteRotation);
            }
            mapPoliceImage = mapPoliceIcon.GetComponent<Image>();
            if (mapPoliceImage != null)
            {
                mapPoliceImage.color = s.policeRed;
                ApplySprite(mapPoliceImage, s.policeSprite, 0f, s.policeSpriteRotation);
            }
            var backdrop = mapArea.GetComponent<Image>();
            if (backdrop != null) backdrop.color = s.mapBackground;
            mapLine.SetStyle(s.mapLineWidth, s.mapLineColor, s.mapDrivenColor);
            mapArea.gameObject.SetActive(s.showTrackMap);
            mapRevision = -1; // markers and line re-styled on the next sample
            if (!Application.isPlaying) PreviewMapLine();

            // The strip: its bar and icons only — the labels on it stay.
            var barImage = bar.GetComponent<Image>();
            if (barImage != null) barImage.enabled = s.showStrip;
            shipIcon.gameObject.SetActive(s.showStrip);
            if (!s.showStrip) policeIcon.gameObject.SetActive(false);

            blinkTimer = 0f;
            blinkState = false;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(shipIcon);
                UnityEditor.EditorUtility.SetDirty(policeIcon);
                if (shipImage != null) UnityEditor.EditorUtility.SetDirty(shipImage);
                if (policeImage != null) UnityEditor.EditorUtility.SetDirty(policeImage);
            }
#endif
        }

        // An assigned sprite keeps its aspect and takes the style's rotation;
        // none gives back the default shape — a plain square turned by
        // defaultAngle (the ship's diamond is a square at 45°) — so clearing a
        // sprite is live too.
        static void ApplySprite(Image image, Sprite sprite, float defaultAngle, float spriteAngle)
        {
            image.sprite = sprite;
            image.preserveAspect = sprite != null;
            image.transform.localRotation = Quaternion.Euler(0f, 0f, sprite != null ? spriteAngle : defaultAngle);
        }

        // Creates every unwired part with the default look (strip on the right
        // edge, icons on it, labels above and below) and wires it. Parts that
        // are already wired are left exactly as they are.
        void BuildMissingParts()
        {
            var s = Style;
            if (GetComponent<Canvas>() == null)
            {
                var canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
            }

            if (bar == null)
            {
                // Vertical strip hugging the right edge, vertically centered.
                bar = CreateRect("Bar", transform, new Vector2(1f, 0.5f), new Vector2(-60f, 0f), new Vector2(10f, 480f));
                bar.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);
            }
            // The patrol first so the ship draws over it.
            if (policeIcon == null)
            {
                policeIcon = CreateRect("Police", bar, new Vector2(0.5f, 0f), Vector2.zero, Vector2.one * s.policeIconSize);
                policeIcon.gameObject.AddComponent<Image>().color = s.policeRed;
                policeIcon.SetAsFirstSibling();
            }
            if (shipIcon == null)
            {
                // A diamond, from the bottom (start) to the top (end).
                shipIcon = CreateRect("Ship", bar, new Vector2(0.5f, 0f), new Vector2(0f, 190f), Vector2.one * s.shipIconSize);
                shipIcon.localRotation = Quaternion.Euler(0f, 0f, 45f);
                shipIcon.gameObject.AddComponent<Image>().color = s.shipColor;
            }
            if (mapArea == null)
            {
                // A square at the right edge, above the speed readout: move and size it by hand.
                mapArea = CreateRect("TrackMap", transform, new Vector2(1f, 0.5f), new Vector2(-190f, 60f), new Vector2(280f, 280f));
                var backdrop = mapArea.gameObject.AddComponent<Image>();
                backdrop.color = s.mapBackground;
                backdrop.raycastTarget = false;
            }
            if (mapLine == null)
            {
                var line = CreateRect("TrackLine", mapArea, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                line.anchorMin = Vector2.zero;
                line.anchorMax = Vector2.one;
                line.sizeDelta = Vector2.zero;
                mapLine = line.gameObject.AddComponent<TrackMapLine>();
            }
            // The patrol first so the ship draws over it.
            if (mapPoliceIcon == null)
            {
                mapPoliceIcon = CreateRect("MapPolice", mapArea, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * s.policeIconSize);
                mapPoliceIcon.gameObject.AddComponent<Image>().color = s.policeRed;
            }
            if (mapShipIcon == null)
            {
                mapShipIcon = CreateRect("MapShip", mapArea, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * s.shipIconSize);
                mapShipIcon.gameObject.AddComponent<Image>().color = s.shipColor;
            }
            if (endText == null) endText = CreateLabel("EndDistance", new Vector2(0.5f, 1f), new Vector2(0f, 44f), "12.4 KM");
            if (distanceText == null) distanceText = CreateLabel("Distance", new Vector2(0.5f, 0f), new Vector2(0f, -44f), "512 M");
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        Text CreateLabel(string name, Vector2 anchor, Vector2 position, string preview)
        {
            var rect = CreateRect(name, bar, anchor, position, new Vector2(180f, 40f));
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = preview;
            return text;
        }

        static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        void Update()
        {
            if (motor == null || bar == null) return;

            // The strip is the track: bottom = start, top = end. An endless
            // track has no end to climb to — the ship stays pinned at the top.
            bool finite = gameManager != null && gameManager.HasTrackEnd;
            float remaining = finite ? gameManager.DistanceRemaining : 0f;
            float travelled = Mathf.Max(0f, motor.DisplayDistance);
            float length = travelled + remaining;
            float shipY = (finite && length > 0f ? Mathf.Clamp01(travelled / length) : 1f) * bar.rect.height;
            if (Style.showStrip) shipIcon.anchoredPosition = new Vector2(shipIcon.anchoredPosition.x, shipY);
            UpdateTrackMap(travelled);

            // Kilometres with one decimal, metres on the last one. Keyed on
            // the shown number (decametres / metres) so the string is rebuilt
            // only when it changes.
            int endKey = !finite ? -2 : remaining >= 1000f ? Mathf.RoundToInt(remaining / 100f) + 1000 : Mathf.RoundToInt(remaining);
            if (endKey != shownEnd)
            {
                shownEnd = endKey;
                endText.text = !finite ? "" : remaining >= 1000f ? $"{remaining / 1000f:0.0} KM" : $"{remaining:0} M";
            }

            // No patrol, one the end of the track took, or one mid-kill: off
            // the map altogether. The kill's teleport must not be visible here
            // either, or the map gives away that it is the same car.
            if (patrol == null || patrol.HiddenFromMap)
            {
                if (policeIcon.gameObject.activeSelf) policeIcon.gameObject.SetActive(false);
                if (mapPoliceIcon.gameObject.activeSelf) mapPoliceIcon.gameObject.SetActive(false);
                if (shownGap != -2)
                {
                    shownGap = -2;
                    distanceText.text = "";
                }
                return;
            }

            float gap = Mathf.Max(0f, patrol.GapToShip);

            // The patrol hangs under the ship on a zoomed scale (rangeMeters =
            // chaseSpan pixels). Shown only inside that range, and only while
            // that spot is still on the strip — a ship at the very bottom has
            // nothing under it to draw the patrol on.
            float policeY = shipY - gap / rangeMeters * Style.chaseSpan;
            bool visible = Style.showStrip && gap <= rangeMeters && policeY >= 0f;
            if (policeIcon.gameObject.activeSelf != visible) policeIcon.gameObject.SetActive(visible);
            if (visible) policeIcon.anchoredPosition = new Vector2(policeIcon.anchoredPosition.x, policeY);

            // On the track map the patrol is where it really is: the ship's distance minus the gap.
            bool onMap = mapPoints.Count > 1 && Style.showTrackMap;
            if (mapPoliceIcon.gameObject.activeSelf != onMap) mapPoliceIcon.gameObject.SetActive(onMap);
            if (onMap) mapPoliceIcon.anchoredPosition = MapPoint(Mathf.Max(0f, travelled - gap));

            int gapKey = Mathf.RoundToInt(gap);
            if (gapKey != shownGap)
            {
                shownGap = gapKey;
                distanceText.text = $"{gapKey} M";
            }
            distanceText.color = gap <= warnMeters ? Style.policeRed : Color.white;

            // Red/blue flicker, same cadence as the patrol's light bar — or
            // one steady colour when the blink is off.
            if (!Style.policeBlink)
            {
                SetPoliceColor(Style.policeRed);
                return;
            }
            blinkTimer += Time.deltaTime;
            if (blinkTimer >= Style.blinkInterval)
            {
                blinkTimer = 0f;
                blinkState = !blinkState;
                SetPoliceColor(blinkState ? Style.policeBlue : Style.policeRed);
            }
        }

        void SetPoliceColor(Color c)
        {
            if (policeImage != null) policeImage.color = c;
            if (mapPoliceImage != null) mapPoliceImage.color = c;
        }

        // ------------------------------------------------------------ track map
        void UpdateTrackMap(float travelled)
        {
            if (!Style.showTrackMap || mapArea == null || generator == null || generator.Track == null) return;
            TrackManager track = generator.Track;
            if (track.Revision != mapRevision || !Mathf.Approximately(track.Length, mapLength) || !Mathf.Approximately(track.EndDistance, mapEnd))
                SampleTrack(track);
            if (mapPoints.Count < 2) return;
            mapShipIcon.anchoredPosition = MapPoint(travelled);
            mapLine.SetDriven(Mathf.FloorToInt(travelled / mapStep) + 1);
        }

        // The whole track, from above: every mapStep metres of the centre line
        // on the XZ plane, turned so the start → finish runs up the map (or
        // world north up), then fitted into the rect with its aspect kept.
        // Then the markers: ramps, laser gates, the finish.
        void SampleTrack(TrackManager track)
        {
            mapRevision = track.Revision;
            mapLength = track.Length;
            mapEnd = track.EndDistance;
            mapPoints.Clear();
            if (track.Length < 1f) { mapLine.SetPoints(mapPoints); return; }

            int samples = Mathf.Max(2, Style.mapSamples);
            mapStep = track.Length / (samples - 1);
            var raw = new List<Vector2>(samples);
            for (int i = 0; i < samples; i++)
            {
                track.GetFrameAtDistance(i * mapStep, out Vector3 p, out _, out _, out _);
                raw.Add(new Vector2(p.x, p.z));
            }

            // Start → finish points up the map.
            float turn = 0f;
            Vector2 run = raw[raw.Count - 1] - raw[0];
            if (Style.mapStartAtBottom && run.sqrMagnitude > 1f) turn = Vector2.SignedAngle(run, Vector2.up);
            Quaternion rotate = Quaternion.Euler(0f, 0f, turn);
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            for (int i = 0; i < raw.Count; i++)
            {
                raw[i] = rotate * raw[i];
                min = Vector2.Min(min, raw[i]);
                max = Vector2.Max(max, raw[i]);
            }

            Rect rect = mapArea.rect;
            float usable = 1f - 2f * Style.mapPadding;
            Vector2 size = Vector2.Max(max - min, Vector2.one);
            float scale = Mathf.Min(rect.width * usable / size.x, rect.height * usable / size.y);
            Vector2 middle = (min + max) * 0.5f;
            foreach (Vector2 r in raw) mapPoints.Add((r - middle) * scale);
            mapLine.SetPoints(mapPoints);
            PlaceMarkers();
        }

        void PlaceMarkers()
        {
            int used = 0;
            foreach (TrackPlacement p in generator.Placements)
            {
                if (p.kind == TrackPlacementKind.Ramp) SetMarker(used++, p.distance, Style.rampMarkerColor, Style.rampMarkerSprite);
                else if (p.kind == TrackPlacementKind.LaserGate) SetMarker(used++, p.distance, Style.gateMarkerColor, Style.gateMarkerSprite);
            }
            if (generator.Track.HasEnd) SetMarker(used++, generator.Track.EndDistance, Style.finishMarkerColor, Style.finishMarkerSprite, 1.6f);
            for (int i = used; i < mapMarkers.Count; i++) mapMarkers[i].gameObject.SetActive(false);
            // The dots draw over the markers.
            mapPoliceIcon.SetAsLastSibling();
            mapShipIcon.SetAsLastSibling();
        }

        void SetMarker(int index, float distance, Color color, Sprite sprite, float scale = 1f)
        {
            while (mapMarkers.Count <= index)
            {
                var marker = CreateRect("Marker", mapArea, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one);
                marker.gameObject.AddComponent<Image>().raycastTarget = false;
                mapMarkers.Add(marker);
            }
            RectTransform m = mapMarkers[index];
            m.gameObject.SetActive(true);
            m.sizeDelta = Vector2.one * Style.markerSize * scale;
            m.anchoredPosition = MapPoint(distance);
            var image = m.GetComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.preserveAspect = sprite != null;
        }

        // A track distance on the map: between the two samples round it.
        Vector2 MapPoint(float distance)
        {
            float at = Mathf.Clamp(distance / mapStep, 0f, mapPoints.Count - 1);
            int i = Mathf.Min(Mathf.FloorToInt(at), mapPoints.Count - 2);
            return Vector2.Lerp(mapPoints[i], mapPoints[i + 1], at - i);
        }

        // Edit mode: a sample curve so the line's look can be judged while laying the rect out.
        void PreviewMapLine()
        {
            if (mapLine == null || mapArea == null) return;
            Rect rect = mapArea.rect;
            var preview = new List<Vector2>();
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f;
                preview.Add(new Vector2(Mathf.Sin(t * Mathf.PI * 2f) * rect.width * 0.3f, (t - 0.5f) * rect.height * 0.8f));
            }
            mapLine.SetPoints(preview);
            mapLine.SetDriven(15);
        }
    }
}
