# Changelog

## Unreleased

- Reuse D3D11 video input views per decoder texture/array slice and cache unchanged color-space state. Rebuild caches when the decoder pool or processor changes.
- Add `VideoStreamRenderBatch` and use it in the video wall: record session events once and submit one Unity command buffer per frame. Closed session handles remain safe to skip.
- Add optional CPU submission/contention counters without changing ABI 1 or existing struct layouts; extend native and Unity benchmarks with diagnostics and an individual-render A/B switch.
- Cover batched disposal/reopen, paused-renderer latest-frame behavior, view reuse, and native resolution-change playback.

## 0.1.0-preview.3

- Add conservative RTSP H.264 SDP fast start, with the original probing path retained when parameters are missing.
- Expose startup stage timings without changing the existing statistics ABI.
- Add a 1/4/9/16 video-wall sample and reproducible native/Unity standalone benchmarks.
- Broadcast cancellation before joining sessions during batch shutdown.
- Add native metadata-gate and video ABI regression checks.
## 0.1.0-preview.2

- Add optional FFmpeg backend with H.264 D3D11VA decoding and RTSP/TCP input.
- Convert NV12 to BGRA on a separate GPU device and synchronize a shared texture with Unity.
- Add VideoStreamSession, StreamVideoPlayerComponent and an RTSP input/display sample.
- Add pinned local dependency setup, local RTSP fixture and native/Unity/player integration checks.
- Add interruptible I/O, explicit errors, disposal and reopening. No audio/reconnect/mobile support yet.

## 0.1.0-preview.1

- Add UPM package com.xmh.streamvideoplayer and MIT-licensed original code.
- Add Windows D3D11 native GPU diagnostic, C ABI checks and Editor diagnostics window.
- Add GPU diagnostic sample and lifecycle tests.
