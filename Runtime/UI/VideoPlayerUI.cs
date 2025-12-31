using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TMPro;
using Prism.FFmpeg;

namespace Prism.UI
{
    /// <summary>
    /// Video player control UI that works with both PrismPlayer and PrismFFmpegPlayer.
    /// Designed to be compatible with BasisVR's UI styling system while working standalone.
    /// </summary>
    [AddComponentMenu("Prism/Video Player UI")]
    public class VideoPlayerUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        // ============================================================================
        // Serialized Fields
        // ============================================================================

        [Header("Player Reference")]
        [SerializeField] private PrismPlayer _prismPlayer;
        [SerializeField] private PrismFFmpegPlayer _ffmpegPlayer;

        [Header("Controls")]
        [SerializeField] private Button _playPauseButton;
        [SerializeField] private Button _stopButton;
        [SerializeField] private Slider _progressSlider;
        [SerializeField] private Slider _volumeSlider;

        [Header("Display")]
        [SerializeField] private TextMeshProUGUI _timeLabel;
        [SerializeField] private TextMeshProUGUI _statusLabel;
        [SerializeField] private Image _playIcon;
        [SerializeField] private Image _pauseIcon;

        [Header("URL Input")]
        [SerializeField] private TMP_InputField _urlInput;
        [SerializeField] private Button _loadButton;

        [Header("Behavior")]
        [SerializeField] private bool _autoHide = true;
        [SerializeField] private float _hideDelay = 3f;
        [SerializeField] private CanvasGroup _controlsCanvasGroup;

        [Header("Events")]
        public UnityEvent<string> OnUrlSubmitted;

        // ============================================================================
        // Private Fields
        // ============================================================================

        private float _hideTimer;
        private bool _isHovered;
        private bool _isDraggingSlider;
        private bool _wasPlayingBeforeSeek;

        // ============================================================================
        // Properties
        // ============================================================================

        /// <summary>
        /// Returns true if any player is currently playing.
        /// </summary>
        public bool IsPlaying
        {
            get
            {
                if (_prismPlayer != null)
                    return _prismPlayer.IsPlaying;
                if (_ffmpegPlayer != null)
                    return _ffmpegPlayer.IsPlaying;
                return false;
            }
        }

        /// <summary>
        /// Returns true if this is a live stream.
        /// </summary>
        public bool IsLiveStream
        {
            get
            {
                if (_prismPlayer != null)
                    return _prismPlayer.IsLiveStream;
                if (_ffmpegPlayer != null)
                    return _ffmpegPlayer.IsLiveStream;
                return false;
            }
        }

        /// <summary>
        /// Current playback time in seconds.
        /// </summary>
        public double CurrentTime
        {
            get
            {
                if (_prismPlayer != null)
                    return _prismPlayer.Time;
                if (_ffmpegPlayer != null)
                    return _ffmpegPlayer.Time;
                return 0;
            }
        }

        /// <summary>
        /// Total duration in seconds.
        /// </summary>
        public double Duration
        {
            get
            {
                if (_prismPlayer != null)
                    return _prismPlayer.Duration;
                if (_ffmpegPlayer != null)
                    return _ffmpegPlayer.Duration;
                return 0;
            }
        }

        /// <summary>
        /// Current volume (0-1).
        /// </summary>
        public float Volume
        {
            get
            {
                if (_prismPlayer != null)
                    return _prismPlayer.Volume;
                if (_ffmpegPlayer != null)
                    return _ffmpegPlayer.Volume;
                return 1f;
            }
            set
            {
                if (_prismPlayer != null)
                    _prismPlayer.Volume = value;
                if (_ffmpegPlayer != null)
                    _ffmpegPlayer.Volume = value;
            }
        }

        // ============================================================================
        // Unity Lifecycle
        // ============================================================================

        private void Start()
        {
            SetupControls();
            UpdateUI();
        }

        private void Update()
        {
            UpdateUI();
            HandleAutoHide();
        }

        private void OnEnable()
        {
            SubscribeToEvents();
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
        }

