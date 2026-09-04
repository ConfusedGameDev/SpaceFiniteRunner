using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Campaign
{
    /// <summary>
    /// Marker base of the city's <c>LevelDefinition</c>. It exists so a
    /// <see cref="MissionDefinition"/> can hold a typed, inspector-safe slot
    /// for a city level without this assembly seeing the PoliceEscape
    /// assembly — the game re-bases its asset class onto this one (same
    /// script guid, no field change, so existing assets load untouched).
    /// </summary>
    public abstract class CityLevelAsset : ScriptableObject { }
}
