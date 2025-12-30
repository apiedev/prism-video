@echo off
setlocal

echo === Prism FFmpeg Native Plugin Build (Windows) ===
echo.

:: Check for CMake
where cmake >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: CMake not found. Please install CMake and add it to PATH.
    exit /b 1
)

:: Check for FFmpeg
if not exist "ffmpeg\windows\include" (
    echo WARNING: FFmpeg not found at ffmpeg\windows
    echo.
    echo Please download FFmpeg development libraries:
    echo   1. Go to https://www.gyan.dev/ffmpeg/builds/
    echo   2. Download "ffmpeg-release-full-shared.7z"
    echo   3. Extract to: Native\ffmpeg\windows\
    echo      ^(should have: include\, lib\, bin\ folders^)
    echo.
    echo Or download from GitHub releases:
    echo   https://github.com/BtbN/FFmpeg-Builds/releases
    echo   ^(Get the "win64-gpl-shared" build^)
    echo.
    pause
    exit /b 1
)

:: Create build directory
if not exist "build" mkdir build
cd build

:: Configure with CMake
echo Configuring with CMake...
cmake -G "Visual Studio 17 2022" -A x64 ..
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: CMake configuration failed
    cd ..
    exit /b 1
)

:: Build
echo.
echo Building...
cmake --build . --config Release
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Build failed
    cd ..
    exit /b 1
)

:: Install to Plugins folder
echo.
echo Installing to Plugins folder...
cmake --install . --config Release
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Installation failed
    cd ..
    exit /b 1
)

cd ..

echo.
echo === Build Complete ===
echo Plugin installed to: Plugins\Windows\x86_64\
echo.
echo Don't forget to copy FFmpeg DLLs:
echo   Copy ffmpeg\windows\bin\*.dll to Plugins\Windows\x86_64\
echo.

pause
