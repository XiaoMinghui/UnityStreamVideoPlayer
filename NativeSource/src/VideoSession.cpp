#include "svp/VideoSession.hpp"
#include "svp/H264Parameters.hpp"
#include <d3d10.h>
#include <chrono>
#include <stdexcept>
#include <algorithm>
extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/hwcontext.h>
#include <libavutil/hwcontext_d3d11va.h>
}

using Microsoft::WRL::ComPtr;
namespace {
int64_t nowMs() {
    return std::chrono::duration_cast<std::chrono::milliseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}
int64_t nowUs() {
    return std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
}
void check(HRESULT hr, const char* stage) {
    if (FAILED(hr)) throw std::runtime_error(std::string(stage) + " (HRESULT " + std::to_string(hr) + ")");
}
AVPixelFormat hardwareFormat(AVCodecContext*, const AVPixelFormat* formats) {
    for (auto p = formats; *p != AV_PIX_FMT_NONE; ++p)
        if (*p == AV_PIX_FMT_D3D11) return *p;
    return AV_PIX_FMT_NONE; // Hardware-only contract: never silently fall back to CPU decode.
}
struct AvResources {
    AVFormatContext* input = nullptr;
    AVCodecContext* decoder = nullptr;
    AVBufferRef* hardware = nullptr;
    AVPacket* packet = nullptr;
    AVFrame* frame = nullptr;
    ~AvResources() {
        av_frame_free(&frame);
        av_packet_free(&packet);
        avcodec_free_context(&decoder);
        avformat_close_input(&input);
        av_buffer_unref(&hardware);
    }
};
}

