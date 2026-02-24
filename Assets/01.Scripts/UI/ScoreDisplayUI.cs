using TMPro;
using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Reveals a score panel when the race ends and displays the final score and stop feedback.
    /// Subscribes to RaceEvents.OnScoreCalculated and RaceEvents.OnFinishLineCrossed.
    ///
    /// Assign references in the Inspector. scorePanel is hidden at start.
    /// </summary>
    public class ScoreDisplayUI : MonoBehaviour
    {
        [SerializeField] GameObject scorePanel;
        [SerializeField] TMP_Text   scoreLabel;
        [Tooltip("Optional label that shows directional feedback (overshot / undershot / perfect).")]
        [SerializeField] TMP_Text   feedbackLabel;

        float lastSignedDistance;

        void Awake()
        {
            scorePanel.SetActive(false);
        }

        void OnEnable()
        {
            RaceEvents.OnFinishLineCrossed += CacheSignedDistance;
            RaceEvents.OnScoreCalculated   += ShowScore;
        }

        void OnDisable()
        {
            RaceEvents.OnFinishLineCrossed -= CacheSignedDistance;
            RaceEvents.OnScoreCalculated   -= ShowScore;
        }

        void CacheSignedDistance(float signedDistance)
        {
            lastSignedDistance = signedDistance;
        }

        void ShowScore(int score)
        {
            scorePanel.SetActive(true);
            scoreLabel.text = $"Score: {score}";

            if (feedbackLabel != null)
                feedbackLabel.text = GetFeedbackText(score, lastSignedDistance);
        }

        static string GetFeedbackText(int score, float signedDistance)
        {
            if (score == 100)
                return "PERFECT STOP!";

            if (signedDistance > 0f)
                return $"Overshot by {signedDistance:F1}m";

            if (signedDistance < 0f)
                return $"Stopped {Mathf.Abs(signedDistance):F1}m short";

            return string.Empty;
        }
    }
}
