using UnityEngine;
using ConfusedGameDev.FiniteRunner.Track;

namespace ConfusedGameDev.FiniteRunner.Traffic
{
    /// <summary>
    /// One pooled oncoming car. It lives in track space — distance from the
    /// track start, lateral offset — and is kinematic: each fixed tick it
    /// drives toward the start at its rolled speed and eases toward its lane;
    /// there is no grip, slide or fall. Its pose is the track's pose at that
    /// spot, interpolated between the last two ticks the way the patrol's is,
    /// with the model turned to face the ship. It owns no collider: every
    /// contact is the <see cref="TrafficSystem"/>'s analytic test.
    /// Built once by the system's pool and never destroyed mid-run.
    /// </summary>
    public class TrafficCar : MonoBehaviour
    {
        /// <summary>Track distance this tick, metres. Decreases: the car drives toward the start.</summary>
        public float Distance { get; private set; }
        /// <summary>Track distance last tick — what a swept contact test spans.</summary>
        public float PrevDistance { get; private set; }
        /// <summary>Lateral offset from the flight line this tick, metres.</summary>
        public float Lateral { get; private set; }
        public float PrevLateral { get; private set; }
        /// <summary>Speed toward the start, m/s.</summary>
        public float Speed { get; private set; }
        /// <summary>The lateral the car is steering for.</summary>
        public float TargetLateral { get; set; }
        /// <summary>The lane the car spawned in: where it heads back to once an obstacle is behind it.</summary>
        public float HomeLateral { get; private set; }
        /// <summary>Index of the vehicle entry this shell was built from.</summary>
        public int VehicleIndex { get; private set; }
        public TrafficVehicle Vehicle { get; private set; }
        public bool Active => gameObject.activeSelf;

        Transform visual;

        /// <summary>Builds the shell's visual from <paramref name="vehicle"/>. Called once by the pool.</summary>
        public void Build(int vehicleIndex, TrafficVehicle vehicle)
        {
            VehicleIndex = vehicleIndex;
            Vehicle = vehicle;

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            // Facing the ship: the track's forward is the ship's, the car's nose is the other way.
            visual.localPosition = Vector3.up * vehicle.hoverHeight;
            visual.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var model = Instantiate(vehicle.prefab, visual);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, vehicle.yawOffset, 0f);
            model.transform.localScale = Vector3.one * vehicle.scale;
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            gameObject.SetActive(false);
        }

        /// <summary>Puts the car on the road at <paramref name="distance"/>, driving toward the start.</summary>
        public void Place(float distance, float lateral, float speed)
        {
            Distance = PrevDistance = distance;
            Lateral = PrevLateral = TargetLateral = HomeLateral = lateral;
            Speed = speed;
            gameObject.SetActive(true);
        }

        /// <summary>One fixed tick of kinematic driving.</summary>
        public void Step(float dt, float lateralSpeed)
        {
            PrevDistance = Distance;
            PrevLateral = Lateral;
            Distance -= Speed * dt;
            Lateral = Mathf.MoveTowards(Lateral, TargetLateral, lateralSpeed * dt);
        }

        /// <summary>Seats the car between its last two ticks.</summary>
        public void ApplyPose(TrackManager track, float alpha)
        {
            float distance = Mathf.Max(0f, Mathf.Lerp(PrevDistance, Distance, alpha));
            float lateral = Mathf.Lerp(PrevLateral, Lateral, alpha);
            track.GetPoseAtDistance(distance, lateral, out Vector3 pos, out Quaternion rot);
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
