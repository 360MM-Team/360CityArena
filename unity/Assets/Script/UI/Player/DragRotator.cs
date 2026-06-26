using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace MovieMap.Core
{
    public class DragRotator : MonoBehaviour
    {
        [SerializeField, Range(0.01f, 0.5f)]
        float _verticalRotateSpeed;
        [SerializeField, Range(0.01f, 0.5f)]
        float _horizontalRotateSpeed;
        Vector2 _prevCursorPosition;
        void Start()
        {
            _prevCursorPosition = Input.mousePosition;
        }
        void Update()
        {
            Vector3 angle = transform.localEulerAngles;
            if (Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0))
            {
                angle.y += (_prevCursorPosition.x - Input.mousePosition.x) * _horizontalRotateSpeed;
                angle.x -= (_prevCursorPosition.y - Input.mousePosition.y) * _verticalRotateSpeed;
                if (angle.x > 85.0f && angle.x < 180.0f) { angle.x = 85.0f; }
                if (angle.x > 180.0f && angle.x < 285.0f) { angle.x = 285.0f; }
                transform.localEulerAngles = angle;
            }
            _prevCursorPosition = Input.mousePosition;
        }
    }
}

