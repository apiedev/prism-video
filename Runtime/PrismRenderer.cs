using UnityEngine;

namespace Prism
{
    [RequireComponent(typeof(Renderer))]
    public class PrismRenderer : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private PrismPlayer _player;

        [Header("Material Settings")]
        [SerializeField] private string _texturePropertyName = "_BaseMap"; // URP default (use _MainTex for built-in)
        [SerializeField] private string _emissionPropertyName = "_EmissionMap";
        [SerializeField] private bool _useEmission = true;
        [SerializeField] [Range(0f, 2f)] private float _emissionIntensity = 1f;

        [Header("Aspect Ratio")]
        [SerializeField] private bool _maintainAspectRatio = true;
        [SerializeField] private AspectRatioMode _aspectRatioMode = AspectRatioMode.FitInside;

        private Renderer _renderer;
        private MaterialPropertyBlock _propertyBlock;
        private int _texturePropertyId;
        private int _emissionPropertyId;
        private int _emissionColorId;
        private Vector3 _originalScale;

        public enum AspectRatioMode
        {
            FitInside,
            FitOutside,
            Stretch
        }

        public PrismPlayer Player
        {
            get => _player;
            set
            {
                _player = value;
                UpdateTexture();
            }
        }

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
            _texturePropertyId = Shader.PropertyToID(_texturePropertyName);
            _emissionPropertyId = Shader.PropertyToID(_emissionPropertyName);
            _emissionColorId = Shader.PropertyToID("_EmissionColor");
            _originalScale = transform.localScale;
        }

        private void Start()
        {
            if (_player == null)
            {
                _player = GetComponentInParent<PrismPlayer>();
            }

            UpdateTexture();
        }

        private void Update()
        {
            if (_player != null && _player.TargetTexture != null)
            {
                UpdateTexture();

                if (_maintainAspectRatio && _player.IsPrepared)
                {
                    UpdateAspectRatio();
                }
            }
        }

        private void UpdateTexture()
        {
            if (_player == null || _player.TargetTexture == null || _renderer == null)
                return;

            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetTexture(_texturePropertyId, _player.TargetTexture);

            if (_useEmission)
            {
                _propertyBlock.SetTexture(_emissionPropertyId, _player.TargetTexture);
                _propertyBlock.SetColor(_emissionColorId, Color.white * _emissionIntensity);
            }

            _renderer.SetPropertyBlock(_propertyBlock);
        }

        private void UpdateAspectRatio()
        {
            if (_player.VideoPlayer == null || !_player.VideoPlayer.isPrepared)
                return;

            float videoWidth = _player.VideoPlayer.width;
            float videoHeight = _player.VideoPlayer.height;

            if (videoWidth <= 0 || videoHeight <= 0)
                return;

            float videoAspect = videoWidth / videoHeight;
            float screenAspect = _originalScale.x / _originalScale.y;

            Vector3 newScale = _originalScale;

            switch (_aspectRatioMode)
            {
                case AspectRatioMode.FitInside:
                    if (videoAspect > screenAspect)
                    {
                        newScale.y = _originalScale.x / videoAspect;
                    }
                    else
                    {
                        newScale.x = _originalScale.y * videoAspect;
                    }
                    break;

                case AspectRatioMode.FitOutside:
                    if (videoAspect > screenAspect)
                    {
                        newScale.x = _originalScale.y * videoAspect;
                    }
                    else
                    {
                        newScale.y = _originalScale.x / videoAspect;
                    }
                    break;

                case AspectRatioMode.Stretch:
                    // Keep original scale
                    break;
            }

            transform.localScale = newScale;
        }

        public void SetEmissionIntensity(float intensity)
        {
            _emissionIntensity = Mathf.Clamp(intensity, 0f, 2f);
            UpdateTexture();
        }

        public void ResetAspectRatio()
        {
            transform.localScale = _originalScale;
        }

        private void OnValidate()
        {
            _texturePropertyId = Shader.PropertyToID(_texturePropertyName);
            _emissionPropertyId = Shader.PropertyToID(_emissionPropertyName);
        }
    }
}
