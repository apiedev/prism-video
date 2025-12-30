# Prism Video Player

High-performance video player for Unity-based VR/XR events. Supports up to 8K video playback and WebRTC streaming for minimal latency.

## Features

- **PrismPlayer** - Core video player using Unity's built-in VideoPlayer
- **PrismRenderer** - Render video to any surface with aspect ratio control
- **PrismAudio** - Spatial audio support for VR (Flat, 3D, Ambient modes)
- **WebRTC Support** - Low-latency streaming via pluggable provider interface

## Installation

### Via Git URL (Unity Package Manager)

1. Open Window > Package Manager
2. Click "+" > "Add package from git URL"
3. Enter: `https://github.com/apiedev/prism-video.git`

### Manual Installation

Clone this repository into your project's `Packages` folder.

## Quick Start

```csharp
using Prism;
using UnityEngine;

public class VideoScreen : MonoBehaviour
{
    [SerializeField] private PrismPlayer player;

    void Start()
    {
        player.SetSource("https://example.com/video.mp4");
        player.Play();
    }
}
```

## Components

### PrismPlayer

Main video player component. Attach to a GameObject with an AudioSource.

```csharp
// Play a video clip
player.SetSource(myVideoClip);
player.Play();

// Play from URL
player.SetSource("https://example.com/video.mp4");
player.Play();

// Control playback
player.Pause();
player.Seek(30.0); // Seek to 30 seconds
player.SetVolume(0.5f);
player.SetPlaybackSpeed(1.5f);
```

### PrismRenderer

Displays video on a Renderer (MeshRenderer, etc).

- Automatically applies video texture to material
- Maintains aspect ratio (FitInside, FitOutside, Stretch)
- Supports emission for self-lit screens in VR

### PrismAudio

Configures audio spatialization for VR environments.

| Mode | Description |
|------|-------------|
| Flat | 2D audio, no spatialization |
| Spatial3D | Full 3D positional audio |
| Ambient | Soft spatialization for background audio |

## WebRTC Streaming

Prism uses a provider interface for WebRTC to support different implementations:

```csharp
using Prism.WebRTC;

public class MyWebRTCSetup : MonoBehaviour
{
    [SerializeField] private PrismWebRTCReceiver receiver;

    void Start()
    {
        // Set your WebRTC provider implementation
        receiver.SetProvider(new MyWebRTCProvider());
        receiver.Connect("wss://signaling.example.com", "stream-123");
    }
}
```

Implement `IPrismWebRTCProvider` to integrate with your preferred WebRTC library.

## Supported Formats

Via Unity's VideoPlayer:
- MP4 (H.264)
- WebM (VP8)
- Ogg (Theora)

## Requirements

- Unity 2021.3 or later
- No external dependencies for core playback

## License

MIT License - See [LICENSE](LICENSE) for details.
