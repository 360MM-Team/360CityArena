using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MovieMap.Core;

namespace MovieMap.UI
{
    public class PlayPauseButton : MonoBehaviour
    {
        MovieChanger _changer;
        MoviePlayer _moviePlayer;

        [SerializeField]
        GameObject _playButtonObject;
        [SerializeField]
        GameObject _pauseButtonObject;

        private void Start()
        {
            _changer = GameObject.Find("Spheres").GetComponent<MovieChanger>();
            _moviePlayer = GameObject.Find("Spheres").GetComponent<MoviePlayer>();
        }

        private void Update()
        {
            bool isPlaying = _moviePlayer != null ? _moviePlayer.IsPlaying : false;
            _playButtonObject.SetActive(!isPlaying);
            _pauseButtonObject.SetActive(isPlaying);
        }
    }
}