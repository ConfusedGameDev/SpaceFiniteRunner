using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Debugging
{
    /// <summary>
    /// The one switch every development overlay hangs off. <see cref="isDebug"/>
    /// is on by default so a visualizer is there the moment it is needed, and
    /// the rule that keeps that safe is enforced here rather than trusted to
    /// each overlay: <b>debug only exists in the editor</b>. Outside it
    /// (<see cref="Application.isEditor"/> false) the flag is forced off, the
    /// manager is never auto-created, and every registered
    /// <see cref="DebugVisualizer"/> is disabled — so a shipped build cannot
    /// draw an overlay even if a debug object was left in a scene.
    ///
    /// Auto-created on first use like the other singletons in the project, so
    /// no scene wiring is required; a hand-placed one always wins, since that
    /// is the copy a designer left their toggles on. The channel bools below
    /// are what the individual overlays read, so a single object turns the
    /// road graph off while paths stay on.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public class DebugManager : MonoBehaviour
    {
        [TitleGroup("Global")]
        [Tooltip("Master switch for every debug visualizer. Forced OFF outside the editor — a build never draws debug.")]
        public bool isDebug = true;

        [TitleGroup("Channels")]
        [Tooltip("Draw the streamed road graph the AI navigates on (nodes, connections, ramps and decks).")]
        public bool showRoadGraph = true;

        [TitleGroup("Channels")]
        [Tooltip("Draw the selected AI car's planned route and the point it is actually steering at.")]
        public bool showCarPaths = true;

        [TitleGroup("Channels")]
        [Tooltip("Draw the AI's collision-prevention probes: forward rays, fender rays, yield whiskers and what they hit.")]
        public bool showCollisionProbes = true;

        [TitleGroup("Channels")]
        [Tooltip("Draw the police line-of-sight ray to the player — the test that starts and ends a chase.")]
        public bool showPerception = true;

        [TitleGroup("Channels")]
        [Tooltip("Fill every dialogue trigger's volume with a translucent orange box, dimmed while it is on cooldown.")]
        public bool showDialogueTriggers = true;

        static DebugManager instance;
        static readonly List<DebugVisualizer> visualizers = new();
        static bool quitting;

        // Creating a GameObject while the player is shutting down throws; a
        // driver polling a channel on its last frame must not be the thing
        // that fills the console with errors.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            quitting = false;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        static void OnQuitting() => quitting = true;

        bool autoCreated;
        bool appliedDebug;

        /// <summary>
        /// The manager, auto-created on first use if the scene has none — an
        /// overlay must work in a scene nobody prepared. Never creates
        /// anything outside the editor, and never outside play mode (edit-mode
        /// creation would dirty the open scene for nothing).
        /// </summary>
        public static DebugManager Instance
        {
            get
            {
                if (instance != null) return instance;
                if (!Application.isEditor || quitting) return null;
                instance = FindAnyObjectByType<DebugManager>(FindObjectsInactive.Include);
                if (instance != null || !Application.isPlaying) return instance;

                var go = new GameObject(nameof(DebugManager));
                instance = go.AddComponent<DebugManager>();
                instance.autoCreated = true;
                DontDestroyOnLoad(go);
                return instance;
            }
        }

        /// <summary>Master gate. False in a build, always — overlays call this before drawing anything.</summary>
        public static bool IsDebug => Application.isEditor && Instance != null && Instance.isDebug;

        public static bool ShowRoadGraph => IsDebug && instance.showRoadGraph;
        public static bool ShowCarPaths => IsDebug && instance.showCarPaths;
        public static bool ShowCollisionProbes => IsDebug && instance.showCollisionProbes;
        public static bool ShowPerception => IsDebug && instance.showPerception;
        public static bool ShowDialogueTriggers => IsDebug && instance.showDialogueTriggers;

        /// <summary>Every visualizer alive right now, enabled or not — the manager owns their enabled state.</summary>
        public static IReadOnlyList<DebugVisualizer> Visualizers => visualizers;

        internal static void Register(DebugVisualizer visualizer)
        {
            if (visualizer == null || visualizers.Contains(visualizer)) return;
            visualizers.Add(visualizer);
            visualizer.enabled = IsDebug;
        }

        internal static void Unregister(DebugVisualizer visualizer) => visualizers.Remove(visualizer);

        // A hand-placed manager beats the auto-created stub: it is the one
        // carrying the toggles someone set in the inspector.
        void Awake()
        {
            if (instance != null && instance != this)
            {
                if (!instance.autoCreated)
                {
                    Destroy(this);
                    return;
                }
                Destroy(instance.gameObject);
            }
            instance = this;
            EnforceEditorOnly();
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
            ApplyToVisualizers();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        // Watched rather than event-driven so the inspector checkbox works
        // mid-play, which is the only way anyone actually uses it.
        void Update()
        {
            EnforceEditorOnly();
            if (appliedDebug != isDebug) ApplyToVisualizers();
        }

        void OnValidate() => EnforceEditorOnly();

        /// <summary>The rule: no debug outside the editor. Cheap enough to re-assert every frame.</summary>
        void EnforceEditorOnly()
        {
            if (!Application.isEditor) isDebug = false;
        }

        /// <summary>Push the master switch onto every visualizer — off means off, wherever it was spawned from.</summary>
        [TitleGroup("Global"), Button("Apply To Visualizers")]
        public void ApplyToVisualizers()
        {
            appliedDebug = isDebug;
            for (int i = visualizers.Count - 1; i >= 0; i--)
            {
                DebugVisualizer visualizer = visualizers[i];
                if (visualizer == null)
                {
                    visualizers.RemoveAt(i);
                    continue;
                }
                visualizer.enabled = isDebug;
            }
        }
    }
}
