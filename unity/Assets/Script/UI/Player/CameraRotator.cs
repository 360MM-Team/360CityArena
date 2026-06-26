using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using Unity.VisualScripting;

namespace MovieMap.Core
{
    public class CameraRotator : MonoBehaviour
    {
        private readonly float _verticalRotateSpeed = 0.2f;
        private readonly float _horizontalRotateSpeed = 0.2f;

        private GameObject _cameraTarget;

        Vector2 _prevCursorPosition;

        [SerializeField]
        float _rotateSpeedRatio = 4.0f;

        [SerializeField]
        MoviePlayer _moviePlayer;



        bool _isGUIClicked;
        private PointerEventData pointData;

        private readonly int _numberOfGUIContainsCanvas = 2;

        // Start is called before the first frame update
        void Start()
        {
            _prevCursorPosition = Input.mousePosition;
            _isGUIClicked = false;
            pointData = new PointerEventData(EventSystem.current);
            _cameraTarget = GameObject.Find("PlayerModels");
            SetInitialPerspective();
        }
        /// <summary>
        /// Removes arrows shown on the map when the screen is clicked.
        /// Does not rotate the view by dragging if the clicked location is on the map.
        /// </summary>
        void Update()
        {
            Vector3 currentMousePosition = RandomInputManager.GetMousePosition();
            GUIClicked();
            ChangePerspective(currentMousePosition);
            SetDefaultPerspective();
            DetermineVideoDirection();
            _prevCursorPosition = currentMousePosition;
        }

        public void SetInitialPerspective()
        {
            Vector3 viewTarget = _cameraTarget.transform.position;
            var lookAtRotation = Quaternion.LookRotation(viewTarget - transform.position);
            transform.rotation = lookAtRotation;
        }
        private void GUIClicked()
        {
            if (RandomInputManager.GetMouseButtonDown(0))
            {
                List<RaycastResult> ray = new List<RaycastResult>();
                pointData.position = RandomInputManager.GetMousePosition();
                EventSystem.current.RaycastAll(pointData, ray);
                _isGUIClicked = ray.Count > _numberOfGUIContainsCanvas;
            }
        }

        private void ChangePerspective(Vector3 currentPosition)
        {
            ChangePerspectiveByDrag(currentPosition);
            ChangePerspectiveViaKey();
            if (RandomInputManager.GetMouseButton(0) && !_isGUIClicked)
            {
                Vector3 angle = transform.localEulerAngles;
                angle.y += (_prevCursorPosition.x - currentPosition.x) * _horizontalRotateSpeed;
                angle.x -= (_prevCursorPosition.y - currentPosition.y) * _verticalRotateSpeed;
                if (angle.x > 85.0f && angle.x < 180.0f) { angle.x = 85.0f; }
                if (angle.x > 180.0f && angle.x < 285.0f) { angle.x = 285.0f; }
                transform.localEulerAngles = angle;
            }
        }

        private void ChangePerspectiveByDrag(Vector3 currentPosition)
        {
            if (RandomInputManager.GetMouseButton(0) && !_isGUIClicked)
            {
                Vector3 angle = transform.localEulerAngles;
                angle.y += (_prevCursorPosition.x - currentPosition.x) * _horizontalRotateSpeed;
                angle.x -= (_prevCursorPosition.y - currentPosition.y) * _verticalRotateSpeed;
                if (angle.x > 85.0f && angle.x < 180.0f) { angle.x = 85.0f; }
                if (angle.x > 180.0f && angle.x < 285.0f) { angle.x = 285.0f; }
                transform.localEulerAngles = angle;
            }
        }
        private void ChangePerspectiveViaKey()
        {
            Vector3 angle = transform.localEulerAngles;
            if (RandomInputManager.GetKey(KeyCode.A))
            {
                angle.y -= _verticalRotateSpeed * _rotateSpeedRatio;
            }
            else if (RandomInputManager.GetKey(KeyCode.D))
            {
                angle.y += _verticalRotateSpeed * _rotateSpeedRatio;
            }
            
            // Add vertical rotation via Q/E keys.
            if (RandomInputManager.GetKey(KeyCode.Q))
            {
                angle.x -= _verticalRotateSpeed * _rotateSpeedRatio;
            }
            else if (RandomInputManager.GetKey(KeyCode.E))
            {
                angle.x += _verticalRotateSpeed * _rotateSpeedRatio;
            }
            
            // Apply vertical rotation limits.
            if (angle.x > 85.0f && angle.x < 180.0f) { angle.x = 85.0f; }
            if (angle.x > 180.0f && angle.x < 285.0f) { angle.x = 285.0f; }
            
            transform.localEulerAngles = angle;
        }

        private void SetDefaultPerspective()
        {
            if (RandomInputManager.GetKeyDown(KeyCode.S))
            {
                SetInitialPerspective();
            }
        }

        private void DetermineVideoDirection()
        {
            float verticalAngle = transform.localEulerAngles.y;
            if (Mathf.Cos(verticalAngle / 180.0f * Mathf.PI) >= 0.0f)
            {
                _moviePlayer.SetForward();
            }
            else
            {
                _moviePlayer.SetBackward();
            }
        }
    }
}
