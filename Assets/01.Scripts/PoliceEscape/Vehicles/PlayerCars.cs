using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// The one answer to "which car is the player's": every
    /// <see cref="CarInput"/> registers itself here for its lifetime, so the
    /// AI, the HUD, the streamer and the level flow ask the registry instead
    /// of scanning every car in the scene (refactor Step 9.1 — it replaced
    /// <c>PatrolManager.FindPlayerCar</c>, which put the patrol manager in the
    /// middle of systems that had nothing to do with the police). A car on an
    /// inactive object is not the player's, as with the scan it replaced; the
    /// newest registered car wins, so a replacement spawned the frame the old
    /// car is destroyed is already the answer. Statics are reset on play-mode
    /// entry (domain reload is off).
    /// </summary>
    public static class PlayerCars
    {
        static readonly List<CarInput> inputs = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => inputs.Clear();

        internal static void Register(CarInput input)
        {
            if (input != null && !inputs.Contains(input)) inputs.Add(input);
        }

        internal static void Unregister(CarInput input) => inputs.Remove(input);

        /// <summary>The player's car (the newest active car driven by a <see cref="CarInput"/>), or null while none exists.</summary>
        public static CarController Current
        {
            get
            {
                for (int i = inputs.Count - 1; i >= 0; i--)
                {
                    CarInput input = inputs[i];
                    if (input == null) { inputs.RemoveAt(i); continue; }
                    if (!input.gameObject.activeInHierarchy) continue;
                    var car = input.GetComponent<CarController>();
                    if (car != null) return car;
                }
                return null;
            }
        }

        /// <summary>Every live player car, oldest first — a snapshot, safe to destroy while iterating.</summary>
        public static List<CarController> All()
        {
            var cars = new List<CarController>();
            foreach (CarInput input in inputs)
            {
                if (input == null) continue;
                var car = input.GetComponent<CarController>();
                if (car != null) cars.Add(car);
            }
            return cars;
        }
    }
}
