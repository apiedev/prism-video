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
#endif

/* ============================================================================
 * Version Info
 * ========================================================================== */

#define PRISM_VERSION "0.1.0"

/* ============================================================================
 * Internal Structures
 * ========================================================================== */

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

    /* Frames and packets */
    AVFrame* frame;
    AVFrame* rgb_frame;
    AVPacket* packet;
    uint8_t* video_buffer;
    int video_buffer_size;

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
    bool has_new_frame;
    bool frame_ready;               /* Frame is ready to display based on timing */

    /* Callbacks */
    PrismVideoFrameCallback video_callback;
    void* video_callback_user_data;
    PrismAudioSamplesCallback audio_callback;
    void* audio_callback_user_data;

    /* Thread safety */
#ifdef _WIN32
    CRITICAL_SECTION lock;
#else
    pthread_mutex_t lock;
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

static void lock_player(PrismPlayer* player) {
#ifdef _WIN32
    EnterCriticalSection(&player->lock);
#else
    pthread_mutex_lock(&player->lock);
#endif
}

static void unlock_player(PrismPlayer* player) {
#ifdef _WIN32
    LeaveCriticalSection(&player->lock);
#else
    pthread_mutex_unlock(&player->lock);
#endif
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

#ifdef _WIN32
    InitializeCriticalSection(&player->lock);
#else
    pthread_mutex_init(&player->lock, NULL);
#endif

    prism_log(1, "Player created");
    return player;
}

PRISM_API void prism_player_destroy(PrismPlayer* player) {
    if (!player) {
        return;
    }

    prism_player_close(player);

#ifdef _WIN32
    DeleteCriticalSection(&player->lock);
#else
    pthread_mutex_destroy(&player->lock);
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

    lock_player(player);

    /* Close any existing media */
    prism_player_close(player);

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
        unlock_player(player);
        return PRISM_ERROR_OPEN_FAILED;
    }

    /* Find stream info */
    ret = avformat_find_stream_info(player->format_ctx, NULL);
    if (ret < 0) {
        set_error(player, PRISM_ERROR_OPEN_FAILED, "Could not find stream info");
        avformat_close_input(&player->format_ctx);
        unlock_player(player);
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
        unlock_player(player);
        return PRISM_ERROR_NO_VIDEO_STREAM;
    }

    /* Initialize video decoder */
    if (player->video_stream_idx >= 0) {
        AVStream* video_stream = player->format_ctx->streams[player->video_stream_idx];
        const AVCodec* codec = avcodec_find_decoder(video_stream->codecpar->codec_id);

        if (!codec) {
            set_error(player, PRISM_ERROR_CODEC_NOT_FOUND, "Video codec not found");
            avformat_close_input(&player->format_ctx);
            unlock_player(player);
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
            unlock_player(player);
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
                /* Set up audio resampler to output float samples */
                player->swr_ctx = swr_alloc();

                AVChannelLayout out_ch_layout = AV_CHANNEL_LAYOUT_STEREO;
                AVChannelLayout in_ch_layout;
                av_channel_layout_copy(&in_ch_layout, &player->audio_codec_ctx->ch_layout);

                swr_alloc_set_opts2(&player->swr_ctx,
                    &out_ch_layout, AV_SAMPLE_FMT_FLT, player->audio_codec_ctx->sample_rate,
                    &in_ch_layout, player->audio_codec_ctx->sample_fmt, player->audio_codec_ctx->sample_rate,
                    0, NULL);

                swr_init(player->swr_ctx);

                /* Set audio time base */
                player->audio_time_base = av_q2d(audio_stream->time_base);

                /* Allocate audio ring buffer (1 second of stereo audio) */
                player->audio_buffer_size = player->audio_codec_ctx->sample_rate * 2;  /* 1 sec stereo */
                player->audio_buffer = (float*)av_malloc(player->audio_buffer_size * sizeof(float));
                player->audio_write_pos = 0;
                player->audio_read_pos = 0;
                player->audio_available = 0;

                prism_log(1, "Audio: %d Hz, %d channels, codec: %s",
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

    unlock_player(player);
    prism_log(1, "Media opened successfully");
    return PRISM_OK;
}

PRISM_API void prism_player_close(PrismPlayer* player) {
    if (!player) {
        return;
    }

    lock_player(player);

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

    if (player->video_buffer) {
        av_free(player->video_buffer);
        player->video_buffer = NULL;
    }

    if (player->audio_buffer) {
        av_free(player->audio_buffer);
        player->audio_buffer = NULL;
    }

    player->video_stream_idx = -1;
    player->audio_stream_idx = -1;
    player->state = PRISM_STATE_IDLE;

    unlock_player(player);
}

PRISM_API int prism_player_play(PrismPlayer* player) {
    if (!player) {
        return PRISM_ERROR_INVALID_PLAYER;
    }

    lock_player(player);

    if (player->state != PRISM_STATE_READY && player->state != PRISM_STATE_PAUSED) {
        unlock_player(player);
        return PRISM_ERROR_NOT_READY;
    }

    /* Initialize playback clock */
    player->playback_start_time = av_gettime();
    player->start_pts = player->current_pts;
    player->state = PRISM_STATE_PLAYING;
    unlock_player(player);

    prism_log(1, "Playback started");
    return PRISM_OK;
}

PRISM_API int prism_player_pause(PrismPlayer* player) {
    if (!player) {
        return PRISM_ERROR_INVALID_PLAYER;
    }

    lock_player(player);

    if (player->state == PRISM_STATE_PLAYING) {
        player->state = PRISM_STATE_PAUSED;
    }

    unlock_player(player);
    return PRISM_OK;
}

PRISM_API int prism_player_stop(PrismPlayer* player) {
    if (!player) {
        return PRISM_ERROR_INVALID_PLAYER;
    }

    lock_player(player);

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
    player->state = PRISM_STATE_STOPPED;

    unlock_player(player);
    return PRISM_OK;
}

PRISM_API int prism_player_seek(PrismPlayer* player, double position_seconds) {
    if (!player || !player->format_ctx) {
        return PRISM_ERROR_INVALID_PLAYER;
    }

    if (player->is_live) {
        return PRISM_ERROR_SEEK_FAILED;  /* Can't seek in live streams */
    }

    lock_player(player);

    int64_t timestamp = (int64_t)(position_seconds * AV_TIME_BASE);
    int ret = av_seek_frame(player->format_ctx, -1, timestamp, AVSEEK_FLAG_BACKWARD);

    if (ret < 0) {
        unlock_player(player);
        return PRISM_ERROR_SEEK_FAILED;
    }

    if (player->video_codec_ctx) {
        avcodec_flush_buffers(player->video_codec_ctx);
    }
    if (player->audio_codec_ctx) {
        avcodec_flush_buffers(player->audio_codec_ctx);
    }

    player->current_pts = position_seconds;

    unlock_player(player);
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
            player->audio_buffer[player->audio_write_pos] = samples[i] * player->volume;
            player->audio_write_pos = (player->audio_write_pos + 1) % player->audio_buffer_size;
            player->audio_available++;
        }
    }
}

