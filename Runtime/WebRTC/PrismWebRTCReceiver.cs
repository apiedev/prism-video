using System;
using UnityEngine;

namespace Prism.WebRTC
{
    public class PrismWebRTCReceiver : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string _signalingUrl;
        [SerializeField] private string _streamId;
        [SerializeField] private bool _autoConnect = false;

        [Header("Output")]
        [SerializeField] private RenderTexture _targetTexture;
        [SerializeField] private Vector2Int _resolution = new Vector2Int(1920, 1080);

        private IPrismWebRTCProvider _provider;
        private bool _isConnected;

        public bool IsConnected => _isConnected;
        public RenderTexture TargetTexture => _targetTexture;
        public string SignalingUrl => _signalingUrl;
        public string StreamId => _streamId;

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        private void Start()
        {
            EnsureRenderTexture();

            if (_autoConnect && !string.IsNullOrEmpty(_signalingUrl))
            {
                Connect();
            }
        }

        private void OnDestroy()
        {
            Disconnect();
        }

        private void EnsureRenderTexture()
        {
            if (_targetTexture == null)
            {
                _targetTexture = new RenderTexture(_resolution.x, _resolution.y, 0, RenderTextureFormat.ARGB32);
                _targetTexture.name = $"Prism_WebRTC_RT_{gameObject.name}";
                _targetTexture.Create();
            }
        }

        public void SetProvider(IPrismWebRTCProvider provider)
        {
            if (_provider != null)
            {
                UnsubscribeFromProvider();
            }

            _provider = provider;
            SubscribeToProvider();
        }

        private void SubscribeToProvider()
        {
            if (_provider == null) return;

            _provider.OnConnected += HandleConnected;
            _provider.OnDisconnected += HandleDisconnected;
            _provider.OnVideoFrameReceived += HandleVideoFrame;
            _provider.OnError += HandleError;
        }

        private void UnsubscribeFromProvider()
        {
            if (_provider == null) return;

            _provider.OnConnected -= HandleConnected;
            _provider.OnDisconnected -= HandleDisconnected;
            _provider.OnVideoFrameReceived -= HandleVideoFrame;
            _provider.OnError -= HandleError;
        }

        public void Connect()
        {
            if (_provider == null)
            {
                Debug.LogWarning("[Prism WebRTC] No provider set. Use SetProvider() to configure a WebRTC implementation.");
                OnError?.Invoke("No WebRTC provider configured");
                return;
            }

            _provider.Connect(_signalingUrl, _streamId);
        }

        public void Connect(string signalingUrl, string streamId)
        {
            _signalingUrl = signalingUrl;
            _streamId = streamId;
            Connect();
        }

        public void Disconnect()
        {
            _provider?.Disconnect();
            _isConnected = false;
        }

        private void HandleConnected()
        {
            _isConnected = true;
            OnConnected?.Invoke();
        }

        private void HandleDisconnected()
        {
            _isConnected = false;
            OnDisconnected?.Invoke();
        }

        private void HandleVideoFrame(Texture sourceTexture)
        {
            if (sourceTexture != null && _targetTexture != null)
            {
                Graphics.Blit(sourceTexture, _targetTexture);
            }
        }

        private void HandleError(string error)
        {
            Debug.LogError($"[Prism WebRTC] {error}");
            OnError?.Invoke(error);
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
    }
}
