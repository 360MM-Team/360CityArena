using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MovieMap.UI
{
    [RequireComponent(typeof(CanvasGroup))]
    public class BlinkUI : MonoBehaviour
    {
        [SerializeField, Range(0, 10.0f)]
        float _appearTime;
        [SerializeField, Range(0, 10.0f)]
        float _appearRemainTime;
        [SerializeField, Range(0, 10.0f)]
        float _vanishTime;
        [SerializeField, Range(0, 10.0f)]
        float _vanishRemainTime;

        enum State
        {
            VANISH,
            APPEARING,
            APPEAR,
            VANISHING,
        }

        State _state;

        CanvasGroup _canvasGroup;

        float _timer;

        void Awake()
        {
            _state = State.VANISH;
            _timer = 0;
        }

        void Start()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        void Update()
        {
            switch (_state)
            {
                case State.VANISH:
                    _timer += Time.deltaTime;
                    if(_timer >= _vanishRemainTime)
                    {
                        _state = State.APPEARING;
                        _timer = 0;
                    }
                    break;
                case State.APPEARING:
                    _canvasGroup.alpha += Time.deltaTime / _appearTime;
                    if(_canvasGroup.alpha >= 1.0f)
                    {
                        _state = State.APPEAR;
                    }
                    break;
                case State.APPEAR:
                    _timer += Time.deltaTime;
                    if (_timer >= _appearRemainTime)
                    {
                        _state = State.VANISHING;
                        _timer = 0;
                    }
                    break;
                case State.VANISHING:
                    _canvasGroup.alpha -= Time.deltaTime / _vanishTime;
                    if (_canvasGroup.alpha <= 0)
                    {
                        _state = State.VANISH;
                    }
                    break;
            }
        }
    }
}