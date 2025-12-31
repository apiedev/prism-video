#!/bin/bash

echo "=== Prism FFmpeg Native Plugin Build (Linux/macOS) ==="
echo

# Check for CMake
if ! command -v cmake &> /dev/null; then
    echo "ERROR: CMake not found. Please install CMake."
    echo "  Ubuntu/Debian: sudo apt install cmake"
    echo "  macOS: brew install cmake"
    exit 1
fi

# Detect platform
PLATFORM=$(uname -s)
echo "Platform: $PLATFORM"

# Check for FFmpeg
USE_SYSTEM_FFMPEG="ON"

if [ "$PLATFORM" = "Linux" ]; then
    if ! pkg-config --exists libavcodec libavformat libswscale libswresample; then
        echo "ERROR: FFmpeg development libraries not found."
        echo "Please install FFmpeg development packages:"
        echo "  Ubuntu/Debian: sudo apt install libavcodec-dev libavformat-dev libswscale-dev libswresample-dev libavutil-dev"
        echo "  Fedora: sudo dnf install ffmpeg-devel"
        echo "  Arch: sudo pacman -S ffmpeg"
        exit 1
    fi
    echo "Found system FFmpeg via pkg-config"
elif [ "$PLATFORM" = "Darwin" ]; then
    if ! pkg-config --exists libavcodec libavformat libswscale libswresample 2>/dev/null; then
        if ! brew list ffmpeg &>/dev/null; then
            echo "ERROR: FFmpeg not found."
            echo "Please install FFmpeg: brew install ffmpeg"
            exit 1
        fi
        # Homebrew FFmpeg might not have pkg-config files
        FFMPEG_PREFIX=$(brew --prefix ffmpeg 2>/dev/null || echo "/usr/local")
        if [ -d "$FFMPEG_PREFIX/include/libavcodec" ]; then
            USE_SYSTEM_FFMPEG="OFF"
            echo "Found Homebrew FFmpeg at: $FFMPEG_PREFIX"
        fi
    else
        echo "Found system FFmpeg via pkg-config"
    fi
fi

# Create build directory
mkdir -p build
cd build

# Configure
echo
echo "Configuring with CMake..."
cmake -DCMAKE_BUILD_TYPE=Release \
      -DPRISM_USE_SYSTEM_FFMPEG=$USE_SYSTEM_FFMPEG \
      -DBUILD_SHARED_LIBS=ON \
      ..

if [ $? -ne 0 ]; then
    echo "ERROR: CMake configuration failed"
    cd ..
    exit 1
fi

# Build
echo
echo "Building..."
cmake --build . --config Release -j$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)

if [ $? -ne 0 ]; then
    echo "ERROR: Build failed"
    cd ..
    exit 1
fi

# Install
echo
echo "Installing to Plugins folder..."
cmake --install . --config Release

if [ $? -ne 0 ]; then
    echo "ERROR: Installation failed"
    cd ..
    exit 1
fi

cd ..

echo
echo "=== Build Complete ==="
if [ "$PLATFORM" = "Linux" ]; then
    echo "Plugin installed to: Plugins/Linux/x86_64/"
else
    echo "Plugin installed to: Plugins/macOS/"
fi
echo
