#include "svp/VideoSession.hpp"
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D11.h"
#include <unordered_map>
#include <limits>
#include <cstring>
extern "C" {
#include <libavutil/log.h>
}
using Microsoft::WRL::ComPtr;
namespace {
std::mutex mutex;
IUnityInterfaces* interfaces = nullptr;
IUnityGraphics* graphics = nullptr;
ComPtr<ID3D11Device> device;
ComPtr<ID3D11DeviceContext> context;
std::unordered_map<int, std::unique_ptr<svp::VideoSession>> sessions;
uint64_t nextId = 1;
static_assert(sizeof(SvpVideoStats) == 40, "Video stats ABI size mismatch");
static_assert(sizeof(SvpVideoTimings) == 56, "Video timings ABI size mismatch");
static_assert(sizeof(SvpVideoPerformance) == 64, "Video performance ABI size mismatch");
void UNITY_INTERFACE_API deviceEvent(UnityGfxDeviceEventType event) {
    decltype(sessions) retired;
    {
        std::lock_guard<std::mutex> guard(mutex);
        if (event == kUnityGfxDeviceEventShutdown || event == kUnityGfxDeviceEventBeforeReset) {
            retired.swap(sessions); context.Reset(); device.Reset();
        }
        if ((event == kUnityGfxDeviceEventInitialize || event == kUnityGfxDeviceEventAfterReset) &&
            graphics && graphics->GetRenderer() == kUnityGfxRendererD3D11) {
            auto* d3d = interfaces->Get<IUnityGraphicsD3D11>();
            if (d3d) {
                device = d3d->GetDevice();
                if (device) device->GetImmediateContext(context.ReleaseAndGetAddressOf());
            }
        }
    } // Cancel all I/O before joining any decoder; shutdown time must not add per-stream waits.
    for (auto& entry : retired) entry.second->requestStop();
}
void UNITY_INTERFACE_API render(int id) {
    try {
        std::lock_guard<std::mutex> guard(mutex);
        const auto found = sessions.find(id);
        if (found != sessions.end() && context) found->second->present(context.Get());
    } catch (...) { /* Never unwind through Unity's render thread. */ }
}
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* value) {
    interfaces = value;
    graphics = interfaces->Get<IUnityGraphics>();
    // FFmpeg's default logging may include RTSP userinfo. Errors are exposed via the sanitized API.
    av_log_set_level(AV_LOG_QUIET);
    if (graphics) { graphics->RegisterDeviceEventCallback(deviceEvent); deviceEvent(kUnityGfxDeviceEventInitialize); }
}
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
    if (graphics) graphics->UnregisterDeviceEventCallback(deviceEvent);
    deviceEvent(kUnityGfxDeviceEventShutdown);
    graphics = nullptr; interfaces = nullptr;
}
uint32_t SVP_CALL svp_video_abi_version() { return 1; }
int32_t SVP_CALL svp_video_open(const char* url, void* texture, int32_t* handle) {
    if (!handle) return SVP_INVALID_ARGUMENT;
    *handle = 0;
    if (!url || !*url || !texture) return SVP_INVALID_ARGUMENT;
    try {
        std::lock_guard<std::mutex> guard(mutex);
        if (!device || !context) return SVP_UNSUPPORTED_GRAPHICS_API;
        if (nextId > static_cast<uint64_t>(std::numeric_limits<int32_t>::max())) return SVP_INTERNAL_ERROR;
        auto session = std::make_unique<svp::VideoSession>(device.Get(), static_cast<ID3D11Texture2D*>(texture), url);
        const int id = static_cast<int>(nextId++);
        sessions.emplace(id, std::move(session));
        *handle = id;
        return SVP_OK;
    } catch (const std::invalid_argument&) { return SVP_INVALID_ARGUMENT; }
    catch (...) { return SVP_GRAPHICS_ERROR; }
}
int32_t SVP_CALL svp_video_close(int32_t id) {
    std::unique_ptr<svp::VideoSession> retired;
    {
        std::lock_guard<std::mutex> guard(mutex);
        const auto found = sessions.find(id);
        if (found == sessions.end()) return SVP_INVALID_HANDLE;
        retired = std::move(found->second); sessions.erase(found);
    }
    return SVP_OK;
}
int32_t SVP_CALL svp_video_request_stop(int32_t id) {
    std::lock_guard<std::mutex> guard(mutex);
    const auto found = sessions.find(id);
    if (found == sessions.end()) return SVP_INVALID_HANDLE;
    found->second->requestStop();
    return SVP_OK;
}
int32_t SVP_CALL svp_video_get_stats(int32_t id, SvpVideoStats* stats, uint32_t size) {
    if (!stats || size != sizeof(SvpVideoStats)) return SVP_INVALID_ARGUMENT;
    *stats = {};
    std::lock_guard<std::mutex> guard(mutex);
    const auto found = sessions.find(id);
    if (found == sessions.end()) return SVP_INVALID_HANDLE;
    *stats = found->second->stats();
    return SVP_OK;
}
int32_t SVP_CALL svp_video_get_error(int32_t id, char* buffer, uint32_t size) {
    if (!buffer || size == 0) return SVP_INVALID_ARGUMENT;
    buffer[0] = 0;
    try {
        std::lock_guard<std::mutex> guard(mutex);
        const auto found = sessions.find(id);
        if (found == sessions.end()) return SVP_INVALID_HANDLE;
        const auto error = found->second->error();
        const auto length = (std::min)(error.size(), static_cast<size_t>(size - 1));
        std::memcpy(buffer, error.data(), length); buffer[length] = 0;
        return SVP_OK;
    } catch (...) { return SVP_INTERNAL_ERROR; }
}
int32_t SVP_CALL svp_video_get_timings(int32_t id, SvpVideoTimings* timings, uint32_t size) {
    if (!timings || size != sizeof(SvpVideoTimings)) return SVP_INVALID_ARGUMENT;
    *timings = {};
    std::lock_guard<std::mutex> guard(mutex);
    const auto found = sessions.find(id);
    if (found == sessions.end()) return SVP_INVALID_HANDLE;
    *timings = found->second->timings();
    return SVP_OK;
}
void* SVP_CALL svp_video_render_event() { return reinterpret_cast<void*>(render); }
int32_t SVP_CALL svp_video_get_performance(int32_t id, SvpVideoPerformance* performance, uint32_t size) {
    if (!performance || size != sizeof(SvpVideoPerformance)) return SVP_INVALID_ARGUMENT;
    *performance = {};
    std::lock_guard<std::mutex> guard(mutex);
    const auto found = sessions.find(id);
    if (found == sessions.end()) return SVP_INVALID_HANDLE;
    *performance = found->second->performance();
    return SVP_OK;
}
