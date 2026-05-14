using TMPro;
using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Three-panel speed HUD:
    ///   - Current speed (updates every FixedUpdate via event)
    ///   - Next checkpoint: target speed + remaining distance (updates every frame)
    ///   - Finish line: required speed + remaining distance (updates every frame)
    ///
    /// Assign all three TMP_Text labels and the CheckpointManager reference in the Inspector.
    /// </summary>
    public class RaceHUD : MonoBehaviour
    {
        [SerializeField] TMP_Text          currentSpeedLabel;
        [SerializeField] TMP_Text          nextCheckpointLabel;
        [SerializeField] TMP_Text          finalSpeedLabel;
        [SerializeField] CheckpointManager checkpointManager;

        float latestSpeed;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        void OnEnable()
        {
            RaceEvents.OnSpeedChanged += OnSpeedChanged;
        }

        void OnDisable()
        {
            RaceEvents.OnSpeedChanged -= OnSpeedChanged;
        }

        void OnSpeedChanged(float speed)
        {
            latestSpeed = speed;
            currentSpeedLabel.text = $"Speed  {speed:F1} m/s";
        }

        void Update()
        {
            if (checkpointManager.HasNextCheckpoint)
            {
                nextCheckpointLabel.text =
                    $"Next   {checkpointManager.NextCheckpointSpeed:F0} m/s" +
                    $"  |  {checkpointManager.NextCheckpointDist:F0} m";
            }
            else
            {
                nextCheckpointLabel.text = "Next   —";
            }

            finalSpeedLabel.text =
                $"Finish {checkpointManager.FinalRequiredSpeed:F0} m/s" +
                $"  |  {checkpointManager.FinalLineDist:F0} m";
        }
    }
}
