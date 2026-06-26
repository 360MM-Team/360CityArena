using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MovieMap.UI;
using System.Linq;
using UnityEngine.InputSystem;


namespace MovieMap.Core
{
    public class ArrowManager : MonoBehaviour
    {
        [SerializeField]
        GameObject _arrowRoot;
        [SerializeField]
        GameObject _arrowUIRoot;
        [SerializeField]
        GameObject _arrowPrefab;
        [SerializeField]
        GameObject _arrowPrefabUI;
        [SerializeField]
        MovieChanger _movieChanger;

        [SerializeField]
        SceneTransitionAnimator _transitionAnim;

        List<GameObject> _arrows;
        List<GameObject> _arrowsUI;

        List<GameObject> _reverseArrows;


        bool _isSelected;
        public bool IsSelected => _isSelected;

        public bool IsBackSelected = false;

        bool _areArrowsPopped = false;
        bool _areReverseArrowsPopped = false;
        bool _isSelectingArrow = false;

        Arrow _selectedArrow;
        public Arrow SelectedArrow => _selectedArrow;

        bool _isOldArrowDestroyed = true;
        public bool IsOldArrowDestroyed => _isOldArrowDestroyed;

        bool _isOldReverseArrowDestroyed = true;
        public bool IsOldReverseArrowDestroyed => _isOldReverseArrowDestroyed;


        Vector3 _localAngles = new Vector3(0, 90, 350);


        private void Start()
        {
            _arrows = new List<GameObject>();
            _arrowsUI = new List<GameObject>();
            _reverseArrows = new List<GameObject>();
            _isSelected = false;
        }

        private void Update()
        {
            float _angle = 1000;
            bool directionKeyPressed = false;
            if (RandomInputManager.GetKeyDown(KeyCode.UpArrow))
            {
                _angle = 0;
                directionKeyPressed = true;
            }
            else if (RandomInputManager.GetKeyDown(KeyCode.LeftArrow))
            {
                _angle = -90.0f;
                directionKeyPressed = true;
            }
            else if (RandomInputManager.GetKeyDown(KeyCode.RightArrow))
            {
                _angle = 90.0f;
                directionKeyPressed = true;
            }
            // Fallback when AI-injected hold timing does not align with gate opening.
            else if (RandomInputManager.GetKey(KeyCode.UpArrow))
            {
                _angle = 0;
                directionKeyPressed = true;
            }
            else if (RandomInputManager.GetKey(KeyCode.LeftArrow))
            {
                _angle = -90.0f;
                directionKeyPressed = true;
            }
            else if (RandomInputManager.GetKey(KeyCode.RightArrow))
            {
                _angle = 90.0f;
                directionKeyPressed = true;
            }
            if ((_movieChanger.IsMovieFinished && _movieChanger.IsGoStraight) || (_movieChanger.IsReverseVideoAlmostFinished() && !_movieChanger.IsGoStraight))
            {

                float _minCos = -2;
                int ind = -1;
                if (_angle != 1000)
                {
                    SelectArrowByKey(_angle, ref _minCos, ref ind, ref _arrows);
                    SelectReverseArrowByKey(_angle, ref _minCos, ref ind, ref _reverseArrows);
                }
                if (ind != -1)
                {
                    // Comment this out when showing arrows at real-world coordinates.
                    // _arrowsUI[ind].GetComponent<Arrow>().SetActiveCircle(true);
                    // _transitionAnim.PlayTransitionAnim(() => true);
                    // _movieChanger.IsMovieFinished = false;
                    // SelectArrow(_arrows[ind].GetComponent<Arrow>(), false);


                    // Uncomment this when showing arrows on the canvas.
                    if (_arrows.Count > ind && _movieChanger.IsGoStraight && _arrows[ind] != null && _arrows[ind].GetComponent<Arrow>() != null)
                    {
                        _arrows[ind].GetComponent<Arrow>().SetActiveCircle(true);
                    }
                    if (_reverseArrows.Count > ind && !_movieChanger.IsGoStraight && _reverseArrows[ind] != null && _reverseArrows[ind].GetComponent<Arrow>() != null)
                    {
                        _reverseArrows[ind].GetComponent<Arrow>().SetActiveCircle(true);
                        IsBackSelected = true;
                    }

                    // When an arrow key is pressed, select the arrow without waiting for Enter.
                    if (directionKeyPressed)
                    {
                        if (TryStartActiveArrowSelection(_arrows, false)) return;
                        if (TryStartActiveArrowSelection(_reverseArrows, false)) return;
                    }
                }
            }

            // Keep the legacy Enter-key path as a fallback.
            if (RandomInputManager.GetButtonDown("Enter") || RandomInputManager.GetKeyDown(KeyCode.Return))
            {
                if (TryStartActiveArrowSelection(_arrows, true)) return;
                if (TryStartActiveArrowSelection(_reverseArrows, false)) return;
            }
            // SetGoBackArrowTransform();
        }

