# Changelog

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
