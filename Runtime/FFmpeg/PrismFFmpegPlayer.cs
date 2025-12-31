using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Events;
using Prism.Streaming;

namespace Prism.FFmpeg
{
    /// <summary>
    /// High-performance video player using native FFmpeg decoder.
    /// Supports HLS, RTMP, and all FFmpeg-supported formats.
    /// </summary>
    [AddComponentMenu("Prism/Prism FFmpeg Player")]
    public class PrismFFmpegPlayer : MonoBehaviour
    {
        // ============================================================================
        // Serialized Fields
        // ============================================================================

        [Header("Source")]
        [SerializeField] private string _url;
        [SerializeField] private StreamQuality _streamQuality = StreamQuality.Auto;
        [SerializeField] private bool _autoResolveUrls = true;

        [Header("Playback")]
        [SerializeField] private bool _playOnAwake = false;
        [SerializeField] private bool _loop = false;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField, Range(0.25f, 4f)] private float _playbackSpeed = 1f;

        [Header("Output")]
        [SerializeField] private RenderTexture _targetTexture;
        [SerializeField] private Renderer _targetRenderer;
        [SerializeField] private string _texturePropertyName = "_BaseMap"; // URP default (use _MainTex for built-in)

        [Header("Settings")]
        [SerializeField] private bool _useHardwareAcceleration = true;

        [Header("Events")]
        public UnityEvent OnPrepared;
        public UnityEvent OnStarted;
        public UnityEvent OnPaused;
        public UnityEvent OnStopped;
        public UnityEvent OnFinished;
        public UnityEvent<string> OnError;

        // ============================================================================
        // Private Fields
        // ============================================================================

        private IntPtr _player = IntPtr.Zero;
        private Texture2D _videoTexture;
        private PrismFFmpegBridge.PrismState _lastState;
        private bool _initialized;
        private bool _isOpening;
        private StreamInfo _currentStreamInfo;
        private string _resolvedUrl;
        private GCHandle _audioBufferHandle;
        private float[] _audioBuffer;
        private AudioSource _audioSource;

        // Audio ring buffer for smooth playback
        private float[] _audioRingBuffer;
        private int _audioRingWritePos;
        private int _audioRingReadPos;
        private int _audioRingSize;
        private int _audioMaxLatencySamples; // Max samples before we drop to catch up
        private readonly object _audioLock = new object();
        private bool _audioStarted;

        // ============================================================================
        // Properties
        // ============================================================================

        public string Url
        {
            get { return _url; }
            set { _url = value; }
        }

        public PrismFFmpegBridge.PrismState State
        {
            get
            {
                if (_player == IntPtr.Zero)
                    return PrismFFmpegBridge.PrismState.Idle;
                return PrismFFmpegBridge.prism_player_get_state(_player);
            }
        }

        public bool IsPlaying
        {
            get { return State == PrismFFmpegBridge.PrismState.Playing; }
        }

        public bool IsPaused
        {
            get { return State == PrismFFmpegBridge.PrismState.Paused; }
        }

        public bool IsReady
        {
            get
            {
                PrismFFmpegBridge.PrismState state = State;
                return state == PrismFFmpegBridge.PrismState.Ready ||
                       state == PrismFFmpegBridge.PrismState.Playing ||
                       state == PrismFFmpegBridge.PrismState.Paused;
            }
        }

        public double Time
        {
            get
            {
                if (_player == IntPtr.Zero)
                    return 0;
                return PrismFFmpegBridge.prism_player_get_position(_player);
            }
        }

        public double Duration
        {
            get
            {
                if (_player == IntPtr.Zero)
                    return 0;
                return PrismFFmpegBridge.prism_player_get_duration(_player);
            }
        }

        public float NormalizedTime
        {
            get
            {
                double duration = Duration;
                if (duration <= 0)
                    return 0;
                return (float)(Time / duration);
            }
        }

        public bool IsLiveStream
        {
            get
            {
                if (_player == IntPtr.Zero)
                    return false;
                return PrismFFmpegBridge.prism_player_is_live(_player);
            }
        }

        public int VideoWidth
        {
            get
            {
                if (_videoTexture != null)
                    return _videoTexture.width;
                return 0;
            }
        }

