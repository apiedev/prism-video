# Building the Prism FFmpeg Native Plugin

This document explains how to build the native FFmpeg plugin for different platforms.

## Prerequisites

- CMake 3.16 or later
- C compiler (Visual Studio on Windows, GCC/Clang on Linux/macOS)
- FFmpeg development libraries

## Windows

### Option 1: Download Pre-built FFmpeg

1. Download FFmpeg shared build from https://www.gyan.dev/ffmpeg/builds/
   - Get `ffmpeg-release-full-shared.7z`
   - Or from GitHub: https://github.com/BtbN/FFmpeg-Builds/releases (win64-gpl-shared)

2. Extract to `Native/ffmpeg/windows/`
   ```
   Native/
     ffmpeg/
       windows/
         include/
         lib/
         bin/
   ```

3. Run `build_windows.bat`

4. Copy FFmpeg DLLs from `ffmpeg/windows/bin/*.dll` to `Plugins/Windows/x86_64/`

### Option 2: Use vcpkg

```bash
vcpkg install ffmpeg:x64-windows
cmake -B build -DCMAKE_TOOLCHAIN_FILE=[vcpkg root]/scripts/buildsystems/vcpkg.cmake
cmake --build build --config Release
```

## Linux

### Install FFmpeg Development Libraries

Ubuntu/Debian:
```bash
sudo apt install libavcodec-dev libavformat-dev libswscale-dev libswresample-dev libavutil-dev
```

Fedora:
```bash
sudo dnf install ffmpeg-devel
```

Arch:
```bash
sudo pacman -S ffmpeg
```

### Build

```bash
chmod +x build_unix.sh
./build_unix.sh
```

Or manually:
```bash
mkdir build && cd build
cmake -DCMAKE_BUILD_TYPE=Release -DPRISM_USE_SYSTEM_FFMPEG=ON ..
cmake --build . -j$(nproc)
cmake --install .
```

## macOS

### Install FFmpeg

```bash
brew install ffmpeg
```

### Build

```bash
chmod +x build_unix.sh
./build_unix.sh
```

## Output

After building, the plugin will be installed to:
- Windows: `Plugins/Windows/x86_64/prism_ffmpeg.dll`
- Linux: `Plugins/Linux/x86_64/libprism_ffmpeg.so`
- macOS: `Plugins/macOS/libprism_ffmpeg.dylib`

## FFmpeg Licensing

FFmpeg is available under LGPL or GPL license depending on configuration.

For LGPL compliance (allows proprietary use):
- Link dynamically to FFmpeg (default)
- Do not include GPL-only codecs (x264, x265, etc.)
- Provide source code for any FFmpeg modifications
- Include FFmpeg license notice

For more information: https://ffmpeg.org/legal.html