PRISM_API int prism_player_update(PrismPlayer* player, double delta_time) {
    if (!player || player->state != PRISM_STATE_PLAYING) {
        return 0;
    }

    lock_player(player);

    int frames_decoded = 0;
    int ret;

    /* Calculate current playback position based on wall clock */
    int64_t elapsed_us = av_gettime() - player->playback_start_time;
    double playback_time = player->start_pts + (elapsed_us / 1000000.0) * player->speed;

    /* For live streams, be more aggressive - always try to catch up */
    double time_diff = 0;
    (void)time_diff;  /* May be unused depending on code path */

    /* Read and decode packets until we have a frame ready for display */
    int max_iterations = 100;  /* Prevent infinite loops */
    while (max_iterations-- > 0) {
        /* Check if current frame is ready for display */
        if (player->has_new_frame) {
            time_diff = player->video_pts - playback_time;

            if (time_diff <= 0.01) {  /* Frame is due or late (within 10ms) */
                player->frame_ready = true;
                frames_decoded = 1;

                /* If we're significantly behind on a live stream, drop frames */
                if (player->is_live && time_diff < -0.1) {
                    player->has_new_frame = false;  /* Force decode next frame */
                    continue;
                }
                break;
            } else {
                /* Frame is too early, wait */
                break;
            }
        }

        /* Need to decode more frames */
        ret = av_read_frame(player->format_ctx, player->packet);

        if (ret < 0) {
            if (ret == AVERROR_EOF) {
                if (player->loop && !player->is_live) {
                    /* Loop back to start */
                    av_seek_frame(player->format_ctx, -1, 0, AVSEEK_FLAG_BACKWARD);
                    if (player->video_codec_ctx) avcodec_flush_buffers(player->video_codec_ctx);
                    if (player->audio_codec_ctx) avcodec_flush_buffers(player->audio_codec_ctx);
                    player->playback_start_time = av_gettime();
                    player->start_pts = 0;
                    player->current_pts = 0;
                    continue;
                } else {
                    player->state = PRISM_STATE_END_OF_FILE;
                    break;
                }
            }
            break;
        }

        /* Video packet */
        if (player->packet->stream_index == player->video_stream_idx && player->video_codec_ctx) {
            ret = avcodec_send_packet(player->video_codec_ctx, player->packet);
            if (ret >= 0) {
                ret = avcodec_receive_frame(player->video_codec_ctx, player->frame);
                if (ret >= 0) {
                    /* Get frame PTS */
                    double frame_pts = 0;
                    if (player->frame->pts != AV_NOPTS_VALUE) {
                        frame_pts = player->frame->pts * player->video_time_base;
                    } else if (player->frame->best_effort_timestamp != AV_NOPTS_VALUE) {
                        frame_pts = player->frame->best_effort_timestamp * player->video_time_base;
                    }

                    /* For non-live: skip frames that are too old */
                    if (!player->is_live && frame_pts < playback_time - 0.5) {
                        av_packet_unref(player->packet);
                        continue;  /* Skip this frame, too old */
                    }

                    /* Convert to RGBA */
                    sws_scale(player->sws_ctx,
                        (const uint8_t* const*)player->frame->data, player->frame->linesize,
                        0, player->video_height,
                        player->rgb_frame->data, player->rgb_frame->linesize);

                    player->video_pts = frame_pts;
                    player->current_pts = frame_pts;
                    player->has_new_frame = true;

                    /* Call callback if set */
                    if (player->video_callback) {
                        player->video_callback(
                            player->video_callback_user_data,
                            player->video_buffer,
                            player->video_width,
                            player->video_height,
                            player->video_stride,
                            player->video_pts
                        );
                    }
                }
            }
        }

        /* Audio packet */
        if (player->packet->stream_index == player->audio_stream_idx && player->audio_codec_ctx) {
            ret = avcodec_send_packet(player->audio_codec_ctx, player->packet);
            if (ret >= 0) {
                AVFrame* audio_frame = av_frame_alloc();
                ret = avcodec_receive_frame(player->audio_codec_ctx, audio_frame);
                if (ret >= 0 && player->swr_ctx) {
                    /* Get audio PTS */
                    if (audio_frame->pts != AV_NOPTS_VALUE) {
                        player->audio_pts = audio_frame->pts * player->audio_time_base;
                    }

                    /* Convert to float samples */
                    int out_samples = swr_get_out_samples(player->swr_ctx, audio_frame->nb_samples);
                    float* temp_buffer = (float*)av_malloc(out_samples * 2 * sizeof(float));
                    uint8_t* out_ptr = (uint8_t*)temp_buffer;

                    int samples_converted = swr_convert(player->swr_ctx,
                        &out_ptr, out_samples,
                        (const uint8_t**)audio_frame->data, audio_frame->nb_samples);

                    if (samples_converted > 0) {
                        /* Write to ring buffer */
                        audio_ring_write(player, temp_buffer, samples_converted * 2);

                        /* Call callback if set */
                        if (player->audio_callback) {
                            player->audio_callback(
                                player->audio_callback_user_data,
                                temp_buffer,
                                samples_converted,
                                2,
                                player->audio_pts
                            );
                        }
                    }
                    av_free(temp_buffer);
                }
                av_frame_free(&audio_frame);
            }
        }

        av_packet_unref(player->packet);
    }

    unlock_player(player);
    return frames_decoded;
}

