using UnityEngine;


using ConfusedGameDev.FiniteRunner.Cheats;
namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles
{
public class PlayerScaleController : MonoBehaviour
{


    Transform playerTransform;
    public float megaScale = 4f;

    /// <summary>The cheat id (CheatDefinition) that turns the player's car into the mega car.</summary>
    const string MegaCarCheatId = "MegaCar";
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if(playerTransform== null)
        playerTransform = transform;
        // The cheat's state is the manager's static set — no instance needed.
        if (CheatManager.IsActive(MegaCarCheatId))
        {
            Debug.Log("Set mega car enabled");
            playerTransform.localScale *= megaScale;
        }
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
}