using Cysharp.Threading.Tasks;

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using UnityEngine;
using UnityEngine.Video;

using MovieMap.UI;
using MovieMap.Service;

namespace MovieMap.Core
{
    public class MovieMapManager : MonoBehaviour
    {
        [SerializeField]
        string _areaName;
        [SerializeField]
        string _initialPath;
        [SerializeField]
        string _initialFromPath;
        [SerializeField]
        string _initialToPath;
        [SerializeField]
        UnityPosition _currentUnityPosition;

        [SerializeField]
        CameraRotator _camera;


        [SerializeField]
        sliderUI _sliderUI;
        [SerializeField]
        ArrowManager _arrowManager;
        [SerializeField]
        SceneTransitionAnimator _transitionAnim;

        [SerializeField]
        public MovieChanger _movieChanger;

        private UserLocationService _userLocationService;

        IntersectionButtonManager _intersectionButtonManager;

        Transform _arrowButtons;

        private List<CancellationTokenSource> _cancellationTokens;

        public bool Initialized { get; private set; } = false;

        async UniTask Initialize()
        {
            Application.targetFrameRate = 60;
            _movieChanger = GameObject.Find("Spheres").GetComponent<MovieChanger>();
            _intersectionButtonManager = GameObject.Find("IntersectionButtons").GetComponent<IntersectionButtonManager>();
            _arrowButtons = GameObject.Find("arrowButtons").transform;
            _cancellationTokens = new List<CancellationTokenSource>();

            // Get the first video path and JSON file path.
            string url = MovieUrlMaker.GetRouteMovieURL(_initialPath, _initialFromPath, _initialToPath) ?? GlobalInfo.FirstUrl;

            // Start loading the first video and JSON file.
            UniTask loadingMovie = _movieChanger.LoadMovie(url, 0, false, default);

            // Get information for the first street.
            LargeIntersection toLarge = GetLargeIntersection(_initialPath, _initialFromPath) ?? GlobalInfo.ToLarge;
            LargeIntersection fromLarge = GetLargeIntersection(_initialPath, _initialToPath) ?? GlobalInfo.FromLarge;
            Segment currentSegment = new(fromLarge, toLarge);

            // Calculate the current world coordinates from latitude and longitude.
            Coordinate currentCoordinate = currentSegment.StartCoordinate();
            Vector3 baseCoordinate = new(GlobalInfo.CenterPosition.x, 0f, GlobalInfo.CenterPosition.z);

            // Apply the initial frame rotation from the coordinate dictionary so the initial pose is not zero rotation (0,0,0,0).
            Quaternion initialRotation = new Quaternion();
            try
            {
                var path = currentSegment.Path;
                var offset = currentSegment.OffsetFrame;
                if (!string.IsNullOrEmpty(path) && GlobalInfo.CoordinateDict.ContainsKey(path))
                {
                    var list = GlobalInfo.CoordinateDict[path];
                    if (list != null && list.Count > offset)
                    {
                        initialRotation = list[offset].Item2;
                    }
                }
            }
            catch { /* 取得に失敗した場合は既定の回転(0,0,0,0)のまま */ }

            MovieMapPosition initialPosition = new(currentCoordinate, initialRotation, currentSegment);
            _userLocationService = new UserLocationService(initialPosition);
            _movieChanger.SetUserLocationService(_userLocationService);

            _currentUnityPosition.UpdatePosition(_userLocationService.CalculateCurrentPosition(baseCoordinate, GlobalInfo.CoordinateScale));

            _sliderUI.Initialize();

            _intersectionButtonManager.SetButtons();

            // Wait for the first video to load.
            await loadingMovie;
            _movieChanger.NextMovieUrl = url;
            _movieChanger.IsFirstMovieLoaded = true;
        }

        async void Update()
        {
            if (!GlobalInfo.Initialized)
            {
                return;
            }
            if (!Initialized)
            {
                Initialized = true;
                _movieChanger.IsMovieFinished = false;
                await Initialize();
            }
            FlipArrows();
            FlipReverseArrows();
            ChangeMovie();
            ChangeMovieViaMap();
            UpdatePosition();
        }

        public void ReverseMovieAsync()
        {
            ReverseMovie().Forget();
        }

        // Move to the frame with the closest coordinates in the opposite video.
        public async UniTask ReverseMovie()
        {
            Segment currentSegment = _userLocationService.CurrentSegment();
            // It is not yet verified that _toLarge and _fromLarge actually exist in the dictionary.
            var reverseMovieUrl = GlobalInfo.LargeIntersectionPathDict[currentSegment.ToLarge][currentSegment.FromLarge];
            var reverseJsonUrl = MovieUrlMaker.ChangeURLMovieToJson(reverseMovieUrl);
            Segment reverseSegment = currentSegment.ReverseSegment();
            var reversePath = reverseSegment.Path;
            var frame = CoordinateAnalyzer.CalcNearestFrame(reversePath, _userLocationService.CurrentCoordinate());
            var offsetFrame = currentSegment.OffsetFrame;
            UniTask loadingRevreseMovie = _movieChanger.LoadMovie(reverseMovieUrl, frame - offsetFrame, true, default);
            // This should also be handled by the Changer-side event flow.
            _movieChanger.SetIsPositionChanged(false);
            _transitionAnim.PlayTransitionAnim(() => _movieChanger.IsPositionChanged);
            _movieChanger.Schedule.Clear();
            _movieChanger.NextMovieUrl = reverseMovieUrl;
            _movieChanger.IsMovieFinished = false;
            _userLocationService.UpdateSegment(reverseSegment);
            await loadingRevreseMovie;
        }

