using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// A scene object the player uses up during a run — a pickup, a one-shot
    /// story volume, a road-found challenge — that an in-place level retry
    /// has to put back. Consuming DEACTIVATES the object instead of destroying
    /// it and remembers it in <see cref="RunConsumables"/>; a restore turns it
    /// back on and calls <see cref="OnRestored"/>, where the instance resets
    /// its own latch (<c>collected</c>, <c>fired</c>, a cooldown) — a
    /// reactivated object re-runs neither Awake nor its field initializers.
    /// </summary>
    public interface IRunConsumable
    {
        /// <summary>The run restarted and this object is active again: forget that it was used.</summary>
        void OnRestored();
    }

    /// <summary>
    /// The registry of consumed run objects, in the Runner assembly so both
    /// games' consumables (the shared <c>Collectible</c>, the city's trigger
    /// volumes) can reach it. A scene reload used to restore all of these
    /// for free; the city's in-place RETRY calls <see cref="RestoreAll"/>
    /// instead. The runner never restores — its generator destroys the
    /// deactivated coins with their track stretch exactly as it destroyed
    /// the live ones.
    ///
    /// Domain reload is off, so the list is a static that survives play
    /// sessions and scene trips: every consumable calls <see cref="Forget"/>
    /// from its <c>OnDestroy</c> (which fires for a deactivated object on
    /// scene unload, culling and leaving play mode), so entries leave the
    /// list with their objects and nothing here subscribes to scene events.
    /// A restore still skips any entry Unity has destroyed underneath it.
    /// </summary>
    public static class RunConsumables
    {
        static readonly List<MonoBehaviour> consumed = new();

        /// <summary>How many consumed objects are waiting to be restored.</summary>
        public static int Count => consumed.Count;

        /// <summary>Use the object up: deactivate it and remember it for the next restore. Idempotent.</summary>
        public static void Consume<T>(T consumable) where T : MonoBehaviour, IRunConsumable
        {
            if (consumable == null) return;
            if (!consumed.Contains(consumable)) consumed.Add(consumable);
            consumable.gameObject.SetActive(false);
        }

        /// <summary>The object is going away for good (scene unload, culling) — drop it from the list.</summary>
        public static void Forget(MonoBehaviour consumable)
        {
            if (consumable != null) consumed.Remove(consumable);
        }

        /// <summary>Put every consumed object back — active again, its own latch reset — and empty the list.</summary>
        public static void RestoreAll()
        {
            for (int i = 0; i < consumed.Count; i++)
            {
                MonoBehaviour consumable = consumed[i];
                if (consumable == null) continue; // destroyed underneath us (a culled coin) — nothing to restore
                consumable.gameObject.SetActive(true);
                if (consumable is IRunConsumable restorable) restorable.OnRestored();
            }
            consumed.Clear();
        }
    }
}
