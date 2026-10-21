#pragma once
#include <stdint.h>

#if defined(_WIN32)
#  if defined(SVP_EXPORTS)
#    define SVP_API __declspec(dllexport)
#  else
#    define SVP_API __declspec(dllimport)
#  endif
#  define SVP_CALL __cdecl
#else
#  define SVP_API __attribute__((visibility("default")))
#  define SVP_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum SvpResult {
    SVP_OK = 0,
    SVP_INVALID_ARGUMENT = 1,
    SVP_UNSUPPORTED_GRAPHICS_API = 2,
    SVP_INVALID_HANDLE = 3,
    SVP_GRAPHICS_ERROR = 4,
    SVP_INTERNAL_ERROR = 5
};

enum SvpCapability {
    SVP_CAP_D3D11_PROBE = 1
};

typedef struct SvpInfo {
    uint32_t abi_version;
    uint32_t capabilities;
    uint32_t active_probes;
    uint32_t reserved;
} SvpInfo;

typedef struct SvpProbeStats {
    uint64_t rendered_frames;
    int32_t status;
    uint32_t reserved;
} SvpProbeStats;

SVP_API uint32_t SVP_CALL svp_get_abi_version(void);
SVP_API int32_t SVP_CALL svp_get_info(SvpInfo* info, uint32_t size);
// texture must be a live ID3D11Texture2D* on Unity's D3D11 device, with render-target binding.
// The plugin holds an independent COM reference until unregister/device shutdown.
SVP_API int32_t SVP_CALL svp_probe_register_texture(void* texture, int32_t* handle);
SVP_API int32_t SVP_CALL svp_probe_unregister(int32_t handle);
SVP_API int32_t SVP_CALL svp_probe_get_stats(int32_t handle, SvpProbeStats* stats, uint32_t size);
// Returned function pointer has UnityRenderingEvent ABI; never invoke it from managed code directly.
SVP_API void* SVP_CALL svp_get_render_event_func(void);

#ifdef __cplusplus
}
#endif
