#include "svp/H264Parameters.hpp"
#include <cstdlib>
#include <iostream>
static void require(bool value, const char* message) {
    if (!value) { std::cerr << message << '\n'; std::exit(1); }
}
int main() {
    const uint8_t combined[] = {0,0,0,1,0x67,0x64,0,0x1f,0,0,1,0x68,0xee};
    require(svp::hasAnnexBParameterSets(combined, sizeof(combined)), "SPS/PPS not recognized");
    for (size_t size = 0; size < sizeof(combined); ++size)
        require(!svp::hasAnnexBParameterSets(combined, size), "Truncated metadata enabled fast start");
    require(!svp::hasAnnexBParameterSets(nullptr, 100), "Null metadata enabled fast start");
    const uint8_t missingSps[] = {0,0,1,0x68,0xee,0,0,1,0x65,0xab};
    require(!svp::hasAnnexBParameterSets(missingSps, sizeof(missingSps)), "IDR mistaken for SPS");
    const uint8_t forbidden[] = {0,0,1,0xe7,0x64,0,0,1,0x68,0xee};
    require(!svp::hasAnnexBParameterSets(forbidden, sizeof(forbidden)), "Invalid NAL header accepted");
    const uint8_t avcc[] = {1,0x64,0,0x1f,0xff,0xe1,0,2,0x67,0x64,1,0,2,0x68,0xee};
    require(!svp::hasAnnexBParameterSets(avcc, sizeof(avcc)), "Non-AnnexB must retain probing");
}
