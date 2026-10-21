#pragma once
#include "Api.h"
#ifdef __cplusplus
extern "C" {
#endif
enum SvpVideoState { SVP_VIDEO_OPENING = 0, SVP_VIDEO_PLAYING = 1, SVP_VIDEO_ENDED = 2, SVP_VIDEO_FAILED = 3 };
typedef struct SvpVideoStats {
    uint64_t decoded_frames;
    uint64_t presented_frames;
    uint64_t dropped_frames;
    int32_t state;
    int32_t error_code;
    int32_t width;
    int32_t height;
} SvpVideoStats;
// Monotonic microseconds since session construction began. Zero = stage not reached.
typedef struct SvpVideoTimings {
    uint64_t device_ready_us;
    uint64_t input_open_us;
    uint64_t stream_info_us;
    uint64_t decoder_ready_us;
    uint64_t first_decoded_us;
    uint64_t first_presented_us;
    uint32_t skipped_stream_probe;
    uint32_t reserved;
} SvpVideoTimings;
SVP_API uint32_t SVP_CALL svp_video_abi_version(void);
// UTF-8 source. A live BGRA8 Unity render texture is retained until close.
SVP_API int32_t SVP_CALL svp_video_open(const char* url, void* texture, int32_t* handle);
SVP_API int32_t SVP_CALL svp_video_close(int32_t handle);
// Nonblocking cancellation request. A subsequent close is still required to release ownership.
SVP_API int32_t SVP_CALL svp_video_request_stop(int32_t handle);
SVP_API int32_t SVP_CALL svp_video_get_stats(int32_t handle, SvpVideoStats* stats, uint32_t size);
SVP_API int32_t SVP_CALL svp_video_get_timings(int32_t handle, SvpVideoTimings* timings, uint32_t size);
// Error text contains stage + numeric result, never the source URL/credentials.
SVP_API int32_t SVP_CALL svp_video_get_error(int32_t handle, char* buffer, uint32_t size);
SVP_API void* SVP_CALL svp_video_render_event(void);
#ifdef __cplusplus
}
#endif