namespace svp {
VideoSession::VideoSession(ID3D11Device* unityDevice, ID3D11Texture2D* target, std::string url, bool fastStart)
    : startUs_(nowUs()), fastStart_(fastStart), url_(std::move(url)), target_(target) {
    if (!unityDevice || !target || url_.empty()) throw std::invalid_argument("Missing device, texture or source.");
    D3D11_TEXTURE2D_DESC desc{};
    target->GetDesc(&desc);
    ComPtr<ID3D11Device> owner;
    target->GetDevice(&owner);
    if (owner.Get() != unityDevice || desc.SampleDesc.Count != 1 || desc.ArraySize != 1 || desc.MipLevels != 1 ||
        (desc.Format != DXGI_FORMAT_B8G8R8A8_TYPELESS && desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM &&
         desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM_SRGB))
        throw std::invalid_argument("Output must be a single-sample BGRA8 texture on the Unity device.");
    outputWidth_ = desc.Width;
    outputHeight_ = desc.Height;
    ComPtr<IDXGIDevice> dxgi;
    ComPtr<IDXGIAdapter> adapter;
    check(unityDevice->QueryInterface(IID_PPV_ARGS(&dxgi)), "Get Unity DXGI device");
    check(dxgi->GetAdapter(&adapter), "Get Unity adapter");
    const D3D_FEATURE_LEVEL levels[] = {D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0};
    check(D3D11CreateDevice(adapter.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr,
        D3D11_CREATE_DEVICE_VIDEO_SUPPORT | D3D11_CREATE_DEVICE_BGRA_SUPPORT,
        levels, 2, D3D11_SDK_VERSION, &device_, nullptr, &context_), "Create decode device");
    ComPtr<ID3D10Multithread> multithread;
    check(context_.As(&multithread), "Get device multithread protection");
    multithread->SetMultithreadProtected(TRUE);

    D3D11_TEXTURE2D_DESC shared{};
    shared.Width = outputWidth_; shared.Height = outputHeight_;
    shared.MipLevels = 1; shared.ArraySize = 1; shared.SampleDesc.Count = 1;
    shared.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    shared.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    shared.MiscFlags = D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;
    check(device_->CreateTexture2D(&shared, nullptr, &sharedWorker_), "Create shared output");
    ComPtr<IDXGIResource> resource;
    check(sharedWorker_.As(&resource), "Get shared resource");
    HANDLE handle = nullptr;
    check(resource->GetSharedHandle(&handle), "Get shared handle");
    check(unityDevice->OpenSharedResource(handle, IID_PPV_ARGS(&sharedUnity_)), "Open shared output");
    check(sharedWorker_.As(&writerMutex_), "Get writer mutex");
    check(sharedUnity_.As(&readerMutex_), "Get reader mutex");
    deviceReadyUs_ = elapsedUs();
    worker_ = std::thread(&VideoSession::run, this);
}

VideoSession::~VideoSession() {
    requestStop();
    if (worker_.joinable()) worker_.join();
}
void VideoSession::requestStop() noexcept { stop_.store(true); }
void VideoSession::deadline() { deadlineMs_.store(nowMs() + 5000); }
uint64_t VideoSession::elapsedUs() const { return static_cast<uint64_t>((std::max)(int64_t{1}, nowUs() - startUs_)); }
SvpVideoTimings VideoSession::timings() const {
    return {deviceReadyUs_.load(), inputOpenUs_.load(), streamInfoUs_.load(), decoderReadyUs_.load(),
        firstDecodedUs_.load(), firstPresentedUs_.load(), skippedProbe_.load(), 0};
}
int VideoSession::interrupt(void* opaque) {
    auto* self = static_cast<VideoSession*>(opaque);
    return self->stop_.load() || nowMs() > self->deadlineMs_.load();
}
void VideoSession::fail(const char* stage, int code) {
    stop_.store(true);
    { std::lock_guard<std::mutex> guard(errorMutex_); error_ = std::string(stage) + " (code " + std::to_string(code) + ")"; }
    errorCode_.store(code);
    state_.store(SVP_VIDEO_FAILED);
}
std::string VideoSession::error() const {
    std::lock_guard<std::mutex> guard(errorMutex_);
    return error_;
}
SvpVideoStats VideoSession::stats() const {
    return {decoded_.load(), presented_.load(), dropped_.load(), state_.load(), errorCode_.load(), width_.load(), height_.load()};
}
void VideoSession::present(ID3D11DeviceContext* unityContext) {
    if (produced_.load() == lastPresented_) return;
    const auto hr = readerMutex_->AcquireSync(0, 0);
    if (hr == WAIT_TIMEOUT) return;
    if (hr != S_OK) { fail("Acquire shared texture for display", static_cast<int>(hr)); return; }
    const auto generation = produced_.load();
    if (generation != lastPresented_) {
        dropped_ += generation - lastPresented_ - 1;
        unityContext->CopyResource(target_.Get(), sharedUnity_.Get());
        // Submit the copy before allowing another device to overwrite the shared resource.
        unityContext->Flush();
        lastPresented_ = generation;
        ++presented_;
        if (firstPresentedUs_.load() == 0) firstPresentedUs_ = elapsedUs();
    }
    const auto release = readerMutex_->ReleaseSync(0);
    if (release != S_OK) fail("Release display texture", static_cast<int>(release));
}
void VideoSession::run() noexcept {
    try { decode(); }
    catch (const std::exception& e) { fail(e.what(), -1); }
    catch (...) { fail("Unexpected decoder exception", -1); }
}

void VideoSession::decode() {
    AvResources av;
    av.input = avformat_alloc_context();
    if (!av.input) throw std::bad_alloc();
    av.input->interrupt_callback = {interrupt, this};
    AVDictionary* options = nullptr;
    av_dict_set(&options, "rtsp_transport", "tcp", 0);
    av_dict_set(&options, "rw_timeout", "5000000", 0);
    av_dict_set(&options, "analyzeduration", "1000000", 0);
    av_dict_set(&options, "probesize", "1000000", 0);
    deadline();
    int result = avformat_open_input(&av.input, url_.c_str(), nullptr, &options);
    av_dict_free(&options);
    if (result < 0) { if (!stop_) fail("Open source", result); return; }
    inputOpenUs_ = elapsedUs();
    const bool live = av.input->iformat && std::string(av.input->iformat->name) == "rtsp";
    int streamIndex = -1;
    if (live && fastStart_) {
        for (unsigned i = 0; i < av.input->nb_streams; ++i) {
            const auto* parameters = av.input->streams[i]->codecpar;
            if (parameters->codec_type != AVMEDIA_TYPE_VIDEO) continue;
            if (parameters->codec_id == AV_CODEC_ID_H264 && parameters->extradata_size > 0 &&
                hasAnnexBParameterSets(parameters->extradata, static_cast<size_t>(parameters->extradata_size))) {
                streamIndex = static_cast<int>(i);
                skippedProbe_ = 1;
            }
            break; // Keep first-video selection stable even if later tracks have different metadata.
        }
    }
    if (streamIndex < 0) {
        deadline();
        result = avformat_find_stream_info(av.input, nullptr);
        if (result < 0) { if (!stop_) fail("Read stream description", result); return; }
        streamIndex = av_find_best_stream(av.input, AVMEDIA_TYPE_VIDEO, -1, -1, nullptr, 0);
    }
    streamInfoUs_ = elapsedUs();
    if (streamIndex < 0) { fail("Find video stream", streamIndex); return; }
    AVStream* stream = av.input->streams[streamIndex];
    if (stream->codecpar->codec_id != AV_CODEC_ID_H264) {
        fail("This preview requires H.264 8-bit 4:2:0", AVERROR(ENOSYS)); return;
    }
    const AVCodec* codec = avcodec_find_decoder(stream->codecpar->codec_id);
    av.decoder = avcodec_alloc_context3(codec);
    if (!av.decoder) throw std::bad_alloc();
    result = avcodec_parameters_to_context(av.decoder, stream->codecpar);
    if (result < 0) { fail("Copy codec parameters", result); return; }
    av.hardware = av_hwdevice_ctx_alloc(AV_HWDEVICE_TYPE_D3D11VA);
    if (!av.hardware) throw std::bad_alloc();
    auto* hw = reinterpret_cast<AVD3D11VADeviceContext*>(reinterpret_cast<AVHWDeviceContext*>(av.hardware->data)->hwctx);
    hw->device = device_.Get();
    hw->device->AddRef(); // AVHWDeviceContext owns this reference.
    result = av_hwdevice_ctx_init(av.hardware);
    if (result < 0) { fail("Initialize D3D11VA", result); return; }
    av.decoder->hw_device_ctx = av_buffer_ref(av.hardware);
    if (!av.decoder->hw_device_ctx) throw std::bad_alloc();
    av.decoder->get_format = hardwareFormat;
    av.decoder->thread_count = 1;
    result = avcodec_open2(av.decoder, codec, nullptr);
    if (result < 0) { fail("Open hardware decoder", result); return; }
    decoderReadyUs_ = elapsedUs();
    av.packet = av_packet_alloc(); av.frame = av_frame_alloc();
    if (!av.packet || !av.frame) throw std::bad_alloc();

    ComPtr<ID3D11VideoDevice> videoDevice;
    ComPtr<ID3D11VideoContext> videoContext;
    check(device_.As(&videoDevice), "Get video device");
    check(context_.As(&videoContext), "Get video context");
    ComPtr<ID3D11VideoProcessorEnumerator> enumerator;
    ComPtr<ID3D11VideoProcessor> processor;
    ComPtr<ID3D11VideoProcessorOutputView> output;
    int configuredWidth = 0, configuredHeight = 0;
    int64_t firstPts = AV_NOPTS_VALUE, playbackStart = 0;
    auto convert = [&]() {
        AVFrame* frame = av.frame;
        if (frame->format != AV_PIX_FMT_D3D11) throw std::runtime_error("Decoder did not return a D3D11 frame");
        ++decoded_;
        if (firstDecodedUs_.load() == 0) firstDecodedUs_ = elapsedUs();
        width_ = frame->width; height_ = frame->height;
        if (!live && frame->best_effort_timestamp != AV_NOPTS_VALUE) {
            if (firstPts == AV_NOPTS_VALUE) { firstPts = frame->best_effort_timestamp; playbackStart = nowMs(); }
            const int64_t due = playbackStart + av_rescale_q(frame->best_effort_timestamp - firstPts, stream->time_base, AVRational{1,1000});
            while (!stop_ && nowMs() < due) std::this_thread::sleep_for(std::chrono::milliseconds(2));
        }
        if (stop_) return;
        auto* inputTexture = reinterpret_cast<ID3D11Texture2D*>(frame->data[0]);
        D3D11_TEXTURE2D_DESC inputDesc{};
        inputTexture->GetDesc(&inputDesc);
        if (inputDesc.Format != DXGI_FORMAT_NV12) throw std::runtime_error("Only NV12 hardware frames are supported");
        if (frame->color_trc == AVCOL_TRC_SMPTE2084 || frame->color_trc == AVCOL_TRC_ARIB_STD_B67 ||
            (frame->colorspace != AVCOL_SPC_UNSPECIFIED && frame->colorspace != AVCOL_SPC_BT709 &&
             frame->colorspace != AVCOL_SPC_SMPTE170M && frame->colorspace != AVCOL_SPC_BT470BG))
            throw std::runtime_error("Only SDR BT.601/BT.709 video is supported");
        if (configuredWidth != frame->width || configuredHeight != frame->height) {
            output.Reset(); processor.Reset(); enumerator.Reset();
            D3D11_VIDEO_PROCESSOR_CONTENT_DESC content{};
            content.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
            content.InputFrameRate = {30,1}; content.OutputFrameRate = {30,1};
            content.InputWidth = static_cast<UINT>(frame->width); content.InputHeight = static_cast<UINT>(frame->height);
            content.OutputWidth = outputWidth_; content.OutputHeight = outputHeight_;
            content.Usage = D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
            check(videoDevice->CreateVideoProcessorEnumerator(&content, &enumerator), "Create video processor enumerator");
            check(videoDevice->CreateVideoProcessor(enumerator.Get(), 0, &processor), "Create video processor");
            D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC view{};
            view.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
            check(videoDevice->CreateVideoProcessorOutputView(sharedWorker_.Get(), enumerator.Get(), &view, &output), "Create video output view");
            videoContext->VideoProcessorSetStreamAutoProcessingMode(processor.Get(), 0, FALSE);
            videoContext->VideoProcessorSetStreamFrameFormat(processor.Get(), 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
            RECT source{0, 0, frame->width, frame->height}, destination{0, 0, static_cast<LONG>(outputWidth_), static_cast<LONG>(outputHeight_)};
            videoContext->VideoProcessorSetStreamSourceRect(processor.Get(), 0, TRUE, &source);
            videoContext->VideoProcessorSetStreamDestRect(processor.Get(), 0, TRUE, &destination);
            videoContext->VideoProcessorSetOutputTargetRect(processor.Get(), TRUE, &destination);
            configuredWidth = frame->width; configuredHeight = frame->height;
        }
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE inputColor{}, outputColor{};
        inputColor.YCbCr_Matrix = frame->colorspace == AVCOL_SPC_BT709 ||
            (frame->colorspace == AVCOL_SPC_UNSPECIFIED && frame->height > 576) ? 1u : 0u;
        inputColor.Nominal_Range = frame->color_range == AVCOL_RANGE_JPEG ? 2u : 1u;
        outputColor.RGB_Range = 0; outputColor.Nominal_Range = 2;
        videoContext->VideoProcessorSetStreamColorSpace(processor.Get(), 0, &inputColor);
        videoContext->VideoProcessorSetOutputColorSpace(processor.Get(), &outputColor);
        ComPtr<ID3D11VideoProcessorInputView> input;
        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC inputView{};
        inputView.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
        inputView.Texture2D.ArraySlice = static_cast<UINT>(reinterpret_cast<uintptr_t>(frame->data[1]));
        check(videoDevice->CreateVideoProcessorInputView(inputTexture, enumerator.Get(), &inputView, &input), "Create decoded frame view");
        const auto acquired = writerMutex_->AcquireSync(0, 0);
        if (acquired == WAIT_TIMEOUT) { ++dropped_; return; }
        if (acquired != S_OK) throw std::runtime_error("Acquire shared texture for decoding failed");
        D3D11_VIDEO_PROCESSOR_STREAM blit{};
        blit.Enable = TRUE; blit.pInputSurface = input.Get();
        const auto hr = videoContext->VideoProcessorBlt(processor.Get(), output.Get(), 0, 1, &blit);
        context_->Flush();
        if (SUCCEEDED(hr)) ++produced_;
        const auto released = writerMutex_->ReleaseSync(0);
        check(hr, "Convert NV12 to BGRA");
        check(released, "Release decoder texture");
        state_ = SVP_VIDEO_PLAYING;
    };
    auto receive = [&]() {
        int code = AVERROR(EAGAIN);
        while (!stop_ && (code = avcodec_receive_frame(av.decoder, av.frame)) >= 0) {
            convert();
            av_frame_unref(av.frame);
        }
        if (!stop_ && code != AVERROR(EAGAIN) && code != AVERROR_EOF) {
            fail("Receive hardware frame", code); return false;
        }
        return true;
    };
    while (!stop_) {
        deadline();
        result = av_read_frame(av.input, av.packet);
        if (result == AVERROR_EOF) {
            result = avcodec_send_packet(av.decoder, nullptr);
            if (result >= 0 && !receive()) return;
            if (!stop_) state_ = SVP_VIDEO_ENDED;
            return;
        }
        if (result < 0) { if (!stop_) fail("Read source (connection interrupted or timed out)", result); return; }
        if (av.packet->stream_index == streamIndex) {
            result = avcodec_send_packet(av.decoder, av.packet);
            if (result == AVERROR(EAGAIN)) {
                if (!receive()) return;
                result = avcodec_send_packet(av.decoder, av.packet);
            }
            if (result < 0) { fail("Send compressed packet", result); return; }
            if (!receive()) return;
        }
        av_packet_unref(av.packet);
    }
}
}
