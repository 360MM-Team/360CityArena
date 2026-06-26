using UnityEngine;
using UnityEngine.Video;
namespace MovieMap.Core
{
    public class MoviePlayer : MonoBehaviour
    {
        enum PlaybackDirection
        {
            Backward,
            Forward
        }
        [SerializeField]
        Transform _sphereRoot;
        [SerializeField]
        float defaultPlaybackSpeed = 1.0f;
        [SerializeField]
        int defaultSpeedRatio = 1;
        GameObject _currentSphere;
        public GameObject CurrentSphere => _currentSphere;

        public int CurrentFrame => _currentSphere != null ? (int)_currentSphere.GetComponent<VideoPlayer>().frame : -1;

        public bool IsPlaying => _currentSphere != null ? _currentSphere.GetComponent<VideoPlayer>().isPlaying : false;

        private int speedRatio = 1; // Ratio for adjusting video playback speed. Stabilized videos are 5x speed, so if avatar walking matches real-world walking speed, videos need to play at 1/5 avatar speed.

        // Stabilized videos are 5x speed, so if avatar walking matches real-world walking speed, videos need to play at 1/5 avatar speed.
        public float PlaybackSpeed => IsVideoAlmostFinished() ? 0.0f : _playbackSpeed * speedRatio;

        public bool IsMovieFinished { get; set; } = false;

        private float _playbackSpeed = 0.0f;

        private PlaybackDirection _playbackDirection = PlaybackDirection.Forward;


        void Update()
        {
            if (RandomInputManager.GetKey(KeyCode.W))
            {
                switch (_playbackDirection)
                {
                    case PlaybackDirection.Forward:
                        _playbackSpeed = defaultPlaybackSpeed;
                        break;
                    case PlaybackDirection.Backward:
                        PlayBack();
                        break;
                    default:
                        Debug.Log("Invalid Direction is selected");
                        break;
                }
            }
            else
            {
                _playbackSpeed = 0.0f;
            }

            if (RandomInputManager.GetKey(KeyCode.LeftShift))
            {
                speedRatio = defaultSpeedRatio * 2;
            }
            else
            {
                speedRatio = defaultSpeedRatio;
            }
            FixPlayBackSpeed();
            FixSphereRotation();
        }

        private void PlayBack()
        {
            _playbackSpeed = 0.0f;
            PauseMovie();
            long currentFrame = _currentSphere.GetComponent<VideoPlayer>().frame;
            _currentSphere.GetComponent<VideoPlayer>().frame = currentFrame - speedRatio;
            ResumeMovie();
        }

        public void ChangeSphere(GameObject sphere)
        {
            if (_currentSphere != null)
            {
                _currentSphere.transform.parent = _sphereRoot;
                _currentSphere.transform.localPosition = Vector3.zero;
            }
            _currentSphere = sphere;
        }

        public bool SameSphere(GameObject sphere)
        {
            return Equals(_currentSphere, sphere);
        }


        /// <summary>
        /// The sphere's local rotation angle must stay constant so it is not affected by currentPosition rotation.
        /// </summary>
        private void FixSphereRotation()
        {
            if (_currentSphere != null)
            {
                _currentSphere.transform.localEulerAngles = new Vector3(0f, 0f, 0f);
            }
        }

        /// <summary>
        /// Keeps the playback speed of the playing video constant.
        /// This only needs to be set when switching videos and should be moved later.
        /// </summary>
        private void FixPlayBackSpeed()
        {
            // Always apply playback speed.
            if (_currentSphere != null)
            {
                _currentSphere.GetComponent<VideoPlayer>().playbackSpeed = PlaybackSpeed;
            }
        }
        public float GetPlayTime()
        {
            // Ratio of the current played portion to the whole video.
            if (_currentSphere == null) { return -1.0f; }
            var vp = _currentSphere.GetComponent<VideoPlayer>();
            return (float)vp.frame / vp.frameRate;
        }

        public float GetRemainTime()
        {
            // Remaining playback time.
            if (_currentSphere == null) { return -1.0f; }
            var vp = _currentSphere.GetComponent<VideoPlayer>();
            return ((long)vp.frameCount - 1 - vp.frame) / vp.frameRate;
        }

        public void ResumeMovie()
        {
            if (_currentSphere != null)
            {
                var vp = _currentSphere.GetComponent<VideoPlayer>();
                if (!IsVideoAlmostFinished() || _playbackDirection == PlaybackDirection.Backward)
                {
                    vp.GetComponent<VideoPlayer>().Play();
                }
            }
        }

        /// <summary>
        /// Returns true when less than one second remains in the video.
        /// </summary>
        /// <returns></returns>
        public bool IsVideoAlmostFinished()
        {
            float remainTime = GetRemainTime();
            return 0.0f <= remainTime && remainTime < 0.3f;
        }

        /// <summary>
        /// Returns true when the elapsed video time is less than one second.
        /// </summary>
        /// <returns></returns>
        public bool IsReverseVideoAlmostFinished()
        {
            float remainTime = GetPlayTime();
            return 0.0f <= remainTime && remainTime < 0.3f;
        }

        public void PauseMovie()
        {
            if (_currentSphere != null)
            {
                _currentSphere.GetComponent<VideoPlayer>().Pause();
            }
        }

        public void SetBackward()
        {
            _playbackDirection = PlaybackDirection.Backward;
        }
        public void SetForward()
        {
            _playbackDirection = PlaybackDirection.Forward;
        }

        public bool IsGoStraight()
        {
            return _playbackDirection == PlaybackDirection.Forward;
        }
    }
}
