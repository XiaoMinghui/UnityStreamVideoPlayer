# Local validation — 2026-10-21

This report records observed local results, not release-wide support or performance guarantees.

## Unreleased performance changes — current RTX 4080 machine

- Native build and CTest: **4/4 passed**. Added optional performance ABI layout/argument checks; existing statistics/timing layouts and ABI version 1 remain unchanged.
- Unity Mono development wall player: final build succeeded. Six sequential 16-session, 30-second before/after runs completed; full measurements and limitations are in Benchmark.md. Main-thread submission time improved, but a whole-player throughput or latency improvement has not been established.
- Unity PlayMode in the isolated validation copy: **7/7 passed, zero skipped**. XML: `Builds/PerformanceValidation/Artifacts/unity-tests-50ca1c672bff4857bd872489b13796f4.xml`.
- Installed updated native libraries into the actual project and refreshed the open Editor. Unity MCP then ran the current tests in that Editor: **7/7 passed, zero skipped**, 11.16 seconds; job `4eb5c7e35fa2499a94a0641f4864436c`. Coverage includes nonuniform decoded pixels, four-session batch rendering/dispose/reopen, queued stale handles, paused-renderer latest-frame behavior, input-view reuse and credential-safe failures.
- Native resolution-change fixture: concatenated H.264 MPEG-TS segments, 1280x720 → 1920x1080 at 25 FPS. `VideoSmoke <fixture.ts> --resolution-change` passed with 66 decoded / 65 presented frames, four input views created, nonblack GPU readback and 30 ms stop. This validates a controlled local-file pool/processor change, not arbitrary real-camera renegotiation.
- Native RTSP smoke: 15 decoded / 15 presented frames, two input views created, nonblack output, 45 ms stop.
- Final native 16-stream harness: 15 seconds, 24.998–25.064 presented FPS, present-loop CPU P95 0.605 ms, process CPU 1.331% of machine, working set 555.6 MB, process local VRAM 1222.8 MB. This verifies native diagnostic reporting; it is not a Unity frame-rate result. Report: `Artifacts/performance/native-final-16/native-16.json`.
- Current environment: Unity 2022.3.62f3c1, D3D11, RTX 4080, driver 32.0.16.1088, MSVC 19.34, Windows SDK 10.0.22621.0, pinned FFmpeg SDK from `BuildScripts/ffmpeg.lock.json`. Portable CMake 3.31.12 was downloaded and checksum-verified under ignored `ThirdParty~/cmake` because CMake was not installed with Visual Studio; pass its executable via `Build-Native.ps1 -CMakePath` on this machine.
- Native DLL SHA-256: `FDBF410DE1B768100662401B2BDB82CAC0C11ECD4EDAEBEDD7E54DFCBA35BC20`.

The sections below describe earlier preview.2/preview.3 laptop validation and are retained as historical records.

## Environment

- Unity 2022.3.62f3c1, URP 14.0.12, Linear color, Windows x64, D3D11.
- NVIDIA GeForce RTX 3050 Ti Laptop GPU (Unity selected this adapter).
- Visual Studio 2022 / MSVC 14.44, Windows SDK 10.0.26100.0.
- FFmpeg n8.1.3-18-g46d899d7a0-20261020 LGPL shared development SDK; exact archive hash in BuildScripts/ffmpeg.lock.json.
- Local MediaMTX v1.21.2 bound only to 127.0.0.1:18554.
- Synthetic moving H.264 1280x720 25fps, GOP 25, no B frames, looped fixture. Four-session test opens four independent RTSP connections to this same source, not four physical cameras.

## Preview.2 baseline — passed

| Check | Result / scope |
|---|---|
| Native CTest | 2/2: LatestFrameSlot ownership/concurrency; C ABI validation |
| Native file decode | Hardware decoded 16 frames, displayed 15, nonblack GPU output; stop 41 ms |
| Native RTSP decode | Hardware decoded 42 frames, displayed 15, nonblack GPU output; stop 39 ms |
| Unity PlayMode | 6/6, 0 skipped; total about 11.53 seconds |
| Unity video tests | Nonuniform decoded pixels; four concurrent RTSP sessions and reopen; connection failure without exposing URL credentials |
| Unity GPU tests | Four independent diagnostic targets; real pixel writes; stale events after disposal |
| Visual inspection | RTSP sample shows moving source; vertical UV correction verified against source frame with timestamp at top left |
| Windows Mono GPU diagnostic player | Native texture pixel and disposal smoke passed |
| Windows Mono video player | Build succeeded with 0 errors / 0 warnings; RTSP hardware playback and pixel readback passed |

