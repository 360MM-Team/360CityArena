using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MovieMap.Sound {
    public class SoundManager : MonoBehaviour
    {
        [SerializeField]
        List<Sound> _seList;

        [SerializeField]
        List<Sound> _bgmList;

        [System.Serializable]
        private class Sound
        {
            [SerializeField]
            private string _key;
            [SerializeField]
            private AudioSource _source;

            public string Key => _key;
            public AudioSource Source => _source;
        }

        public void PlaySE(string key)
        {
            var se = _seList.Find(p => p.Key == key);

            if (se != null)
            {
                se.Source.Play();
            }
            else
            {
                Debug.Log($"SE再生が呼ばれましたが、指定された{key} keyが存在しません");
            }
        }

        public void PlayBGM(string key)
        {
            var bgm = _bgmList.Find(p => p.Key == key);

            if (bgm != null)
            {
                bgm.Source.Play();
            }
            else
            {
                Debug.Log($"BGM再生が呼ばれましたが、指定された{key} keyが存在しません");
            }
        }
    }
}