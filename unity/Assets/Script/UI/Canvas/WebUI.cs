using UnityEngine;

public class WebUI : MonoBehaviour
{
    void Start()
    {
        // Show the web UI from the start.
        if (Application.isMobilePlatform)
        {
            gameObject.SetActive(false);
        }
    }
}
