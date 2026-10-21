#include "svp/VideoSession.hpp"
#include <chrono>
#include <iostream>
using Microsoft::WRL::ComPtr;
int main(int argc, char** argv) {
    if (argc != 2) { std::cerr << "Usage: VideoSmoke <file-or-rtsp-url>\n"; return 2; }
    try {
        ComPtr<ID3D11Device> device;
        ComPtr<ID3D11DeviceContext> context;
        const D3D_FEATURE_LEVEL level = D3D_FEATURE_LEVEL_11_0;
        if (FAILED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            &level, 1, D3D11_SDK_VERSION, &device, nullptr, &context))) return 3;
        D3D11_TEXTURE2D_DESC desc{};
        desc.Width = 320; desc.Height = 180; desc.ArraySize = 1; desc.MipLevels = 1;
        desc.SampleDesc.Count = 1; desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.BindFlags = D3D11_BIND_RENDER_TARGET;
        ComPtr<ID3D11Texture2D> target;
        if (FAILED(device->CreateTexture2D(&desc, nullptr, &target))) return 4;
        const auto start = std::chrono::steady_clock::now();
        auto session = std::make_unique<svp::VideoSession>(device.Get(), target.Get(), argv[1]);
        SvpVideoStats stats{};
        while (std::chrono::steady_clock::now() - start < std::chrono::seconds(15)) {
            session->present(context.Get());
            stats = session->stats();
            if (stats.state == SVP_VIDEO_FAILED) throw std::runtime_error(session->error());
            if (stats.presented_frames >= 15) break;
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
        if (stats.presented_frames < 15) throw std::runtime_error("Not enough hardware frames presented");
        desc.BindFlags = 0; desc.Usage = D3D11_USAGE_STAGING; desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> staging;
        if (FAILED(device->CreateTexture2D(&desc, nullptr, &staging))) return 5;
        context->CopyResource(staging.Get(), target.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        if (FAILED(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped))) return 6;
        unsigned long long luminance = 0;
        for (unsigned y = 0; y < desc.Height; ++y)
            for (unsigned x = 0; x < desc.Width; ++x) {
                auto* pixel = static_cast<unsigned char*>(mapped.pData) + y * mapped.RowPitch + x * 4;
                luminance += pixel[0] + pixel[1] + pixel[2];
            }
        context->Unmap(staging.Get(), 0);
        if (luminance < 100000) throw std::runtime_error("Decoded output is black");
        const auto stopStart = std::chrono::steady_clock::now();
        session.reset();
        const auto stopMs = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - stopStart).count();
        std::cout << "PASS decoded=" << stats.decoded_frames << " presented=" << stats.presented_frames
            << " source=" << stats.width << "x" << stats.height << " pixelSum=" << luminance << " stopMs=" << stopMs << '\n';
        return stopMs < 6000 ? 0 : 7;
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
