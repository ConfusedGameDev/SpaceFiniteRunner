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
    ///
    /// <b>Wraps only land on land.</b> A wrap that would put the car over
    /// open sea (<see cref="CityRoot.IsOpenWater"/> — a causeway's deck road
    /// still counts as land) is refused and treated as a splash instead:
    /// beyond the map rectangle there is no slab and no splash trigger, so
    /// letting the car continue would mean falling through the void forever.
    /// The splash goes through the landing block's own
    /// <see cref="WaterSplashZone"/> when it has one, so the charge and the
    /// respawn are the same as driving in from the shore.
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

            if (root.IsOpenWater(wrapped))
            {
                RefuseIntoWater(wrapped);
                return;
            }

            Rigidbody body = player.Body != null ? player.Body : player.GetComponent<Rigidbody>();
            if (body == null) return;
            Vehicles.CarFactory.Teleport(body, wrapped, body.rotation);
        }

        /// <summary>The landing is sea: no wrap — the car splashes (damage + respawn on the nearest road, which is back on the side it left).</summary>
        void RefuseIntoWater(Vector3 landing)
        {
            WaterSplashZone zone = root.TryGetBlock(root.BlockCoordAt(landing), out CityBlock block)
                ? block.GetComponentInChildren<WaterSplashZone>()
                : null;
            if (zone != null) zone.Splash(player);
            else WaterSplashZone.Splash(player, WaterSplashZone.DefaultDamage);
        }
    }
}
