using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Campaign
{
    /// <summary>
    /// Marker base of the runner's <c>RunnerLevelDefinition</c> — the typed
    /// slot a <see cref="MissionDefinition"/> holds for its escape run
    /// without this assembly referencing the Runner assembly. See
    /// <see cref="CityLevelAsset"/>.
    /// </summary>
    public abstract class RunnerLevelAsset : ScriptableObject { }
}
