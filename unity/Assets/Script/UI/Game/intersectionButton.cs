using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.Networking;
using UnityEngine.UI;
using MovieMap.Core;

namespace MovieMap.UI
{
    public class IntersectionButton : MonoBehaviour
    {
        [SerializeField]
        GameObject ArrowButton;
        public Vector2 _position;
        public Vector3 _initialLocalPosition;
        public List<float> _angle = new List<float>();
        public List<GameObject> _arrows = new List<GameObject>();
        List<string> MovieUrls = new List<string>();
        List<string> JsonUrls = new List<string>();
        List<LargeIntersection> NextLarge = new List<LargeIntersection>();
        LargeIntersection FromLarge;

        private Vector2 _prevRect;
        private float _radius = 15.0f;
        Button _button;
        RectTransform rect;
        public void SetButton(List<string> movieUrls, List<string> jsonUrls, Vector2 pos, List<Vector2> toPos, List<LargeIntersection> nextLarges, LargeIntersection fromLarge)
        {
            _position = pos;
            for(int i = 0; i < toPos.Count; ++i)
            {
                var diffX = toPos[i].y - pos.y;
                var diffY = toPos[i].x - pos.x;
                _angle.Add(Mathf.Atan2(diffY, diffX));
                MovieUrls.Add(movieUrls[i]);
                JsonUrls.Add(jsonUrls[i]);
                NextLarge.Add(nextLarges[i]);
                FromLarge = fromLarge;
            }
        }
        public void SetButtonPlace(float width, float height, float size)
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(displayArrow);
            rect = GetComponent<RectTransform>();
            var buttonX = _position.x / width * size;
            var buttonY = _position.y / height * size;
            rect.localPosition = new Vector3(buttonX, buttonY - size, 0);
            _initialLocalPosition = rect.localPosition;
        }
        public void PositionUpdate(Vector3 diff)
        {
            rect.localPosition += diff;
        }
        void displayArrow()
        {
            Transform _buttons = GameObject.Find("arrowButtons").transform;
            var _sliderUI = GameObject.Find("MapSlider").GetComponent<sliderUI>();
            for(int i = 0; i < _buttons.childCount; ++i)
            {
                _buttons.GetChild(i).gameObject.SetActive(false);
            }
            for(int i = 0; i < _angle.Count; ++i)
            {
                Transform _tmpButton = _buttons.transform.GetChild(i);
                _tmpButton.gameObject.SetActive(true);
                _arrows.Add(_tmpButton.gameObject);
                ArrowButton _arrowButton = _arrows[i].GetComponent<ArrowButton>();
                _arrowButton.Jsons.Clear();
                _arrowButton.Urls.Clear();
                _arrowButton.Jsons.Add(JsonUrls[i]);
                _arrowButton.Urls.Add(MovieUrls[i]);
                _arrowButton.ToLarge = NextLarge[i];
                _arrowButton.FromLarge = FromLarge;
                _arrows[i].GetComponent<Button>().onClick.RemoveAllListeners();
                _arrows[i].GetComponent<Button>().onClick.AddListener(() => selectArrowButton(_arrowButton)); 
                // In uGUI, keep the arrow in front of the button by intentionally not placing the arrow button as a child.
                _tmpButton.position = transform.position + new Vector3(Mathf.Sin(_angle[i]), Mathf.Cos(_angle[i]), 0) * _radius * _sliderUI._sliderValue;
                _tmpButton.eulerAngles = transform.eulerAngles + new Vector3(0, 0 , -_angle[i] / Mathf.PI * 180);
            }
        }
        void selectArrowButton(ArrowButton _arrow)
        {
            // Behavior when a direction arrow is selected.
            IntersectionButtonManager _intersectionButtonManager = GameObject.Find("IntersectionButtons").GetComponent<IntersectionButtonManager>();
            _intersectionButtonManager.SelectedArrow = _arrow;
            _intersectionButtonManager.IsSelected = true;
            
            // Get and display the segment index.
            int segmentIndex = GlobalInfo.GetIndexFromSegment(_arrow.FromLarge, _arrow.ToLarge);
            if (segmentIndex >= 0)
            {
                // Log to the console.
                Debug.Log($"========================================");
                Debug.Log($"選択した場所のインデックス番号: {segmentIndex}");
                Debug.Log($"========================================");
            }
        }
    }
}
