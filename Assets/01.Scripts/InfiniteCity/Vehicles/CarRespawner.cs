using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
    /// <summary>
    /// Recovery for the WheelCollider car. A car sitting flipped (or on its
    /// side) and nearly stopped for longer than the config's flip timeout is
    /// a wrecked run: the LevelManager reboots the level behind the maxed
    /// glitch — same death as full police corruption — rather than quietly
    /// teleporting the car back onto the road. The teleport (upright on the
    /// nearest road cell, yaw preserved, velocities zeroed, via
    /// CityManager.TryFindNearestRoadCell) remains for the manual respawn
    /// press and for scenes that have no LevelManager to reboot. Timing
    /// knobs live on the CarConfig asset.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class CarRespawner : MonoBehaviour
    {
        CarController car;
        ICarInput input;
        CityManager city;
        LevelManager levelManager;
        float flippedTimer;

        void Awake()
        {
            car = GetComponent<CarController>();
            input = GetComponent<ICarInput>();
        }

        void Update()
        {
            CarConfig config = car.config;
            if (config == null) return;

            if (input != null && input.RespawnPressed)
            {
                Respawn();
                return;
            }

            // Flipped = roof or side down; only count while (nearly) stopped so
            // a mid-air barrel roll at speed doesn't trigger the wreck.
            bool flipped = transform.up.y < 0.25f;
            bool stalled = car.Velocity.magnitude < 1.5f;
            flippedTimer = flipped && stalled ? flippedTimer + Time.deltaTime : 0f;
            if (flippedTimer >= config.flipTimeout) OnWrecked();
        }

        /// <summary>
        /// The run ended on the roof: the level reboots behind the glitch.
        /// Only scenes without a LevelManager fall back to the teleport —
        /// and while a reboot (or the completion handoff) is already in
        /// flight, do nothing at all: a teleport during the held glitch
        /// would be visible.
        /// </summary>
        void OnWrecked()
        {
            flippedTimer = 0f;
            if (levelManager == null) levelManager = FindAnyObjectByType<LevelManager>();
            if (levelManager != null)
            {
                levelManager.RequestReboot("car wrecked upside down");
                return;
            }
            Respawn();
        }

        [Button("Respawn"), EnableIf("@UnityEngine.Application.isPlaying")]
        public void Respawn()
        {
            flippedTimer = 0f;

            if (city == null) city = FindAnyObjectByType<CityManager>();
            Vector3 position = city != null && city.TryFindNearestRoadCell(transform.position, out Vector3 roadCenter, out _)
                ? roadCenter
                : transform.position;
            position.y += car.config != null ? car.config.respawnHeight : 0.5f;

            Rigidbody body = car.Body;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            // Interpolation-safe teleport — a plain transform move would be
            // undone by the rigidbody's interpolation history next frame.
            CarFactory.Teleport(body, position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        }
    }
}