        // ============================================================================
        // Setup
        // ============================================================================

        private void SetupControls()
        {
            // Play/Pause button
            if (_playPauseButton != null)
            {
                _playPauseButton.onClick.AddListener(OnPlayPauseClicked);
            }

            // Stop button
            if (_stopButton != null)
            {
                _stopButton.onClick.AddListener(OnStopClicked);
            }

            // Progress slider
            if (_progressSlider != null)
            {
                _progressSlider.onValueChanged.AddListener(OnProgressChanged);

                // Add drag handlers for better seek behavior
                EventTrigger trigger = _progressSlider.gameObject.GetComponent<EventTrigger>();
                if (trigger == null)
                    trigger = _progressSlider.gameObject.AddComponent<EventTrigger>();

                EventTrigger.Entry beginDrag = new EventTrigger.Entry();
                beginDrag.eventID = EventTriggerType.BeginDrag;
                beginDrag.callback.AddListener((data) => OnBeginSeek());
                trigger.triggers.Add(beginDrag);

                EventTrigger.Entry endDrag = new EventTrigger.Entry();
                endDrag.eventID = EventTriggerType.EndDrag;
                endDrag.callback.AddListener((data) => OnEndSeek());
                trigger.triggers.Add(endDrag);
            }

            // Volume slider
            if (_volumeSlider != null)
            {
                _volumeSlider.value = Volume;
                _volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            }

            // Load button
            if (_loadButton != null)
            {
                _loadButton.onClick.AddListener(OnLoadClicked);
            }

            // URL input
            if (_urlInput != null)
            {
                _urlInput.onSubmit.AddListener(OnUrlInputSubmit);
            }
        }

        private void SubscribeToEvents()
        {
            if (_prismPlayer != null)
            {
                _prismPlayer.OnPrepared.AddListener(OnPlayerPrepared);
                _prismPlayer.OnStarted.AddListener(OnPlayerStarted);
                _prismPlayer.OnPaused.AddListener(OnPlayerPaused);
                _prismPlayer.OnStopped.AddListener(OnPlayerStopped);
                _prismPlayer.OnError.AddListener(OnPlayerError);
            }

            if (_ffmpegPlayer != null)
            {
                _ffmpegPlayer.OnPrepared.AddListener(OnPlayerPrepared);
                _ffmpegPlayer.OnStarted.AddListener(OnPlayerStarted);
                _ffmpegPlayer.OnPaused.AddListener(OnPlayerPaused);
                _ffmpegPlayer.OnStopped.AddListener(OnPlayerStopped);
                _ffmpegPlayer.OnError.AddListener(OnPlayerError);
            }
        }

        private void UnsubscribeFromEvents()
        {
            if (_prismPlayer != null)
            {
                _prismPlayer.OnPrepared.RemoveListener(OnPlayerPrepared);
                _prismPlayer.OnStarted.RemoveListener(OnPlayerStarted);
                _prismPlayer.OnPaused.RemoveListener(OnPlayerPaused);
                _prismPlayer.OnStopped.RemoveListener(OnPlayerStopped);
                _prismPlayer.OnError.RemoveListener(OnPlayerError);
            }

            if (_ffmpegPlayer != null)
            {
                _ffmpegPlayer.OnPrepared.RemoveListener(OnPlayerPrepared);
                _ffmpegPlayer.OnStarted.RemoveListener(OnPlayerStarted);
                _ffmpegPlayer.OnPaused.RemoveListener(OnPlayerPaused);
                _ffmpegPlayer.OnStopped.RemoveListener(OnPlayerStopped);
                _ffmpegPlayer.OnError.RemoveListener(OnPlayerError);
            }
        }

        // ============================================================================
        // UI Update
        // ============================================================================

        private void UpdateUI()
        {
            UpdatePlayPauseButton();
            UpdateProgressSlider();
            UpdateTimeLabel();
            UpdateStatusLabel();
        }

