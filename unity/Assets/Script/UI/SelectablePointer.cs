using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MovieMap.UI
{
    [RequireComponent(typeof(Selectable))]
    public class SelectablePointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField]
        UnityEvent _onPointerEnterEvent;
        [SerializeField]
        UnityEvent _onPointerExitEvent;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _onPointerEnterEvent.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _onPointerExitEvent.Invoke();
        }
    }
}