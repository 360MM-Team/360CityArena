using UnityEngine;

public class VirtualStick : MonoBehaviour
{
    void Start()
    {
        if (!Application.isMobilePlatform)
        {
            gameObject.SetActive(true);
        }

    }
}