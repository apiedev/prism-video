using System;
using UnityEngine;

namespace Prism.WebRTC
{
    public interface IPrismWebRTCProvider
    {
        bool IsConnected { get; }
        bool IsReceiving { get; }

        Texture VideoTexture { get; }
        AudioSource AudioOutput { get; }

        event Action OnConnected;
        event Action OnDisconnected;
        event Action<Texture> OnVideoFrameReceived;
        event Action<string> OnError;

        void Connect(string signalingUrl, string streamId);
        void Disconnect();
    }
}