        private void UpdatePlayPauseButton()
        {
            if (_playIcon != null)
                _playIcon.gameObject.SetActive(!IsPlaying);
            if (_pauseIcon != null)
                _pauseIcon.gameObject.SetActive(IsPlaying);
        }

        private void UpdateProgressSlider()
        {
            if (_progressSlider == null || _isDraggingSlider)
                return;

            double duration = Duration;
            bool canSeek = duration > 0 && !IsLiveStream;

            _progressSlider.interactable = canSeek;

            if (canSeek)
            {
                _progressSlider.value = (float)(CurrentTime / duration);
            }
            else
            {
                _progressSlider.value = IsLiveStream ? 1f : 0f;
            }
        }

        private void UpdateTimeLabel()
        {
            if (_timeLabel == null)
                return;

            if (IsLiveStream)
            {
                _timeLabel.text = "LIVE";
            }
            else
            {
                string current = FormatTime(CurrentTime);
                string total = FormatTime(Duration);
                _timeLabel.text = $"{current} / {total}";
            }
        }

        private void UpdateStatusLabel()
        {
            if (_statusLabel == null)
                return;

            string status = "";

            if (_ffmpegPlayer != null)
            {
                var state = _ffmpegPlayer.State;
                switch (state)
                {
                    case PrismFFmpegBridge.PrismState.Opening:
                        status = "Loading...";
                        break;
                    case PrismFFmpegBridge.PrismState.Error:
                        status = "Error";
                        break;
                    case PrismFFmpegBridge.PrismState.EndOfFile:
                        status = "Ended";
                        break;
                }

                if (_ffmpegPlayer.IsReconnecting)
                    status = "Reconnecting...";
            }
            else if (_prismPlayer != null)
            {
                var state = _prismPlayer.State;
                switch (state)
                {
                    case PrismState.Resolving:
                    case PrismState.Preparing:
                        status = "Loading...";
                        break;
                    case PrismState.Error:
                        status = "Error";
                        break;
                }
            }

            _statusLabel.text = status;
            _statusLabel.gameObject.SetActive(!string.IsNullOrEmpty(status));
        }

        private string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                return "0:00";

