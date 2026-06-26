using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace MovieMap.Core
{
    // TODO: Use separate files for avatar selection and scene selection.
    public class ChangeScene : MonoBehaviour
    {
        private AsyncOperation nextScene;

        public void Initialize()
        {
            Sequence fadeSequence = DOTween.Sequence();
            fadeSequence.Append(GameObject.Find("Canvas").GetComponent<CanvasGroup>().DOFade(0f, 1.0f));
            fadeSequence.Play();
        }

        public async void SelectArea(string areaName)
        {
            await GlobalInfo.Initialize(areaName);
            SceneManager.sceneLoaded += AreaLoad;
            GoToTargetScene("Akihabara");
        }
        public void GoToTargetScene(string targetScene)
        {
            Sequence fadeSequence = DOTween.Sequence();
            fadeSequence.Append(GameObject.Find("Canvas").GetComponent<CanvasGroup>().DOFade(1.0f, 1.0f));
            fadeSequence.AppendCallback(() =>
            {
                nextScene = SceneManager.LoadSceneAsync(targetScene);
            });
            fadeSequence.Append(GameObject.Find("Canvas").GetComponent<CanvasGroup>().DOFade(0f, 1.0f));
            fadeSequence.Play();
        }


        private void AreaLoad(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= AreaLoad;
        }
    }
}
