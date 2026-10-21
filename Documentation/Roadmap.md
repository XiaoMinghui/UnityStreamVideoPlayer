# Development milestones

## M0 / GPU interop foundation — locally verified

UPM identity and MIT source license; Windows native build; C ABI checks; GPU diagnostic; stale-event/disposal tests; Mono standalone GPU smoke test.

## M1 / First actual video — functional preview

Implemented: checksum-pinned local FFmpeg shared SDK, RTSP/TCP H.264 8-bit SDR, interruptible I/O, D3D11VA, GPU conversion/shared textures, video session/component/sample. Native RTSP and four Unity session tests pass on the development machine. See Validation.md for exact scope.

Remaining before declaring a usable release: pixel color/orientation chart, resolution changes, prolonged stream loss, cancellation/DNS edge cases, frame-timing measurements, native leak/VRAM checks, reproducible audited minimal FFmpeg source builds and corresponding-source bundle. Independent Unity Windows player video tests are tracked in Validation.md.

## M2 / Multi-stream and latency

- Startup-stage diagnostics and conservative SDP fast start are implemented. Next optimize device pooling and GPU synchronization; add explicit decoder capability diagnostics.
- Unreleased: decoder input-view/color-state caching and reusable Unity batch submission are implemented. Same-machine RTX 4080 measurements reduce 16-way main-thread submission from 37.31 to 18.08 microseconds (median of three per-run means); see Benchmark.md for full scope and throughput variability. Device pooling, GPU execution-time profiling and cross-vendor measurements remain open.
- Short local 1/4/9/16 independent RTSP connection benchmarks are implemented and recorded in Benchmark.md. Sixteen-way decoding works, but the Unity 60 FPS / per-stream 25 FPS targets remain unmet. Next: real multiple sources, longer runs, profiling and repeatability. 32 x 720p15 remains a future stress tier.
- Initial hardware: RTX 3050 Ti Laptop / Intel Iris Xe. Unity currently runs on NVIDIA.
- Target 60 FPS in a simple Unity scene; measure CPU, video-engine utilization, render frame times, VRAM and frame drops.
- Controlled wired-LAN source, no B frames and short GOP: target median <=200 ms and P95 <=350 ms glass-to-glass. Measure optically/synchronized, not by subtracting unrelated PTS clocks.
- H.265, UDP, reconnect/backoff, packet-loss and network-jitter recovery, repeated create/destroy.

## M3 / Windows usable release

- Audio for selected stream, clock/sync policy, RawImage/Mesh adapters, multi-stream grid.
- Intel/AMD/NVIDIA matrix, Mono + IL2CPP standalone, URP + Built-in validation.
- 24-hour soak with no sustained native memory/VRAM growth.
- Source and audited native release artifacts with matching dependencies/build/license material.

## Later

- RTMP / HTTP-FLV / HLS, then SRT as needed; WebRTC is a separate integration.
- Android MediaCodec feasibility, then iOS VideoToolbox/Metal on real devices.
- D3D12, HDR/10-bit, synchronized cameras and recording need separate milestones.