        public int VideoHeight
        {
            get
            {
                if (_videoTexture != null)
                    return _videoTexture.height;
                return 0;
            }
        }

        public Texture2D VideoTexture
        {
            get { return _videoTexture; }
        }

        public StreamInfo CurrentStreamInfo
        {
            get { return _currentStreamInfo; }
        }

        public string ResolvedUrl
        {
            get { return _resolvedUrl; }
        }

        public float Volume
        {
            get { return _volume; }
            set
            {
                _volume = Mathf.Clamp01(value);
                if (_player != IntPtr.Zero)
                    PrismFFmpegBridge.prism_player_set_volume(_player, _volume);
                if (_audioSource != null)
                    _audioSource.volume = _volume;
            }
        }

        public float PlaybackSpeed
        {
            get { return _playbackSpeed; }
            set
            {
                _playbackSpeed = Mathf.Clamp(value, 0.25f, 4f);
                if (_player != IntPtr.Zero)
                    PrismFFmpegBridge.prism_player_set_speed(_player, _playbackSpeed);
            }
        }

        public bool Loop
        {
            get { return _loop; }
            set
            {
                _loop = value;
                if (_player != IntPtr.Zero)
                    PrismFFmpegBridge.prism_player_set_loop(_player, _loop);
            }
        }

        // ============================================================================
        // Unity Lifecycle
        // ============================================================================

        private void Awake()
        {
            InitializeLibrary();
        }

        private void Start()
        {
            if (_playOnAwake && !string.IsNullOrEmpty(_url))
            {
                Open(_url);
            }
        }

        private void Update()
        {
            if (_player == IntPtr.Zero || _isOpening)
                return;

            // Update decoder
            int result = PrismFFmpegBridge.prism_player_update(_player, UnityEngine.Time.deltaTime);

            // Check for state changes
            PrismFFmpegBridge.PrismState currentState = State;
            if (currentState != _lastState)
            {
                HandleStateChange(_lastState, currentState);
                _lastState = currentState;
            }

            // Update video texture if playing
            if (currentState == PrismFFmpegBridge.PrismState.Playing ||
                currentState == PrismFFmpegBridge.PrismState.Paused)
            {
                UpdateVideoTexture();
            }

            // Update audio ring buffer from main thread
            if (currentState == PrismFFmpegBridge.PrismState.Playing)
            {
                UpdateAudioBuffer();
            }
        }

        private void OnDestroy()
        {
            Close();
            ShutdownLibrary();
        }

        private void OnApplicationQuit()
        {
            Close();
            ShutdownLibrary();
        }

        // ============================================================================
        // Public Methods
        // ============================================================================

        public void Open(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogError("[PrismFFmpeg] URL is null or empty");
                return;
            }

            _url = url;

            if (_autoResolveUrls)
            {
                IStreamResolver resolver = StreamResolverFactory.GetResolver(url);
                if (!(resolver is DirectUrlResolver))
                {
                    StartCoroutine(OpenWithResolution(url));
                    return;
                }
            }

