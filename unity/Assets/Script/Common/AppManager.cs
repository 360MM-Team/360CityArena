using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using MovieMap.Sound;
using DG.Tweening;

namespace MovieMap.Common
{
    // Manage scene transitions and audio.
    public class AppManager : MonoBehaviour
    {
        static AppManager _instance;

        SoundManager _soundManager;
        SceneManager _sceneManager;

        public static AppManager Instance => _instance;

        public SoundManager SoundManager => _soundManager;
        public SceneManager SceneManager => _sceneManager;

        void Awake()
        {
            if (_instance != null)
            {
                Destroy(gameObject);
                return;
            }

            // Initialize DOTween here.
            DOTween.Init();
            DOTween.defaultAutoPlay = AutoPlay.None;

            _instance = this;
            _sceneManager = transform.Find("SceneManager").GetComponent<SceneManager>();

            DontDestroyOnLoad(this);

            _sceneManager.Initialize();
        }
    }
}