            TimeSpan time = TimeSpan.FromSeconds(seconds);
            if (time.TotalHours >= 1)
                return string.Format("{0}:{1:D2}:{2:D2}", (int)time.TotalHours, time.Minutes, time.Seconds);
            return string.Format("{0}:{1:D2}", time.Minutes, time.Seconds);
        }

        // ============================================================================
        // Auto-Hide
        // ============================================================================

        private void HandleAutoHide()
        {
            if (!_autoHide || _controlsCanvasGroup == null)
                return;

            if (_isHovered || _isDraggingSlider || !IsPlaying)
            {
                _hideTimer = _hideDelay;
                _controlsCanvasGroup.alpha = 1f;
                _controlsCanvasGroup.interactable = true;
                _controlsCanvasGroup.blocksRaycasts = true;
            }
            else
            {
                _hideTimer -= Time.deltaTime;
                if (_hideTimer <= 0)
                {
                    _controlsCanvasGroup.alpha = Mathf.Lerp(_controlsCanvasGroup.alpha, 0f, Time.deltaTime * 3f);
                    _controlsCanvasGroup.interactable = false;
                    _controlsCanvasGroup.blocksRaycasts = false;
                }
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
        }

        // ============================================================================
        // Control Handlers
        // ============================================================================

        private void OnPlayPauseClicked()
        {
            if (IsPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        private void OnStopClicked()
        {
            Stop();
        }

        private void OnProgressChanged(float value)
        {
            if (!_isDraggingSlider)
                return;

            // Only seek when dragging
            SeekNormalized(value);
        }

        private void OnBeginSeek()
        {
            _isDraggingSlider = true;
            _wasPlayingBeforeSeek = IsPlaying;
            Pause();
        }

        private void OnEndSeek()
        {
            _isDraggingSlider = false;
            SeekNormalized(_progressSlider.value);
            if (_wasPlayingBeforeSeek)
                Play();
        }

        private void OnVolumeChanged(float value)
        {
            Volume = value;
        }

        private void OnLoadClicked()
        {
            if (_urlInput != null && !string.IsNullOrEmpty(_urlInput.text))
            {
                LoadUrl(_urlInput.text);
            }
        }

        private void OnUrlInputSubmit(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                LoadUrl(url);
            }
        }

        // ============================================================================
        // Player Event Handlers
        // ============================================================================

        private void OnPlayerPrepared()
        {
            UpdateUI();
        }

        private void OnPlayerStarted()
        {
            UpdateUI();
        }

        private void OnPlayerPaused()
        {
            UpdateUI();
        }

        private void OnPlayerStopped()
        {
            UpdateUI();
        }

        private void OnPlayerError(string error)
        {
            UpdateUI();
            Debug.LogWarning("[VideoPlayerUI] Player error: " + error);
        }

        // ============================================================================
        // Public Control Methods
        // ============================================================================

        /// <summary>
        /// Start playback.
        /// </summary>
        public void Play()
        {
            if (_prismPlayer != null)
                _prismPlayer.Play();
            if (_ffmpegPlayer != null)
                _ffmpegPlayer.Play();
        }

        /// <summary>
        /// Pause playback.
        /// </summary>
        public void Pause()
        {
            if (_prismPlayer != null)
                _prismPlayer.Pause();
            if (_ffmpegPlayer != null)
                _ffmpegPlayer.Pause();
        }

        /// <summary>
        /// Stop playback.
        /// </summary>
        public void Stop()
        {
            if (_prismPlayer != null)
                _prismPlayer.Stop();
            if (_ffmpegPlayer != null)
                _ffmpegPlayer.Stop();
        }

        /// <summary>
        /// Seek to normalized position (0-1).
        /// </summary>
        public void SeekNormalized(float normalizedTime)
        {
            if (_prismPlayer != null)
                _prismPlayer.SeekNormalized(normalizedTime);
            if (_ffmpegPlayer != null)
                _ffmpegPlayer.SeekNormalized(normalizedTime);
        }

        /// <summary>
        /// Seek to time in seconds.
        /// </summary>
        public void Seek(double timeSeconds)
        {
            if (_prismPlayer != null)
                _prismPlayer.Seek(timeSeconds);
            if (_ffmpegPlayer != null)
                _ffmpegPlayer.Seek(timeSeconds);
        }

        /// <summary>
        /// Load a URL and start playing.
        /// </summary>
        public void LoadUrl(string url)
        {
            if (_urlInput != null)
                _urlInput.text = url;

            if (_prismPlayer != null)
            {
                _prismPlayer.Url = url;
                _prismPlayer.Play();
            }

            if (_ffmpegPlayer != null)
            {
                _ffmpegPlayer.Url = url;
                _ffmpegPlayer.Open(url);
                _ffmpegPlayer.Play();
            }

            OnUrlSubmitted?.Invoke(url);
        }

        /// <summary>
        /// Set the player reference at runtime.
        /// </summary>
        public void SetPlayer(PrismPlayer player)
        {
            UnsubscribeFromEvents();
            _prismPlayer = player;
            _ffmpegPlayer = null;
            SubscribeToEvents();
            UpdateUI();
        }

        /// <summary>
        /// Set the FFmpeg player reference at runtime.
        /// </summary>
        public void SetPlayer(PrismFFmpegPlayer player)
        {
            UnsubscribeFromEvents();
            _ffmpegPlayer = player;
            _prismPlayer = null;
            SubscribeToEvents();
            UpdateUI();
        }

        /// <summary>
        /// Show the controls.
        /// </summary>
        public void ShowControls()
        {
            _hideTimer = _hideDelay;
            if (_controlsCanvasGroup != null)
            {
                _controlsCanvasGroup.alpha = 1f;
                _controlsCanvasGroup.interactable = true;
                _controlsCanvasGroup.blocksRaycasts = true;
            }
        }

        /// <summary>
        /// Hide the controls.
        /// </summary>
        public void HideControls()
        {
            _hideTimer = 0f;
        }
    }
}
