using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MovieMap.UI
{
    [RequireComponent(typeof(RectTransform))]
    public class ScaleUI : MonoBehaviour
    {
        [SerializeField]
        bool _isInitialized;
        [SerializeField]
        float _scaleTime = 0.5f;
        [SerializeField]
        Vector3 _targetScale;
        [SerializeField]
        UnityEvent _onScaleEvent;
        [SerializeField]
        UnityEvent _onReverseEvent;

        RectTransform _rectTransform;
        Vector3 _initialScale;
        bool _initialized = false;

        void Initialize()
        {
            if (_initialized) { return; }
            _rectTransform = GetComponent<RectTransform>();
            _initialScale = _rectTransform.localScale;
            _initialized = true;
        }

        void Start()
        {
            Initialize();
        }

        public void Scale()
        {
            Initialize();

            Sequence scaleSequence = DOTween.Sequence();
            scaleSequence.AppendCallback(() =>
            {
                if (_isInitialized) { _rectTransform.localScale = _initialScale; }
            });
            scaleSequence.Append(_rectTransform.DOScale(_targetScale, _scaleTime).SetEase(Ease.InOutCirc));
            scaleSequence.OnComplete(() =>
            {
                _onScaleEvent.Invoke();
            });
            scaleSequence.Play();
        }

        public void Reverse()
        {
            Initialize();

            Sequence reverseSequence = DOTween.Sequence();
            reverseSequence.AppendCallback(() =>
            {
                if (_isInitialized) { _rectTransform.localScale = _targetScale; }
            });
            reverseSequence.Append(_rectTransform.DOScale(_initialScale, _scaleTime).SetEase(Ease.InOutCirc));
            reverseSequence.OnComplete(() =>
            {
                _onReverseEvent.Invoke();
            });
            reverseSequence.Play();
        }
    }
}