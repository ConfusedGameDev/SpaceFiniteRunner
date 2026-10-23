using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// The debug PATROL CAM: a picture-in-picture in the HUD's top-left corner
    /// showing a camera that follows the patrol cruiser, toggled with F2
    /// (read raw, like a menu chord — a debug key, never a gameplay binding).
    /// With no patrol to film — the chase switched off (the GameManager hands
    /// in null), the cruiser gone off the end of the track or hidden across a
    /// kill — the plate shows the NO SIGNAL dead screen instead. The camera
    /// renders into its own RenderTexture and is switched OFF whenever the
    /// feed is hidden or dead, so the feed costs nothing unless it is watched.
    /// The camera is bolted to the cruiser's ROOT (the flight line, not the
    /// bobbing visual) and only its heading is smoothed: a lagged position
    /// would trail a kilometre behind at Light Speed. Debug only — a release
    /// build never shows it. The dash prompt's pattern: hand-placed in
    /// <c>PF_UI</c>, <see cref="Spawn"/> finds and binds it and builds the UI
    /// from code on its own overlay canvas (sorting 11, above the HUD, under
    /// the prompts). All knobs live on <see cref="PatrolCamSettings"/>.
    /// </summary>
    public class PatrolCamFeed : MonoBehaviour
    {
        [Tooltip("All patrol cam knobs live on this asset — add new ones there, not here.")]
        [SerializeField, Required, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        PatrolCamSettings settings;

        [Tooltip("Menu theme for fonts and colours; empty falls back to the Resources default.")]
        [SerializeField, InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        MenuTheme themeOverride;

        PolicePatrol patrol;   // null = no chase this run: the plate reads NO SIGNAL
        MenuTheme theme;
        bool bound;
        bool visible;
        bool hadSignal;
        Quaternion heading;    // the cruiser's heading as the camera follows it, smoothed

        RectTransform plate;
        RectTransform backing;
        Image frame;
        RawImage picture;
        RawImage staticNoise;
        RawImage noSignalImage;
        Text noSignalText;
        Text caption;
        Camera feedCamera;
        RenderTexture texture;
        Texture2D noise;

        const float TagHeight = 26f;

        PatrolCamSettings Settings => settings != null ? settings : settings = ScriptableObject.CreateInstance<PatrolCamSettings>();

        /// <summary>True while the feed is up (F2).</summary>
        public bool Visible => visible;

        /// <summary>
        /// Finds the hand-placed feed (or makes one), binds it to the run's
        /// patrol — null when the chase is off — and builds its UI. Starts
        /// hidden unless the settings say otherwise.
        /// </summary>
        public static PatrolCamFeed Spawn(PolicePatrol patrol)
        {
            var feed = FindFirstObjectByType<PatrolCamFeed>(FindObjectsInactive.Include);
            if (feed == null) feed = new GameObject("PatrolCam").AddComponent<PatrolCamFeed>();
            if (!feed.gameObject.activeSelf) feed.gameObject.SetActive(true);
            feed.patrol = patrol;
            feed.bound = true;
            feed.Build();
            feed.SetVisible(feed.Settings.startVisible);
            return feed;
        }

        /// <summary>Show or hide the feed; F2 flips it.</summary>
        public void SetVisible(bool show)
        {
            visible = show && Debug.isDebugBuild;
            hadSignal = false; // re-seat the heading on the next signal frame
            if (plate != null) plate.gameObject.SetActive(visible);
            if (feedCamera != null && !visible) feedCamera.enabled = false;
        }

        void Awake()
        {
            // Scene-placed instance whose Spawn never came (no runner here):
            // nothing to film and nothing to toggle, so it stays empty.
            if (!bound) TearDown();
        }

        void OnDestroy()
        {
            // The children go with the object; only the textures would leak.
            if (texture != null) texture.Release();
            Kill(texture);
            Kill(noise);
        }

        /// <summary>Editor bake: builds the feed so the prefab shows its plate before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            Build();
            plate.gameObject.SetActive(true);
            ShowSignal(false);
        }

        bool HasSignal => patrol != null && patrol.isActiveAndEnabled && !patrol.HiddenFromMap;

        void Update()
        {
            if (!bound || plate == null) return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame) SetVisible(!visible);
            if (!visible) return;

            bool signal = HasSignal;
            ShowSignal(signal);
            if (signal)
                caption.text = $"PATROL CAM  P{patrol.PatrolNumber}  {patrol.EncounterState}";
            else
                ScrollStatic();
            hadSignal = signal;
        }

        void LateUpdate()
        {
            // After the patrol's Update has rendered its pose for this frame.
            if (!visible || feedCamera == null || !feedCamera.enabled || patrol == null) return;

            Transform root = patrol.transform;
            var s = Settings;
            if (!hadSignal) heading = root.rotation;
            else heading = Quaternion.Slerp(heading, root.rotation,
                                            1f - Mathf.Exp(-s.followSharpness * Time.unscaledDeltaTime));

            Vector3 pos = root.position + heading * s.followOffset;
            Vector3 aim = root.position + heading * (Vector3.up * s.lookHeight);
            Vector3 look = aim - pos;
            if (look.sqrMagnitude < 0.0001f) look = heading * Vector3.forward;
            feedCamera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look, heading * Vector3.up));
            feedCamera.fieldOfView = s.fieldOfView;
            feedCamera.nearClipPlane = s.nearClip;
            feedCamera.farClipPlane = s.farClip;
        }

        void ShowSignal(bool signal)
        {
            var s = Settings;
            ApplyLayout();
            picture.enabled = signal;
            if (feedCamera != null) feedCamera.enabled = signal && visible && Application.isPlaying;

            bool image = !signal && s.noSignalImage != null;
            noSignalImage.texture = s.noSignalImage;
            noSignalImage.enabled = image;
            noSignalText.enabled = !signal && !image;
            staticNoise.enabled = !signal && !image && s.staticStrength > 0f;
            staticNoise.color = new Color(1f, 1f, 1f, s.staticStrength);
            if (!signal) caption.text = "PATROL CAM";
        }

        // The static is one small noise tile, jumped to a random offset every
        // frame: the dead screen of a CRT, for the price of a uvRect.
        void ScrollStatic()
        {
            if (!staticNoise.enabled) return;
            Rect r = staticNoise.uvRect;
            r.x = Random.value;
            r.y = Random.value;
            staticNoise.uvRect = r;
        }

        // The plate's rect follows the settings live: anchored top-left, the
        // picture inset by the frame, the tag under it.
        void ApplyLayout()
        {
            var s = Settings;
            plate.anchoredPosition = new Vector2(s.margin, -s.margin);
            plate.sizeDelta = new Vector2(s.width, s.height);
            frame.color = s.frameColor;
            var inner = new Vector2(-s.frameWidth * 2f, -s.frameWidth * 2f);
            backing.sizeDelta = inner;
            ((RectTransform)picture.transform).sizeDelta = inner;
            ((RectTransform)staticNoise.transform).sizeDelta = inner;
            ((RectTransform)noSignalImage.transform).sizeDelta = inner;
            staticNoise.uvRect = new Rect(staticNoise.uvRect.position,
                                          new Vector2(s.width / (noise.width * 3f), s.height / (noise.height * 3f)));
        }

        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            plate = backing = null;
            frame = null;
            picture = staticNoise = noSignalImage = null;
            noSignalText = caption = null;
            feedCamera = null;
            if (texture != null)
            {
                texture.Release();
                Kill(texture);
                texture = null;
            }
            if (noise != null)
            {
                Kill(noise);
                noise = null;
            }
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static RawImage MakeRaw(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            var raw = go.AddComponent<RawImage>();
            raw.raycastTarget = false;
            return raw;
        }

        void Build()
        {
            TearDown();
            var s = Settings;
            theme = themeOverride != null ? themeOverride : MenuTheme.Load();

            var canvas = GetOrAdd<Canvas>(gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 11; // above the HUD (10), under the dash / duel prompts (12)

            var scaler = GetOrAdd<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // The frame IS the plate: a flat image the picture sits inset in.
            frame = MenuScreen.MakeImage("Feed", transform, Vector2.zero, new Vector2(s.width, s.height), null, s.frameColor);
            plate = frame.rectTransform;
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(0f, 1f);

            // The dead screen's dark backing, under everything else.
            backing = MenuScreen.MakeImage("Backing", plate, Vector2.zero, Vector2.zero, null, new Color(0.01f, 0.02f, 0.03f, 0.95f))
                .rectTransform;
            backing.anchorMin = Vector2.zero;
            backing.anchorMax = Vector2.one;

            texture = new RenderTexture(Mathf.Max(16, s.textureWidth), Mathf.Max(16, s.textureHeight), 24)
            {
                name = "PatrolCamFeed",
            };
            picture = MakeRaw("Picture", plate);
            picture.texture = texture;
            ((RectTransform)picture.transform).anchorMin = Vector2.zero;
            ((RectTransform)picture.transform).anchorMax = Vector2.one;

            noise = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                name = "PatrolCamStatic",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            var pixels = new Color32[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte v = (byte)Random.Range(0, 256);
                pixels[i] = new Color32(v, v, v, 255);
            }
            noise.SetPixels32(pixels);
            noise.Apply();
            staticNoise = MakeRaw("Static", plate);
            staticNoise.texture = noise;
            ((RectTransform)staticNoise.transform).anchorMin = Vector2.zero;
            ((RectTransform)staticNoise.transform).anchorMax = Vector2.one;

            noSignalImage = MakeRaw("NoSignalImage", plate);
            ((RectTransform)noSignalImage.transform).anchorMin = Vector2.zero;
            ((RectTransform)noSignalImage.transform).anchorMax = Vector2.one;

            noSignalText = MenuScreen.MakeText("NoSignal", plate, Vector2.zero, new Vector2(s.width, 60f),
                                               "— NO SIGNAL —", 30, theme.TextDim, theme.BodyFont, TextAnchor.MiddleCenter);

            caption = MenuScreen.MakeText("Tag", plate, Vector2.zero, new Vector2(s.width, TagHeight),
                                      "PATROL CAM", 18, s.frameColor, theme.BodyFont, TextAnchor.UpperLeft);
            var tagRect = caption.rectTransform;
            tagRect.anchorMin = tagRect.anchorMax = tagRect.pivot = new Vector2(0f, 1f);
            tagRect.anchoredPosition = new Vector2(s.frameWidth + 8f, -s.frameWidth - 6f);
            var outline = caption.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);

            var camGo = new GameObject("PatrolCamera");
            camGo.transform.SetParent(transform, false);
            feedCamera = camGo.AddComponent<Camera>();
            feedCamera.targetTexture = texture;
            feedCamera.enabled = false;
            var urp = feedCamera.GetUniversalAdditionalCameraData();
            urp.renderPostProcessing = false;
            urp.renderShadows = false;
        }
    }
}
