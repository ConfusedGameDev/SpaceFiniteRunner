using UnityEngine;
using UnityEngine.UI;

using ConfusedGameDev.FiniteRunner.UI;
namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// The one-line objective readout at the top of the screen: what the
    /// active step wants right now and how close the player is — the speed
    /// to reach against the current one, the seconds left to survive, the
    /// distance to a go-to target (or a red "NO TARGET" when its id is not in
    /// the scene), or simply "escape". A dim caption counts the steps. Built
    /// from code on its own overlay canvas like the Speedometer, spawned by
    /// the LevelManager, hidden while no player car exists and once the level
    /// completes. Prefixes come from the menu text library so they follow the
    /// language setting; numbers and ids stay raw.
    /// </summary>
    public class ObjectiveHud : MonoBehaviour
    {
        const int UiLayer = 5;
        static readonly Color MissingColor = new(1f, 0.35f, 0.3f);

        LevelManager manager;
        Canvas canvas;
        Text line;
        Text caption;
        bool built;

        public static ObjectiveHud Spawn(LevelManager manager)
        {
            var go = new GameObject("ObjectiveHud");
            go.transform.SetParent(manager.transform, false);
            var hud = go.AddComponent<ObjectiveHud>();
            hud.manager = manager;
            return hud;
        }

        void Update()
        {
            if (manager == null) { Destroy(gameObject); return; }
            if (!built) Build();

            LevelDefinition level = manager.Level;
            LevelObjective step = manager.CurrentObjective;
            bool visible = manager.Player != null && !manager.Completed && level != null && step != null;
            if (canvas.enabled != visible) canvas.enabled = visible;
            if (!visible) return;

            int index = manager.CurrentIndex;
            caption.text = level.mode == CompletionMode.AllMustHold
                ? $"OBJECTIVE {index + 1}/{level.Count}  ·  HOLD ALL"
                : $"OBJECTIVE {index + 1}/{level.Count}";

            MenuTextLibrary texts = MenuTextLibrary.Load();
            Color color = step.Accent;
            string text;
            switch (step.type)
            {
                case ObjectiveType.ReachSpeed:
                    text = $"{texts.Get(MenuTextId.ObjectiveReachSpeed)}  {step.targetSpeedKmh:0} KM/H   ({manager.Player.SpeedKmh:0})";
                    break;
                case ObjectiveType.SurviveTime:
                    float left = Mathf.Max(0f, step.surviveSeconds - manager.Timer(index));
                    text = $"{texts.Get(MenuTextId.ObjectiveSurvive)}  {Mathf.CeilToInt(left)} S";
                    break;
                case ObjectiveType.GoToTarget:
                    if (manager.TryGetTargetDistance(index, out float meters))
                        text = $"{texts.Get(MenuTextId.ObjectiveGoTo)} {(step.targetId ?? "").ToUpperInvariant()}  —  {meters:0} M";
                    else
                    {
                        text = $"NO TARGET \"{step.targetId}\"";
                        color = MissingColor;
                    }
                    break;
                default:
                    text = texts.Get(MenuTextId.ObjectiveEscapePolice);
                    break;
            }
            line.text = text;
            line.color = color;
        }

        // --------------------------------------------------------------- build

        void Build()
        {
            built = true;

            canvas = new GameObject("ObjectiveCanvas").AddComponent<Canvas>();
            canvas.transform.SetParent(transform, false);
            canvas.gameObject.layer = UiLayer;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Anchored to the top edge, centered.
            var root = new GameObject("Root", typeof(RectTransform)).GetComponent<RectTransform>();
            root.gameObject.layer = UiLayer;
            root.SetParent(canvas.transform, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -28f);
            root.sizeDelta = new Vector2(1200f, 110f);

            caption = CreateText("Caption", root, 22, new Color(1f, 1f, 1f, 0.55f), FontStyle.Normal);
            caption.rectTransform.anchoredPosition = new Vector2(0f, -14f);
            line = CreateText("Line", root, 38, Color.white, FontStyle.Bold);
            line.rectTransform.anchoredPosition = new Vector2(0f, -54f);

            var shadow = line.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
            shadow.effectDistance = new Vector2(2f, -2f);
        }

        static Text CreateText(string name, Transform parent, int fontSize, Color color, FontStyle style)
        {
            var text = new GameObject(name).AddComponent<Text>();
            text.gameObject.layer = UiLayer;
            text.transform.SetParent(parent, false);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(10f, 10f); // overflow renders beyond this
            return text;
        }
    }
}
