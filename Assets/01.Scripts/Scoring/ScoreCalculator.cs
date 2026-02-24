using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Pure-function score calculator. No MonoBehaviour — call statically from anywhere.
    ///
    /// Score is 0–100 based on how close the vehicle stopped to the finish line:
    ///   - Within perfectRadius  →  100 (perfect stop)
    ///   - Beyond zeroRadius     →  0
    ///   - Between the two       →  linear interpolation
    ///
    /// The sign of signedDistance carries directional info for UI feedback
    /// (positive = overshot past line, negative = stopped before line)
    /// but does NOT affect the score value itself.
    /// </summary>
    public static class ScoreCalculator
    {
        /// <param name="signedDistance">
        /// Signed metres from the finish line.
        /// Negative = stopped before the line; positive = stopped past the line; 0 = perfect.
        /// </param>
        /// <param name="perfectRadius">
        /// Distance from the finish line that still earns a perfect score of 100.
        /// </param>
        /// <param name="zeroRadius">
        /// Distance from the finish line at which the score reaches 0.
        /// </param>
        public static int Calculate(float signedDistance, float perfectRadius, float zeroRadius)
        {
            float absDistance = Mathf.Abs(signedDistance);

            if (absDistance <= perfectRadius) return 100;
            if (absDistance >= zeroRadius)    return 0;

            float t = (absDistance - perfectRadius) / (zeroRadius - perfectRadius);
            return Mathf.RoundToInt(Mathf.Lerp(100f, 0f, t));
        }
    }
}