        private bool TryStartActiveArrowSelection(List<GameObject> arrows, bool resetMovieFinished)
        {
            if (_isSelectingArrow) return false;

            Arrow activeArrow = FindActiveArrow(arrows);
            if (activeArrow == null) return false;

            _isSelectingArrow = true;
            SelectArrowAfterTransition(activeArrow, resetMovieFinished).Forget();
            return true;
        }

        private Arrow FindActiveArrow(List<GameObject> arrows)
        {
            foreach (var arrowObject in arrows)
            {
                Arrow arrow = GetArrow(arrowObject);
                if (arrow == null || arrow._circle == null) continue;
                if (arrow._circle.activeSelf) return arrow;
            }

            return null;
        }

        private async UniTaskVoid SelectArrowAfterTransition(Arrow arrow, bool resetMovieFinished)
        {
            try
            {
                _transitionAnim.PlayTransitionAnim(() => true);
                await UniTask.Delay(500, cancellationToken: this.GetCancellationTokenOnDestroy());

                if (arrow == null) return;

                SelectArrow(arrow, false);
                if (resetMovieFinished)
                {
                    _movieChanger.IsMovieFinished = false;
                }
            }
            catch (System.OperationCanceledException)
            {
            }
            finally
            {
                _isSelectingArrow = false;
            }
        }

        private Arrow GetArrow(GameObject arrowObject)
        {
            if (arrowObject == null) return null;
            return arrowObject.GetComponent<Arrow>();
        }

        private void SelectArrowByKey(float _angle, ref float _minCos, ref int ind, ref List<GameObject> arrows)
        {
            for (int i = 0; i < arrows.Count; ++i)
            {
                // Add a null check.
                if (arrows[i] != null)
                {
                    float _ang = arrows[i].transform.localEulerAngles.y - _localAngles.y;
                    float _cos = Mathf.Cos((_angle - _ang) / 180 * Mathf.PI);
                    if (_minCos < _cos)
                    {
                        _minCos = _cos;
                        ind = i;
                    }
                }
            }
            for (int i = 0; i < arrows.Count; ++i)
            {
                // Add a null check.
                if (arrows[i] != null && arrows[i].GetComponent<Arrow>() != null)
                {
                    arrows[i].GetComponent<Arrow>().SetActiveCircle(false);
                }
            }
        }

        private void SelectReverseArrowByKey(float _angle, ref float _minCos, ref int ind, ref List<GameObject> arrows)
        {
            _angle += 180;
            for (int i = 0; i < arrows.Count; ++i)
            {
                // Add a null check.
                if (arrows[i] != null)
                {
                    float _ang = arrows[i].transform.localEulerAngles.y - _localAngles.y;
                    float _cos = Mathf.Cos((_angle - _ang) / 180 * Mathf.PI);
                    if (_minCos < _cos)
                    {
                        _minCos = _cos;
                        ind = i;
                    }
                }
            }
            for (int i = 0; i < arrows.Count; ++i)
            {
                // Add a null check.
                if (arrows[i] != null && arrows[i].GetComponent<Arrow>() != null)
                {
                    arrows[i].GetComponent<Arrow>().SetActiveCircle(false);
                }
            }
        }

        public void PopArrows(Segment segment)
        {
            if (_areArrowsPopped) return;
            _isOldArrowDestroyed = false;
            _areArrowsPopped = true;
            var candidates = IntersectionAnalyzer.CalcNextLargeCandidates(segment.FromLarge, segment.ToLarge);
            foreach (var candidate in candidates)
            {
                PopArrow(segment.FromLarge, segment.ToLarge, candidate);
            }
            _selectedArrow = null;
        }

