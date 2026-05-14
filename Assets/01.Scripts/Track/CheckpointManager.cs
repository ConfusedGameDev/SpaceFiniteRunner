using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Owns the ordered list of track checkpoints and the finish-line speed requirement.
    /// Listens to OnCheckpointPassed to advance the sequence.
    /// Provides live distance and speed data queried by RaceHUD.
    ///
    /// Setup in Inspector:
    ///   - Checkpoints: drag in order (first to last before finish line)
    ///   - Finish Line Transform: the finish-line GameObject
    ///   - Vehicle Transform: the ship root GameObject
    ///   - Final Required Speed: target speed at the finish line
    /// </summary>
    public class CheckpointManager : MonoBehaviour
    {
        [SerializeField] Checkpoint[] checkpoints;
        [SerializeField] Transform    finishLine;
        [SerializeField] Transform    vehicle;
        [SerializeField] float        finalRequiredSpeed;

        int nextIndex;

        // ── Public queries used by RaceHUD ────────────────────────────────────────

        public bool  HasNextCheckpoint     => nextIndex < checkpoints.Length;
        public float NextCheckpointSpeed   => HasNextCheckpoint ? checkpoints[nextIndex].TargetSpeed : 0f;
        public float NextCheckpointDist    => HasNextCheckpoint
            ? Vector3.Distance(vehicle.position, checkpoints[nextIndex].transform.position) : 0f;

        public float FinalRequiredSpeed    => finalRequiredSpeed;
        public float FinalLineDist         => finishLine != null
            ? Vector3.Distance(vehicle.position, finishLine.position) : 0f;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        void OnEnable()
        {
            RaceEvents.OnCheckpointPassed += HandleCheckpointPassed;
        }

        void OnDisable()
        {
            RaceEvents.OnCheckpointPassed -= HandleCheckpointPassed;
        }

        void HandleCheckpointPassed(CheckpointRating _)
        {
            nextIndex++;
        }
    }
}
