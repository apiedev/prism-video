/*
 * Prism FFmpeg Native Plugin
 *
 * Open-source video decoding library for Unity
 * Uses FFmpeg for codec support (LGPL/GPL depending on build)
 *
 * MIT License - see LICENSE file
 */

#ifndef PRISM_FFMPEG_H
#define PRISM_FFMPEG_H

#include <stdint.h>
#include <stdbool.h>

#ifdef _WIN32
    #ifdef PRISM_FFMPEG_EXPORTS
        #define PRISM_API __declspec(dllexport)
    #else
        #define PRISM_API __declspec(dllimport)
    #endif
#else
    #define PRISM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ============================================================================
 * Types and Constants
 * ========================================================================== */

typedef struct PrismPlayer PrismPlayer;

typedef enum PrismPixelFormat {
    PRISM_PIXEL_FORMAT_RGBA = 0,
    PRISM_PIXEL_FORMAT_BGRA = 1,
    PRISM_PIXEL_FORMAT_RGB24 = 2,
    PRISM_PIXEL_FORMAT_YUV420P = 3
} PrismPixelFormat;

typedef enum PrismState {
    PRISM_STATE_IDLE = 0,
    PRISM_STATE_OPENING = 1,
    PRISM_STATE_READY = 2,
    PRISM_STATE_PLAYING = 3,
    PRISM_STATE_PAUSED = 4,
    PRISM_STATE_STOPPED = 5,
    PRISM_STATE_ERROR = 6,
    PRISM_STATE_END_OF_FILE = 7
} PrismState;

typedef enum PrismError {
    PRISM_OK = 0,
    PRISM_ERROR_INVALID_PLAYER = -1,
    PRISM_ERROR_OPEN_FAILED = -2,
    PRISM_ERROR_NO_VIDEO_STREAM = -3,
    PRISM_ERROR_NO_AUDIO_STREAM = -4,
    PRISM_ERROR_CODEC_NOT_FOUND = -5,
    PRISM_ERROR_CODEC_OPEN_FAILED = -6,
    PRISM_ERROR_DECODE_FAILED = -7,
    PRISM_ERROR_SEEK_FAILED = -8,
    PRISM_ERROR_OUT_OF_MEMORY = -9,
    PRISM_ERROR_NOT_READY = -10,
    PRISM_ERROR_INVALID_PARAMETER = -11
} PrismError;

/* Video frame info */
typedef struct PrismVideoInfo {
    int width;
    int height;
    double fps;
    double duration;        /* Duration in seconds, 0 for live streams */
    int64_t total_frames;
    PrismPixelFormat pixel_format;
    bool is_live;
    const char* codec_name;
} PrismVideoInfo;

/* Audio info */
typedef struct PrismAudioInfo {
    int sample_rate;
    int channels;
    int bits_per_sample;
    double duration;
    const char* codec_name;
} PrismAudioInfo;

/* Callbacks */
typedef void (*PrismLogCallback)(int level, const char* message);
typedef void (*PrismVideoFrameCallback)(void* user_data, uint8_t* data, int width, int height, int stride, double pts);
typedef void (*PrismAudioSamplesCallback)(void* user_data, float* samples, int num_samples, int channels, double pts);

/* ============================================================================
 * Initialization
 * ========================================================================== */

/* Initialize the library (call once at startup) */
PRISM_API int prism_init(void);

/* Shutdown the library (call once at exit) */
PRISM_API void prism_shutdown(void);

/* Get FFmpeg version string */
PRISM_API const char* prism_get_ffmpeg_version(void);

/* Get Prism plugin version string */
PRISM_API const char* prism_get_version(void);

/* Set global log callback */
PRISM_API void prism_set_log_callback(PrismLogCallback callback);

/* ============================================================================
 * Player Lifecycle
 * ========================================================================== */

/* Create a new player instance */
PRISM_API PrismPlayer* prism_player_create(void);

/* Destroy a player instance */
PRISM_API void prism_player_destroy(PrismPlayer* player);

/* ============================================================================
 * Media Control
 * ========================================================================== */

/* Open a media file or URL */
PRISM_API int prism_player_open(PrismPlayer* player, const char* url);

/* Open with custom options (for HLS, RTMP, etc.) */
PRISM_API int prism_player_open_with_options(PrismPlayer* player, const char* url, const char* options);

/* Close the current media */
PRISM_API void prism_player_close(PrismPlayer* player);

/* Start playback */
PRISM_API int prism_player_play(PrismPlayer* player);

/* Pause playback */
PRISM_API int prism_player_pause(PrismPlayer* player);

