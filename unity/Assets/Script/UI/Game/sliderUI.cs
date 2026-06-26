using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using MovieMap.Core;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using System.Reflection;

namespace MovieMap.UI
{
    public class sliderUI : MonoBehaviour
    {
        public Slider mapSlider;
        readonly float maxMapSize = 2.0f;
        readonly float minMapSize = 0f;
        readonly float initialSize = 0.7f;

        RawImage _image;
        RawImage playerImage;

        [SerializeField]
        MovieMapManager _movieMapManager;

        [SerializeField]
        UnityPosition _unityPosition;

        [SerializeField]
        CameraRotator _camera;


        // Map width and height in latitude and longitude.
        public float width = 100000, height = 100000;
        public float _initialSize = 300.0f;
        public Vector2 _playerPosition;
        public float _sliderValue;
        private PointerEventData pointData;
        private Vector2 _prevCursorPosition;
        private bool _isMapClicked = false;

        bool _initialized = false;


        public void Initialize()
        {
            mapSlider = GetComponent<Slider>();
            mapSlider.maxValue = maxMapSize;
            mapSlider.minValue = minMapSize;
            _image = GameObject.Find("MapUI").GetComponent<RawImage>();
            playerImage = GameObject.Find("PlayerImage").GetComponent<RawImage>();
            mapSlider.value = initialSize;
            width = GlobalInfo.PositionDistance.x;
            height = GlobalInfo.PositionDistance.z;
            pointData = new PointerEventData(EventSystem.current);
            _prevCursorPosition = Input.mousePosition;
            SetFirstMapAnchor();
            _initialized = true;
        }
        void Update()
        {
            if (!_initialized)
            {
                return;
            }
            ChangePlayerAngle();
            DragMap();
            ChangePlayerPosition();
        }
        /// <summary>
        /// Changes the map size by moving the slider below the map.
        /// </summary>
        public void ChangeSize()
        {
            _sliderValue = 1 + mapSlider.value;
            _image.transform.localScale = new Vector3(_sliderValue, _sliderValue, 1);
            SetAnchoredOffsetPosition(0, 0);
        }
        /// <summary>
        /// Aligns the arrow on the map with the avatar direction.
        /// </summary>
        private void ChangePlayerAngle()
        {
            // Rotate the UI arrow using the player (UnityPosition) direction.
            Vector3 euler = _unityPosition.transform.eulerAngles;
            Vector3 angle = Vector3.zero;
            angle.z = -euler.y; // Map Y-axis rotation (yaw) to 2D Z rotation.
            playerImage.transform.eulerAngles = angle;
        }
        /// <summary>
        /// Changes the displayed map position by dragging on the screen.
        /// </summary>
        private void DragMap()
        {
            // Move the map according to the mouse position.
            Vector2 currentCursorPosition = Input.mousePosition;
            // Detect map dragging.
            if (Input.GetMouseButtonDown(0))
            {
                List<RaycastResult> ray = new List<RaycastResult>();
                pointData.position = Input.mousePosition;
                EventSystem.current.RaycastAll(pointData, ray);
                var mapUI = ray.Find(res => res.gameObject.name.Contains("MapUI"));
                if (mapUI.module != null && ray.Count >= 3)
                {
                    _isMapClicked = true;
                    _prevCursorPosition = currentCursorPosition;
                }
            }
            // Set this to false when the map drag is released.
            if (Input.GetMouseButtonUp(0))
            {
                _isMapClicked = false;
            }
            if (Input.GetMouseButton(0) && _isMapClicked)
            {
                Vector2 changedValue = currentCursorPosition - _prevCursorPosition;
                SetAnchoredOffsetPosition(changedValue.x, changedValue.y);
            }
            _prevCursorPosition = currentCursorPosition;
        }
        /// <summary>
        /// Moves the map so the arrow is centered according to the player's map position.
        /// </summary>
        private void ChangePlayerPosition()
        {
            // Calculate the player's relative position on the map.
            var playerX = _unityPosition.transform.position.x / width * _initialSize;
            var playerY = _unityPosition.transform.position.z / height * _initialSize;
            // Show the player's position on the map as an arrow.
            playerImage.rectTransform.localPosition = new Vector3(playerX, playerY - _initialSize, 0);
        }

        /// <summary>
        /// Initially centers the map on the player's location.
        /// </summary>
        private void SetFirstMapAnchor()
        {
            var playerX = _unityPosition.transform.position.x / width * _initialSize;
            var playerY = _unityPosition.transform.position.z / height * _initialSize;
            _playerPosition = new Vector2(playerX, playerY);
            playerImage.rectTransform.localPosition = new Vector3(playerX, playerY - _initialSize, 0);
            var playery = playerY - _initialSize;
            // Determine the map anchor position from the player's position.
            _image.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(-playerX * mapSlider.value, -_initialSize * mapSlider.value, 0), Mathf.Clamp(-playery * mapSlider.value, 0, _initialSize * mapSlider.value));
        }

        /// <summary>
        /// Changes the current anchor position by the offset.
        /// </summary>
        private void SetAnchoredOffsetPosition(float offsetX, float offsetY)
        {
            Vector2 currentAnchoredPosition = _image.rectTransform.anchoredPosition;
            _image.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(currentAnchoredPosition.x + offsetX, -_initialSize * mapSlider.value, 0), Mathf.Clamp(currentAnchoredPosition.y + offsetY, 0, _initialSize * mapSlider.value));
        }
    }
}
