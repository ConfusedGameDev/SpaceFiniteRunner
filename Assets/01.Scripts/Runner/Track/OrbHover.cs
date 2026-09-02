using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Hover animation for floating power-up orbs: a gentle bob around the
    /// spawn position plus a slow spin, and — for the rarer orb tiers — a
    /// lateral sway across the track that makes them harder to aim for.
    /// Added at runtime by the TrackGenerator. The bob stays small so the
    /// orb's trigger collider keeps overlapping the ship's flight line; the
    /// sway is what actually moves the orb off it. Both axes are the TRACK's
    /// at the spawn pose (its up and its right), not the world's, so an orb
    /// on a loop or under a tube bobs away from the road and sways across it.
    /// </summary>
    public class OrbHover : MonoBehaviour
    {
        [SerializeField, Min(0f)] float bobAmplitude = 0.6f;
        [SerializeField, Min(0f)] float bobFrequency = 1.2f;
        [SerializeField] float spinDegreesPerSecond = 60f;
        [SerializeField, Min(0f)] float swayAmplitude;
        [SerializeField, Min(0f)] float swayFrequency = 0.5f;

        Vector3 basePosition;
        Vector3 bobDirection;
        Vector3 swayDirection;
        Vector3 spinAxis;
        float phase;

        /// <summary>Tier setup from the TrackGenerator: how far and how fast the orb sways across the track.</summary>
        public void Configure(float amplitude, float frequency)
        {
            swayAmplitude = amplitude;
            swayFrequency = frequency;
        }

        void Start()
        {
            basePosition = transform.position;
            // The spin rotates the transform, so grab the track axes once.
            bobDirection = transform.up;
            swayDirection = transform.right;
            spinAxis = transform.up;
            // Desync neighbouring orbs so the track doesn't bob in unison.
            phase = (basePosition.x + basePosition.z) * 0.7f;
        }

        void Update()
        {
            float bob = Mathf.Sin((Time.time + phase) * bobFrequency * 2f * Mathf.PI) * bobAmplitude;
            float sway = Mathf.Sin((Time.time + phase) * swayFrequency * 2f * Mathf.PI) * swayAmplitude;
            transform.position = basePosition + bobDirection * bob + swayDirection * sway;
            transform.Rotate(spinAxis, spinDegreesPerSecond * Time.deltaTime, Space.World);
        }
    }
}