        /// <summary>
        /// Shows the direction arrow reached by following the given segment in reverse.
        /// </summary>
        public void PopReverseArrows(Segment segment)
        {
            if (_areReverseArrowsPopped) return;
            _areReverseArrowsPopped = true;
            _isOldReverseArrowDestroyed = false;
            var candidates = IntersectionAnalyzer.CalcNextLargeCandidates(segment.ToLarge, segment.FromLarge);
            foreach (var candidate in candidates)
            {
                PopReverseArrow(segment.ToLarge, segment.FromLarge, candidate);
            }
            _selectedArrow = null;
        }
        public void PopArrowsUI()
        {
            foreach (var arrow in _arrowsUI)
            {
                arrow.SetActive(!arrow.activeSelf);
            }
            foreach (var arrow in _arrows)
            {
                arrow.SetActive(!arrow.activeSelf);
            }
        }

        void PopArrow(LargeIntersection fromLarge, LargeIntersection middleLarge, LargeIntersection nextLarge)
        {
            List<string> urls = new List<string>();
            List<string> jsons = new List<string>();
            // Exclude videos whose URL contains i_, which are intersection videos.
            // Also exclude videos like A_29-30_A_30-29.
            // Assume the JSON file corresponding to the URL exists.
            var angle = CalcTurnAngle(fromLarge, middleLarge, nextLarge);
            if (angle >= 135.0 && angle <= 225.0) return;
            if (!GlobalInfo.LargeIntersectionPathDict.ContainsKey(middleLarge))
            {
                Debug.Log($"{middleLarge.Points[0].Path} is not contained in LargeIntersectionPathDict");
                return;
            }
            if(!GlobalInfo.LargeIntersectionPathDict[middleLarge].ContainsKey(nextLarge))
            {
                Debug.Log($"nextLarge = {nextLarge}");
                Debug.Log($"{nextLarge.Points[0].Path} is not contained in LargeIntersectionPathDict[{middleLarge.Points[0].Path}]");
                return;
            }
            string url = GlobalInfo.LargeIntersectionPathDict[middleLarge][nextLarge];
            urls.Add(url);
            jsons.Add(MovieUrlMaker.ChangeURLMovieToJson(url));
            var arrow = Instantiate(_arrowPrefab);
            var arrowUI = Instantiate(_arrowPrefabUI);

            arrow.GetComponent<Arrow>().NextLarge = nextLarge;
            arrow.GetComponent<Arrow>().Urls = urls;
            arrow.GetComponent<Arrow>().SetActiveCircle(false);
            arrowUI.GetComponent<Arrow>().SetActiveCircle(false);

            SetArrowTransform(arrow.transform, angle);
            SetArrowUITransform(arrowUI.transform, angle);
            // arrow.SetActive(false);
            arrowUI.SetActive(false);

            _arrows.Add(arrow);
            _arrowsUI.Add(arrowUI);
        }

        void PopReverseArrow(LargeIntersection fromLarge, LargeIntersection middleLarge, LargeIntersection nextLarge)
        {
            List<string> urls = new List<string>();
            // Exclude videos whose URL contains i_, which are intersection videos.
            // Also exclude videos like A_29-30_A_30-29.
            // Assume the JSON file corresponding to the URL exists.
            var angle = CalcTurnAngle(fromLarge, middleLarge, nextLarge);
            if (angle >= 135.0 && angle <= 225.0) return;
            if (!GlobalInfo.LargeIntersectionPathDict.ContainsKey(middleLarge))
            {
                Debug.Log($"{middleLarge.Points[0].Path} is not contained in LargeIntersectionPathDict");
                return;
            }
            if(!GlobalInfo.LargeIntersectionPathDict[middleLarge].ContainsKey(nextLarge))
            {
                Debug.Log($"nextLarge = {nextLarge}");
                Debug.Log($"{nextLarge.Points[0].Path} is not contained in LargeIntersectionPathDict[{middleLarge.Points[0].Path}]");
                return;
            }
            string url = GlobalInfo.LargeIntersectionPathDict[middleLarge][nextLarge];
            urls.Add(url);
            var arrow = Instantiate(_arrowPrefab);

            arrow.GetComponent<Arrow>().NextLarge = nextLarge;
            arrow.GetComponent<Arrow>().Urls = urls;
            arrow.GetComponent<Arrow>().SetActiveCircle(false);

            SetReverseArrowTransform(arrow.transform, angle);

            _reverseArrows.Add(arrow);
        }

