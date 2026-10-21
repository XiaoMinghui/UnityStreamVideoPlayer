#include "svp/Api.h"
#include <d3d11.h>
#include <wrl/client.h>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D11.h"
#include <cmath>
#include <limits>
#include <mutex>
#include <unordered_map>

using Microsoft::WRL::ComPtr;

namespace {
constexpr uint32_t kAbiVersion = 1;
static_assert(sizeof(SvpInfo) == 16, "C ABI layout mismatch");
static_assert(sizeof(SvpProbeStats) == 16, "C ABI layout mismatch");

struct Probe {
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<ID3D11RenderTargetView> target;
    uint64_t frames = 0;
    int32_t status = SVP_OK;
};

std::mutex gMutex;
IUnityInterfaces* gInterfaces = nullptr;
IUnityGraphics* gGraphics = nullptr;
ComPtr<ID3D11Device> gDevice;
ComPtr<ID3D11DeviceContext> gContext;
std::unordered_map<int32_t, Probe> gProbes;
// Never reuse ids: queued callbacks for a destroyed probe cannot target a new probe.
uint64_t gNextId = 1;

void UNITY_INTERFACE_API OnDeviceEvent(UnityGfxDeviceEventType event) {
    std::lock_guard<std::mutex> lock(gMutex);
    if (event == kUnityGfxDeviceEventShutdown || event == kUnityGfxDeviceEventBeforeReset) {
        gProbes.clear();
        gContext.Reset();
        gDevice.Reset();
    }
    if ((event == kUnityGfxDeviceEventInitialize || event == kUnityGfxDeviceEventAfterReset) &&
        gGraphics && gGraphics->GetRenderer() == kUnityGfxRendererD3D11) {
        auto* d3d = gInterfaces->Get<IUnityGraphicsD3D11>();
        if (d3d) {
            gDevice = d3d->GetDevice();
            if (gDevice) gDevice->GetImmediateContext(gContext.ReleaseAndGetAddressOf());
        }
    }
}

void UNITY_INTERFACE_API OnRenderEvent(int id) {
    // Unity's immediate context is touched only in this render-thread callback.
    // No worker thread may reuse it without an explicit interop/synchronization design.
    std::lock_guard<std::mutex> lock(gMutex);
    auto found = gProbes.find(id);
    if (found == gProbes.end() || !gDevice || !gContext) return;
    auto& probe = found->second;
    if (probe.status != SVP_OK) return;
    if (!probe.target) {
        // Unity can allocate typeless backing resources even for an ARGB32 RenderTexture.
        // D3D11 requires a concrete view format in that case.
        D3D11_TEXTURE2D_DESC textureDesc{};
        probe.texture->GetDesc(&textureDesc);
        D3D11_RENDER_TARGET_VIEW_DESC viewDesc{};
        viewDesc.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
        viewDesc.Format = textureDesc.Format;
        if (viewDesc.Format == DXGI_FORMAT_R8G8B8A8_TYPELESS) viewDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        if (viewDesc.Format == DXGI_FORMAT_B8G8R8A8_TYPELESS) viewDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        if (FAILED(gDevice->CreateRenderTargetView(probe.texture.Get(), &viewDesc, probe.target.GetAddressOf()))) {
            probe.status = SVP_GRAPHICS_ERROR;
            return;
        }
    }
    const float phase = static_cast<float>(probe.frames % 600) * 0.0104719755f + static_cast<float>(id % 16);
    const float color[4] = {
        0.35f + 0.30f * std::sin(phase),
        0.35f + 0.30f * std::sin(phase + 2.0943951f),
        0.35f + 0.30f * std::sin(phase + 4.1887902f), 1.0f
    };
    gContext->ClearRenderTargetView(probe.target.Get(), color);
    ++probe.frames;
}
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces) {
    gInterfaces = interfaces;
    gGraphics = interfaces->Get<IUnityGraphics>();
    if (gGraphics) {
        gGraphics->RegisterDeviceEventCallback(OnDeviceEvent);
        OnDeviceEvent(kUnityGfxDeviceEventInitialize);
    }
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload() {
    if (gGraphics) gGraphics->UnregisterDeviceEventCallback(OnDeviceEvent);
    OnDeviceEvent(kUnityGfxDeviceEventShutdown);
    gGraphics = nullptr;
    gInterfaces = nullptr;
}

uint32_t SVP_CALL svp_get_abi_version() { return kAbiVersion; }

int32_t SVP_CALL svp_get_info(SvpInfo* info, uint32_t size) {
    if (!info || size != sizeof(SvpInfo)) return SVP_INVALID_ARGUMENT;
    std::lock_guard<std::mutex> lock(gMutex);
    *info = {kAbiVersion, gDevice && gContext ? SVP_CAP_D3D11_PROBE : 0u,
             static_cast<uint32_t>(gProbes.size()), 0};
    return SVP_OK;
}

int32_t SVP_CALL svp_probe_register_texture(void* texture, int32_t* handle) {
    if (!handle) return SVP_INVALID_ARGUMENT;
    *handle = 0;
    if (!texture) return SVP_INVALID_ARGUMENT;
    try {
        std::lock_guard<std::mutex> lock(gMutex);
        if (!gDevice || !gContext) return SVP_UNSUPPORTED_GRAPHICS_API;
        if (gNextId > static_cast<uint64_t>(std::numeric_limits<int32_t>::max())) return SVP_INTERNAL_ERROR;
        Probe probe;
        probe.texture = static_cast<ID3D11Texture2D*>(texture);
        ComPtr<ID3D11Device> owner;
        probe.texture->GetDevice(owner.GetAddressOf());
        if (owner.Get() != gDevice.Get()) return SVP_INVALID_ARGUMENT;
        D3D11_TEXTURE2D_DESC desc{};
        probe.texture->GetDesc(&desc);
        if ((desc.BindFlags & D3D11_BIND_RENDER_TARGET) == 0 || desc.SampleDesc.Count != 1 || desc.ArraySize != 1)
            return SVP_INVALID_ARGUMENT;
        const auto id = static_cast<int32_t>(gNextId++);
        gProbes.emplace(id, std::move(probe));
        *handle = id;
        return SVP_OK;
    } catch (...) {
        // No C++ exception crosses the managed/native boundary.
        return SVP_INTERNAL_ERROR;
    }
}

int32_t SVP_CALL svp_probe_unregister(int32_t handle) {
    std::lock_guard<std::mutex> lock(gMutex);
    return gProbes.erase(handle) != 0 ? SVP_OK : SVP_INVALID_HANDLE;
}

int32_t SVP_CALL svp_probe_get_stats(int32_t handle, SvpProbeStats* stats, uint32_t size) {
    if (!stats || size != sizeof(SvpProbeStats)) return SVP_INVALID_ARGUMENT;
    *stats = {};
    std::lock_guard<std::mutex> lock(gMutex);
    const auto found = gProbes.find(handle);
    if (found == gProbes.end()) return SVP_INVALID_HANDLE;
    *stats = {found->second.frames, found->second.status, 0};
    return SVP_OK;
}

void* SVP_CALL svp_get_render_event_func() {
    return reinterpret_cast<void*>(OnRenderEvent);
}
