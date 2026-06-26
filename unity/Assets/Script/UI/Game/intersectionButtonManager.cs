using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using UnityEngine.UI;
using MovieMap.UI;

namespace MovieMap.Core
{
    public class IntersectionButtonManager : MonoBehaviour
    {
        public static List<IntersectionButton> _intersectionButtons;
        public bool IsSelected = false;
        public ArrowButton SelectedArrow;
        sliderUI _sliderUI;
        public void SetButtons()
        {
            _intersectionButtons = new List<IntersectionButton>();
            _sliderUI = GameObject.Find("MapSlider").GetComponent<sliderUI>();
            int cnt = 0;
            // Get intersections and roads extending from them from intersection info (roads from large to nextLarge).
            // Since they are stored in a dictionary, check every intersection.
            Vector2 _scale = new Vector2(GlobalInfo.CoordinateScale.x, GlobalInfo.CoordinateScale.z);
            foreach (var (large, intersectionDictionary) in GlobalInfo.LargeIntersectionReferenceDict)
            {
                List<string> urls = new List<string>();
                List<string> jsonUrls = new List<string>();
                List<Vector2> toPos = new List<Vector2>();
                List<LargeIntersection> nextLarges = new List<LargeIntersection>();
                // intersectionDictionary stores road names and destination intersections (nextLarge).
                foreach (var (path, nextLarge) in intersectionDictionary)
                {
                    if (nextLarge == null)
                    {
                        continue;
                    }
                    string url = "";
                    if (GlobalInfo.LargeIntersectionPathDict.ContainsKey(large) && GlobalInfo.LargeIntersectionPathDict[large].ContainsKey(nextLarge))
                    {
                        url = GlobalInfo.LargeIntersectionPathDict[large][nextLarge];
                    }
                    else
                    {
                        continue;
                    }
                    var jsonUrl = MovieUrlMaker.ChangeURLMovieToJson(url);
                    urls.Add(url);
                    jsonUrls.Add(jsonUrl);
                    nextLarges.Add(nextLarge);
                    toPos.Add(new Vector2(nextLarge.Coordinate.Longitude - GlobalInfo.CenterPosition.x, nextLarge.Coordinate.Latitude - GlobalInfo.CenterPosition.z) * _scale);
                }
                // Initial setup for the button that represents an intersection.
                Vector2 pos = new Vector2(large.Coordinate.Longitude - GlobalInfo.CenterPosition.x, large.Coordinate.Latitude - GlobalInfo.CenterPosition.z) * _scale;
                GameObject _intersectionButton = transform.GetChild(cnt++).gameObject;
                IntersectionButton _button = _intersectionButton.GetComponent<IntersectionButton>();
                // _button.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                // Himeji-specific.
                _button.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                _intersectionButton.SetActive(true);
                _button.SetButton(urls, jsonUrls, pos, toPos, nextLarges, large);
                _button.SetButtonPlace(_sliderUI.width, _sliderUI.height, _sliderUI._initialSize);
                _intersectionButtons.Add(_button);
            }
        }

    }
}
