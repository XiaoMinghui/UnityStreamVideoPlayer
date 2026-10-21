#include "svp/Api.h"
#include <cstdlib>
#include <iostream>

static void require(bool value, const char* message) {
    if (!value) { std::cerr << message << '\n'; std::exit(1); }
}
int main() {
    require(svp_get_abi_version() == 1, "unexpected ABI");
    SvpInfo info{};
    require(svp_get_info(nullptr, sizeof(info)) == SVP_INVALID_ARGUMENT, "null info accepted");
    require(svp_get_info(&info, sizeof(info) - 1) == SVP_INVALID_ARGUMENT, "wrong struct size accepted");
    require(svp_get_info(&info, sizeof(info)) == SVP_OK, "info failed");
    require(info.capabilities == 0 && info.active_probes == 0, "capabilities invented without Unity");
    int32_t handle = 42;
    require(svp_probe_register_texture(nullptr, &handle) == SVP_INVALID_ARGUMENT && handle == 0, "null texture accepted");
    require(svp_probe_unregister(42) == SVP_INVALID_HANDLE, "unknown handle accepted");
    SvpProbeStats stats{};
    require(svp_probe_get_stats(42, &stats, sizeof(stats)) == SVP_INVALID_HANDLE, "unknown stats handle accepted");
    require(svp_get_render_event_func() != nullptr, "missing render callback");
    std::cout << "Native ABI: exported symbols, validation, missing-device behavior passed\n";
}
