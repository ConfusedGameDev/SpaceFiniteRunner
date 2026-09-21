using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The runner's rules, handed to everything that speaks
    /// <see cref="ShipSettings"/>. The ship's own components (blink, roll
    /// trail, and the standalone ship itself) live below the runner and know
    /// nothing of <see cref="GameSettings"/>; the runner's rules must still
    /// reach them, and <b>live</b> — the FALL &amp; RESPAWN debug page edits
    /// the GameSettings asset mid-run. So this component owns ONE runtime
    /// <see cref="ShipSettings"/> for the run and re-pushes the run rules
    /// into it every frame, exactly as the motor pushes them into its body
    /// every tick. GameSettings stays whole and authoritative: nothing was
    /// split out of it and no asset was migrated.
    /// </summary>
    public sealed class RunnerShipSettingsSync : MonoBehaviour
    {
        GameSettings source;
        bool ownsSettings;

        /// <summary>The run's ship settings — a runtime object, never an asset.</summary>
        public ShipSettings Settings { get; private set; }

        /// <summary>Finds or adds the sync on the ship. <paramref name="template"/> (a standalone ship's own clone) keeps its physics knobs; without one a fresh instance with the defaults is made.</summary>
        public static RunnerShipSettingsSync Ensure(GameObject ship, GameSettings source, ShipSettings template = null)
        {
            var sync = ship.GetComponent<RunnerShipSettingsSync>();
            if (sync == null) sync = ship.AddComponent<RunnerShipSettingsSync>();
            sync.source = source;
            if (sync.Settings == null)
            {
                sync.Settings = template != null ? template : ScriptableObject.CreateInstance<ShipSettings>();
                sync.ownsSettings = template == null;
                if (sync.ownsSettings) sync.Settings.name = "Runner ship settings (run)";
            }
            sync.Push();
            return sync;
        }

        void Update() => Push();

        void Push()
        {
            if (source == null || Settings == null) return;
            Settings.dashEnabled = source.dashEnabled;
            Settings.dashCost = source.dashCost;
            Settings.dashSinglePress = source.dashSinglePress;
            Settings.dashDoubleTapSeconds = source.dashDoubleTapSeconds;
            Settings.stallGraceSeconds = source.stallGraceSeconds;
            Settings.wallHitCooldownSeconds = source.dashWallHitCooldownSeconds;
            Settings.ghostMaterial = source.dashGhostMaterial;
            Settings.respawnBlinkRate = source.respawnBlinkRate;
            Settings.barrelRollTrailSeconds = source.barrelRollTrailSeconds;
            Settings.barrelRollTrailWidth = source.barrelRollTrailWidth;
            Settings.barrelRollTrailSpan = source.barrelRollTrailSpan;
            Settings.barrelRollTrailColor = source.barrelRollTrailColor;
            Settings.barrelRollTrailMaterial = source.barrelRollTrailMaterial;
        }

        void OnDestroy()
        {
            if (ownsSettings && Settings != null) Destroy(Settings);
        }
    }
}
