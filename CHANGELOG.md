# Changelog

All notable changes to Prism Video Player will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2025-01-01

### Added
- Initial package structure
- `PrismPlayer` - Core video player component wrapping Unity's VideoPlayer
  - Support for VideoClip and URL sources
  - RenderTexture output for flexible display
  - Playback controls (play, pause, stop, seek)
  - Volume and playback speed control
  - Event callbacks for state changes
- `PrismRenderer` - Display component for rendering video on surfaces
  - MaterialPropertyBlock-based texture assignment
  - Aspect ratio maintenance with multiple modes
  - Emission support for self-lit screens
- `PrismAudio` - Audio routing and spatial audio for VR
  - Flat, Spatial 3D, and Ambient audio modes
  - Distance-based volume attenuation
  - Configurable spatial settings
- Assembly definitions for Runtime and Editor