PRISM_API uint8_t* prism_player_get_video_frame(PrismPlayer* player, int* out_width, int* out_height, int* out_stride) {
    if (!player || !player->video_buffer) {
        return NULL;
    }

    if (out_width) *out_width = player->video_width;
    if (out_height) *out_height = player->video_height;
    if (out_stride) *out_stride = player->video_stride;

    player->has_new_frame = false;
    return player->video_buffer;
}

PRISM_API double prism_player_get_video_pts(PrismPlayer* player) {
    return player ? player->video_pts : 0.0;
}

PRISM_API int prism_player_copy_video_frame(PrismPlayer* player, uint8_t* dest_buffer, int dest_stride) {
    if (!player || !player->video_buffer || !dest_buffer) {
        return PRISM_ERROR_INVALID_PARAMETER;
    }

    lock_player(player);

    if (dest_stride == player->video_stride) {
        memcpy(dest_buffer, player->video_buffer, player->video_height * player->video_stride);
    } else {
        /* Copy row by row if strides differ */
        int copy_width = (dest_stride < player->video_stride) ? dest_stride : player->video_stride;
        for (int y = 0; y < player->video_height; y++) {
            memcpy(dest_buffer + y * dest_stride, player->video_buffer + y * player->video_stride, copy_width);
        }
    }

    unlock_player(player);
    return PRISM_OK;
}

/* ============================================================================
 * Audio Access
 * ========================================================================== */

PRISM_API int prism_player_get_audio_samples(PrismPlayer* player, float* buffer, int max_samples) {
    if (!player || !player->audio_buffer || !buffer) {
        return 0;
    }

    lock_player(player);

    int to_copy = (player->audio_available < max_samples) ? player->audio_available : max_samples;

    for (int i = 0; i < to_copy; i++) {
        buffer[i] = player->audio_buffer[player->audio_read_pos];
        player->audio_read_pos = (player->audio_read_pos + 1) % player->audio_buffer_size;
    }
    player->audio_available -= to_copy;

    unlock_player(player);
    return to_copy;
}

PRISM_API int prism_player_get_audio_sample_rate(PrismPlayer* player) {
    return (player && player->audio_codec_ctx) ? player->audio_codec_ctx->sample_rate : 0;
}

PRISM_API int prism_player_get_audio_channels(PrismPlayer* player) {
    return (player && player->audio_codec_ctx) ? player->audio_codec_ctx->ch_layout.nb_channels : 0;
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
