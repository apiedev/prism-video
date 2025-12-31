# Prism Video Player - Development Status

**Last Updated:** December 30, 2025

## Project Overview

Prism is an open-source video player for Unity designed as an alternative to AVPro Video. It uses a native FFmpeg plugin to support formats that Unity's built-in VideoPlayer cannot handle (especially HLS streams on Windows).

**Repository:** `git@github.com:apiedev/prism-video.git`

---

## Current State

### What's Working
- ✅ Native FFmpeg plugin builds via GitHub Actions (Windows, Linux, macOS)
- ✅ Basic video playback from direct URLs (MP4, WebM, etc.)
- ✅ HLS stream support (the main advantage over Unity VideoPlayer)
- ✅ yt-dlp integration for URL resolution (Twitch, Dailymotion, etc.)
- ✅ Auto-download of yt-dlp if not installed
- ✅ Basic Unity component (`PrismFFmpegPlayer`)
- ✅ Custom Editor inspector

### What's Partially Working
- ⚠️ **Audio playback** - Has issues (choppy, sometimes missing)
- ⚠️ **Video timing** - Just pushed fixes for A/V sync, needs testing
- ⚠️ **Dailymotion** - Video plays but audio was missing, video was sped up (timing fix just pushed)

### What's Not Working
- ❌ **YouTube** - Returns 403 Forbidden (needs HTTP headers passed to FFmpeg)
- ❌ **Vimeo** - Requires authentication
- ❌ **Hardware acceleration** - Flag exists but not implemented
- ❌ **WebRTC** - Interface exists but no provider implementation

---

## Recent Changes (Tonight's Session)

1. **Created native FFmpeg plugin from scratch**
   - `Native/src/prism_ffmpeg.c` - Full FFmpeg decoder implementation
   - `Native/include/prism_ffmpeg.h` - C API header
   - `Native/CMakeLists.txt` - Cross-platform build system

2. **C# Integration**
   - `Runtime/FFmpeg/PrismFFmpegBridge.cs` - P/Invoke wrapper
   - `Runtime/FFmpeg/PrismFFmpegPlayer.cs` - Unity MonoBehaviour component
   - `Editor/PrismFFmpegPlayerEditor.cs` - Custom inspector

3. **GitHub Actions CI**
   - `.github/workflows/build-native.yml` - Auto-builds for all platforms
   - Git LFS configured for large DLLs

4. **Bug Fixes (just pushed, awaiting rebuild)**
   - Added proper frame timing based on PTS and wall clock
   - Audio ring buffer for smooth playback
   - Frame dropping instead of catching up when behind
   - Removed unnecessary vertical flip from C#
   - Added null checks to prevent NullReferenceExceptions

---

## Next Steps (Priority Order)

### 1. Test A/V Sync Fix
The timing fix was just pushed and is being rebuilt. Test with:
- Direct MP4: `http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4`
- HLS stream: `https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8`
- Dailymotion video

### 2. Fix YouTube Support
YouTube URLs return 403 because they require specific HTTP headers. Need to:
- Update `YtdlpResolver` to extract headers along with URL
- Pass headers to FFmpeg via `prism_player_open_with_options()`
- Update native code to parse and apply HTTP headers

### 3. Audio Issues
If audio is still problematic after the timing fix:
- Check sample rate matching between FFmpeg output and Unity AudioSource
- Verify stereo conversion is working correctly
- May need to resample to Unity's audio sample rate (usually 48000 Hz)

### 4. Performance Optimization
- The native plugin could benefit from a decode thread separate from the main thread
- Consider double-buffering for video frames
- Profile on lower-end hardware

### 5. Additional Features (Lower Priority)
- Hardware acceleration (DXVA2 on Windows, VideoToolbox on macOS)
- WebRTC provider implementation
- Subtitle support
- Multiple audio track support

---

## Testing Checklist

| Source | Status | Notes |
|--------|--------|-------|
| Direct MP4 URL | ✅ Works | Best for testing |
| Direct HLS URL | ✅ Works | Main FFmpeg advantage |
| Twitch Live | ✅ Works | Via yt-dlp |
| Twitch VOD | ✅ Works | Via yt-dlp |
| Dailymotion | ⚠️ Partial | Video works, audio/timing issues |
| YouTube | ❌ Broken | 403 Forbidden |
| Vimeo | ❌ Broken | Requires auth |
| Local files | ? | Not tested |

---

## File Structure

```
prism-video/
├── Native/
│   ├── src/prism_ffmpeg.c      # Main native implementation
│   ├── include/prism_ffmpeg.h  # C API header
│   ├── CMakeLists.txt          # Build system
│   ├── build_windows.bat       # Windows build script
│   ├── build_unix.sh           # Linux/macOS build script
│   └── BUILD.md                # Build instructions
├── Runtime/
│   ├── FFmpeg/
│   │   ├── PrismFFmpegBridge.cs   # P/Invoke wrapper
│   │   ├── PrismFFmpegPlayer.cs   # Main Unity component
│   │   └── Prism.FFmpeg.asmdef
│   ├── Streaming/
│   │   ├── YtdlpResolver.cs       # yt-dlp URL resolution
│   │   ├── YtdlpDownloader.cs     # Auto-download yt-dlp
│   │   └── IStreamResolver.cs     # Resolver interface
│   └── PrismPlayer.cs             # Original Unity VideoPlayer wrapper
├── Editor/
│   ├── PrismFFmpegPlayerEditor.cs
│   ├── PrismPlayerEditor.cs
│   └── PrismMenuItems.cs
├── Plugins/
│   └── Windows/x86_64/            # Built DLLs (via GitHub Actions)
└── .github/workflows/
    └── build-native.yml           # CI build workflow
```

---

## Commands

```bash
# Pull latest including LFS files
git pull && git lfs pull

# Check GitHub Actions build status
# https://github.com/apiedev/prism-video/actions

# Test in Unity
# GameObject → Prism → FFmpeg Player with Screen
# Set URL and enter Play mode
```

---

## Known Issues

1. **Audio sometimes choppy** - Ring buffer and timing fix just pushed
2. **Video orientation** - Was flipped, fix pushed (removed unnecessary flip)
3. **YouTube 403** - Needs HTTP header support
4. **Null reference on error** - Fixed with null checks (just pushed)
