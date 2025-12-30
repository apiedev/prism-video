using System;
using UnityEngine;
using UnityEngine.Video;

namespace Prism
{
    public enum PrismSourceType
    {
        None,
        VideoClip,
        Url,
        WebRTC
    }

    public enum PrismState
    {
        Idle,
        Preparing,
        Ready,
        Playing,
        Paused,
        Error
    }

    [RequireComponent(typeof(AudioSource))]
    public class PrismPlayer : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private PrismSourceType _sourceType = PrismSourceType.None;
        [SerializeField] private VideoClip _videoClip;
        [SerializeField] private string _url;

        [Header("Playback")]
        [SerializeField] private bool _playOnAwake = false;
        [SerializeField] private bool _loop = false;
        [SerializeField] [Range(0f, 1f)] private float _volume = 1f;
        [SerializeField] [Range(0f, 10f)] private float _playbackSpeed = 1f;

        [Header("Output")]
        [SerializeField] private RenderTexture _targetTexture;
        [SerializeField] private Vector2Int _resolution = new Vector2Int(1920, 1080);

        private VideoPlayer _videoPlayer;
        private AudioSource _audioSource;
        private PrismState _state = PrismState.Idle;

        public PrismSourceType SourceType => _sourceType;
        public PrismState State => _state;
        public VideoPlayer VideoPlayer => _videoPlayer;
        public RenderTexture TargetTexture => _targetTexture;

        public double Duration => _videoPlayer != null ? _videoPlayer.length : 0;
        public double Time => _videoPlayer != null ? _videoPlayer.time : 0;
        public float NormalizedTime => Duration > 0 ? (float)(Time / Duration) : 0f;
        public bool IsPlaying => _state == PrismState.Playing;
        public bool IsPrepared => _videoPlayer != null && _videoPlayer.isPrepared;

        public event Action OnPrepared;
        public event Action OnStarted;
        public event Action OnPaused;
        public event Action OnStopped;
        public event Action OnLoopPointReached;
        public event Action<string> OnError;

        private void Awake()
        {
            InitializeComponents();

            if (_playOnAwake && _sourceType != PrismSourceType.None)
            {
                Prepare();
            }
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private void InitializeComponents()
        {
            _audioSource = GetComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;

            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _videoPlayer.SetTargetAudioSource(0, _audioSource);

            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.started += OnVideoStarted;
            _videoPlayer.loopPointReached += OnVideoLoopPointReached;
            _videoPlayer.errorReceived += OnVideoError;

            EnsureRenderTexture();
        }

        private void EnsureRenderTexture()
        {
            if (_targetTexture == null)
            {
                _targetTexture = new RenderTexture(_resolution.x, _resolution.y, 0, RenderTextureFormat.ARGB32);
                _targetTexture.name = $"Prism_RT_{gameObject.name}";
                _targetTexture.Create();
            }
            _videoPlayer.targetTexture = _targetTexture;
        }

        private void ReleaseResources()
        {
            if (_videoPlayer != null)
            {
                _videoPlayer.prepareCompleted -= OnVideoPrepared;
                _videoPlayer.started -= OnVideoStarted;
                _videoPlayer.loopPointReached -= OnVideoLoopPointReached;
                _videoPlayer.errorReceived -= OnVideoError;
            }
        }

        public void SetSource(VideoClip clip)
        {
            _sourceType = PrismSourceType.VideoClip;
            _videoClip = clip;
            _url = null;
            _state = PrismState.Idle;
        }

        public void SetSource(string url)
        {
            _sourceType = PrismSourceType.Url;
            _url = url;
            _videoClip = null;
            _state = PrismState.Idle;
        }

        public void SetResolution(int width, int height)
        {
            _resolution = new Vector2Int(width, height);

            if (_targetTexture != null)
            {
                _targetTexture.Release();
                _targetTexture.width = width;
                _targetTexture.height = height;
                _targetTexture.Create();
            }
        }

        public void Prepare()
        {
            if (_sourceType == PrismSourceType.None)
            {
                Debug.LogWarning("[Prism] No source set. Cannot prepare.");
                return;
            }

            _state = PrismState.Preparing;
            _videoPlayer.isLooping = _loop;
            _videoPlayer.playbackSpeed = _playbackSpeed;
            _audioSource.volume = _volume;

            switch (_sourceType)
            {
                case PrismSourceType.VideoClip:
                    _videoPlayer.source = VideoSource.VideoClip;
                    _videoPlayer.clip = _videoClip;
                    break;
                case PrismSourceType.Url:
                    _videoPlayer.source = VideoSource.Url;
                    _videoPlayer.url = _url;
                    break;
                case PrismSourceType.WebRTC:
                    // WebRTC handling will be implemented separately
                    Debug.LogWarning("[Prism] WebRTC source not yet implemented.");
                    return;
            }

            _videoPlayer.Prepare();
        }

        public void Play()
        {
            if (_state == PrismState.Idle)
            {
                Prepare();
                return;
            }

            if (_state == PrismState.Ready || _state == PrismState.Paused)
            {
                _videoPlayer.Play();
                _state = PrismState.Playing;
            }
        }

        public void Pause()
        {
            if (_state == PrismState.Playing)
            {
                _videoPlayer.Pause();
                _state = PrismState.Paused;
                OnPaused?.Invoke();
            }
        }

        public void Stop()
        {
            _videoPlayer.Stop();
            _state = PrismState.Idle;
            OnStopped?.Invoke();
        }

        public void Seek(double timeSeconds)
        {
            if (_videoPlayer.canSetTime)
            {
                _videoPlayer.time = Mathf.Clamp((float)timeSeconds, 0f, (float)Duration);
            }
        }

        public void SeekNormalized(float normalizedTime)
        {
            Seek(normalizedTime * Duration);
        }

        public void SetVolume(float volume)
        {
            _volume = Mathf.Clamp01(volume);
            if (_audioSource != null)
            {
                _audioSource.volume = _volume;
            }
        }

        public void SetPlaybackSpeed(float speed)
        {
            _playbackSpeed = Mathf.Clamp(speed, 0f, 10f);
            if (_videoPlayer != null)
            {
                _videoPlayer.playbackSpeed = _playbackSpeed;
            }
        }

        public void SetLoop(bool loop)
        {
            _loop = loop;
            if (_videoPlayer != null)
            {
                _videoPlayer.isLooping = loop;
            }
        }

        private void OnVideoPrepared(VideoPlayer source)
        {
            _state = PrismState.Ready;
            OnPrepared?.Invoke();

            if (_playOnAwake)
            {
                Play();
            }
        }

        private void OnVideoStarted(VideoPlayer source)
        {
            _state = PrismState.Playing;
            OnStarted?.Invoke();
        }

        private void OnVideoLoopPointReached(VideoPlayer source)
        {
            OnLoopPointReached?.Invoke();

            if (!_loop)
            {
                _state = PrismState.Ready;
            }
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            _state = PrismState.Error;
            Debug.LogError($"[Prism] Video error: {message}");
            OnError?.Invoke(message);
        }
    }
}
