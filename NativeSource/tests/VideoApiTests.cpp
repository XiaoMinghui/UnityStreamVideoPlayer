#include "svp/VideoApi.h"
#include <cstdlib>
#include <iostream>
static void require(bool value, const char* message) {
    if (!value) { std::cerr << message << '\n'; std::exit(1); }
}
int main() {
    require(svp_video_abi_version() == 1, "Unexpected ABI");
    require(sizeof(SvpVideoStats) == 40 && sizeof(SvpVideoTimings) == 56, "Unexpected struct layout");
    require(sizeof(SvpVideoPerformance) == 64, "Unexpected performance layout");
    int handle = 123;
    require(svp_video_open(nullptr, nullptr, &handle) == SVP_INVALID_ARGUMENT && handle == 0, "Invalid open did not clear handle");
    require(svp_video_close(-1) == SVP_INVALID_HANDLE, "Closing missing handle succeeded");
    require(svp_video_request_stop(-1) == SVP_INVALID_HANDLE, "Stop request for missing handle succeeded");
    SvpVideoTimings timings{};
    require(svp_video_get_timings(1, &timings, sizeof(timings) - 1) == SVP_INVALID_ARGUMENT, "Timings size not checked");
    require(svp_video_get_timings(1, nullptr, sizeof(timings)) == SVP_INVALID_ARGUMENT, "Null timings not checked");
    timings.first_presented_us = 999;
    require(svp_video_get_timings(1, &timings, sizeof(timings)) == SVP_INVALID_HANDLE && timings.first_presented_us == 0, "Failed lookup did not clear timings");
    SvpVideoPerformance performance{};
    require(svp_video_get_performance(1, &performance, sizeof(performance) - 1) == SVP_INVALID_ARGUMENT, "Performance size not checked");
    require(svp_video_get_performance(1, nullptr, sizeof(performance)) == SVP_INVALID_ARGUMENT, "Null performance not checked");
    performance.converted_frames = 999;
    require(svp_video_get_performance(1, &performance, sizeof(performance)) == SVP_INVALID_HANDLE && performance.converted_frames == 0, "Failed lookup did not clear performance");
    char message[4] = {'a',0,0,0};
    require(svp_video_get_error(1, message, sizeof(message)) == SVP_INVALID_HANDLE && message[0] == 0, "Failed error lookup did not clear buffer");
}
