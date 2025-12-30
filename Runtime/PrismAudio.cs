using UnityEngine;

namespace Prism
{
    public enum PrismAudioMode
    {
        Flat,
        Spatial3D,
        Ambient
    }

    [RequireComponent(typeof(AudioSource))]
    public class PrismAudio : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private PrismPlayer _player;

        [Header("Audio Mode")]
        [SerializeField] private PrismAudioMode _audioMode = PrismAudioMode.Flat;

        [Header("Spatial Settings")]
        [SerializeField] [Range(0f, 1f)] private float _spatialBlend = 1f;
        [SerializeField] private float _minDistance = 1f;
        [SerializeField] private float _maxDistance = 50f;
        [SerializeField] private AudioRolloffMode _rolloffMode = AudioRolloffMode.Logarithmic;

        [Header("Ambient Settings")]
        [SerializeField] [Range(0f, 1f)] private float _ambientSpatialBlend = 0.3f;

        [Header("Volume")]
        [SerializeField] [Range(0f, 1f)] private float _masterVolume = 1f;
        [SerializeField] private bool _muteWhenDistant = true;
        [SerializeField] private float _muteDistance = 100f;

        private AudioSource _audioSource;
        private Transform _listenerTransform;

        public PrismPlayer Player
        {
            get => _player;
            set => _player = value;
        }

        public PrismAudioMode AudioMode
        {
            get => _audioMode;
            set
            {
                _audioMode = value;
                ApplyAudioMode();
            }
        }

        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = Mathf.Clamp01(value);
                UpdateVolume();
            }
        }

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
        }

        private void Start()
        {
            if (_player == null)
            {
                _player = GetComponentInParent<PrismPlayer>();
            }

            FindAudioListener();
            ApplyAudioMode();
        }

        private void Update()
        {
            if (_muteWhenDistant && _listenerTransform != null)
            {
                UpdateDistanceBasedVolume();
            }
        }

        private void FindAudioListener()
        {
            AudioListener listener = FindObjectOfType<AudioListener>();
            if (listener != null)
            {
                _listenerTransform = listener.transform;
            }
        }

        private void ApplyAudioMode()
        {
            if (_audioSource == null)
                return;

            switch (_audioMode)
            {
                case PrismAudioMode.Flat:
                    _audioSource.spatialBlend = 0f;
                    _audioSource.spread = 0f;
                    break;

                case PrismAudioMode.Spatial3D:
                    _audioSource.spatialBlend = _spatialBlend;
                    _audioSource.minDistance = _minDistance;
                    _audioSource.maxDistance = _maxDistance;
                    _audioSource.rolloffMode = _rolloffMode;
                    _audioSource.spread = 60f;
                    break;

                case PrismAudioMode.Ambient:
                    _audioSource.spatialBlend = _ambientSpatialBlend;
                    _audioSource.minDistance = _minDistance * 2f;
                    _audioSource.maxDistance = _maxDistance * 2f;
                    _audioSource.rolloffMode = AudioRolloffMode.Linear;
                    _audioSource.spread = 180f;
                    break;
            }

            UpdateVolume();
        }

        private void UpdateVolume()
        {
            if (_audioSource != null)
            {
                _audioSource.volume = _masterVolume;
            }
        }

        private void UpdateDistanceBasedVolume()
        {
            if (_listenerTransform == null || _audioSource == null)
                return;

            float distance = Vector3.Distance(transform.position, _listenerTransform.position);

            if (distance > _muteDistance)
            {
                _audioSource.volume = 0f;
            }
            else if (distance > _maxDistance)
            {
                float fadeRange = _muteDistance - _maxDistance;
                float fadeProgress = (distance - _maxDistance) / fadeRange;
                _audioSource.volume = _masterVolume * (1f - fadeProgress);
            }
            else
            {
                _audioSource.volume = _masterVolume;
            }
        }

        public void SetSpatialSettings(float minDistance, float maxDistance, AudioRolloffMode rolloffMode)
        {
            _minDistance = minDistance;
            _maxDistance = maxDistance;
            _rolloffMode = rolloffMode;

            if (_audioMode == PrismAudioMode.Spatial3D)
            {
                ApplyAudioMode();
            }
        }

        public void Mute()
        {
            if (_audioSource != null)
            {
                _audioSource.mute = true;
            }
        }

        public void Unmute()
        {
            if (_audioSource != null)
            {
                _audioSource.mute = false;
            }
        }

        public bool IsMuted => _audioSource != null && _audioSource.mute;

        private void OnValidate()
        {
            if (_audioSource != null)
            {
                ApplyAudioMode();
            }
        }
    }
}