Video player smoke result: decodedFrames=23, presentedFrames=15, pixelContrast=608, firstFrameMs=2366.6268, stopMs=45.1973. These are one short functional run. First-frame time includes initialization/probing/GOP acquisition and is **not glass-to-glass latency**. No claim of the planned <=200/350 ms median/P95 target is made.

Unity's D3D11 BGRA sRGB texture does not support direct AsyncGPUReadback on this environment. Test code samples it into an RGBA texture before checking pixels; production playback performs no CPU pixel readback. Example TextureCoordinates corrects the vertical orientation without a full-frame copy.

Artifacts are local and ignored by Git:
- Artifacts/rtsp-player-corrected.png and Artifacts/source-frame.png.
- Builds/WindowsVideoMono/smoke-90fcdf0f57d14345b5497606901944d4.json.
- Builds/WindowsMono/smoke-fa3e7542228e4e1abe3893ab6dee4692.json.

## Not yet validated

- Steady-state end-to-end latency, long-duration per-stream FPS, GPU video-engine load and 24-hour stability. Short preview.3 1080p concurrency, CPU, memory and native VRAM measurements are recorded below and in Benchmark.md.
- Real cameras, loss/jitter, disconnect/reconnect, resolution changes, malformed streams, stalled DNS and graphics-device loss.
- Color-chart numerical accuracy, other vendor GPUs, Built-in RP, IL2CPP, Android, iOS.
- Binary redistribution source/license audit and a reproducible minimal FFmpeg build.

## Repeat

1. BuildScripts/Build-Native.ps1 -WithVideo.
2. BuildScripts/Start-LocalRtsp.ps1.
3. NativeSource/build-windows-x64/RelWithDebInfo/VideoSmoke.exe rtsp://127.0.0.1:18554/test.
4. Set SVP_TEST_RTSP_URL before starting the Editor, then run StreamVideoPlayer.Tests.PlayMode.
5. Build Assets/StreamVideoPlayerDev/Scenes/RtspPlayer.unity for Windows Mono, then run BuildScripts/Test-Player.ps1 -PlayerPath <exe> -Video.
6. BuildScripts/Stop-LocalRtsp.ps1.

## Preview.3 follow-up — 2026-10-21

- Added SPS/PPS-gated RTSP fast start and monotonic startup-stage timings. Original probing is retained when the gate is not met, including local files.
- Native CTest now passes 4/4: frame-slot ownership, H.264 startup gate, diagnostic ABI and video ABI/layout checks.
- After installing the updated DLL with the Editor closed, Unity D3D11 batch PlayMode tests passed 6/6 with zero failures and zero skipped tests. This covers decoded pixels/startup timings, four-session RTSP reopen, sanitized connection failures and diagnostic GPU/disposal behavior. Fixture: H.264 1920x1080 at 25 fps. Results: Artifacts/unity-tests-cb33590c450947c4a745db9e36ae1d7c.xml. Reproduce with BuildScripts/Test-UnityBatch.ps1 while the Editor is closed and the local RTSP fixture is running.
- Updated native smoke: real 1920x1080 RTSP and 1280x720 local file both produced verified nonblack output; stop 36/35 ms in those short runs.
- New 1/4/9/16 video wall was visually checked with all 16 tiles showing video. Screenshot: Artifacts/video-wall-16.png. The screenshot used the already loaded previous Editor DLL; final standalone measurements use the new DLL.
- Final Windows Mono wall build succeeded with zero errors and zero warnings. Four standalone tiers passed (15 seconds measured each, plus warm-up). Full result/limitations are in Benchmark.md.
- Original and new DLLs retain ABI 1; new timings/cancel exports are additive. Managed code handles older loaded DLLs explicitly. Installed package DLL SHA-256: 625e0501cd2b3dad60dcf6603cfaa7c4bb665021f8cbc5dd770e36336b506c0c.
- No GPU video-engine utilization or glass-to-glass latency was measured. The 16-stream Unity run misses the 60/25 FPS goals; do not treat this as a production acceptance result.
