using ConfusedGameDev.FiniteRunner.Collectibles;
using ConfusedGameDev.FiniteRunner.SaveData;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// The money counter in the top-right corner, shared by both games: one
    /// legacy-font label on its own overlay canvas (HUD tier, sorting 10 —
    /// the city gauges' recipe) showing this run's pickups as
    /// <c>$1,234</c> (<see cref="StatFormat.Money"/>). It listens to
    /// <see cref="CollectibleManager.MoneyChanged"/>, counts the displayed
    /// number up toward the run total and punches its scale on every pickup;
    /// it hides while the RPG dialogue box asks the gauges to step aside
    /// (<see cref="RpgMessageSystem.HudSuppressed"/>). A hand-placed
    /// scene-lifetime system like the manager it reads (a root object in the
    /// runner scene, under <c>===SYSTEMS===</c> in the city) — the canvas is
    /// built under it at play and never saved. All knobs live here since
    /// there is one label to tune.
    /// The Store reuses it as the wallet: with a <see cref="ValueSource"/>
    /// set it ignores the collectible manager, follows that value in BOTH
    /// directions (a purchase counts the money down instead of snapping) and
    /// takes its punch from <see cref="Punch"/>.
    /// </summary>
    public class MoneyHud : MonoBehaviour
    {
        const int UiLayer = 5;

        [Tooltip("Pixels from the top-right corner at 1920×1080.")]
        [SerializeField] Vector2 offset = new(-60f, -40f);

        [PropertyRange(16, 120)]
        [SerializeField] int fontSize = 44;

        [SerializeField] Color color = new(1f, 0.85f, 0.3f);

        [Tooltip("Scale the label jumps to on a pickup before settling back.")]
        [PropertyRange(1f, 2f)]
        [SerializeField] float punchScale = 1.25f;

        [Tooltip("How fast the punch settles (per second).")]
        [PropertyRange(1f, 20f)]
        [SerializeField] float punchDecay = 6f;

        [Tooltip("How fast the shown number catches up with the real total (exponential; higher = snappier).")]
        [PropertyRange(1f, 30f)]
        [SerializeField] float countUpSharpness = 10f;

        Canvas canvas;
        Text label;
        double displayed;
        long target;
        float pulse = 1f;
        bool built;

        /// <summary>
        /// Optional value to show instead of the run's pickups (the Store's
        /// wallet). While set, the collectible events are ignored and the
        /// number counts toward the value both up and down.
        /// </summary>
        public System.Func<long> ValueSource { get; set; }

        /// <summary>Kicks the label's scale — the Store's purchase feedback. 0 = the inspector's punch.</summary>
        public void Punch(float scale = 0f) => pulse = scale > 1f ? scale : punchScale;

        void OnEnable()
        {
            CollectibleManager.MoneyChanged += OnMoneyChanged;
        }

        void OnDisable()
        {
            CollectibleManager.MoneyChanged -= OnMoneyChanged;
        }

        void OnMoneyChanged(int total, int delta)
        {
            if (ValueSource != null) return; // the wallet follows its own source
            target = total;
            if (delta > 0) pulse = punchScale;
            else displayed = total; // a reset snaps, it never counts down
        }

        void Update()
        {
            if (!built) Build();

            // HudSuppressed: the dialogue box can ask the gauges to step aside while it talks.
            bool visible = !RpgMessageSystem.HudSuppressed;
            if (canvas.enabled != visible) canvas.enabled = visible;
            if (!visible) return;

            if (ValueSource != null)
            {
                target = ValueSource();
            }
            else if (Application.isPlaying)
            {
                CollectibleManager manager = CollectibleManager.Instance;
                if (manager != null) target = manager.RunMoney;
            }

            displayed += (target - displayed) * (1.0 - System.Math.Exp(-countUpSharpness * Time.unscaledDeltaTime));
            if (System.Math.Abs(target - displayed) < 0.5) displayed = target;
            pulse = Mathf.MoveTowards(pulse, 1f, punchDecay * Time.unscaledDeltaTime);

            label.text = StatFormat.Money((long)System.Math.Round(displayed));
            label.color = color;
            label.rectTransform.localScale = Vector3.one * pulse;
        }

        // --------------------------------------------------------------- build

        /// <summary>Editor bake: shows a sample amount so the corner can be judged before play.</summary>
        [Button("Rebuild Preview", ButtonSizes.Large), GUIColor(0.6f, 1f, 0.6f)]
        public void RebuildPreview()
        {
            Build();
            canvas.enabled = true;
            label.text = StatFormat.Money(1234);
        }

        void TearDown()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            canvas = null;
            label = null;
        }

        void Build()
        {
            TearDown();
            built = true;

            canvas = new GameObject("MoneyHudCanvas").AddComponent<Canvas>();
            canvas.transform.SetParent(transform, false);
            canvas.gameObject.layer = UiLayer;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10; // HUD tier — below the RPG messages (15) and the pause menu (20)
            var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            label = new GameObject("Money").AddComponent<Text>();
            label.gameObject.layer = UiLayer;
            label.transform.SetParent(canvas.transform, false);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Bold;
            label.color = color;
            label.alignment = TextAnchor.UpperRight;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.text = StatFormat.Money(0);
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(2f, -2f);

            // Anchored to the top-right corner; the pivot sits there too so the
            // punch scales the label into the screen, never off it.
            RectTransform rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(400f, fontSize * 1.4f);
        }
    }
}
