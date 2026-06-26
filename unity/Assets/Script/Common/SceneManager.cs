using DG.Tweening;

using UnityEngine;

namespace MovieMap.Common
{
    public class SceneManager : MonoBehaviour
    {
        public void Initialize()
        {
            Sequence fadeSequence = DOTween.Sequence();
            fadeSequence.Append(transform.Find("Canvas").GetComponent<CanvasGroup>().DOFade(0f, 1.0f));
            fadeSequence.Play();
        }

        public void ChangeScene(string sceneName)
        {
            Sequence fadeSequence = DOTween.Sequence();
            fadeSequence.Append(transform.Find("Canvas").GetComponent<CanvasGroup>().DOFade(1.0f, 1.0f));
            fadeSequence.AppendCallback(() =>
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
            });
            fadeSequence.Append(transform.Find("Canvas").GetComponent<CanvasGroup>().DOFade(0f, 1.0f));
            fadeSequence.Play();
        }
    }
}