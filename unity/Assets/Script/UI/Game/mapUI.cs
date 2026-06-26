using UnityEngine;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.UI;
using MovieMap.Core;

namespace MovieMap.UI
{
    
    public class mapUI : MonoBehaviour 
    {
        [SerializeField]
        RawImage mapImage;

        private void Start()
        {
            mapImage = GetComponent<RawImage>();
            StartCoroutine(GetText());
        }
        IEnumerator GetText()
        {
            var mapImageUrl = GlobalInfo.AreaUrl + "/image/map_image.png";
            UnityWebRequest www = UnityWebRequestTexture.GetTexture(mapImageUrl);
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.Log(www.error);
            }
            else
            {
                mapImage.texture = DownloadHandlerTexture.GetContent(www);
            }
        }
    }
}