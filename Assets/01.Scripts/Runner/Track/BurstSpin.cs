using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// A marker that spins in bursts — one fast turn, a pause, another turn —
    /// so a pickup reads from far down the track: a moving shape catches the
    /// eye where a steady one blends into the road. Used by the boost orbs'
    /// ring. With <see cref="faceCamera"/> on it is a Y-axis billboard: it
    /// turns about the track's up (the parent's up captured at start) to face
    /// the camera every frame, so it is always seen face on and never edge on,
    /// and the burst spins it in its own plane (about the axis pointing at the
    /// camera). The mesh is authored flat, its face along local +Y. The
    /// rotation is written in world space every frame, so whatever the parent
    /// does to its own rotation (<see cref="OrbHover"/>'s slow spin) never
    /// leaks in — only the parent's bob and sway carry it along.
    /// </summary>
    public class BurstSpin : MonoBehaviour
    {
        [Tooltip("Turn about the track's up to face the camera every frame (a Y-axis billboard), spinning in its own plane. Off = spin about the track's up from the authored pose.")]
        [SerializeField] bool faceCamera = true;

        [Tooltip("Degrees turned in one burst.")]
        [PropertyRange(0f, 1080f), SuffixLabel("°", true)]
        [SerializeField] float degreesPerBurst = 360f;

        [Tooltip("How long one burst takes. It eases out, so it starts fast and settles.")]
        [PropertyRange(0.05f, 3f), SuffixLabel("s", true)]
        [SerializeField] float burstSeconds = 0.45f;

        [Tooltip("Stillness between two bursts.")]
        [PropertyRange(0f, 10f), SuffixLabel("s", true)]
        [SerializeField] float pauseSeconds = 1.4f;

        [Tooltip("Start each marker at a random point of its cycle, so a row of them does not spin in unison.")]
        [SerializeField] bool randomPhase = true;

        // Maps the flat mesh's face (+Y) onto a look rotation's forward.
        static readonly Quaternion FaceToForward = Quaternion.Euler(90f, 0f, 0f);

        Quaternion baseRotation;
        Vector3 up;
        float phase;
        Camera cam;

        void Start()
        {
            baseRotation = transform.rotation;
            up = transform.parent != null ? transform.parent.up : Vector3.up;
            phase = randomPhase ? Random.value * Cycle : 0f;
        }

        float Cycle => Mathf.Max(0.01f, burstSeconds + pauseSeconds);

        void LateUpdate()
        {
            float t = Mathf.Repeat(Time.time + phase, Cycle);
            float burst = Mathf.Clamp01(t / Mathf.Max(0.01f, burstSeconds));
            float angle = (1f - (1f - burst) * (1f - burst) * (1f - burst)) * degreesPerBurst; // ease-out cubic

            if (faceCamera && TryFacing(out Quaternion facing))
                transform.rotation = facing * Quaternion.AngleAxis(angle, Vector3.forward) * FaceToForward;
            else
                transform.rotation = Quaternion.AngleAxis(angle, up) * baseRotation;
        }

        // The rotation looking from the marker toward the camera, flattened
        // onto the plane of the track's up so it only ever yaws.
        bool TryFacing(out Quaternion facing)
        {
            facing = Quaternion.identity;
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            if (cam == null) return false;

            Vector3 toCamera = Vector3.ProjectOnPlane(cam.transform.position - transform.position, up);
            if (toCamera.sqrMagnitude < 1e-4f) return false;
            facing = Quaternion.LookRotation(toCamera, up);
            return true;
        }
    }
}
