using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// The player's interest-point marker, persisted in PlayerPrefs so it is
    /// still there next launch.
    ///
    /// Two rules are baked in. First, the marker is stored as a <b>global cell
    /// coordinate</b>, not a world position: cells are what the road graph and
    /// the map schematic both speak, so a stored cell needs no conversion to
    /// route to or to paint, and it can never drift half a street sideways.
    ///
    /// Second, and more important, the marker is stored <b>against the seed of
    /// the city it was placed in</b>. A marker only means anything relative to
    /// a particular layout, so when the player generates a new city
    /// (a rebake of the city prefab with a new seed) the old marker is
    /// discarded rather than left pointing at a junction that no longer
    /// exists. Checking the seed here means no other code has to remember to.
    ///
    /// One marker, deliberately: placing again moves it, which keeps "route to
    /// the marker" unambiguous.
    /// </summary>
    public static class MapMarkerStore
    {
        const string SetKey = "map.marker.set";
        const string XKey = "map.marker.x";
        const string YKey = "map.marker.y";
        const string SeedKey = "map.marker.seed";

        static Vector2Int cell;
        static bool hasMarker;
        static int markerSeed;
        static bool loaded;

        /// <summary>Raised whenever the marker is placed, moved or cleared — the map repaints and the route recomputes on it.</summary>
        public static event System.Action Changed;

        public static bool HasMarker
        {
            get { EnsureLoaded(); return hasMarker; }
        }

        /// <summary>The marked cell. Only meaningful while <see cref="HasMarker"/>.</summary>
        public static Vector2Int Cell
        {
            get { EnsureLoaded(); return cell; }
        }

        /// <summary>Place or move the marker, pinning it to the city it belongs to.</summary>
        public static void SetMarker(Vector2Int markedCell, int citySeed)
        {
            EnsureLoaded();
            cell = markedCell;
            hasMarker = true;
            markerSeed = citySeed;

            PlayerPrefs.SetInt(SetKey, 1);
            PlayerPrefs.SetInt(XKey, cell.x);
            PlayerPrefs.SetInt(YKey, cell.y);
            PlayerPrefs.SetInt(SeedKey, citySeed);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void ClearMarker()
        {
            EnsureLoaded();
            hasMarker = false;
            PlayerPrefs.DeleteKey(SetKey);
            PlayerPrefs.DeleteKey(XKey);
            PlayerPrefs.DeleteKey(YKey);
            PlayerPrefs.DeleteKey(SeedKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>
        /// Drop the marker if it belongs to a different city. Call once the
        /// active seed is known — the marker survives relaunches, but must not
        /// survive a regenerated city.
        /// </summary>
        public static void DiscardIfForeign(int citySeed)
        {
            EnsureLoaded();
            if (!hasMarker || markerSeed == citySeed) return;
            ClearMarker();
        }

        // Lazy, not a static constructor: domain reload is disabled in this
        // project so these statics outlive a play session, and the load must
        // be able to happen again on demand rather than once per process.
        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            hasMarker = PlayerPrefs.GetInt(SetKey, 0) != 0;
            cell = new Vector2Int(PlayerPrefs.GetInt(XKey, 0), PlayerPrefs.GetInt(YKey, 0));
            markerSeed = PlayerPrefs.GetInt(SeedKey, 0);
        }
    }
}