            OpenDirect(url);
        }

        public void OpenDirect(string url)
        {
            if (_player == IntPtr.Zero)
            {
                CreatePlayer();
            }
            else
            {
                Close();
                CreatePlayer();
            }

            _isOpening = true;
            _resolvedUrl = url;

            Debug.Log("[PrismFFmpeg] Opening: " + url);

            int result = PrismFFmpegBridge.prism_player_open(_player, url);
            _isOpening = false;

            if (result != 0)
            {
                string error = PrismFFmpegBridge.GetErrorMessage(_player);
                Debug.LogError("[PrismFFmpeg] Failed to open: " + error);
                if (OnError != null)
                    OnError.Invoke(error);
                return;
            }

            // Apply settings
            PrismFFmpegBridge.prism_player_set_loop(_player, _loop);
            PrismFFmpegBridge.prism_player_set_volume(_player, _volume);
            PrismFFmpegBridge.prism_player_set_speed(_player, _playbackSpeed);
            PrismFFmpegBridge.prism_player_set_hardware_acceleration(_player, _useHardwareAcceleration);

            // Get video info and create texture
            PrismFFmpegBridge.PrismVideoInfo videoInfo;
            if (PrismFFmpegBridge.prism_player_get_video_info(_player, out videoInfo))
            {
                CreateVideoTexture(videoInfo.width, videoInfo.height);
                Debug.Log("[PrismFFmpeg] Video: " + videoInfo.width + "x" + videoInfo.height +
                          " @ " + videoInfo.fps.ToString("F2") + " fps");
            }

            // Setup audio if available
            if (PrismFFmpegBridge.prism_player_has_audio(_player))
            {
                SetupAudio();
            }

            _lastState = State;
            if (OnPrepared != null)
                OnPrepared.Invoke();

            // Auto-play if configured
            if (_playOnAwake)
            {
                Play();
            }
        }

        public void Play()
        {
            if (_player == IntPtr.Zero)
            {
                if (!string.IsNullOrEmpty(_url))
                {
                    Open(_url);
                }
                return;
            }

            int result = PrismFFmpegBridge.prism_player_play(_player);
            if (result != 0)
            {
                Debug.LogWarning("[PrismFFmpeg] Play failed: " + PrismFFmpegBridge.GetErrorMessage(_player));
            }
        }

        public void Pause()
        {
            if (_player == IntPtr.Zero)
                return;

            PrismFFmpegBridge.prism_player_pause(_player);
        }

        public void Stop()
        {
            if (_player == IntPtr.Zero)
                return;

            PrismFFmpegBridge.prism_player_stop(_player);
        }

        public void Seek(double timeSeconds)
        {
            if (_player == IntPtr.Zero)
                return;

            int result = PrismFFmpegBridge.prism_player_seek(_player, timeSeconds);
            if (result != 0)
            {
                Debug.LogWarning("[PrismFFmpeg] Seek failed");
            }
        }

        public void SeekNormalized(float normalizedTime)
        {
            Seek(normalizedTime * Duration);
        }

        public void Close()
        {
            // Stop audio first
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }
            _audioStarted = false;

            if (_player != IntPtr.Zero)
            {
                PrismFFmpegBridge.prism_player_close(_player);
                PrismFFmpegBridge.prism_player_destroy(_player);
                _player = IntPtr.Zero;
            }

            if (_videoTexture != null)
            {
                Destroy(_videoTexture);
                _videoTexture = null;
            }

            if (_audioBufferHandle.IsAllocated)
            {
                _audioBufferHandle.Free();
            }

            // Clear ring buffer
            lock (_audioLock)
            {
                _audioRingBuffer = null;
                _audioRingWritePos = 0;
                _audioRingReadPos = 0;
            }

            _currentStreamInfo = null;
            _resolvedUrl = null;
        }

        // ============================================================================
        // Private Methods
        // ============================================================================

        private void InitializeLibrary()
        {
            if (_initialized)
                return;

            try
            {
                int result = PrismFFmpegBridge.prism_init();
                if (result != 0)
                {
                    Debug.LogError("[PrismFFmpeg] Failed to initialize library");
                    return;
                }

                _initialized = true;
                Debug.Log("[PrismFFmpeg] Initialized - FFmpeg " + PrismFFmpegBridge.GetFFmpegVersion() +
                          ", Plugin " + PrismFFmpegBridge.GetVersion());
            }
            catch (DllNotFoundException ex)
            {
                Debug.LogError("[PrismFFmpeg] Native library not found. Make sure prism_ffmpeg is built and in the Plugins folder.\n" + ex.Message);
            }
            catch (Exception ex)
            {
                Debug.LogError("[PrismFFmpeg] Initialization failed: " + ex.Message);
            }
        }

        private void ShutdownLibrary()
        {
            if (!_initialized)
                return;

            PrismFFmpegBridge.prism_shutdown();
            _initialized = false;
        }

        private void CreatePlayer()
        {
            _player = PrismFFmpegBridge.prism_player_create();
            if (_player == IntPtr.Zero)
            {
                Debug.LogError("[PrismFFmpeg] Failed to create player");
            }
        }

        private void CreateVideoTexture(int width, int height)
        {
            if (_videoTexture != null)
            {
                if (_videoTexture.width == width && _videoTexture.height == height)
                    return;
                Destroy(_videoTexture);
            }

            _videoTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _videoTexture.filterMode = FilterMode.Bilinear;
            _videoTexture.wrapMode = TextureWrapMode.Clamp;

            // Update target texture
            if (_targetTexture != null)
            {
                // Will blit in update
            }

            // Update renderer material
            if (_targetRenderer != null)
            {
                Material mat = _targetRenderer.material;
                if (mat != null)
                {
                    mat.SetTexture(_texturePropertyName, _videoTexture);
                }
            }
        }

        private void UpdateVideoTexture()
        {
            if (_player == IntPtr.Zero || _videoTexture == null)
                return;

            int width, height, stride;
            IntPtr frameData = PrismFFmpegBridge.prism_player_get_video_frame(_player, out width, out height, out stride);

            if (frameData == IntPtr.Zero)
                return;

            // Resize texture if needed
            if (_videoTexture.width != width || _videoTexture.height != height)
            {
                CreateVideoTexture(width, height);
            }

            // Load frame data directly to texture
            _videoTexture.LoadRawTextureData(frameData, width * height * 4);
            _videoTexture.Apply(false);

            // Blit to render texture if set
            if (_targetTexture != null)
            {
                Graphics.Blit(_videoTexture, _targetTexture);
            }
        }

        private void SetupAudio()
        {
            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
                if (_audioSource == null)
                {
                    _audioSource = gameObject.AddComponent<AudioSource>();
                }
            }

            _audioSource.playOnAwake = false;
            _audioSource.volume = _volume;
            _audioSource.spatialBlend = 0f; // 2D audio
            _audioSource.loop = true; // Keep audio source running

            int sampleRate = PrismFFmpegBridge.prism_player_get_audio_sample_rate(_player);
            int channels = PrismFFmpegBridge.prism_player_get_audio_channels(_player);

            // Create transfer buffer for native calls - larger buffer for fewer P/Invoke calls
            int transferSize = 8192 * channels;
            _audioBuffer = new float[transferSize];
            _audioBufferHandle = GCHandle.Alloc(_audioBuffer, GCHandleType.Pinned);

            // Create ring buffer (1 second worth of audio for smooth playback)
            _audioRingSize = sampleRate * channels;
            _audioRingBuffer = new float[_audioRingSize];
            _audioRingWritePos = 0;
            _audioRingReadPos = 0;
            _audioStarted = false;

            // Max latency before dropping frames (500ms)
            _audioMaxLatencySamples = (sampleRate * channels) / 2;

            Debug.Log("[PrismFFmpeg] Audio: " + sampleRate + " Hz, " + channels + " channels, ring buffer: 1 sec, max latency: 500ms");
        }

        private void UpdateAudioBuffer()
        {
            if (_player == IntPtr.Zero || _audioBuffer == null || !_audioBufferHandle.IsAllocated)
                return;

            if (State != PrismFFmpegBridge.PrismState.Playing)
                return;

            IntPtr bufferPtr = _audioBufferHandle.AddrOfPinnedObject();

            // Keep fetching all available audio samples from native buffer
            // This prevents audio starvation when frame rate varies
            int samplesRead;
            int maxIterations = 100; // Safety limit

            while (maxIterations-- > 0)
            {
                samplesRead = PrismFFmpegBridge.prism_player_get_audio_samples(_player, bufferPtr, _audioBuffer.Length);

                if (samplesRead <= 0)
                    break;

                lock (_audioLock)
                {
                    int used = (_audioRingWritePos - _audioRingReadPos + _audioRingSize) % _audioRingSize;

                    // If buffer is getting too full (latency building up), drop old samples
                    if (used + samplesRead > _audioMaxLatencySamples)
                    {
                        int toDrop = (used + samplesRead) - _audioMaxLatencySamples;
                        _audioRingReadPos = (_audioRingReadPos + toDrop) % _audioRingSize;
                        // Recalculate used after dropping
                        used = (_audioRingWritePos - _audioRingReadPos + _audioRingSize) % _audioRingSize;
                    }

                    // Write new samples
                    int available = _audioRingSize - used - 1;
                    int toWrite = Math.Min(samplesRead, available);

                    for (int i = 0; i < toWrite; i++)
                    {
                        _audioRingBuffer[_audioRingWritePos] = _audioBuffer[i];
                        _audioRingWritePos = (_audioRingWritePos + 1) % _audioRingSize;
                    }

                    // Start audio playback once we have enough data buffered (200ms)
                    if (!_audioStarted)
                    {
                        int buffered = (_audioRingWritePos - _audioRingReadPos + _audioRingSize) % _audioRingSize;
                        // Wait for at least 200ms (40% of max latency) before starting playback
                        if (buffered > _audioMaxLatencySamples * 2 / 5)
                        {
                            _audioStarted = true;
                            _audioSource.Play();
                            Debug.Log("[PrismFFmpeg] Audio playback started with " + (buffered * 1000 / _audioRingSize) + "ms buffered");
                        }
                    }

                    // If ring buffer is full, stop fetching
                    if (available <= 0)
                        break;
                }
            }
        }

        private System.Collections.IEnumerator OpenWithResolution(string url)
        {
            _isOpening = true;

            IStreamResolver resolver = StreamResolverFactory.GetResolver(url);
            Debug.Log("[PrismFFmpeg] Resolving URL with " + resolver.Name + "...");

            System.Threading.Tasks.Task<StreamInfo> task = resolver.ResolveAsync(url, _streamQuality);

            while (!task.IsCompleted)
            {
                yield return null;
            }

            _isOpening = false;

            if (task.IsFaulted)
            {
                string error = "Resolution failed: " + task.Exception.Message;
                Debug.LogError("[PrismFFmpeg] " + error);
                if (OnError != null)
                    OnError.Invoke(error);
                yield break;
            }

            StreamInfo info = task.Result;
            _currentStreamInfo = info;

            if (!info.Success)
            {
                Debug.LogError("[PrismFFmpeg] Resolution failed: " + info.Error);
                if (OnError != null)
                    OnError.Invoke(info.Error);
                yield break;
            }

            if (!string.IsNullOrEmpty(info.Warning))
            {
                Debug.LogWarning("[PrismFFmpeg] " + info.Warning);
            }

            Debug.Log("[PrismFFmpeg] Resolved to: " + info.DirectUrl);
            OpenDirect(info.DirectUrl);
        }

        private void HandleStateChange(PrismFFmpegBridge.PrismState oldState, PrismFFmpegBridge.PrismState newState)
        {
            switch (newState)
            {
                case PrismFFmpegBridge.PrismState.Playing:
                    if (OnStarted != null)
                        OnStarted.Invoke();
                    break;

                case PrismFFmpegBridge.PrismState.Paused:
                    if (OnPaused != null)
                        OnPaused.Invoke();
                    break;

                case PrismFFmpegBridge.PrismState.Stopped:
                    if (OnStopped != null)
                        OnStopped.Invoke();
                    break;

                case PrismFFmpegBridge.PrismState.EndOfFile:
                    if (OnFinished != null)
                        OnFinished.Invoke();
                    if (_loop)
                    {
                        Seek(0);
                        Play();
                    }
                    break;

                case PrismFFmpegBridge.PrismState.Error:
                    string error = PrismFFmpegBridge.GetErrorMessage(_player);
                    Debug.LogError("[PrismFFmpeg] Error: " + error);
                    if (OnError != null)
                        OnError.Invoke(error);
                    break;
            }
        }

        // ============================================================================
        // Audio Filter (for native audio output)
        // ============================================================================

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_audioRingBuffer == null || !_audioStarted)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            lock (_audioLock)
            {
                int available = (_audioRingWritePos - _audioRingReadPos + _audioRingSize) % _audioRingSize;

                if (available == 0)
                {
                    // Buffer underrun - fill with silence
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                int toCopy = Math.Min(data.Length, available);

                // Copy samples directly - AudioSource.volume handles volume control
                for (int i = 0; i < toCopy; i++)
                {
                    data[i] = _audioRingBuffer[_audioRingReadPos];
                    _audioRingReadPos = (_audioRingReadPos + 1) % _audioRingSize;
                }

                // Fill remaining with silence if buffer underrun
                if (toCopy < data.Length)
                {
                    Array.Clear(data, toCopy, data.Length - toCopy);
                }
            }
        }
    }
}
