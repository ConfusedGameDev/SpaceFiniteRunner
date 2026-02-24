using UnityEngine;

namespace HoverRacer
{
    public enum GameState { Racing, Stopped, Finished }

    /// <summary>
    /// Session orchestrator. Listens to RaceEvents and decides when to score.
    ///
    /// Two scoring paths:
    ///   A) Vehicle crossed the finish line, then stopped
    ///      → FinishLine fires OnFinishLineCrossed → GameManager scores from that distance.
    ///   B) Vehicle stopped without ever crossing the finish line
    ///      → GameManager measures distance directly and scores from it.
    ///
    /// Place a single GameManager in the scene. Assign references in the Inspector.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] VehicleController vehicle;
        [SerializeField] Transform         finishLineTransform;

        [Header("Scoring Thresholds")]
        [Tooltip("Distance from the finish line (metres) that still earns a perfect score.")]
        [Min(0f)] [SerializeField] float perfectRadius = 0.5f;
        [Tooltip("Distance from the finish line (metres) at which the score reaches zero.")]
        [Min(0f)] [SerializeField] float zeroRadius    = 30f;

        GameState currentState = GameState.Racing;
        bool      finishLineCrossedHandled;

        void OnEnable()
        {
            RaceEvents.OnVehicleStopped     += HandleVehicleStopped;
            RaceEvents.OnFinishLineCrossed  += HandleFinishLineCrossed;
        }

        void OnDisable()
        {
            RaceEvents.OnVehicleStopped     -= HandleVehicleStopped;
            RaceEvents.OnFinishLineCrossed  -= HandleFinishLineCrossed;
        }

        // Called when FinishLine detected the vehicle crossing AND then stopping.
        void HandleFinishLineCrossed(float signedDistance)
        {
            finishLineCrossedHandled = true;
            FinaliseScore(signedDistance);
        }

        // Called when the vehicle speed reaches zero.
        void HandleVehicleStopped()
        {
            currentState = GameState.Stopped;

            // Give FinishLine.HandleVehicleStopped one frame to fire first (they both
            // subscribe to the same event). If it has not handled scoring by then, we do it.
            // Using a coroutine for a one-frame delay.
            StartCoroutine(ScoreIfNotHandled());
        }

        System.Collections.IEnumerator ScoreIfNotHandled()
        {
            yield return null; // wait one frame

            if (finishLineCrossedHandled) yield break;

            // Vehicle stopped before reaching the finish line.
            // Compute signed distance: negative means the vehicle is behind the line.
            float signedDistance = CalculateDistanceToFinishLine();
            FinaliseScore(signedDistance);
        }

        void FinaliseScore(float signedDistance)
        {
            currentState = GameState.Finished;
            int score = ScoreCalculator.Calculate(signedDistance, perfectRadius, zeroRadius);
            RaceEvents.RaiseScoreCalculated(score);

            Debug.Log($"[GameManager] Race finished. Stop distance from line: {signedDistance:F2}m  |  Score: {score}");
        }

        /// <summary>
        /// Signed distance from the finish line's local forward perspective.
        /// Negative = vehicle is before the line, positive = past it.
        /// </summary>
        float CalculateDistanceToFinishLine()
        {
            if (finishLineTransform == null)
            {
                Debug.LogWarning("[GameManager] finishLineTransform is not assigned. Score will be 0.");
                return float.MaxValue;
            }

            Vector3 toVehicle = vehicle.WorldPosition - finishLineTransform.position;
            return Vector3.Dot(toVehicle, finishLineTransform.forward);
        }

        public GameState CurrentState => currentState;
    }
}
