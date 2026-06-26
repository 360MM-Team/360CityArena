using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MovieMap.UI;
using UnityEngine.Events;
using DG.Tweening;
using System;

namespace MovieMap.UI
{

    [RequireComponent(typeof(CanvasGroup))]
    public class SceneTransitionAnimator : MonoBehaviour
    {
        // Adapted from FadeUI.cs.
        [SerializeField]
        float _fadeTime = 0.5f;
        [SerializeField]
        UnityEvent _onFadeEvent;
        [SerializeField]
        UnityEvent _onReverseEvent;

        CanvasGroup _canvasGroup;
        float _initialFade;
        bool _initialized = false;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.C))
                PlayTransitionAnim(() => true); // Sample call; safe to remove.
        }

        public void PlayTransitionAnim(Func<bool> canFadein)
        {
            Initialize();
            StartCoroutine(PlayTransitionAnimC(canFadein));
        }

        IEnumerator PlayTransitionAnimC(Func<bool> canFadein)
        {
            Fade();
            yield return new WaitForSeconds(_fadeTime);
            yield return new WaitUntil(canFadein);
            Reverse();
        }

        void Initialize()
        {
            if (_initialized) { return; }
            _canvasGroup = GetComponent<CanvasGroup>();
            _initialFade = _canvasGroup.alpha;
            _initialized = true;
        }

        public void Fade()
        {
            Initialize();

            Sequence fadeSequence = DOTween.Sequence();
            fadeSequence.AppendCallback(() =>
            {
                _canvasGroup.alpha = 0;
            });
            fadeSequence.Append(_canvasGroup.DOFade(1, _fadeTime).SetEase(Ease.Linear));
            fadeSequence.OnComplete(() =>
            {
                _onFadeEvent.Invoke();
            });
            fadeSequence.Play();
        }

        public void Reverse()
        {
            Initialize();

            Sequence reverseSequence = DOTween.Sequence();
            reverseSequence.AppendCallback(() =>
            {
                _canvasGroup.alpha = 1;
            });
            reverseSequence.Append(_canvasGroup.DOFade(0, _fadeTime).SetEase(Ease.Linear));
            reverseSequence.OnComplete(() =>
            {
                _onReverseEvent.Invoke();
            });
            reverseSequence.Play();
        }

    }
}