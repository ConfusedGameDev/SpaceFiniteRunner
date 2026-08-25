using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// The pacman edge: when the PLAYER's car leaves the city's ground
    /// rectangle, it is teleported to the opposite side. Only the player
    /// wraps — police and traffic left behind are cleaned up by their own
    /// despawn ticks and respawn around the player's new position, so
    /// wrapping doubles as a (deliberate) way to break a chase. The teleport
    /// goes through <see cref="Vehicles.CarFactory.Teleport"/>, which
    /// suspends rigidbody interpolation and warps the Cinemachine rig, and
    /// velocity is left untouched — the arterial field is periodic, so the
    /// road the player exits on is the road they re-enter on. Added to the
    /// city root by <see cref="CityRoot"/> when its wrap toggle is on.
    /// </summary>
    public class CityWrap : MonoBehaviour
    {
        CityRoot root;
        Vehicles.CarController player;
        float playerRefreshTimer;

        void Awake()
        {
            root = GetComponent<CityRoot>();
        }

        void FixedUpdate()
        {
            if (root == null) return;
            if (player == null)
            {
                playerRefreshTimer -= Time.fixedDeltaTime;
                if (playerRefreshTimer > 0f) return;
                playerRefreshTimer = 1f;
                player = AI.PatrolManager.FindPlayerCar();
                if (player == null) return;
            }

            if (!root.TryWrap(player.transform.position, out Vector3 wrapped)) return;
            Rigidbody body = player.Body != null ? player.Body : player.GetComponent<Rigidbody>();
            if (body == null) return;
            Vehicles.CarFactory.Teleport(body, wrapped, body.rotation);
        }
    }
}
