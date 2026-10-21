# Architecture

## Implemented Windows preview

RTSP/TCP → FFmpeg demux → D3D11VA H.264/NV12 → D3D11 VideoProcessor → shared BGRA8 texture → Unity render-event CopyResource → Unity RenderTexture.

`StreamVideoPlayerNative` remains a small FFmpeg-independent GPU diagnostic. `StreamVideoPlayerFFmpeg` implements optional playback and its own versioned C ABI. Both hold COM references and use monotonic, non-reused handles. C# never receives per-frame pixel arrays. Package identity is `com.xmh.streamvideoplayer`, original code MIT, author xmh.

Each video session creates a separate D3D11 device on Unity's DXGI adapter with VIDEO_SUPPORT and BGRA_SUPPORT. FFmpeg owns an additional reference to this device. The worker owns network I/O, decoder objects and the private immediate/video context; it never submits commands to Unity's context. Multithread protection is enabled for the decoder device. Decoded surface array slices are passed to VideoProcessor; commands execute in order on that device before surfaces are reused by decoding.

A shared BGRA8 render target uses IDXGIKeyedMutex. Both writer and reader acquire key 0 without a CPU wait, submit GPU work, flush and release key 0. A generation counter prevents displaying the same output twice. The producer overwrites the latest output, so a slow renderer does not grow a decoded-frame FIFO. The renderer copies into the Unity-owned texture only in a plugin render event. Contention drops/skips frames rather than waiting on the render thread. This still has GPU conversion/copy/synchronization overhead and is not a zero-copy claim.

`LatestFrameSlot<T>` is separately tested infrastructure for future frame scheduling; current video presentation uses the shared texture and generation counter, not this C++ slot. Neither mechanism permits arbitrarily dropping reference-dependent compressed packets.

## Color and timing

Only H.264 NV12 SDR is accepted. BT.601/BT.709 matrices and limited/full nominal range are configured on VideoProcessor. Unspecified matrix falls back to BT.709 above 576 lines, BT.601 otherwise. Output is full-range BGRA8; Unity Linear projects sample through an sRGB view. The preview does not implement HDR, interlaced processing, display rotation, or pixel-aspect corrections. Detailed color-chart accuracy is still to be validated.

RTSP renders newest available frames without a presentation clock. File fixtures are paced using best-effort timestamps. H.264 RTSP inputs with Annex B SPS/PPS in the SDP skip stream-info probing; other sources retain it. Stage timings show where startup time is spent. Stream probing may buffer startup packets; first-frame time must not be presented as glass-to-glass latency. Source GOP/B frames, network buffering and decoder reference surfaces still affect delay. Fixed output size is scaled on the GPU; processor views are recreated on source dimension changes. Runtime resolution-change behavior still needs an integration fixture.

## Lifetime and failure

The managed session unregisters its handle before disposing its command buffer and RenderTexture. Closing removes the native session under the render-map mutex; it then interrupts and joins the worker outside that mutex. Batch shutdown first signals every session through RequestStop, then joins/releases them, so blocking network waits can overlap. COM/device destruction still has per-session cost. Late render events find no handle. Editor reload, exit and play-mode transitions dispose managed sessions. Device shutdown retires all sessions; device-loss recovery currently requires recreating them.

An AVIOInterruptCB checks stop state and a 5-second deadline for connection/probing/read. Error strings report a stage and code without the source URL. FFmpeg's default logger is suppressed in this module because it can include userinfo. Stop is synchronous in this preview: local stream stop is fast, while interrupted network I/O can take longer; OS DNS and driver stalls need further testing. Automatic reconnect and asynchronous lifecycle control are not implemented.

## Next performance work

Separate devices simplify Unity thread ownership but add per-stream overhead. Measure device pooling, video processing, flush/keyed-mutex contention, source buffering and VRAM before selecting a shared decode-device design. Benchmark 1/4/9/16 independent streams with real per-stream FPS, frame-time percentiles, CPU/video-engine load, VRAM and drops. Native and actual Unity video-wall 1/4/9/16 benchmarks now exist; see Benchmark.md for observed results and remaining bottlenecks. No performance guarantee follows from a short single-machine run.

Android will require MediaCodec Surface and validated GLES/Vulkan interop. iOS will require VideoToolbox/CVPixelBuffer/Metal and macOS/Xcode/device validation. The fixed C ABI must not expose platform-specific FFmpeg/D3D types to managed consumers.

D3D11 video texture rows have the opposite vertical convention from Unity presentation UVs. The API exposes TextureCoordinates = (0,1,1,-1); consumers apply this sampling transform, avoiding another full-frame conversion/copy. The sample applies it explicitly.
