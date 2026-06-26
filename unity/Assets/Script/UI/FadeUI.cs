using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MovieMap.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public class FadeUI : MonoBehaviour
    {
        [SerializeField]
        bool _isInitialized;
        [SerializeField]
        float _fadeTime = 0.5f;
        [SerializeField]
        float _targetFade;
        [SerializeField]
        UnityEvent _onFadeEvent;
        [SerializeField]
        UnityEvent _onReverseEvent;

        CanvasGroup _canvasGroup;
        float _initialFade;
        bool _initialized = false;

        void Initialize()
        {
            if (_initialized) { return; }
            _canvasGroup = GetComponent<CanvasGroup>();
            _initialFade = _canvasGroup.alpha;
            _initialized = true;
        }

        void Start()
        {
            Initialize();
        }

        public void Fade()
        {
            Initialize();

            Sequence fadeSequence = DOTween.Sequence();
            fadeSequence.AppendCallback(() =>
            {
                if (_isInitialized) { _canvasGroup.alpha = _initialFade; }
            });
            fadeSequence.Append(_canvasGroup.DOFade(_targetFade, _fadeTime).SetEase(Ease.Linear));
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
                if (_isInitialized) { _canvasGroup.alpha = _targetFade; }
            });
            reverseSequence.Append(_canvasGroup.DOFade(_initialFade, _fadeTime).SetEase(Ease.Linear));
            reverseSequence.OnComplete(() =>
            {
                _onReverseEvent.Invoke();
            });
            reverseSequence.Play();
        }
    }
}