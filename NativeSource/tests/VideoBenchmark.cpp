#include "svp/VideoSession.hpp"
#include <dxgi1_6.h>
#include <psapi.h>
#include <mmsystem.h>
#include <algorithm>
#include <chrono>
#include <fstream>
#include <iostream>
#include <iomanip>
#include <vector>
extern "C" {
#include <libavutil/log.h>
}
using Microsoft::WRL::ComPtr;
using Clock = std::chrono::steady_clock;
static double seconds(Clock::duration duration) { return std::chrono::duration<double>(duration).count(); }
static double cpuSeconds() {
    FILETIME created{}, ended{}, kernel{}, user{};
    if (!GetProcessTimes(GetCurrentProcess(), &created, &ended, &kernel, &user)) throw std::runtime_error("GetProcessTimes failed");
    const auto ticks = [](FILETIME time) { return (static_cast<uint64_t>(time.dwHighDateTime) << 32) | time.dwLowDateTime; };
    return static_cast<double>(ticks(kernel) + ticks(user)) / 1e7;
}
static double percentile(std::vector<double> values, double p) {
    if (values.empty()) return 0;
    std::sort(values.begin(), values.end());
    return values[static_cast<size_t>((values.size() - 1) * p)];
}
static void require(bool value, const char* stage) { if (!value) throw std::runtime_error(stage); }
struct TimerResolution {
    bool active = timeBeginPeriod(1) == TIMERR_NOERROR;
    ~TimerResolution() { if (active) timeEndPeriod(1); }
};
int main(int argc, char** argv) {
    if (argc < 5 || argc > 6) {
        std::cerr << "Usage: VideoBenchmark <source> <count 1..32> <measure-seconds 2..300> <report.json> [probe]\n"; return 2;
    }
    try {
        av_log_set_level(AV_LOG_QUIET); // Never include source credentials in a report/log.
        TimerResolution timerResolution; // Avoid the default ~15.6 ms timer quantizing a 60 Hz loop.
        const int count = std::stoi(argv[2]), duration = std::stoi(argv[3]);
        require(count >= 1 && count <= 32 && duration >= 2 && duration <= 300, "Invalid count/duration");
        const bool fast = argc != 6 || std::string(argv[5]) != "probe";
        ComPtr<IDXGIFactory6> factory;
        require(SUCCEEDED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))), "Create DXGI factory failed");
        ComPtr<IDXGIAdapter1> adapter;
        require(SUCCEEDED(factory->EnumAdapterByGpuPreference(0, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, IID_PPV_ARGS(&adapter))), "Select high-performance adapter failed");
        DXGI_ADAPTER_DESC1 adapterDesc{}; adapter->GetDesc1(&adapterDesc);
        char gpu[256]{};
        WideCharToMultiByte(CP_UTF8, 0, adapterDesc.Description, -1, gpu, sizeof(gpu), nullptr, nullptr);
        std::string gpuName;
        for (const char* c = gpu; *c; ++c) { if (*c == '"' || *c == '\\') gpuName += '\\'; gpuName += *c; }
        ComPtr<IDXGIAdapter3> memoryAdapter; adapter.As(&memoryAdapter);
        ComPtr<ID3D11Device> device;
        ComPtr<ID3D11DeviceContext> context;
        const D3D_FEATURE_LEVEL level = D3D_FEATURE_LEVEL_11_0;
        require(SUCCEEDED(D3D11CreateDevice(adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            &level, 1, D3D11_SDK_VERSION, &device, nullptr, &context)), "Create presentation device failed");
        std::vector<std::unique_ptr<svp::VideoSession>> sessions;
        const auto start = Clock::now();
        for (int i = 0; i < count; ++i) {
            D3D11_TEXTURE2D_DESC desc{};
            desc.Width = 640; desc.Height = 360; desc.ArraySize = 1; desc.MipLevels = 1;
            desc.SampleDesc.Count = 1; desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
            desc.BindFlags = D3D11_BIND_RENDER_TARGET;
            ComPtr<ID3D11Texture2D> target;
            require(SUCCEEDED(device->CreateTexture2D(&desc, nullptr, &target)), "Create output texture failed");
            sessions.push_back(std::make_unique<svp::VideoSession>(device.Get(), target.Get(), argv[1], fast));
        }
        auto tick = [&]() {
            for (auto& session : sessions) {
                session->present(context.Get());
                if (session->stats().state == SVP_VIDEO_FAILED) throw std::runtime_error(session->error());
            }
        };
        auto allReady = [&]() {
            for (auto& session : sessions) if (session->stats().presented_frames < 5) return false;
            return true;
        };
        while (!allReady() && seconds(Clock::now() - start) < 20) { tick(); std::this_thread::sleep_for(std::chrono::milliseconds(5)); }
        require(allReady(), "Some sessions did not present five frames within 20 seconds");
        // Let startup/GOP/probing bursts settle before measuring steady-state FPS.
        const auto settle = Clock::now();
        while (seconds(Clock::now() - settle) < 2) { tick(); std::this_thread::sleep_for(std::chrono::milliseconds(16)); }
        std::vector<SvpVideoStats> before;
        std::vector<SvpVideoPerformance> performanceBefore;
        for (auto& session : sessions) { before.push_back(session->stats()); performanceBefore.push_back(session->performance()); }
        const auto measureStart = Clock::now();
        auto previous = measureStart, next = measureStart;
        const double cpuStart = cpuSeconds();
        std::vector<double> tickMs, intervalMs;
        uint64_t peakWorkingSet = 0, peakLocalVideoMemory = 0;
        bool videoMemoryAvailable = false;
        while (seconds(Clock::now() - measureStart) < duration) {
            const auto begin = Clock::now();
            if (!tickMs.empty()) intervalMs.push_back(seconds(begin - previous) * 1000);
            previous = begin;
            tick();
            tickMs.push_back(seconds(Clock::now() - begin) * 1000);
            PROCESS_MEMORY_COUNTERS memory{};
            if (GetProcessMemoryInfo(GetCurrentProcess(), &memory, sizeof(memory))) peakWorkingSet = (std::max)(peakWorkingSet, static_cast<uint64_t>(memory.WorkingSetSize));
            DXGI_QUERY_VIDEO_MEMORY_INFO videoMemory{};
            if (memoryAdapter && SUCCEEDED(memoryAdapter->QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, &videoMemory))) {
                videoMemoryAvailable = true;
                peakLocalVideoMemory = (std::max)(peakLocalVideoMemory, videoMemory.CurrentUsage);
            }
            next += std::chrono::microseconds(16667);
            if (next < Clock::now()) next = Clock::now();
            std::this_thread::sleep_until(next);
        }
        const double measured = seconds(Clock::now() - measureStart);
        const double cpu = cpuSeconds() - cpuStart;
        std::vector<SvpVideoStats> after;
        std::vector<SvpVideoTimings> timings;
        std::vector<SvpVideoPerformance> performanceAfter;
        for (auto& session : sessions) { after.push_back(session->stats()); timings.push_back(session->timings()); performanceAfter.push_back(session->performance()); }
        const auto stopStart = Clock::now();
        for (auto& session : sessions) session->requestStop();
        sessions.clear();
        const double stopMs = seconds(Clock::now() - stopStart) * 1000;
        std::ofstream report(argv[4]); require(report.good(), "Cannot write benchmark report");
        report << std::fixed << std::setprecision(3)
            << "{\n  \"schema\": 1,\n  \"scope\": \"native decode + GPU copy; no Unity scene\",\n  \"gpu\": \"" << gpuName
            << "\",\n  \"sessions\": " << count << ",\n  \"fastStart\": " << (fast ? "true" : "false")
            << ",\n  \"outputWidth\": 640,\n  \"outputHeight\": 360,\n  \"measurementSeconds\": " << measured
            << ",\n  \"processCpuPercentOfMachine\": " << cpu / measured / (std::max)(1u, std::thread::hardware_concurrency()) * 100
            << ",\n  \"peakWorkingSetMB\": " << peakWorkingSet / 1048576.0
            << ",\n  \"peakProcessLocalVideoMemoryMB\": " << (videoMemoryAvailable ? std::to_string(peakLocalVideoMemory / 1048576.0) : "null")
            << ",\n  \"presentLoopCpuP95Ms\": " << percentile(tickMs, .95)
            << ",\n  \"presentLoopIntervalP95Ms\": " << percentile(intervalMs, .95)
            << ",\n  \"stopAllMs\": " << stopMs << ",\n  \"streams\": [\n";
        for (int i = 0; i < count; ++i) {
            const auto& a = after[i]; const auto& b = before[i]; const auto& t = timings[i];
            const auto& p = performanceAfter[i]; const auto& q = performanceBefore[i];
            require(a.presented_frames > b.presented_frames, "Stream stopped presenting during benchmark");
            report << "    {\"index\": " << i << ", \"width\": " << a.width << ", \"height\": " << a.height
                << ", \"decodedFps\": " << (a.decoded_frames - b.decoded_frames) / measured
                << ", \"presentedFps\": " << (a.presented_frames - b.presented_frames) / measured
                << ", \"droppedFrames\": " << a.dropped_frames - b.dropped_frames
                << ", \"deviceReadyMs\": " << t.device_ready_us / 1000.0 << ", \"inputOpenMs\": " << t.input_open_us / 1000.0
                << ", \"streamInfoMs\": " << t.stream_info_us / 1000.0 << ", \"decoderReadyMs\": " << t.decoder_ready_us / 1000.0
                << ", \"firstDecodedMs\": " << t.first_decoded_us / 1000.0 << ", \"firstPresentedMs\": " << t.first_presented_us / 1000.0
                << ", \"skippedProbe\": " << t.skipped_stream_probe
                << ", \"writerBusyDrops\": " << p.writer_busy_drops - q.writer_busy_drops
                << ", \"readerBusySkips\": " << p.reader_busy_skips - q.reader_busy_skips
                << ", \"inputViewCreations\": " << p.input_view_creations - q.input_view_creations
                << ", \"conversionCpuMeanUs\": " << static_cast<double>(p.conversion_cpu_us - q.conversion_cpu_us) / (std::max)(uint64_t{1}, p.conversion_attempts - q.conversion_attempts)
                << ", \"presentationCpuMeanUs\": " << static_cast<double>(p.presentation_cpu_us - q.presentation_cpu_us) / (std::max)(uint64_t{1}, p.present_calls - q.present_calls)
                << "}" << (i + 1 == count ? "\n" : ",\n");
        }
        report << "  ]\n}\n";
        require(report.good(), "Writing benchmark report failed");
        std::cout << "Completed " << count << " sessions for " << measured << " s; report written.\n";
        return 0;
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
