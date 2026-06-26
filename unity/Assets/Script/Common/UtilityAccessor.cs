using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MovieMap.Common
{
    public class UtilityAccessor : MonoBehaviour
    {
        AppManager _manager;

        void Start()
        {
            _manager = AppManager.Instance;
        }

        public void PlaySE(string key) 
        {
            _manager.SoundManager.PlaySE(key);
        }

        public void ChangeScene(string sceneName)
        {
            _manager.SceneManager.ChangeScene(sceneName);
        }
    }
}
