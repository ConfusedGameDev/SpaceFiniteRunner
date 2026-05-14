using TMPro;
using UnityEngine;

namespace HoverRacer
{
    /// <summary>
    /// Place on a trigger-collider GameObject on the track.
    /// Assign a TextMeshPro (3D) child — it displays the target speed before the player
    /// arrives and switches to the rating label once triggered.
    ///
    /// Rigidbody requirements: the Vehicle's Rigidbody must NOT be set to Kinematic,
    /// and its collision detection must include this trigger layer.
    /// </summary>
    public class Checkpoint : MonoBehaviour
    {
        [SerializeField] CheckpointConfigSO config;

        [Tooltip("Speed the player should be travelling when passing through this checkpoint, in m/s.")]
        [SerializeField] float targetSpeed;

        [Tooltip("3D TextMeshPro component that shows the target speed (and later the rating).")]
        [SerializeField] TextMeshPro speedLabel;

        bool triggered;

        public float TargetSpeed => targetSpeed;

        void Start()
        {
            speedLabel.text = $"{targetSpeed:F0} m/s";
        }

        void OnTriggerEnter(Collider other)
        {
            if (triggered) return;
            if (!other.TryGetComponent<VehicleController>(out var vehicle)) return;

            triggered = true;

            float delta  = Mathf.Abs(vehicle.CurrentSpeed - targetSpeed);
            var   rating = Evaluate(delta);

            ApplyBoostEffect(vehicle, rating);
            speedLabel.text = RatingLabel(rating);

            RaceEvents.RaiseCheckpointPassed(rating);
        }

        // ── Rating ───────────────────────────────────────────────────────────────

        CheckpointRating Evaluate(float delta)
        {
            if (delta <= config.perfectRange) return CheckpointRating.Perfect;
            if (delta <= config.awesomeRange) return CheckpointRating.Awesome;
            if (delta <= config.greatRange)   return CheckpointRating.Great;
            return CheckpointRating.Meh;
        }

        static string RatingLabel(CheckpointRating rating) => rating switch
        {
            CheckpointRating.Perfect => "PERFECT!",
            CheckpointRating.Awesome => "AWESOME!",
            CheckpointRating.Great   => "GREAT",
            _                        => "MEH"
        };

        // ── Boost effects ────────────────────────────────────────────────────────

        void ApplyBoostEffect(VehicleController vehicle, CheckpointRating rating)
        {
            switch (rating)
            {
                case CheckpointRating.Perfect: vehicle.ReplenishBoosts();                       break;
                case CheckpointRating.Awesome: vehicle.ModifyBoosts(config.awesomeEffect);      break;
                case CheckpointRating.Great:   vehicle.ModifyBoosts(config.greatEffect);        break;
                case CheckpointRating.Meh:     vehicle.ModifyBoosts(config.mehEffect);          break;
            }
        }
    }
}