        // Return the LargeIntersection where two paths intersect from GlobalInfo.
        LargeIntersection GetLargeIntersection(string path1, string path2)
        {
            if (path1.IsNullOrEmpty() || path2.IsNullOrEmpty())
            {
                return null;
            }
            // List related to path1.
            var path1List = GlobalInfo.LargeIntersectionList.Where(p => p.Points.Where(q => q.Path == path1).Count() > 0);
            return path1List.Where(p => p.Points.Where(q => q.Path == path2).Count() > 0).FirstOrDefault() ?? new LargeIntersection();
        }

        private void FlipArrows()
        {
            if (_movieChanger.IsVideoAlmostFinished() && _arrowManager.IsOldArrowDestroyed)
            {
                _arrowManager.PopArrows(_userLocationService.CurrentSegment());
                _movieChanger.IsMovieFinished = true;
            }
            else if (!_movieChanger.IsVideoAlmostFinished())
            {
                _arrowManager.HideArrows();
                _movieChanger.IsMovieFinished = false;
            }
        }

        private void FlipReverseArrows()
        {
            if (_movieChanger.IsReverseVideoAlmostFinished() && _arrowManager.IsOldReverseArrowDestroyed)
            {
                _arrowManager.PopReverseArrows(_userLocationService.CurrentSegment());
            }
            else if (!_movieChanger.IsReverseVideoAlmostFinished())
            {
                _arrowManager.HideReverseArrows();
            }
        }

        /// <summary>
        /// Changes the video when a red arrow is selected at an intersection.
        /// </summary>
        private void ChangeMovie()
        {
            if (_movieChanger.IsMovieFinished || _movieChanger.IsReverseVideoAlmostFinished())
            {
                // Set the schedule if the direction has been selected.
                if (_arrowManager.IsSelected)
                {
                    _movieChanger.IsMovieFinished = false;
                    _movieChanger.Schedule = _arrowManager.SelectedArrow.Urls;
                    _arrowManager.setIsSelected(false);
                    if (_arrowManager.IsBackSelected)
                    {
                        _userLocationService.UpdateToReverseNextSegment(_arrowManager.SelectedArrow.NextLarge);
                        // This takes some time because the sphere load starts from scratch.
                        _movieChanger.LoadMovie(_movieChanger.Schedule[0], 0, false, default).Forget();
                        _movieChanger.NextMovieUrl = "";
                        _arrowManager.IsBackSelected = false;
                    }
                    else
                    {
                        _userLocationService.UpdateToNextSegment(_arrowManager.SelectedArrow.NextLarge);
                    }
                    _arrowManager.HideArrows();
                    _arrowManager.HideReverseArrows();
                }
                // This if block is not executed alone, but is needed when using intersection videos like MovieMap does.
                if (_movieChanger.Schedule.Count() > 0 && _movieChanger.NextMovieUrl == "")
                {
                    _movieChanger.NextMovieUrl = _movieChanger.Schedule[0];
                    _movieChanger.Schedule.RemoveAt(0);
                    _movieChanger.IsMovieFinished = false;
                    _camera.SetInitialPerspective();
                }
            }
        }

        /// <summary>
        /// Called when switching videos using an arrow on the map.
        /// </summary>
        private void ChangeMovieViaMap()
        {
            // If an arrow was selected from the map.
            if (_intersectionButtonManager != null && _intersectionButtonManager.IsSelected)
            {
                _movieChanger.Schedule = _intersectionButtonManager.SelectedArrow.Urls;
                Segment newSegment = new(_intersectionButtonManager.SelectedArrow.FromLarge, _intersectionButtonManager.SelectedArrow.ToLarge);
                _userLocationService.UpdateSegment(newSegment);
                _intersectionButtonManager.IsSelected = false;
                if (_movieChanger.Schedule.Count > 0)
                {
                    _movieChanger.SetCanChangeNextMovieUrl(false);
                    _movieChanger.SetIsPositionChanged(false);
                    _transitionAnim.PlayTransitionAnim(() => _movieChanger.IsPositionChanged);
                    _movieChanger.NextMovieUrl = _movieChanger.Schedule[0];
                    Debug.Log($"The number of cancellationToken: {_cancellationTokens.Count}");
                    _cancellationTokens.ForEach((token) => token.Cancel());
                    _cancellationTokens.Clear();
                    var cts = new CancellationTokenSource();
                    _cancellationTokens.Add(cts);
                    _movieChanger.LoadMovie(_movieChanger.NextMovieUrl, 2, true, cts.Token).Forget();
                    Debug.Log("load video!");
                    _movieChanger.Schedule.RemoveAt(0);
                }
                for (int i = 0; i < _arrowButtons.childCount; ++i)
                {
                    _arrowButtons.GetChild(i).gameObject.SetActive(false);
                }
            }

        }

        private void UpdatePosition()
        {
            _userLocationService.UpdateCurrentPosition(_movieChanger.CurrentFrame());
            (Vector3 newUnityPosition, Quaternion newUnityRotation) = _userLocationService.CurrentPositionState();
            _currentUnityPosition.UpdatePosition(newUnityPosition);
            _currentUnityPosition.UpdateRotation(newUnityRotation);
        }
    }
}
