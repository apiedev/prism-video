/*
 * Prism FFmpeg Native Plugin - Implementation
 *
 * Open-source video decoding library for Unity
 * MIT License
 */

#define PRISM_FFMPEG_EXPORTS

#include "prism_ffmpeg.h"

#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/imgutils.h>
#include <libavutil/opt.h>
#include <libavutil/time.h>
#include <libswscale/swscale.h>
#include <libswresample/swresample.h>

#include <stdlib.h>
#include <string.h>
#include <stdio.h>

#ifdef _WIN32
#include <windows.h>
#else
#include <pthread.h>
#include <unistd.h>
#endif

/* ============================================================================
 * Version Info
 * ========================================================================== */

#define PRISM_VERSION "0.1.0"

/* ============================================================================
 * Internal Structures
 * ========================================================================== */

/* Video frame queue entry */
#define VIDEO_QUEUE_SIZE 8
typedef struct {
    uint8_t* data;
    int width;
    int height;
    int stride;
    double pts;
    bool valid;
} VideoFrameEntry;

struct PrismPlayer {
    /* FFmpeg contexts */
    AVFormatContext* format_ctx;
    AVCodecContext* video_codec_ctx;
    AVCodecContext* audio_codec_ctx;
    struct SwsContext* sws_ctx;
    struct SwrContext* swr_ctx;

    /* Stream indices */
    int video_stream_idx;
    int audio_stream_idx;

    /* Frames and packets (used by decoder thread) */
    AVFrame* frame;
    AVFrame* rgb_frame;
    AVPacket* packet;
    uint8_t* video_buffer;          /* Temp buffer for frame conversion in decoder thread */
    int video_buffer_size;

    /* Video frame queue (thread-safe) */
    VideoFrameEntry video_queue[VIDEO_QUEUE_SIZE];
    int video_queue_write;
    int video_queue_read;
    int video_queue_count;

    /* Current display frame (for main thread) */
    uint8_t* display_buffer;
    int display_width;
    int display_height;
    int display_stride;
    double display_pts;
    bool display_ready;

    /* Audio ring buffer for proper queuing */
    float* audio_buffer;
    int audio_buffer_size;      /* Total buffer size in samples */
    int audio_write_pos;        /* Write position in ring buffer */
    int audio_read_pos;         /* Read position in ring buffer */
    int audio_available;        /* Available samples to read */

    /* State */
    PrismState state;
    PrismError last_error;
    char error_message[256];

    /* Playback clock for A/V sync */
    int64_t playback_start_time;    /* When playback started (microseconds) */
    double start_pts;               /* PTS at playback start */
    double current_pts;
    double video_pts;
    double audio_pts;
    double duration;
    double video_time_base;         /* Video stream time base */
    double audio_time_base;         /* Audio stream time base */
    double frame_duration;          /* Expected frame duration in seconds */
    bool is_live;
    bool loop;
    float speed;
    float volume;

    /* Output settings */
    PrismPixelFormat output_format;
    bool use_hw_accel;

    /* Frame info */
    int video_width;
    int video_height;
    int video_stride;
    bool first_frame_decoded;       /* Track if we've decoded the first frame */

    /* Callbacks */
    PrismVideoFrameCallback video_callback;
    void* video_callback_user_data;
    PrismAudioSamplesCallback audio_callback;
    void* audio_callback_user_data;

    /* Decoder thread */
#ifdef _WIN32
    HANDLE decoder_thread;
    HANDLE stop_event;
    CRITICAL_SECTION queue_lock;
#else
    pthread_t decoder_thread;
    bool stop_requested;
    pthread_mutex_t queue_lock;
    pthread_cond_t queue_cond;
#endif
    bool decoder_running;

    /* Thread safety for state */
#ifdef _WIN32
    CRITICAL_SECTION state_lock;
#else
    pthread_mutex_t state_lock;
#endif
};

/* Global state */
static PrismLogCallback g_log_callback = NULL;
static bool g_initialized = false;

/* ============================================================================
 * Utility Functions
 * ========================================================================== */

static void prism_log(int level, const char* fmt, ...) {
    if (g_log_callback) {
        char buffer[1024];
        va_list args;
        va_start(args, fmt);
        vsnprintf(buffer, sizeof(buffer), fmt, args);
        va_end(args);
        g_log_callback(level, buffer);
    }
}

static void set_error(PrismPlayer* player, PrismError error, const char* message) {
    if (player) {
        player->last_error = error;
        strncpy(player->error_message, message, sizeof(player->error_message) - 1);
        player->error_message[sizeof(player->error_message) - 1] = '\0';
        player->state = PRISM_STATE_ERROR;
        prism_log(0, "Error: %s", message);
    }
}

static void lock_state(PrismPlayer* player) {
#ifdef _WIN32
    EnterCriticalSection(&player->state_lock);
#else
    pthread_mutex_lock(&player->state_lock);
#endif
}

static void unlock_state(PrismPlayer* player) {
#ifdef _WIN32
    LeaveCriticalSection(&player->state_lock);
#else
    pthread_mutex_unlock(&player->state_lock);
#endif
}

static void lock_queue(PrismPlayer* player) {
#ifdef _WIN32
    EnterCriticalSection(&player->queue_lock);
#else
    pthread_mutex_lock(&player->queue_lock);
#endif
}

static void unlock_queue(PrismPlayer* player) {
#ifdef _WIN32
    LeaveCriticalSection(&player->queue_lock);
#else
    pthread_mutex_unlock(&player->queue_lock);
#endif
}

/* Forward declaration */
static void stop_decoder_thread(PrismPlayer* player);

