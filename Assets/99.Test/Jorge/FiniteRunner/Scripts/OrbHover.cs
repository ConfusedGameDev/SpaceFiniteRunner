using UnityEngine;

namespace FiniteRunner
{
    /// <summary>
    /// Hover animation for floating power-up orbs: a gentle vertical bob
    /// around the spawn position plus a slow spin. Added at runtime by the
    /// TrackGenerator. The bob stays small so the orb's trigger collider
    /// keeps overlapping the ship's flight line.
    /// </summary>
    public class OrbHover : MonoBehaviour
    {
        [SerializeField, Min(0f)] float bobAmplitude = 0.6f;
        [SerializeField, Min(0f)] float bobFrequency = 1.2f;
        [SerializeField] float spinDegreesPerSecond = 60f;

        Vector3 basePosition;
        float phase;

        void Start()
        {
            basePosition = transform.position;
            // Desync neighbouring orbs so the track doesn't bob in unison.
            phase = (basePosition.x + basePosition.z) * 0.7f;
        }

        void Update()
        {
            float bob = Mathf.Sin((Time.time + phase) * bobFrequency * 2f * Mathf.PI) * bobAmplitude;
            transform.position = basePosition + Vector3.up * bob;
            transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
        }
    }
}
