using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// The list of <see cref="TrackSpawner"/>s a <see cref="TrackGenerator"/>
    /// streams onto the track — the one asset to open to tune what spawns and
    /// how often. Order matters within a phase: a pickup spawner keeps off the
    /// pickups of every spawner listed before it (ground claimers always run
    /// first, whatever their place). A spawner asset left out of the list —
    /// the brake pads today — is simply never spawned.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_TrackSpawnSet", menuName = "FiniteRunner/Track Spawn Set")]
    public class TrackSpawnSet : ScriptableObject
    {
        [Tooltip("Everything the generator spawns along the track. Add a spawner asset to spawn it, remove it (or untick it on the asset) to stop.")]
        [InlineEditor(InlineEditorObjectFieldModes.Foldout)]
        [SerializeField] TrackSpawner[] spawners = System.Array.Empty<TrackSpawner>();

        public TrackSpawner[] Spawners => spawners;
    }
}