/* Stop playback */
PRISM_API int prism_player_stop(PrismPlayer* player);

/* Seek to position in seconds */
PRISM_API int prism_player_seek(PrismPlayer* player, double position_seconds);

/* ============================================================================
 * State and Info
 * ========================================================================== */

/* Get current player state */
PRISM_API PrismState prism_player_get_state(PrismPlayer* player);

/* Get last error code */
PRISM_API PrismError prism_player_get_error(PrismPlayer* player);

/* Get last error message */
PRISM_API const char* prism_player_get_error_message(PrismPlayer* player);

/* Check if media has video stream */
PRISM_API bool prism_player_has_video(PrismPlayer* player);

/* Check if media has audio stream */
PRISM_API bool prism_player_has_audio(PrismPlayer* player);

/* Get video info (returns false if no video) */
PRISM_API bool prism_player_get_video_info(PrismPlayer* player, PrismVideoInfo* info);

/* Get audio info (returns false if no audio) */
PRISM_API bool prism_player_get_audio_info(PrismPlayer* player, PrismAudioInfo* info);

/* Get current playback position in seconds */
PRISM_API double prism_player_get_position(PrismPlayer* player);

/* Get total duration in seconds (0 for live streams) */
PRISM_API double prism_player_get_duration(PrismPlayer* player);

/* Check if current media is a live stream */
PRISM_API bool prism_player_is_live(PrismPlayer* player);

/* ============================================================================
 * Frame Access
 * ========================================================================== */

/* Update decoder - call this each frame from Unity
 * Returns number of frames decoded, or negative on error */
PRISM_API int prism_player_update(PrismPlayer* player, double delta_time);

/* Get the latest decoded video frame
 * Returns pointer to RGBA pixel data, or NULL if no frame available
 * The pointer is valid until the next call to update or close */
PRISM_API uint8_t* prism_player_get_video_frame(PrismPlayer* player, int* out_width, int* out_height, int* out_stride);

/* Get video frame timestamp (presentation time) */
PRISM_API double prism_player_get_video_pts(PrismPlayer* player);

/* Copy video frame to provided buffer
 * Buffer must be at least width * height * 4 bytes (RGBA) */
PRISM_API int prism_player_copy_video_frame(PrismPlayer* player, uint8_t* dest_buffer, int dest_stride);

/* ============================================================================
 * Audio Access
 * ========================================================================== */

/* Get decoded audio samples
 * Returns number of samples available, samples are interleaved floats [-1, 1]
 * Copies up to max_samples to the buffer */
PRISM_API int prism_player_get_audio_samples(PrismPlayer* player, float* buffer, int max_samples);

/* Get audio sample rate (output rate, not source) */
PRISM_API int prism_player_get_audio_sample_rate(PrismPlayer* player);

/* Set audio output sample rate (call before Open, should match Unity's AudioSettings.outputSampleRate) */
PRISM_API void prism_player_set_audio_sample_rate(PrismPlayer* player, int sample_rate);

/* Get number of audio channels */
PRISM_API int prism_player_get_audio_channels(PrismPlayer* player);

/* ============================================================================
 * Settings
 * ========================================================================== */

/* Set output pixel format (default RGBA) */
PRISM_API void prism_player_set_pixel_format(PrismPlayer* player, PrismPixelFormat format);

/* Set looping */
PRISM_API void prism_player_set_loop(PrismPlayer* player, bool loop);

/* Get looping state */
PRISM_API bool prism_player_get_loop(PrismPlayer* player);

/* Set playback speed (1.0 = normal) */
PRISM_API void prism_player_set_speed(PrismPlayer* player, float speed);

/* Get playback speed */
PRISM_API float prism_player_get_speed(PrismPlayer* player);

/* Set volume (0.0 - 1.0) */
PRISM_API void prism_player_set_volume(PrismPlayer* player, float volume);

/* Get volume */
PRISM_API float prism_player_get_volume(PrismPlayer* player);

/* Enable/disable hardware acceleration */
PRISM_API void prism_player_set_hardware_acceleration(PrismPlayer* player, bool enabled);

/* ============================================================================
 * Callbacks (alternative to polling)
 * ========================================================================== */

/* Set callback for video frames */
PRISM_API void prism_player_set_video_callback(PrismPlayer* player, PrismVideoFrameCallback callback, void* user_data);

/* Set callback for audio samples */
PRISM_API void prism_player_set_audio_callback(PrismPlayer* player, PrismAudioSamplesCallback callback, void* user_data);

#ifdef __cplusplus
}
#endif

#endif /* PRISM_FFMPEG_H */
