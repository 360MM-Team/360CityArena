using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MovieMap.Common
{
    public class StartEvents : MonoBehaviour
    {
        [SerializeField]
        UnityEvent _onStartEvent;

        void Start()
        {
            // Run after all initialization has completed.
            StartCoroutine(Initialize());
        }

        IEnumerator Initialize()
        {
            yield return null;

            _onStartEvent.Invoke();
        }
    }
}
