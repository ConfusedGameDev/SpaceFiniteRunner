using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Contracts
{
    /// <summary>
    /// Discovery by contract, for a system dropped into a scene with no
    /// composition root to hand it its collaborators: finds the first live
    /// component implementing <typeparamref name="T"/>. A system asks for an
    /// interface, never a concrete type, so it keeps working — or quietly
    /// runs on its own defaults — whatever the scene holds. A composition
    /// root that binds the collaborator explicitly always wins over this.
    /// </summary>
    public static class ContractLookup
    {
        public static T Find<T>() where T : class
        {
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (behaviour is T match) return match;
            return null;
        }
    }
}
