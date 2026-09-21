using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// How a ship detects that its level has a guide: guides register while
    /// enabled, and a ship asks for the one it is in reach of. No guide
    /// registered in the ship's scene — or none within its capture range —
    /// simply means free flight; nothing has to be wired. Scene-scoped for the
    /// same reason the ship registry is (the city→runner handoff keeps two
    /// scenes alive), static and therefore emptied at boot (domain reload off).
    /// </summary>
    public static class ShipGuideRegistry
    {
        static readonly List<IShipGuide> Guides = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot() => Guides.Clear();

        public static void Register(IShipGuide guide)
        {
            if (guide != null && !Guides.Contains(guide)) Guides.Add(guide);
        }

        public static void Unregister(IShipGuide guide) => Guides.Remove(guide);

        /// <summary>
        /// The guide of <paramref name="scene"/> the point is within capture
        /// range of — the nearest line when several are. A whole-line search
        /// per guide, so ships call it on a timer, not every tick.
        /// </summary>
        public static IShipGuide FindInReach(Scene scene, Vector3 worldPosition, out float hint)
        {
            IShipGuide best = null;
            float bestOffset = float.MaxValue;
            hint = float.NaN;
            for (int i = Guides.Count - 1; i >= 0; i--)
            {
                IShipGuide guide = Guides[i];
                if (guide as Object == null) { Guides.RemoveAt(i); continue; }
                if (scene.IsValid() && guide.Scene != scene) continue;

                float search = float.NaN;
                if (!guide.TryProject(worldPosition, ref search, out GuideSample sample)) continue;
                float offset = new Vector2(sample.lateral, sample.height).magnitude;
                if (offset > guide.CaptureRange || offset >= bestOffset) continue;
                best = guide;
                bestOffset = offset;
                hint = search;
            }
            return best;
        }
    }
}
