using System;
using System.Runtime.InteropServices;

namespace Prism.FFmpeg
{
    /// <summary>
    /// P/Invoke wrapper for the native Prism FFmpeg plugin.
    /// </summary>
    public static class PrismFFmpegBridge
    {
        #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private const string LIBRARY_NAME = "prism_ffmpeg";
        #elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        private const string LIBRARY_NAME = "prism_ffmpeg";
        #elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
        private const string LIBRARY_NAME = "prism_ffmpeg";
        #elif UNITY_ANDROID
        private const string LIBRARY_NAME = "prism_ffmpeg";
        #elif UNITY_IOS
        private const string LIBRARY_NAME = "__Internal";
        #else
        private const string LIBRARY_NAME = "prism_ffmpeg";
        #endif

        // ============================================================================
        // Enums (must match native definitions)
        // ============================================================================

        public enum PrismPixelFormat
        {
            RGBA = 0,
            BGRA = 1,
            RGB24 = 2,
            YUV420P = 3
        }

        public enum PrismState
        {
            Idle = 0,
            Opening = 1,
            Ready = 2,
            Playing = 3,
            Paused = 4,
            Stopped = 5,
            Error = 6,
            EndOfFile = 7
        }

        public enum PrismError
        {
            OK = 0,
            InvalidPlayer = -1,
            OpenFailed = -2,
            NoVideoStream = -3,
            NoAudioStream = -4,
            CodecNotFound = -5,
            CodecOpenFailed = -6,
            DecodeFailed = -7,
            SeekFailed = -8,
            OutOfMemory = -9,
            NotReady = -10,
            InvalidParameter = -11
        }

        // ============================================================================
        // Structs (must match native definitions)
        // ============================================================================

        [StructLayout(LayoutKind.Sequential)]
        public struct PrismVideoInfo
        {
            public int width;
            public int height;
            public double fps;
            public double duration;
            public long totalFrames;
            public PrismPixelFormat pixelFormat;
            [MarshalAs(UnmanagedType.I1)]
            public bool isLive;
            public IntPtr codecName; // const char*
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PrismAudioInfo
        {
            public int sampleRate;
            public int channels;
            public int bitsPerSample;
            public double duration;
            public IntPtr codecName; // const char*
        }

        // ============================================================================
        // Delegates for callbacks
        // ============================================================================

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void LogCallback(int level, IntPtr message);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void VideoFrameCallback(IntPtr userData, IntPtr data, int width, int height, int stride, double pts);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void AudioSamplesCallback(IntPtr userData, IntPtr samples, int numSamples, int channels, double pts);

        // ============================================================================
        // Initialization
        // ============================================================================

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_init();

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_shutdown();

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_get_ffmpeg_version();

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_get_version();

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_set_log_callback(LogCallback callback);

        // ============================================================================
        // Player Lifecycle
        // ============================================================================

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_player_create();

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_player_destroy(IntPtr player);

        // ============================================================================
        // Media Control
        // ============================================================================

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_player_open(IntPtr player, [MarshalAs(UnmanagedType.LPStr)] string url);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_player_open_with_options(IntPtr player,
            [MarshalAs(UnmanagedType.LPStr)] string url,
            [MarshalAs(UnmanagedType.LPStr)] string options);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_player_close(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_player_play(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_player_pause(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_player_stop(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_player_seek(IntPtr player, double positionSeconds);

        // ============================================================================
        // State and Info
        // ============================================================================

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismState prism_player_get_state(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern PrismError prism_player_get_error(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_player_get_error_message(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool prism_player_has_video(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool prism_player_has_audio(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool prism_player_get_video_info(IntPtr player, out PrismVideoInfo info);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool prism_player_get_audio_info(IntPtr player, out PrismAudioInfo info);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern double prism_player_get_position(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern double prism_player_get_duration(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool prism_player_is_live(IntPtr player);

        // ============================================================================
        // Frame Access
        // ============================================================================

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_player_update(IntPtr player, double deltaTime);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr prism_player_get_video_frame(IntPtr player, out int width, out int height, out int stride);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern double prism_player_get_video_pts(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_player_copy_video_frame(IntPtr player, IntPtr destBuffer, int destStride);

        // ============================================================================
        // Audio Access
        // ============================================================================

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_player_get_audio_samples(IntPtr player, IntPtr buffer, int maxSamples);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_player_get_audio_sample_rate(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int prism_player_get_audio_channels(IntPtr player);

        // ============================================================================
        // Settings
        // ============================================================================

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_player_set_pixel_format(IntPtr player, PrismPixelFormat format);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_player_set_loop(IntPtr player, [MarshalAs(UnmanagedType.I1)] bool loop);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool prism_player_get_loop(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_player_set_speed(IntPtr player, float speed);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern float prism_player_get_speed(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_player_set_volume(IntPtr player, float volume);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern float prism_player_get_volume(IntPtr player);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_player_set_hardware_acceleration(IntPtr player, [MarshalAs(UnmanagedType.I1)] bool enabled);

        // ============================================================================
        // Callbacks
        // ============================================================================

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_player_set_video_callback(IntPtr player, VideoFrameCallback callback, IntPtr userData);

        [DllImport(LIBRARY_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void prism_player_set_audio_callback(IntPtr player, AudioSamplesCallback callback, IntPtr userData);

        // ============================================================================
        // Helper methods
        // ============================================================================

        public static string GetString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return null;
            return Marshal.PtrToStringAnsi(ptr);
        }

        public static string GetFFmpegVersion()
        {
            return GetString(prism_get_ffmpeg_version());
        }

        public static string GetVersion()
        {
            return GetString(prism_get_version());
        }

        public static string GetErrorMessage(IntPtr player)
        {
            return GetString(prism_player_get_error_message(player));
        }
    }
}
