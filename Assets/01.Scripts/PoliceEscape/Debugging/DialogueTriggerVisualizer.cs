using ConfusedGameDev.FiniteRunner.Debugging;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Cinema;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Debugging
{
    /// <summary>
    /// Fills every live story-trigger volume — <see cref="DialogueTrigger"/>
    /// in translucent orange, <see cref="CinemaTrigger"/> in blue, <see cref="ChallengeTrigger"/> in green — with a
    /// box (plus outline) so triggers can be seen and driven through in the
    /// Game view while playing — a Gizmo can't do that. A trigger on
    /// cooldown draws dimmed, so "why didn't it fire" answers itself at a
    /// glance. Installed by the first trigger of either kind that wakes up
    /// (see <see cref="EnsureInstalled"/>), gated by
    /// <see cref="DebugManager.ShowDialogueTriggers"/>, and — like every
    /// <see cref="DebugVisualizer"/> — impossible to draw in a build.
    /// Box colliders draw oriented; any other collider shape falls back to
    /// its world-space bounds.
    /// </summary>
    public class DialogueTriggerVisualizer : DebugVisualizer
    {
        static readonly Color ReadyFill = new(1f, 0.55f, 0.1f, 0.18f);
        static readonly Color ReadyEdge = new(1f, 0.6f, 0.15f, 0.9f);
        static readonly Color CoolingFill = new(1f, 0.55f, 0.1f, 0.05f);
        static readonly Color CoolingEdge = new(1f, 0.6f, 0.15f, 0.3f);
        static readonly Color CinemaReadyFill = new(0.2f, 0.6f, 1f, 0.18f);
        static readonly Color CinemaReadyEdge = new(0.3f, 0.65f, 1f, 0.9f);
        static readonly Color CinemaCoolingFill = new(0.2f, 0.6f, 1f, 0.05f);
        static readonly Color CinemaCoolingEdge = new(0.3f, 0.65f, 1f, 0.3f);
        static readonly Color ChallengeFill = new(0.3f, 1f, 0.5f, 0.18f);
        static readonly Color ChallengeEdge = new(0.35f, 1f, 0.55f, 0.9f);

        protected override bool ChannelEnabled => DebugManager.ShowDialogueTriggers;

        /// <summary>
        /// Adds the overlay to the <see cref="DebugManager"/>'s object if it
        /// isn't there yet. Called from DialogueTrigger.OnEnable so any scene
        /// containing a trigger gets the view with no wiring — and a scene
        /// with none never pays for it.
        /// </summary>
        public static void EnsureInstalled()
        {
            if (!Application.isEditor || !Application.isPlaying) return;
            DebugManager manager = DebugManager.Instance;
            if (manager == null) return;
            if (manager.GetComponent<DialogueTriggerVisualizer>() == null)
                manager.gameObject.AddComponent<DialogueTriggerVisualizer>();
        }

        protected override void Rebuild()
        {
            foreach (DialogueTrigger trigger in DialogueTrigger.Active)
                if (trigger != null)
                    DrawVolume(trigger, trigger.IsReady ? ReadyFill : CoolingFill, trigger.IsReady ? ReadyEdge : CoolingEdge);

            foreach (CinemaTrigger trigger in CinemaTrigger.Active)
                if (trigger != null)
                    DrawVolume(trigger, trigger.IsReady ? CinemaReadyFill : CinemaCoolingFill, trigger.IsReady ? CinemaReadyEdge : CinemaCoolingEdge);

            // Challenge triggers are always one-shot, so a live one is always ready.
            foreach (ChallengeTrigger trigger in ChallengeTrigger.Active)
                if (trigger != null)
                    DrawVolume(trigger, ChallengeFill, ChallengeEdge);
        }

        void DrawVolume(Component trigger, Color fill, Color edge)
        {
            foreach (Collider col in trigger.GetComponents<Collider>())
            {
                if (col is BoxCollider box)
                {
                    Matrix4x4 m = box.transform.localToWorldMatrix;
                    Lines.SolidBox(m, box.center, box.size, fill);
                    Lines.WireBox(m, box.center, box.size, edge);
                }
                else
                {
                    Bounds b = col.bounds;
                    Lines.SolidBox(Matrix4x4.identity, b.center, b.size, fill);
                    Lines.WireBox(Matrix4x4.identity, b.center, b.size, edge);
                }
            }
        }
    }
}
