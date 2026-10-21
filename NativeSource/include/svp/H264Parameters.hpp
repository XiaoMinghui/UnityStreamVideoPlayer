#pragma once
#include <cstddef>
#include <cstdint>

namespace svp {
// Conservative SDP fast-start gate. FFmpeg's RTSP H.264 handler supplies Annex B
// parameter sets from sprop-parameter-sets. Other layouts retain normal probing.
// This is not a bitstream validator: the decoder still validates SPS/PPS contents.
inline bool hasAnnexBParameterSets(const uint8_t* data, size_t size) {
    if (!data) return false;
    bool sps = false, pps = false;
    for (size_t i = 0; i + 4 < size; ++i) {
        if (data[i] != 0 || data[i + 1] != 0 || data[i + 2] != 1) continue;
        const uint8_t header = data[i + 3];
        if (header & 0x80) return false;
        sps |= (header & 0x1f) == 7;
        pps |= (header & 0x1f) == 8;
    }
    return sps && pps;
}
}
