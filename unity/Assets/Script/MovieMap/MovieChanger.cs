using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Video;
using Cysharp.Threading.Tasks;
using System.Threading;
using MovieMap.Service;

namespace MovieMap.Core
{
    // Handles video switching.
    public class MovieChanger : MonoBehaviour
    {
        [SerializeField]
        GameObject[] _spheres;

        [SerializeField]
        Transform _sphereRoot;

        [SerializeField]
        Transform _currentPositionRoot;

        [SerializeField]
        public float _videoSpeed = 0.25f;

        [SerializeField]
        MoviePlayer _moviePlayer;


        public UserLocationService UserLocationService { get; private set; }



        List<GameObject> _readySpheres;
        List<GameObject> _loadingSpheres;
        private static bool _canChangeNextMovieUrl;
        public static bool CanChangeNextMovieUrl => _canChangeNextMovieUrl;

        private static bool _isPositionChanged;
        public bool IsPositionChanged => _isPositionChanged;


        public bool IsMovieFinished { get; set; } = false;

        public bool IsGoStraight => _moviePlayer.IsGoStraight();

        public string NextMovieUrl;
        public List<string> Schedule;

        // public int CurrentFrame => _currentSphere != null ? (int)_currentSphere.GetComponent<VideoPlayer>().frame : -1;

        public bool IsFirstMovieLoaded { get; set; } = false;


        private void Start()
        {
            _readySpheres = new List<GameObject>();
            _loadingSpheres = new List<GameObject>();
            _canChangeNextMovieUrl = true;
            _isPositionChanged = true;
            //PlaybackSpeed = 0.25f;
            NextMovieUrl = "";
            Schedule = new List<string>();
        }

        private void Update()
        {
            if (!IsFirstMovieLoaded) return;
            SwitchToNextVideo();
            PauseReadyVideos();
        }


        /// <summary>
        /// Switches if _readySphere contains a sphere matching NextMovieUrl.
        /// </summary>
        private void SwitchToNextVideo()
        {
            if (UserLocationService is null) return;
            if (NextMovieUrl != "")
            {
                if (ChangeSphere(NextMovieUrl))
                {
                    if (CanChangeNextMovieUrl)
                    {
                        NextMovieUrl = "";
                    }
                    SetCanChangeNextMovieUrl(true);
                }
            }
        }

        /// <summary>
        /// Pauses videos loaded into standby spheres.
        /// This does not need to run every time and should be moved later.
        /// </summary>
        private void PauseReadyVideos()
        {
            foreach (var sphere in _readySpheres)
            {
                var vp = sphere.GetComponent<VideoPlayer>();
                if (vp.GetComponent<VideoPlayerExtension>() == null) { continue; }
                if (vp.frame > vp.GetComponent<VideoPlayerExtension>().StartFrame + 10)
                {
                    vp.Pause();
                }
            }
        }

        public bool ChangeSphere(string url)
        {
            // The URL was retrieved successfully.
            // However, _readySpheres does not contain an item with that URL.
            var sphere = _readySpheres.Find(p => p.GetComponent<VideoPlayer>().url == url);
            // Debug.Log(url);
            if (sphere == null) { return false; }
            Debug.Log("url is loaded!");
            sphere.transform.parent = _currentPositionRoot;
            sphere.transform.localPosition = Vector3.zero;
            _readySpheres.Remove(sphere);
            _moviePlayer.ChangeSphere(sphere);
            SetIsPositionChanged(true);
            NextMovieUrl = "";

            VideoPlayer vp = sphere.GetComponent<VideoPlayer>();
            if (sphere.GetComponent<VideoPlayerExtension>() != null)
            {
                vp.frame = sphere.GetComponent<VideoPlayerExtension>().StartFrame;
            }
            vp.Play();

            // If Schedule is empty, can the upcoming video safely be treated as not inside an intersection?
            // Is it safe to release loading and ready state here?
            // Is it safe to start preparing the next intersection here?
            if (Schedule.Count() == 0)
            {
                Schedule.Clear();
                _readySpheres.Clear();
                _loadingSpheres.Clear();
                Debug.Log($"start CandidateUrls!: {url}");
                var urls = IntersectionAnalyzer.CreateCandidateUrl(UserLocationService.CurrentSegment());
                Debug.Log($"Finish CandidateUrls!: {url}");
                LoadMovies(urls, default);
            }

            return true;
        }

