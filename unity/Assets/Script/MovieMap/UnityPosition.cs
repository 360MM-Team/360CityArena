using UnityEngine;
namespace MovieMap.Core
{

    public class UnityPosition : MonoBehaviour
    {
        private void Start()
        {

        }
        private void Update()
        {

        }

        public void UpdatePosition(Vector3 position)
        {
            transform.position = position;
        }

        public void UpdateRotation(Quaternion rotation)
        {
            transform.rotation = rotation;
        }
    }
}