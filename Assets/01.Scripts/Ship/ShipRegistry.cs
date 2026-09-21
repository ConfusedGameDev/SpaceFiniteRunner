using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Who is flying, without a scene search or a concrete type: every ship
    /// registers while it is enabled, and a reader asks for the player's ship
    /// OF ITS OWN SCENE. Scoped by scene because the city→runner handoff keeps
    /// two scenes alive at once — a global "the ship" would hand the runner's
    /// HUD a ship from the scene that is on its way out. The list is static
    /// and domain reload is off, so it is emptied at boot; ships add and
    /// remove themselves in OnEnable / OnDisable and nothing else writes it.
    /// </summary>
    public static class ShipRegistry
    {
        static readonly List<IShip> Ships = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot() => Ships.Clear();

        public static void Register(IShip ship)
        {
            if (ship != null && !Ships.Contains(ship)) Ships.Add(ship);
        }

        public static void Unregister(IShip ship) => Ships.Remove(ship);

        /// <summary>The ship living in <paramref name="scene"/>, else any ship when the scene is unknown or has none.</summary>
        public static IShip Find(Scene scene)
        {
            IShip fallback = null;
            for (int i = Ships.Count - 1; i >= 0; i--)
            {
                IShip ship = Ships[i];
                if (ship as Object == null) { Ships.RemoveAt(i); continue; } // destroyed without a disable
                if (scene.IsValid() && ship.transform.gameObject.scene == scene) return ship;
                fallback ??= ship;
            }
            return fallback;
        }
    }
}