        // URLs passed here must have a valid video file.
        public async UniTask LoadMovie(string url, float playTimeRate, bool clear, CancellationToken token)
        {
            GameObject sphere = null;
            if (clear)
            {
                _loadingSpheres.Clear();
                _readySpheres.Clear();
            }
            foreach (var s in _spheres)
            {
                if (_loadingSpheres.Contains(s)) { continue; }
                if (_readySpheres.Contains(s)) { continue; }
                if (_moviePlayer.SameSphere(s)) { continue; }

                sphere = s;
                break;
            }

            if (sphere == null)
            {
                Debug.LogError("予備のSphereが足りません！");
                return;
            }
            _loadingSpheres.Add(sphere);

            VideoPlayer vp = sphere.GetComponent<VideoPlayer>();
            vp.url = url;
            var frame = (int)(vp.frameCount * playTimeRate);
            if (vp.GetComponent<VideoPlayerExtension>() != null)
            {
                vp.GetComponent<VideoPlayerExtension>().StartFrame = frame;
            }
            await vp.PlayAsync(token);
            if (_loadingSpheres.Remove(sphere))
            {
                _readySpheres.Add(sphere);
            }
        }

        public void SetUserLocationService(UserLocationService locationService)
        {
            UserLocationService = locationService;
        }

        // URLs passed here must have a valid video file.
        public async UniTask LoadMovie(string url, int playFrame, bool clear, CancellationToken token)
        {
            GameObject sphere = null;
            if (clear)
            {
                _loadingSpheres.Clear();
                _readySpheres.Clear();
            }

            foreach (var s in _spheres)
            {
                if (_loadingSpheres.Contains(s)) { continue; }
                if (_readySpheres.Contains(s)) { continue; }
                if (_moviePlayer.SameSphere(s)) { continue; }

                sphere = s;
                break;
            }
            if (sphere == null)
            {
                Debug.LogError("予備のSphereが足りません！");
                return;
            }

            _loadingSpheres.Add(sphere);

            VideoPlayer vp = sphere.GetComponent<VideoPlayer>();
            vp.url = url;
            var frame = playFrame;
            if (vp.GetComponent<VideoPlayerExtension>() != null)
            {
                vp.GetComponent<VideoPlayerExtension>().StartFrame = frame;
            }
            // Debug.Log("video play!");
            // Debug.Log(vp.url);
            await vp.PlayAsync(token);
            // Debug.Log("video loaded!");
            if (_loadingSpheres.Remove(sphere))
            {
                _readySpheres.Add(sphere);
            }
        }

        // Load videos for the URLs one by one from the beginning.
        public void LoadMovies(List<string> urls, CancellationToken token)
        {
            foreach (var url in urls)
            {
                LoadMovie(url, 0, false, token).Forget();
            }
        }


        /// <summary>
        /// Returns true when less than one second remains in the video.
        /// </summary>
        /// <returns></returns>
        public bool IsVideoAlmostFinished()
        {
            return NextMovieUrl == "" && _moviePlayer.IsVideoAlmostFinished();
        }

        public bool IsReverseVideoAlmostFinished()
        {
            return NextMovieUrl == "" && _moviePlayer.IsReverseVideoAlmostFinished();
        }
        public void SetCanChangeNextMovieUrl(bool flag)
        {
            _canChangeNextMovieUrl = flag;
        }
        public void SetIsPositionChanged(bool flag)
        {
            _isPositionChanged = flag;
        }

        public int CurrentFrame()
        {
            return _moviePlayer.CurrentFrame;
        }
    }
}