/* Decoder thread function */
#ifdef _WIN32
static DWORD WINAPI decoder_thread_func(LPVOID arg) {
#else
static void* decoder_thread_func(void* arg) {
#endif
    PrismPlayer* player = (PrismPlayer*)arg;
    AVPacket* packet = av_packet_alloc();
    AVFrame* frame = av_frame_alloc();
    AVFrame* rgb_frame = av_frame_alloc();

    prism_log(1, "Decoder thread started");

    while (1) {
        /* Check if we should stop */
#ifdef _WIN32
        if (WaitForSingleObject(player->stop_event, 0) == WAIT_OBJECT_0) {
            break;
        }
#else
        lock_state(player);
        bool should_stop = player->stop_requested;
        unlock_state(player);
        if (should_stop) {
            break;
        }
#endif

        /* Check player state */
        lock_state(player);
        PrismState current_state = player->state;
        unlock_state(player);

        if (current_state != PRISM_STATE_PLAYING) {
            /* Sleep a bit when not playing */
#ifdef _WIN32
            Sleep(10);
#else
            usleep(10000);
#endif
            continue;
        }

        /* Check if video queue is full (only throttle for VOD, not live) */
        lock_queue(player);
        bool queue_full = (player->video_queue_count >= VIDEO_QUEUE_SIZE - 1);
        bool audio_full = (player->audio_stream_idx < 0) ||
                          (player->audio_available > player->audio_buffer_size * 3 / 4);
        bool is_live_stream = player->is_live;
        unlock_queue(player);

        /* For live streams, never throttle - we drop old data instead */
        if (!is_live_stream && queue_full && audio_full) {
            /* Buffers are full, wait a bit */
#ifdef _WIN32
            Sleep(5);
#else
            usleep(5000);
#endif
            continue;
        }

        /* Read a packet */
        int ret = av_read_frame(player->format_ctx, packet);

        if (ret < 0) {
            if (ret == AVERROR_EOF) {
                lock_state(player);
                if (player->loop && !player->is_live) {
                    /* Loop back to start */
                    av_seek_frame(player->format_ctx, -1, 0, AVSEEK_FLAG_BACKWARD);
                    if (player->video_codec_ctx) avcodec_flush_buffers(player->video_codec_ctx);
                    if (player->audio_codec_ctx) avcodec_flush_buffers(player->audio_codec_ctx);
                    player->playback_start_time = av_gettime();
                    player->start_pts = 0;
                    player->current_pts = 0;
                    player->first_frame_decoded = false;
                    unlock_state(player);
                    continue;
                } else {
                    player->state = PRISM_STATE_END_OF_FILE;
                    unlock_state(player);
                    break;
                }
            }
            /* Other error or EAGAIN, just continue */
            av_packet_unref(packet);
            continue;
        }

        /* Video packet */
        if (packet->stream_index == player->video_stream_idx && player->video_codec_ctx) {
            ret = avcodec_send_packet(player->video_codec_ctx, packet);
            if (ret >= 0) {
                ret = avcodec_receive_frame(player->video_codec_ctx, frame);
                if (ret >= 0) {
                    /* Get frame PTS */
                    double frame_pts = 0;
                    if (frame->pts != AV_NOPTS_VALUE) {
                        frame_pts = frame->pts * player->video_time_base;
                    } else if (frame->best_effort_timestamp != AV_NOPTS_VALUE) {
                        frame_pts = frame->best_effort_timestamp * player->video_time_base;
                    }

                    /* Sync playback clock on first frame */
                    lock_state(player);
                    if (!player->first_frame_decoded) {
                        player->first_frame_decoded = true;
                        player->start_pts = frame_pts;
                        player->playback_start_time = av_gettime();
                        prism_log(1, "First video frame PTS: %.3f", frame_pts);
                    }
                    unlock_state(player);

                    /* Convert to RGBA */
                    sws_scale(player->sws_ctx,
                        (const uint8_t* const*)frame->data, frame->linesize,
                        0, player->video_height,
                        player->rgb_frame->data, player->rgb_frame->linesize);

                    /* Add to video queue */
                    lock_queue(player);

                    /* For live streams: if queue is full, drop oldest frame */
                    if (player->is_live && player->video_queue_count >= VIDEO_QUEUE_SIZE) {
                        /* Drop oldest frame */
                        player->video_queue[player->video_queue_read].valid = false;
                        player->video_queue_read = (player->video_queue_read + 1) % VIDEO_QUEUE_SIZE;
                        player->video_queue_count--;
                    }

                    if (player->video_queue_count < VIDEO_QUEUE_SIZE) {
                        int idx = player->video_queue_write;
                        VideoFrameEntry* entry = &player->video_queue[idx];

                        /* Allocate buffer if needed */
                        int frame_size = player->video_width * player->video_height * 4;
                        if (!entry->data) {
                            entry->data = (uint8_t*)av_malloc(frame_size);
                        }

                        /* Copy frame data */
                        memcpy(entry->data, player->rgb_frame->data[0], frame_size);
                        entry->width = player->video_width;
                        entry->height = player->video_height;
                        entry->stride = player->video_stride;
                        entry->pts = frame_pts;
                        entry->valid = true;

                        player->video_queue_write = (player->video_queue_write + 1) % VIDEO_QUEUE_SIZE;
                        player->video_queue_count++;
                    }
                    unlock_queue(player);

                    /* Update current PTS */
                    lock_state(player);
                    player->video_pts = frame_pts;
                    player->current_pts = frame_pts;
                    unlock_state(player);
                }
            }
        }

        /* Audio packet */
        if (packet->stream_index == player->audio_stream_idx && player->audio_codec_ctx) {
            ret = avcodec_send_packet(player->audio_codec_ctx, packet);
            if (ret >= 0) {
                AVFrame* audio_frame = av_frame_alloc();
                ret = avcodec_receive_frame(player->audio_codec_ctx, audio_frame);
                if (ret >= 0 && player->swr_ctx) {
                    /* Get audio PTS */
                    if (audio_frame->pts != AV_NOPTS_VALUE) {
                        lock_state(player);
                        player->audio_pts = audio_frame->pts * player->audio_time_base;
                        unlock_state(player);
                    }

                    /* Convert to float samples */
                    int out_samples = swr_get_out_samples(player->swr_ctx, audio_frame->nb_samples);
                    float* temp_buffer = (float*)av_malloc(out_samples * 2 * sizeof(float));
                    uint8_t* out_ptr = (uint8_t*)temp_buffer;

                    int samples_converted = swr_convert(player->swr_ctx,
                        &out_ptr, out_samples,
                        (const uint8_t**)audio_frame->data, audio_frame->nb_samples);

                    if (samples_converted > 0) {
                        /* Write to audio ring buffer */
                        lock_queue(player);

                        int total_samples = samples_converted * 2;

                        /* For live streams: if buffer would overflow, drop OLD audio to make room */
                        if (player->is_live) {
                            int space_needed = total_samples;
                            int space_available = player->audio_buffer_size - player->audio_available;
                            if (space_available < space_needed) {
                                /* Drop old samples to make room */
                                int to_drop = space_needed - space_available;
                                player->audio_read_pos = (player->audio_read_pos + to_drop) % player->audio_buffer_size;
                                player->audio_available -= to_drop;
                            }
                        }

                        for (int i = 0; i < total_samples; i++) {
                            if (player->audio_available < player->audio_buffer_size) {
                                player->audio_buffer[player->audio_write_pos] = temp_buffer[i];
                                player->audio_write_pos = (player->audio_write_pos + 1) % player->audio_buffer_size;
                                player->audio_available++;
                            }
                        }
                        unlock_queue(player);
                    }
                    av_free(temp_buffer);
                }
                av_frame_free(&audio_frame);
            }
        }

        av_packet_unref(packet);
    }

    av_packet_free(&packet);
    av_frame_free(&frame);
    av_frame_free(&rgb_frame);

    prism_log(1, "Decoder thread stopped");

#ifdef _WIN32
    return 0;
#else
    return NULL;
#endif
}

static void start_decoder_thread(PrismPlayer* player) {
    if (player->decoder_running) {
        return;
    }

#ifdef _WIN32
    player->stop_event = CreateEvent(NULL, TRUE, FALSE, NULL);
    player->decoder_thread = CreateThread(NULL, 0, decoder_thread_func, player, 0, NULL);
#else
    player->stop_requested = false;
    pthread_create(&player->decoder_thread, NULL, decoder_thread_func, player);
#endif

    player->decoder_running = true;
    prism_log(1, "Started decoder thread");
}

static void stop_decoder_thread(PrismPlayer* player) {
    if (!player->decoder_running) {
        return;
    }

#ifdef _WIN32
    SetEvent(player->stop_event);
    WaitForSingleObject(player->decoder_thread, 2000);
    CloseHandle(player->decoder_thread);
    CloseHandle(player->stop_event);
    player->decoder_thread = NULL;
    player->stop_event = NULL;
#else
    lock_state(player);
    player->stop_requested = true;
    unlock_state(player);
    pthread_join(player->decoder_thread, NULL);
#endif

    player->decoder_running = false;
    prism_log(1, "Stopped decoder thread");
}

/* ============================================================================
 * Initialization
 * ========================================================================== */

PRISM_API int prism_init(void) {
    if (g_initialized) {
        return PRISM_OK;
    }

    /* FFmpeg 4.0+ doesn't require av_register_all() */
    avformat_network_init();

    g_initialized = true;
    prism_log(1, "Prism FFmpeg initialized. FFmpeg version: %s", av_version_info());

    return PRISM_OK;
}

PRISM_API void prism_shutdown(void) {
    if (!g_initialized) {
        return;
    }

    avformat_network_deinit();
    g_initialized = false;
    prism_log(1, "Prism FFmpeg shutdown");
}

PRISM_API const char* prism_get_ffmpeg_version(void) {
    return av_version_info();
}

PRISM_API const char* prism_get_version(void) {
    return PRISM_VERSION;
}

PRISM_API void prism_set_log_callback(PrismLogCallback callback) {
    g_log_callback = callback;
}

/* ============================================================================
 * Player Lifecycle
 * ========================================================================== */

PRISM_API PrismPlayer* prism_player_create(void) {
    PrismPlayer* player = (PrismPlayer*)calloc(1, sizeof(PrismPlayer));
    if (!player) {
        return NULL;
    }

    player->state = PRISM_STATE_IDLE;
    player->video_stream_idx = -1;
    player->audio_stream_idx = -1;
    player->output_format = PRISM_PIXEL_FORMAT_RGBA;
    player->speed = 1.0f;
    player->volume = 1.0f;
    player->use_hw_accel = false;
    player->decoder_running = false;

    /* Initialize locks */
#ifdef _WIN32
    InitializeCriticalSection(&player->state_lock);
    InitializeCriticalSection(&player->queue_lock);
#else
    pthread_mutex_init(&player->state_lock, NULL);
    pthread_mutex_init(&player->queue_lock, NULL);
#endif

    /* Initialize video queue */
    for (int i = 0; i < VIDEO_QUEUE_SIZE; i++) {
        player->video_queue[i].data = NULL;
        player->video_queue[i].valid = false;
    }
    player->video_queue_write = 0;
    player->video_queue_read = 0;
    player->video_queue_count = 0;

    prism_log(1, "Player created");
    return player;
}

PRISM_API void prism_player_destroy(PrismPlayer* player) {
    if (!player) {
        return;
    }

    prism_player_close(player);

    /* Free video queue buffers */
    for (int i = 0; i < VIDEO_QUEUE_SIZE; i++) {
        if (player->video_queue[i].data) {
            av_free(player->video_queue[i].data);
            player->video_queue[i].data = NULL;
        }
    }

    /* Free display buffer */
    if (player->display_buffer) {
        av_free(player->display_buffer);
        player->display_buffer = NULL;
    }

#ifdef _WIN32
    DeleteCriticalSection(&player->state_lock);
    DeleteCriticalSection(&player->queue_lock);
#else
    pthread_mutex_destroy(&player->state_lock);
    pthread_mutex_destroy(&player->queue_lock);
#endif

    free(player);
    prism_log(1, "Player destroyed");
}

/* ============================================================================
 * Media Control
 * ========================================================================== */

PRISM_API int prism_player_open(PrismPlayer* player, const char* url) {
    return prism_player_open_with_options(player, url, NULL);
}

PRISM_API int prism_player_open_with_options(PrismPlayer* player, const char* url, const char* options) {
    if (!player || !url) {
        return PRISM_ERROR_INVALID_PARAMETER;
    }

    /* Close any existing media (this also stops decoder thread) */
    prism_player_close(player);

    lock_state(player);

    player->state = PRISM_STATE_OPENING;
    prism_log(1, "Opening: %s", url);

    /* Set up format context with options */
    AVDictionary* format_opts = NULL;

    /* Default options for network streams */
    av_dict_set(&format_opts, "reconnect", "1", 0);
    av_dict_set(&format_opts, "reconnect_streamed", "1", 0);
    av_dict_set(&format_opts, "reconnect_delay_max", "5", 0);

    /* HLS specific options */
    if (strstr(url, ".m3u8") || strstr(url, "m3u8")) {
        av_dict_set(&format_opts, "protocol_whitelist", "file,http,https,tcp,tls,crypto", 0);
    }

    /* Parse custom options if provided */
    if (options && strlen(options) > 0) {
        av_dict_parse_string(&format_opts, options, "=", ",", 0);
    }

    /* Open input */
    int ret = avformat_open_input(&player->format_ctx, url, NULL, &format_opts);
    av_dict_free(&format_opts);

    if (ret < 0) {
        char errbuf[256];
        av_strerror(ret, errbuf, sizeof(errbuf));
        set_error(player, PRISM_ERROR_OPEN_FAILED, errbuf);
        unlock_state(player);
        return PRISM_ERROR_OPEN_FAILED;
    }

    /* Find stream info */
    ret = avformat_find_stream_info(player->format_ctx, NULL);
    if (ret < 0) {
        set_error(player, PRISM_ERROR_OPEN_FAILED, "Could not find stream info");
        avformat_close_input(&player->format_ctx);
        unlock_state(player);
        return PRISM_ERROR_OPEN_FAILED;
    }

    /* Detect if live stream */
    player->is_live = (player->format_ctx->duration == AV_NOPTS_VALUE);
    player->duration = player->is_live ? 0.0 : (double)player->format_ctx->duration / AV_TIME_BASE;

    /* Find video stream */
    for (unsigned int i = 0; i < player->format_ctx->nb_streams; i++) {
        if (player->format_ctx->streams[i]->codecpar->codec_type == AVMEDIA_TYPE_VIDEO) {
            player->video_stream_idx = i;
            break;
        }
    }

    /* Find audio stream */
    for (unsigned int i = 0; i < player->format_ctx->nb_streams; i++) {
        if (player->format_ctx->streams[i]->codecpar->codec_type == AVMEDIA_TYPE_AUDIO) {
            player->audio_stream_idx = i;
            break;
        }
    }

    if (player->video_stream_idx < 0 && player->audio_stream_idx < 0) {
        set_error(player, PRISM_ERROR_NO_VIDEO_STREAM, "No video or audio streams found");
        avformat_close_input(&player->format_ctx);
        unlock_state(player);
        return PRISM_ERROR_NO_VIDEO_STREAM;
    }

    /* Initialize video decoder */
    if (player->video_stream_idx >= 0) {
        AVStream* video_stream = player->format_ctx->streams[player->video_stream_idx];
        const AVCodec* codec = avcodec_find_decoder(video_stream->codecpar->codec_id);

        if (!codec) {
            set_error(player, PRISM_ERROR_CODEC_NOT_FOUND, "Video codec not found");
            avformat_close_input(&player->format_ctx);
            unlock_state(player);
            return PRISM_ERROR_CODEC_NOT_FOUND;
        }

        player->video_codec_ctx = avcodec_alloc_context3(codec);
        avcodec_parameters_to_context(player->video_codec_ctx, video_stream->codecpar);

        /* Try hardware acceleration if enabled */
        if (player->use_hw_accel) {
            /* TODO: Implement hardware acceleration */
        }

        ret = avcodec_open2(player->video_codec_ctx, codec, NULL);
        if (ret < 0) {
            set_error(player, PRISM_ERROR_CODEC_OPEN_FAILED, "Could not open video codec");
            avformat_close_input(&player->format_ctx);
            unlock_state(player);
            return PRISM_ERROR_CODEC_OPEN_FAILED;
        }

        player->video_width = player->video_codec_ctx->width;
        player->video_height = player->video_codec_ctx->height;
        player->video_time_base = av_q2d(video_stream->time_base);
        player->frame_duration = av_q2d(av_inv_q(video_stream->avg_frame_rate));
        if (player->frame_duration <= 0 || player->frame_duration > 1.0) {
            player->frame_duration = 1.0 / 30.0;  /* Default to 30fps */
        }

        /* Allocate video conversion context */
        enum AVPixelFormat dst_fmt = AV_PIX_FMT_RGBA;
        if (player->output_format == PRISM_PIXEL_FORMAT_BGRA) {
            dst_fmt = AV_PIX_FMT_BGRA;
        }

        player->sws_ctx = sws_getContext(
            player->video_width, player->video_height, player->video_codec_ctx->pix_fmt,
            player->video_width, player->video_height, dst_fmt,
            SWS_BILINEAR, NULL, NULL, NULL
        );

        /* Allocate frames */
        player->frame = av_frame_alloc();
        player->rgb_frame = av_frame_alloc();

        player->video_buffer_size = av_image_get_buffer_size(dst_fmt, player->video_width, player->video_height, 1);
        player->video_buffer = (uint8_t*)av_malloc(player->video_buffer_size);
        player->video_stride = player->video_width * 4;

        av_image_fill_arrays(player->rgb_frame->data, player->rgb_frame->linesize,
            player->video_buffer, dst_fmt, player->video_width, player->video_height, 1);

        prism_log(1, "Video: %dx%d, codec: %s", player->video_width, player->video_height, codec->name);
    }

    /* Initialize audio decoder */
    if (player->audio_stream_idx >= 0) {
        AVStream* audio_stream = player->format_ctx->streams[player->audio_stream_idx];
        const AVCodec* codec = avcodec_find_decoder(audio_stream->codecpar->codec_id);

        if (codec) {
            player->audio_codec_ctx = avcodec_alloc_context3(codec);
            avcodec_parameters_to_context(player->audio_codec_ctx, audio_stream->codecpar);

            ret = avcodec_open2(player->audio_codec_ctx, codec, NULL);
            if (ret >= 0) {
                /* Set up audio resampler to output float samples at 48000 Hz stereo (Unity standard) */
                player->swr_ctx = swr_alloc();

                AVChannelLayout out_ch_layout = AV_CHANNEL_LAYOUT_STEREO;
                AVChannelLayout in_ch_layout;
                av_channel_layout_copy(&in_ch_layout, &player->audio_codec_ctx->ch_layout);

                /* Resample to 48000 Hz stereo float - Unity's default audio rate */
                const int output_sample_rate = 48000;
                swr_alloc_set_opts2(&player->swr_ctx,
                    &out_ch_layout, AV_SAMPLE_FMT_FLT, output_sample_rate,
                    &in_ch_layout, player->audio_codec_ctx->sample_fmt, player->audio_codec_ctx->sample_rate,
                    0, NULL);

                swr_init(player->swr_ctx);

                /* Set audio time base */
                player->audio_time_base = av_q2d(audio_stream->time_base);

                /* Allocate audio ring buffer (2 seconds of stereo audio for smooth playback) */
                player->audio_buffer_size = output_sample_rate * 2 * 2;  /* 2 sec stereo at 48kHz */
                player->audio_buffer = (float*)av_malloc(player->audio_buffer_size * sizeof(float));
                player->audio_write_pos = 0;
                player->audio_read_pos = 0;
                player->audio_available = 0;

                prism_log(1, "Audio: source %d Hz %d ch, output 48000 Hz stereo, codec: %s",
                    player->audio_codec_ctx->sample_rate,
                    player->audio_codec_ctx->ch_layout.nb_channels,
                    codec->name);
            }
        }
    }

    /* Allocate packet */
    player->packet = av_packet_alloc();

    player->state = PRISM_STATE_READY;
    player->last_error = PRISM_OK;
    player->first_frame_decoded = false;

    unlock_state(player);
    prism_log(1, "Media opened successfully");
    return PRISM_OK;
}

PRISM_API void prism_player_close(PrismPlayer* player) {
    if (!player) {
        return;
    }

    /* Stop decoder thread first (must be done before acquiring lock) */
    stop_decoder_thread(player);

    lock_state(player);

    if (player->sws_ctx) {
        sws_freeContext(player->sws_ctx);
        player->sws_ctx = NULL;
    }

    if (player->swr_ctx) {
        swr_free(&player->swr_ctx);
    }

    if (player->video_codec_ctx) {
        avcodec_free_context(&player->video_codec_ctx);
    }

    if (player->audio_codec_ctx) {
        avcodec_free_context(&player->audio_codec_ctx);
    }

    if (player->format_ctx) {
        avformat_close_input(&player->format_ctx);
    }

    if (player->frame) {
        av_frame_free(&player->frame);
    }

    if (player->rgb_frame) {
        av_frame_free(&player->rgb_frame);
    }

    if (player->packet) {
        av_packet_free(&player->packet);
    }

    if (player->audio_buffer) {
        av_free(player->audio_buffer);
        player->audio_buffer = NULL;
    }

    if (player->video_buffer) {
        av_free(player->video_buffer);
        player->video_buffer = NULL;
    }

    /* Clear video queue */
    lock_queue(player);
    player->video_queue_write = 0;
    player->video_queue_read = 0;
    player->video_queue_count = 0;
    for (int i = 0; i < VIDEO_QUEUE_SIZE; i++) {
        player->video_queue[i].valid = false;
    }
    player->display_ready = false;
    player->audio_available = 0;
    player->audio_write_pos = 0;
    player->audio_read_pos = 0;
    unlock_queue(player);

    player->video_stream_idx = -1;
    player->audio_stream_idx = -1;
    player->state = PRISM_STATE_IDLE;
    player->first_frame_decoded = false;

    unlock_state(player);
}

PRISM_API int prism_player_play(PrismPlayer* player) {
    if (!player) {
        return PRISM_ERROR_INVALID_PLAYER;
    }

    lock_state(player);

    if (player->state != PRISM_STATE_READY &&
        player->state != PRISM_STATE_PAUSED &&
        player->state != PRISM_STATE_STOPPED) {
        unlock_state(player);
        return PRISM_ERROR_NOT_READY;
    }

    /* Initialize playback clock */
    player->playback_start_time = av_gettime();
    player->start_pts = player->current_pts;
    player->state = PRISM_STATE_PLAYING;
    unlock_state(player);

    /* Start decoder thread if not already running */
    if (!player->decoder_running) {
        start_decoder_thread(player);
    }

    prism_log(1, "Playback started");
    return PRISM_OK;
}

PRISM_API int prism_player_pause(PrismPlayer* player) {
    if (!player) {
        return PRISM_ERROR_INVALID_PLAYER;
    }

    lock_state(player);

    if (player->state == PRISM_STATE_PLAYING) {
        player->state = PRISM_STATE_PAUSED;
        /* Note: decoder thread will notice the state change and sleep */
    }

    unlock_state(player);
    return PRISM_OK;
}

PRISM_API int prism_player_stop(PrismPlayer* player) {
    if (!player) {
        return PRISM_ERROR_INVALID_PLAYER;
    }

    /* Stop decoder thread first */
    stop_decoder_thread(player);

    lock_state(player);

    if (player->format_ctx) {
        av_seek_frame(player->format_ctx, -1, 0, AVSEEK_FLAG_BACKWARD);
        if (player->video_codec_ctx) {
            avcodec_flush_buffers(player->video_codec_ctx);
        }
        if (player->audio_codec_ctx) {
            avcodec_flush_buffers(player->audio_codec_ctx);
        }
    }

    player->current_pts = 0;
    player->first_frame_decoded = false;
    player->state = PRISM_STATE_STOPPED;

    unlock_state(player);

    /* Clear queues */
    lock_queue(player);
    player->video_queue_write = 0;
    player->video_queue_read = 0;
    player->video_queue_count = 0;
    for (int i = 0; i < VIDEO_QUEUE_SIZE; i++) {
        player->video_queue[i].valid = false;
    }
    player->display_ready = false;
    player->audio_available = 0;
    player->audio_write_pos = 0;
    player->audio_read_pos = 0;
    unlock_queue(player);

    return PRISM_OK;
}

PRISM_API int prism_player_seek(PrismPlayer* player, double position_seconds) {
    if (!player || !player->format_ctx) {
        return PRISM_ERROR_INVALID_PLAYER;
    }

    if (player->is_live) {
        return PRISM_ERROR_SEEK_FAILED;  /* Can't seek in live streams */
    }

    /* Stop decoder thread during seek to avoid race conditions */
    bool was_running = player->decoder_running;
    if (was_running) {
        stop_decoder_thread(player);
    }

    lock_state(player);

    int64_t timestamp = (int64_t)(position_seconds * AV_TIME_BASE);
    int ret = av_seek_frame(player->format_ctx, -1, timestamp, AVSEEK_FLAG_BACKWARD);

    if (ret < 0) {
        unlock_state(player);
        if (was_running && player->state == PRISM_STATE_PLAYING) {
            start_decoder_thread(player);
        }
        return PRISM_ERROR_SEEK_FAILED;
    }

    if (player->video_codec_ctx) {
        avcodec_flush_buffers(player->video_codec_ctx);
    }
    if (player->audio_codec_ctx) {
        avcodec_flush_buffers(player->audio_codec_ctx);
    }

    player->current_pts = position_seconds;
    player->first_frame_decoded = false;  /* Re-sync clock on next frame */

    unlock_state(player);

    /* Clear video queue and audio buffer */
    lock_queue(player);
    player->video_queue_write = 0;
    player->video_queue_read = 0;
    player->video_queue_count = 0;
    for (int i = 0; i < VIDEO_QUEUE_SIZE; i++) {
        player->video_queue[i].valid = false;
    }
    player->display_ready = false;
    player->audio_available = 0;
    player->audio_write_pos = 0;
    player->audio_read_pos = 0;
    unlock_queue(player);

    /* Restart decoder thread if it was running */
    if (was_running && player->state == PRISM_STATE_PLAYING) {
        start_decoder_thread(player);
    }

    return PRISM_OK;
}

/* ============================================================================
 * State and Info
 * ========================================================================== */

PRISM_API PrismState prism_player_get_state(PrismPlayer* player) {
    return player ? player->state : PRISM_STATE_IDLE;
}

PRISM_API PrismError prism_player_get_error(PrismPlayer* player) {
    return player ? player->last_error : PRISM_ERROR_INVALID_PLAYER;
}

PRISM_API const char* prism_player_get_error_message(PrismPlayer* player) {
    return player ? player->error_message : "Invalid player";
}

PRISM_API bool prism_player_has_video(PrismPlayer* player) {
    return player && player->video_stream_idx >= 0;
}

PRISM_API bool prism_player_has_audio(PrismPlayer* player) {
    return player && player->audio_stream_idx >= 0;
}

PRISM_API bool prism_player_get_video_info(PrismPlayer* player, PrismVideoInfo* info) {
    if (!player || !info || player->video_stream_idx < 0) {
        return false;
    }

    AVStream* stream = player->format_ctx->streams[player->video_stream_idx];

    info->width = player->video_width;
    info->height = player->video_height;
    info->fps = av_q2d(stream->avg_frame_rate);
    info->duration = player->duration;
    info->total_frames = stream->nb_frames;
    info->pixel_format = player->output_format;
    info->is_live = player->is_live;
    info->codec_name = player->video_codec_ctx ? player->video_codec_ctx->codec->name : "unknown";

    return true;
}

PRISM_API bool prism_player_get_audio_info(PrismPlayer* player, PrismAudioInfo* info) {
    if (!player || !info || !player->audio_codec_ctx) {
        return false;
    }

    info->sample_rate = player->audio_codec_ctx->sample_rate;
    info->channels = player->audio_codec_ctx->ch_layout.nb_channels;
    info->bits_per_sample = 32;  /* We convert to float */
    info->duration = player->duration;
    info->codec_name = player->audio_codec_ctx->codec->name;

    return true;
}

PRISM_API double prism_player_get_position(PrismPlayer* player) {
    return player ? player->current_pts : 0.0;
}

PRISM_API double prism_player_get_duration(PrismPlayer* player) {
    return player ? player->duration : 0.0;
}

PRISM_API bool prism_player_is_live(PrismPlayer* player) {
    return player ? player->is_live : false;
}

/* ============================================================================
 * Frame Access
 * ========================================================================== */

/* Helper to write samples to audio ring buffer */
static void audio_ring_write(PrismPlayer* player, float* samples, int count) {
    for (int i = 0; i < count; i++) {
        if (player->audio_available < player->audio_buffer_size) {
            /* Don't apply volume here - let Unity handle it for consistency */
            player->audio_buffer[player->audio_write_pos] = samples[i];
            player->audio_write_pos = (player->audio_write_pos + 1) % player->audio_buffer_size;
            player->audio_available++;
        }
    }
}

PRISM_API int prism_player_update(PrismPlayer* player, double delta_time) {
    if (!player) {
        return 0;
    }

    /* Check state without holding lock for quick exit */
    PrismState current_state = player->state;
    if (current_state != PRISM_STATE_PLAYING && current_state != PRISM_STATE_END_OF_FILE) {
        return 0;
    }

    int frames_ready = 0;
    (void)delta_time;  /* Using wall clock instead */

    /* Calculate current playback position based on wall clock */
    lock_state(player);
    int64_t elapsed_us = av_gettime() - player->playback_start_time;
    double playback_time = player->start_pts + (elapsed_us / 1000000.0) * player->speed;
    bool is_live = player->is_live;
    unlock_state(player);

    /* Pull frames from video queue based on timing - this is non-blocking */
    lock_queue(player);

    if (is_live) {
        /* LIVE STREAM MODE: Always show the newest frame, drop everything else */
        VideoFrameEntry* newest_entry = NULL;
        int newest_idx = -1;

        /* Find the newest valid frame and discard all others */
        while (player->video_queue_count > 0) {
            int idx = player->video_queue_read;
            VideoFrameEntry* entry = &player->video_queue[idx];

            if (entry->valid) {
                /* If we had a previous newest, discard it */
                if (newest_entry != NULL) {
                    newest_entry->valid = false;
                }
                newest_entry = entry;
                newest_idx = idx;
            }

            player->video_queue_read = (player->video_queue_read + 1) % VIDEO_QUEUE_SIZE;
            player->video_queue_count--;
        }

        /* Display the newest frame if we found one */
        if (newest_entry != NULL) {
            int frame_size = newest_entry->width * newest_entry->height * 4;
            if (!player->display_buffer) {
                player->display_buffer = (uint8_t*)av_malloc(frame_size);
            }

            memcpy(player->display_buffer, newest_entry->data, frame_size);
            player->display_width = newest_entry->width;
            player->display_height = newest_entry->height;
            player->display_stride = newest_entry->stride;
            player->display_pts = newest_entry->pts;
            player->display_ready = true;
            newest_entry->valid = false;

            frames_ready = 1;

            /* Update current PTS */
            unlock_queue(player);
            lock_state(player);
            player->video_pts = player->display_pts;
            player->current_pts = player->display_pts;
            unlock_state(player);
            lock_queue(player);

            if (player->video_callback) {
                player->video_callback(
                    player->video_callback_user_data,
                    player->display_buffer,
                    player->display_width,
                    player->display_height,
                    player->display_stride,
                    player->display_pts
                );
            }
        }
    } else {
        /* VOD MODE: Respect timing for smooth playback */
        while (player->video_queue_count > 0) {
            int idx = player->video_queue_read;
            VideoFrameEntry* entry = &player->video_queue[idx];

            if (!entry->valid) {
                player->video_queue_read = (player->video_queue_read + 1) % VIDEO_QUEUE_SIZE;
                player->video_queue_count--;
                continue;
            }

            double time_diff = entry->pts - playback_time;

            if (time_diff <= 0.016) {
                /* Frame is due - copy to display buffer */
                int frame_size = entry->width * entry->height * 4;
                if (!player->display_buffer) {
                    player->display_buffer = (uint8_t*)av_malloc(frame_size);
                }

                memcpy(player->display_buffer, entry->data, frame_size);
                player->display_width = entry->width;
                player->display_height = entry->height;
                player->display_stride = entry->stride;
                player->display_pts = entry->pts;
                player->display_ready = true;

                entry->valid = false;
                player->video_queue_read = (player->video_queue_read + 1) % VIDEO_QUEUE_SIZE;
                player->video_queue_count--;

                unlock_queue(player);
                lock_state(player);
                player->video_pts = player->display_pts;
                player->current_pts = player->display_pts;
                unlock_state(player);
                lock_queue(player);

                frames_ready = 1;

                if (player->video_callback) {
                    player->video_callback(
                        player->video_callback_user_data,
                        player->display_buffer,
                        player->display_width,
                        player->display_height,
                        player->display_stride,
                        player->display_pts
                    );
                }

                break;
            } else {
                /* Frame is early - wait */
                break;
            }
        }
    }

    unlock_queue(player);

    return frames_ready;
}

PRISM_API uint8_t* prism_player_get_video_frame(PrismPlayer* player, int* out_width, int* out_height, int* out_stride) {
    if (!player) {
        return NULL;
    }

    lock_queue(player);

    /* Only return frame if display is ready */
    if (!player->display_ready || !player->display_buffer) {
        unlock_queue(player);
        return NULL;
    }

    if (out_width) *out_width = player->display_width;
    if (out_height) *out_height = player->display_height;
    if (out_stride) *out_stride = player->display_stride;

    /* Mark as consumed so we don't return the same frame twice */
    player->display_ready = false;

    unlock_queue(player);

    return player->display_buffer;
}

PRISM_API double prism_player_get_video_pts(PrismPlayer* player) {
    return player ? player->video_pts : 0.0;
}

PRISM_API int prism_player_copy_video_frame(PrismPlayer* player, uint8_t* dest_buffer, int dest_stride) {
    if (!player || !dest_buffer) {
        return PRISM_ERROR_INVALID_PARAMETER;
    }

    lock_queue(player);

    if (!player->display_buffer || !player->display_ready) {
        unlock_queue(player);
        return PRISM_ERROR_INVALID_PARAMETER;
    }

    if (dest_stride == player->display_stride) {
        memcpy(dest_buffer, player->display_buffer, player->display_height * player->display_stride);
    } else {
        /* Copy row by row if strides differ */
        int copy_width = (dest_stride < player->display_stride) ? dest_stride : player->display_stride;
        for (int y = 0; y < player->display_height; y++) {
            memcpy(dest_buffer + y * dest_stride, player->display_buffer + y * player->display_stride, copy_width);
        }
    }

    unlock_queue(player);
    return PRISM_OK;
}

/* ============================================================================
 * Audio Access
 * ========================================================================== */

PRISM_API int prism_player_get_audio_samples(PrismPlayer* player, float* buffer, int max_samples) {
    if (!player || !player->audio_buffer || !buffer) {
        return 0;
    }

    lock_queue(player);

    int to_copy = (player->audio_available < max_samples) ? player->audio_available : max_samples;

    for (int i = 0; i < to_copy; i++) {
        buffer[i] = player->audio_buffer[player->audio_read_pos];
        player->audio_read_pos = (player->audio_read_pos + 1) % player->audio_buffer_size;
    }
    player->audio_available -= to_copy;

    unlock_queue(player);
    return to_copy;
}

PRISM_API int prism_player_get_audio_sample_rate(PrismPlayer* player) {
    /* Return output sample rate (48000 Hz), not source sample rate */
    return (player && player->audio_codec_ctx) ? 48000 : 0;
}

PRISM_API int prism_player_get_audio_channels(PrismPlayer* player) {
    /* Return output channels (always stereo after resampling), not source channels */
    return (player && player->audio_codec_ctx) ? 2 : 0;
}

/* ============================================================================
 * Settings
 * ========================================================================== */

PRISM_API void prism_player_set_pixel_format(PrismPlayer* player, PrismPixelFormat format) {
    if (player) {
        player->output_format = format;
    }
}

PRISM_API void prism_player_set_loop(PrismPlayer* player, bool loop) {
    if (player) {
        player->loop = loop;
    }
}

PRISM_API bool prism_player_get_loop(PrismPlayer* player) {
    return player ? player->loop : false;
}

PRISM_API void prism_player_set_speed(PrismPlayer* player, float speed) {
    if (player) {
        player->speed = speed;
    }
}

PRISM_API float prism_player_get_speed(PrismPlayer* player) {
    return player ? player->speed : 1.0f;
}

PRISM_API void prism_player_set_volume(PrismPlayer* player, float volume) {
    if (player) {
        player->volume = (volume < 0.0f) ? 0.0f : ((volume > 1.0f) ? 1.0f : volume);
    }
}

PRISM_API float prism_player_get_volume(PrismPlayer* player) {
    return player ? player->volume : 0.0f;
}

PRISM_API void prism_player_set_hardware_acceleration(PrismPlayer* player, bool enabled) {
    if (player) {
        player->use_hw_accel = enabled;
    }
}

/* ============================================================================
 * Callbacks
 * ========================================================================== */

PRISM_API void prism_player_set_video_callback(PrismPlayer* player, PrismVideoFrameCallback callback, void* user_data) {
    if (player) {
        player->video_callback = callback;
        player->video_callback_user_data = user_data;
    }
}

PRISM_API void prism_player_set_audio_callback(PrismPlayer* player, PrismAudioSamplesCallback callback, void* user_data) {
    if (player) {
        player->audio_callback = callback;
        player->audio_callback_user_data = user_data;
    }
}
