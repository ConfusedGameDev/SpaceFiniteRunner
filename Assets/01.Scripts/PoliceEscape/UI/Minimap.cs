using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.AI;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// GTA-style circular radar in the bottom-right corner: a top-down
    /// orthographic camera renders the actual city into a render texture,
    /// shown through a circular mask. The camera follows the player and (by
    /// default) rotates with them, so up is always "ahead"; the player is a
    /// centered arrow, police are blips colored by AI state (Chase flashes
    /// red/blue), clamped to the rim when beyond radar range. Built entirely
    /// from code on its own overlay canvas — no scene wiring, no layers, no
    /// fonts; sprites are generated at runtime. CityManager spawns it when
    /// its minimap settings field is assigned; hides itself while no player
    /// car exists. All look/feel knobs live on MinimapSettings.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        const int UiLayer = 5;

        [Required, InlineEditor]
        [Tooltip("All radar tunables live on this asset — add new knobs there, not here.")]
        public MinimapSettings settings;

        Canvas canvas;
        Camera mapCamera;
        RenderTexture mapTexture;
        RectTransform blipRoot;
        RectTransform playerArrow;
        Sprite circleSprite;
        readonly List<Image> blips = new();
        readonly List<Image> routeDots = new();
        RectTransform routeRoot;
        readonly List<PoliceCarInput> police = new();
        CarController player;
        float refreshTimer;
        bool built;

        void LateUpdate()
        {
            if (settings == null) return;
            if (!built) Build();

            RefreshTargets();
            bool visible = player != null;
            if (canvas.enabled != visible)
            {
                canvas.enabled = visible;
                mapCamera.enabled = visible;
            }
            if (!visible) return;

            UpdateCamera();
            UpdateRoute();
            UpdateBlips();
        }

        void OnDestroy()
        {
            if (mapTexture == null) return;
            mapTexture.Release();
            Destroy(mapTexture);
        }

        // --------------------------------------------------------------- build

        /// <summary>Editor bake: regenerates the radar chrome preview so the prefab shows before play (the map texture itself is runtime-only).</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            Build();
            canvas.enabled = true;
            mapCamera.enabled = false; // no stray fullscreen render in edit mode
        }

        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            if (mapTexture != null) { mapTexture.Release(); Kill(mapTexture); mapTexture = null; }
            canvas = null;
            mapCamera = null;
            blipRoot = playerArrow = routeRoot = null;
            blips.Clear();
            routeDots.Clear();
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void Build()
        {
            TearDown();
            built = true;
            circleSprite = CreateCircleSprite(128);
            float size = settings.sizePixels;

            canvas = new GameObject("MinimapCanvas").AddComponent<Canvas>();
            canvas.transform.SetParent(transform, false);
            canvas.gameObject.layer = UiLayer;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Anchored to the bottom-right corner.
            RectTransform root = CreateRect("Root", canvas.transform, new Vector2(size, size));
            root.anchorMin = root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(1f, 0f);
            root.anchoredPosition = new Vector2(-settings.marginPixels, settings.marginPixels);

            // Ring behind the map circle.
            CreateImage("Border", root, circleSprite, settings.borderColor,
                new Vector2(size + settings.borderWidth * 2f, size + settings.borderWidth * 2f));

            // Circular mask with the rendered map inside.
            Image maskImage = CreateImage("MapMask", root, circleSprite, Color.white, new Vector2(size, size));
            maskImage.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            mapTexture = new RenderTexture(settings.renderTextureSize, settings.renderTextureSize, 16)
            {
                name = "MinimapRT",
            };
            var map = new GameObject("Map").AddComponent<RawImage>();
            map.transform.SetParent(maskImage.transform, false);
            map.gameObject.layer = UiLayer;
            map.texture = mapTexture;
            map.raycastTarget = false;
            CenterRect(map.rectTransform, new Vector2(size, size));

            routeRoot = CreateRect("Route", maskImage.transform, new Vector2(size, size));
            blipRoot = CreateRect("Blips", maskImage.transform, new Vector2(size, size));

            Image arrow = CreateImage("PlayerArrow", maskImage.transform, CreateArrowSprite(64), settings.playerColor,
                new Vector2(settings.playerArrowSize * 0.8f, settings.playerArrowSize));
            playerArrow = arrow.rectTransform;

            // The radar camera: straight down, orthographic, into the RT.
            var cameraGo = new GameObject("MinimapCamera");
            cameraGo.transform.SetParent(transform, false);
            mapCamera = cameraGo.AddComponent<Camera>();
            mapCamera.orthographic = true;
            mapCamera.orthographicSize = settings.viewRadius;
            mapCamera.targetTexture = mapTexture;
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.backgroundColor = settings.backgroundColor;
            mapCamera.cullingMask = ~(1 << UiLayer);
            mapCamera.nearClipPlane = 1f;
            mapCamera.farClipPlane = settings.cameraHeight * 2f + 50f;
            mapCamera.allowHDR = false;
            mapCamera.allowMSAA = false;
        }

        // -------------------------------------------------------------- update

        void RefreshTargets()
        {
            refreshTimer -= Time.deltaTime;
            if (refreshTimer > 0f && player != null) return;
            refreshTimer = 1f;
            player = PatrolManager.FindPlayerCar();
            police.Clear();
            police.AddRange(FindObjectsByType<PoliceCarInput>(FindObjectsSortMode.None));
        }

        void UpdateCamera()
        {
            Transform target = player.transform;
            float yaw = settings.rotateWithPlayer ? target.eulerAngles.y : 0f;
            mapCamera.transform.SetPositionAndRotation(
                target.position + Vector3.up * settings.cameraHeight,
                Quaternion.Euler(90f, yaw, 0f));
            mapCamera.orthographicSize = settings.viewRadius; // live-tunable
            mapCamera.backgroundColor = settings.backgroundColor;

            // Rotating map = fixed arrow; fixed map = the arrow shows heading.
            playerArrow.localEulerAngles = settings.rotateWithPlayer
                ? Vector3.zero
                : new Vector3(0f, 0f, -player.transform.eulerAngles.y);
        }

        void UpdateBlips()
        {
            float uiRadius = settings.sizePixels * 0.5f;
            float scale = uiRadius / Mathf.Max(1f, settings.viewRadius);

            // Radar axes: with rotation, up = player forward; else up = world +Z.
            Vector3 forward = settings.rotateWithPlayer ? player.transform.forward : Vector3.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            Vector3 right = new(forward.z, 0f, -forward.x);

            bool flashA = Mathf.FloorToInt(Time.time / settings.chaseFlashInterval) % 2 == 0;

            int used = 0;
            foreach (PoliceCarInput cruiser in police)
            {
                if (cruiser == null) continue;
                Image blip = GetBlip(used++);

                Vector3 delta = cruiser.transform.position - player.transform.position;
                var position = new Vector2(Vector3.Dot(delta, right), Vector3.Dot(delta, forward)) * scale;
                float maxRadius = uiRadius * 0.92f;
                if (position.sqrMagnitude > maxRadius * maxRadius)
                    position = position.normalized * maxRadius; // clamp to the rim, GTA-style

                blip.rectTransform.anchoredPosition = position;
                blip.rectTransform.sizeDelta = new Vector2(settings.blipSize, settings.blipSize);
                blip.color = cruiser.State switch
                {
                    PoliceCarInput.AiState.Chase => flashA ? settings.chaseColorA : settings.chaseColorB,
                    PoliceCarInput.AiState.Search => settings.searchColor,
                    _ => settings.patrolColor,
                };
                blip.enabled = true;
            }
            for (int i = used; i < blips.Count; i++) blips[i].enabled = false;
        }

        /// <summary>
        /// Draw the map's route as a dotted line on the radar, using exactly
        /// the same projection as the police blips — the route has to sit on
        /// the streets under it, so it must rotate with the map and clamp to
        /// the rim the same way. Read from the shared
        /// <see cref="MapRoute.Current"/>, so the radar never needs a reference
        /// to the map screen (which only exists while it is open).
        ///
        /// Dots are resampled at a fixed spacing in metres rather than one per
        /// path node: nodes are one per cell, so at radar scale they would
        /// bunch into a blob, and a long route would spawn hundreds of images.
        /// </summary>
        void UpdateRoute()
        {
            MapRoute route = MapRoute.Current;
            int used = 0;

            if (route != null && route.HasRoute)
            {
                float uiRadius = settings.sizePixels * 0.5f;
                float scale = uiRadius / Mathf.Max(1f, settings.viewRadius);
                float maxRadius = uiRadius * 0.92f;

                Vector3 forward = settings.rotateWithPlayer ? player.transform.forward : Vector3.forward;
                forward.y = 0f;
                forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
                Vector3 right = new(forward.z, 0f, -forward.x);
                Vector3 origin = player.transform.position;

                float spacing = Mathf.Max(1f, settings.routeDotSpacing);
                IReadOnlyList<Vector3> points = route.Points;
                float carry = 0f;

                for (int i = 1; i < points.Count && used < settings.routeMaxDots; i++)
                {
                    Vector3 a = points[i - 1];
                    Vector3 b = points[i];
                    Vector3 segment = b - a;
                    segment.y = 0f;
                    float length = segment.magnitude;
                    if (length < 0.0001f) continue;
                    Vector3 step = segment / length;

                    for (float travelled = carry; travelled < length && used < settings.routeMaxDots; travelled += spacing)
                    {
                        Vector3 world = a + step * travelled;
                        Vector3 delta = world - origin;
                        var position = new Vector2(Vector3.Dot(delta, right), Vector3.Dot(delta, forward)) * scale;
                        if (position.sqrMagnitude > maxRadius * maxRadius) continue;   // off-radar, skip rather than pile on the rim

                        Image dot = GetRouteDot(used++);
                        dot.rectTransform.anchoredPosition = position;
                        dot.rectTransform.sizeDelta = new Vector2(settings.routeDotSize, settings.routeDotSize);
                        dot.color = settings.routeColor;
                        dot.enabled = true;
                    }
                    carry = Mathf.Repeat(carry - length, spacing);
                }
            }

            for (int i = used; i < routeDots.Count; i++) routeDots[i].enabled = false;
        }

        Image GetRouteDot(int index)
        {
            while (routeDots.Count <= index)
                routeDots.Add(CreateImage($"Route_{routeDots.Count}", routeRoot, circleSprite, Color.white,
                    new Vector2(settings.routeDotSize, settings.routeDotSize)));
            return routeDots[index];
        }

        Image GetBlip(int index)
        {
            while (blips.Count <= index)
                blips.Add(CreateImage($"Blip_{blips.Count}", blipRoot, circleSprite, Color.white,
                    new Vector2(settings.blipSize, settings.blipSize)));
            return blips[index];
        }

        // ---------------------------------------------------------- UI helpers

        static RectTransform CreateRect(string name, Transform parent, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = UiLayer;
            rect.SetParent(parent, false);
            CenterRect(rect, size);
            return rect;
        }

        static Image CreateImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
        {
            var image = new GameObject(name).AddComponent<Image>();
            image.gameObject.layer = UiLayer;
            image.transform.SetParent(parent, false);
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            CenterRect(image.rectTransform, size);
            return image;
        }

        static void CenterRect(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }

        // ------------------------------------------------------------- sprites

        /// <summary>Anti-aliased white disc — the mask, the border ring and every blip reuse it tinted.</summary>
        static Sprite CreateCircleSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
            float radius = size * 0.5f - 1f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                byte alpha = (byte)(255f * Mathf.Clamp01(radius - distance + 0.5f));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>White triangle pointing up — the player arrow.</summary>
        static Sprite CreateArrowSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var pixels = new Color32[size * size];
            float centerX = size * 0.5f - 0.5f;
            for (int y = 0; y < size; y++)
            {
                float y01 = y / (size - 1f);                       // 0 = base, 1 = tip
                float halfWidth = (1f - y01) * size * 0.42f;
                for (int x = 0; x < size; x++)
                {
                    byte alpha = (byte)(255f * Mathf.Clamp01(halfWidth - Mathf.Abs(x - centerX) + 0.5f));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
