using System.Collections.Generic;

using UnityEngine;

namespace MovieMap.Core
{
    public class Arrow : MonoBehaviour
    {
        [SerializeField]
        public GameObject _circle;

        public LargeIntersection NextLarge { get; set; }

        public List<string> Urls;

        public void SetActiveCircle(bool active)
        {
            _circle.SetActive(active);
        }
        void OnTriggerEnter(Collider other)
        {
            SetActiveCircle(true);
        }
        void OnTriggerExit(Collider other)
        {
            SetActiveCircle(false);
        }
    }
}
