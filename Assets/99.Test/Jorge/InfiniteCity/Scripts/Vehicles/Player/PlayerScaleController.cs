using UnityEngine;


namespace FiniteRunner
{
public class PlayerScaleController : MonoBehaviour
{


    Transform playerTransform;
    public float megaScale = 4f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if(playerTransform== null)
        playerTransform = transform;
        var cheatManager = CheatManager.Instance;

        if(cheatManager!=null)
        {
            if(cheatManager.isMegaCarEnabled)
            {
                Debug.Log("Set mega car enabled");
                playerTransform.localScale*=megaScale;
            }
        }
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
}