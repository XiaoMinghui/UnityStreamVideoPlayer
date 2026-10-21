#pragma once
#include "VideoApi.h"
#include <d3d11.h>
#include <wrl/client.h>
#include <atomic>
#include <memory>
#include <mutex>
#include <string>
#include <thread>

namespace svp {
class VideoSession final {
public:
    VideoSession(ID3D11Device* unityDevice, ID3D11Texture2D* target, std::string url, bool fastStart = true);
    ~VideoSession();
    void requestStop() noexcept;
    VideoSession(const VideoSession&) = delete;
    VideoSession& operator=(const VideoSession&) = delete;
    void present(ID3D11DeviceContext* unityContext);
    SvpVideoStats stats() const;
    SvpVideoTimings timings() const;
    std::string error() const;
private:
    void run() noexcept;
    void decode();
    void fail(const char* stage, int code);
    static int interrupt(void* opaque);
    void deadline();
    uint64_t elapsedUs() const;
    const int64_t startUs_;
    const bool fastStart_;
    std::atomic<uint64_t> deviceReadyUs_{0}, inputOpenUs_{0}, streamInfoUs_{0}, decoderReadyUs_{0}, firstDecodedUs_{0}, firstPresentedUs_{0};
    std::atomic<uint32_t> skippedProbe_{0};
    std::string url_;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> target_, sharedWorker_, sharedUnity_;
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_;
    Microsoft::WRL::ComPtr<IDXGIKeyedMutex> writerMutex_, readerMutex_;
    unsigned outputWidth_ = 0, outputHeight_ = 0;
    std::thread worker_;
    std::atomic<bool> stop_{false};
    std::atomic<int64_t> deadlineMs_{0};
    std::atomic<uint64_t> decoded_{0}, produced_{0}, presented_{0}, dropped_{0};
    uint64_t lastPresented_ = 0; // render thread only
    std::atomic<int> state_{SVP_VIDEO_OPENING}, errorCode_{0}, width_{0}, height_{0};
    mutable std::mutex errorMutex_;
    std::string error_;
};
}
