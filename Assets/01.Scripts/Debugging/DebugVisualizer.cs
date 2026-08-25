using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Debugging
{
    /// <summary>
    /// Base for every development overlay: owns a <see cref="DebugLineBuffer"/>,
    /// rebuilds it once per frame from live gameplay data and renders it for
    /// every camera. Subclasses only implement <see cref="Rebuild"/> — the
    /// gating, the registration with <see cref="DebugManager"/> and the
    /// rendering are handled here so a new overlay cannot forget the rule that
    /// debug never draws in a build.
    ///
    /// The rebuild runs in LateUpdate on purpose: the AI decides in Update, so
    /// a frame drawn any earlier shows last frame's plan and last frame's
    /// probes — exactly the off-by-one that makes a debug view lie.
    /// </summary>
    public abstract class DebugVisualizer : MonoBehaviour
    {
        [TitleGroup("Draw")]
        [Tooltip("Draw through geometry. Off, the overlay is hidden behind buildings like a real object.")]
        public bool xray = true;

        [TitleGroup("Draw")]
        [Tooltip("Rebuild the overlay at most this often. 0 = every frame.")]
        [PropertyRange(0f, 1f)]
        public float refreshInterval;

        [TitleGroup("Draw"), ShowInInspector, ReadOnly]
        [Tooltip("Line segments in the current buffer — the overlay's cost, at a glance.")]
        public int SegmentCount => Lines.Count;

        protected readonly DebugLineBuffer Lines = new();

        float nextRebuild;

        /// <summary>The <see cref="DebugManager"/> channel this overlay belongs to.</summary>
        protected abstract bool ChannelEnabled { get; }

        /// <summary>Fill <see cref="Lines"/> from live data. Called with the buffer already cleared.</summary>
        protected abstract void Rebuild();

        protected virtual void Awake() => DebugManager.Register(this);

        protected virtual void OnDestroy() => DebugManager.Unregister(this);

        protected virtual void OnDisable() => Lines.Clear();

        void LateUpdate()
        {
            if (!DebugManager.IsDebug)
            {
                enabled = false; // the manager re-enables when the master switch comes back
                Lines.Clear();
                return;
            }
            if (!ChannelEnabled)
            {
                Lines.Clear();
                return;
            }
            if (refreshInterval > 0f)
            {
                if (Time.unscaledTime < nextRebuild) return;
                nextRebuild = Time.unscaledTime + refreshInterval;
            }
            Lines.Clear();
            Rebuild();
        }

        // Once per rendering camera, so the same overlay lands in the Game
        // view and the Scene view. Preview and reflection cameras are skipped:
        // an asset thumbnail must not pick up a debug line.
        void OnRenderObject()
        {
            if (Lines.Count == 0) return;
            Camera camera = Camera.current;
            if (camera == null) return;
            if (camera.cameraType is CameraType.Preview or CameraType.Reflection) return;
            Lines.Render(xray);
        }
    }
}