        public void HideArrows()
        {
            if (!_areArrowsPopped) return;
            foreach (var arrow in _arrows)
            {
                Destroy(arrow.gameObject);
            }
            foreach (var arrow in _arrowsUI)
            {
                Destroy(arrow.gameObject);
            }
            _arrows.Clear();
            _arrowsUI.Clear();
            Debug.Log("Cleaer arrows");
            IsBackSelected = false;
            // _goBackArrow.GetComponent<Arrow>().SetActiveCircle(false);
            _selectedArrow = null;
            _areArrowsPopped = false;
            _isOldArrowDestroyed = true;
        }

        public void HideReverseArrows()
        {
            if (!_areReverseArrowsPopped) return;
            foreach (var arrow in _reverseArrows)
            {
                Destroy(arrow.gameObject);
            }
            _reverseArrows.Clear();
            _selectedArrow = null;
            _areReverseArrowsPopped = false;
            _isOldReverseArrowDestroyed = true;
        }


        public void SelectArrow(Arrow targetArrow, bool goBack)
        {
            foreach (var arrow in _arrows)
            {
                // Add a null check.
                if (arrow != null && arrow.GetComponent<Arrow>() != null)
                {
                    arrow.GetComponent<Arrow>().SetActiveCircle(false);
                }
            }
            Debug.Log("TargetArrow = " + targetArrow);
            if (targetArrow != null)
            {
                targetArrow.SetActiveCircle(true);
                _isSelected = true;
                _selectedArrow = targetArrow;
            }
        }
        
        public void setIsSelected(bool f)
        {
            _isSelected = f;
        }

        float CalcTurnAngle(LargeIntersection fromLarge, LargeIntersection middleLarge, LargeIntersection nextLarge)
        {
            var inUnitVector = new Vector2(middleLarge.Coordinate.Longitude - fromLarge.Coordinate.Longitude, middleLarge.Coordinate.Latitude - fromLarge.Coordinate.Latitude).normalized;
            var outUnitVector = new Vector2(nextLarge.Coordinate.Longitude - middleLarge.Coordinate.Longitude, nextLarge.Coordinate.Latitude - middleLarge.Coordinate.Latitude).normalized;
            return Vector2.SignedAngle(inUnitVector, outUnitVector);
        }

        void SetArrowTransform(Transform target, float angle)
        {
            target.parent = _arrowRoot.transform;
            target.transform.rotation = target.parent.rotation;
            var sig = angle / Mathf.Abs(angle);
            target.localPosition = new Vector3(Mathf.Min(50, angle * sig) * sig * -0.2f, -5, 20);
            target.transform.localEulerAngles = _localAngles;
            target.Rotate(Vector3.down, angle);
        }

        void SetReverseArrowTransform(Transform target, float angle)
        {
            target.parent = _arrowRoot.transform;
            target.transform.rotation = target.parent.rotation;
            Vector3 currentAngle = _localAngles + new Vector3(0, 180, 0);
            target.transform.localEulerAngles = currentAngle;
            var sig = angle / Mathf.Abs(angle);
            target.localPosition = new Vector3(Mathf.Min(50, angle * sig) * sig * 0.2f, -5, -20);
            target.Rotate(Vector3.down, angle);
        }
        void SetArrowUITransform(Transform target, float angle)
        {
            target.parent = _arrowUIRoot.transform;
            target.localPosition = new Vector3(-200 * Mathf.Sin(angle / 180 * Mathf.PI), 200, 20);
            target.transform.localEulerAngles = new Vector3(0, 0, 90);

            target.Rotate(Vector3.forward, angle);
        }

        public void SetGoBack(bool f)
        {
            IsBackSelected = f;
        }


    }
}
