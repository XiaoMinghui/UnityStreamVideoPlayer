# Local performance measurements — preview.3, 2026-10-21

## Unreleased performance follow-up — RTX 4080, 2026-10-21

This follow-up uses a different PC from the preview.3 laptop below. It is a same-machine before/after comparison, not a cross-GPU speedup claim. The retained changes cache D3D11 input views/color state and submit the video wall through one reusable Unity command buffer. Native per-stream devices, keyed-mutex synchronization, GPU copies and explicit flushes are unchanged.

Setup: Unity 2022.3.62f3c1, Windows x64 Mono development player, D3D11, RTX 4080 (driver 32.0.16.1088), 16 independent connections to the same local H.264 1920x1080 25 FPS source, GOP 25, 640x360 output per stream, 1920x1080 player window, 60 FPS cap. A separate validation project was used while the user's Editor remained open. Measurements are 30 seconds per process after startup/warm-up, alternating before/after for three pairs. The same final managed player was used for both modes: before loads the original `f990ab0` DLL and uses `-svp-individual-render`; after loads the updated DLL and uses batching. This is a combined-change comparison; it does not isolate the native cache's contribution to total process CPU.

| Run | Mode | Mean main-thread submission µs/frame | Process CPU % of machine | Unity FPS | Per-stream presented FPS range | Peak working set MB |
|---:|---|---:|---:|---:|---:|---:|
| 1 | Before | 37.31 | 1.432 | 59.83 | 24.70–24.77 | 1085.1 |
| 1 | After | 18.32 | 1.235 | 59.61 | 24.16–24.23 | 1102.7 |
| 2 | Before | 37.40 | 1.464 | 59.91 | 24.72–24.79 | 1072.6 |
| 2 | After | 18.08 | 1.159 | 59.99 | 24.99–24.99 | 751.8 |
| 3 | Before | 36.85 | 1.261 | 59.87 | 24.67–24.73 | 1092.5 |
| 3 | After | 17.46 | 0.986 | 59.85 | 24.66–24.73 | 1085.7 |

The median of per-run mean submission times fell from **37.31 to 18.08 µs (51.5%)**, saving about **0.019 ms per Unity frame**. This measures the main-thread validation/command-buffer submission path, not the render thread or GPU execution. It does not imply a 52% increase in frame rate. Median process CPU changed from 1.432% to 1.159% of machine capacity, but memory/frame throughput varied substantially between runs; no stable whole-player CPU, memory, latency or capacity improvement is claimed. In particular, the first after-run presented fewer video frames than its before-run.

All three after-runs recorded **zero new input views during steady state** (the original code creates one for every conversion attempt). The native resolution-change smoke decoded 66 frames across 1280x720 → 1920x1080 with four input views created, confirming that caches are rebuilt for the new pool/processor. CPU submission diagnostics are available through `TryGetPerformanceStatistics`; baseline DLLs return false and their missing counters must not be interpreted as measured zeros.

Reports: `Artifacts/performance/final-before-{1,2,3}` and `final-after-{1,2,3}`. Final native DLL SHA-256: `FDBF410DE1B768100662401B2BDB82CAC0C11ECD4EDAEBEDD7E54DFCBA35BC20`. An exploratory three-output-surface mailbox was also tested (`unity-*-ab*` reports) and removed because it did not show a repeatable benefit. It is not part of the final change.

To compare the submission modes in a current video-wall build:

```powershell
./BuildScripts/Test-VideoWall.ps1 -PlayerPath <player.exe> -Counts 16 -Seconds 30 -IndividualRendering -OutputDirectory Artifacts/individual
./BuildScripts/Test-VideoWall.ps1 -PlayerPath <player.exe> -Counts 16 -Seconds 30 -OutputDirectory Artifacts/batched
```

To repeat the original before/after comparison, additionally pass `-NativePluginPath` with the corresponding original/updated DLL. Always keep other workload and publisher settings fixed and run sequentially.

## Earlier preview.3 laptop measurements

These are short development measurements on one laptop. They are not capacity guarantees, 24-hour results, or glass-to-glass latency measurements. The measured 16-stream Unity tier does **not** meet the intended steady 60 Unity FPS / 25 video FPS target.

## Method

- Windows, Unity 2022.3.62f3c1, URP 14.0.12, Linear, D3D11, Mono development player.
- GPU: NVIDIA GeForce RTX 3050 Ti Laptop GPU. The native benchmark explicitly chooses the high-performance adapter and records its name.
- MediaMTX loopback server; H.264 1920x1080 25 fps, GOP 25, no B frames, 16-second synthetic moving pattern loop, encoded with libopenh264 at 6 Mbps target.
- Each session opens a separate RTSP/TCP connection and decoder, but all connect to the **same published stream**. This does not test independent real cameras/network paths or packet loss/jitter.
- Each output texture is 640x360; input is decoded at 1920x1080. Unity player window is 1920x1080 and draws every tile with IMGUI, targetFrameRate=60 and vSync off.
- Wait for all streams to present five frames, settle for two seconds, then measure 15 seconds per tier. Tiers are separate processes. Startup, settle and shutdown are excluded from steady-state FPS.
- CPU is the process CPU time divided by elapsed time and logical processor count (percentage of total machine capacity). It excludes the separate RTSP publisher/server and Editor. Working set is process memory, not managed heap. Unity Mono returned zero for Process.WorkingSet64; the final runner uses GetProcessMemoryInfo instead.
- The native runner measures decode/conversion/CopyResource, **not a Unity scene**. Its loop is capped near 60 Hz using a 1 ms Windows timer request; it restores timer resolution at exit. P95 loop CPU time is not GPU execution time.
- Per-stream presented counts mean GPU copy submitted. Unity FPS/frame intervals are measured over actual Update ticks; neither measures display scanout or source-to-screen delay.

## Final Unity video-wall run
| Streams | Unity FPS | Frame P95 ms | Video FPS range | CPU % of machine | Working set MB | Stop all ms |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 60 | 16.87 | 23.8–23.8 | 0.32 | 268.9 | 78.2 |
| 4 | 59.94 | 16.88 | 23.44–23.58 | 0.72 | 434.2 | 97.9 |
| 9 | 59.71 | 16.9 | 22.92–22.99 | 1.55 | 765.6 | 239.3 |
| 16 | 57.64 | 16.98 | 22.39–23.06 | 2.59 | 1115.6 | 428.9 |
Reports: `Artifacts/unity-wall-1080p25-final/summary.csv` and the four `wall-*.json` files. These final runs use the updated native DLL, the native timings API and broadcast cancellation. Report `success` means all streams stayed alive and produced frames; it is not a performance-threshold pass.

The earlier run before batch cancellation measured 16-way StopAll at 1379.8 ms. The final run measured 428.9 ms: cancellation is broadcast before joins, but device/texture destruction still costs time. These are separate short runs, not a statistically controlled stop-latency distribution. Stop remains synchronous.

## Startup A/B (720p25 fixture)

Three alternating single-session runs used the original 1280x720 fixture with GOP 25 and the same native code, toggling only the fast-start gate. Connection timing relative to keyframes was not synchronized.

| Mode | First submitted frame, three runs (ms) | Median (ms) |
|---|---|---:|
| Normal stream-info probe | 2027.523 / 1268.727 / 1363.252 | 1363.252 |
| SDP SPS/PPS fast start | 1056.724 / 930.282 / 1082.472 | 1056.724 |

The normal path spent approximately 1189–1941 ms in stream-info probing. The fast path skipped it, but still waited for usable source frames/IDR. The small sample and differing GOP phase do not establish a universal percentage improvement. Final 1080p Unity runs have their own per-session startup timings in JSON; do not compare an isolated best startup sample against this median.

## Measurement corrections and limitations

An initial native run used the default Windows timer granularity. Its intended 16.7 ms loop had P95 intervals around 30 ms and understated display FPS. After correcting the harness timer, a 10-second native 16-stream check showed about 24.9 displayed fps per stream and about 1189 MB process local video memory. The final 15-second native matrix is recorded below. The initial Unity run's zero memory values were a Mono API limitation; they are not evidence of zero memory use.

The Editor was closed during the final Unity matrix so its locked DLL could be replaced; transition-related disturbances are possible. A brief native compilation overlapped part of an earlier exploratory run, which is not used as the final matrix. No GPU video-engine utilization, optical latency, battery/thermal stability, or independent real-camera workload was captured.

## Reproduce

```powershell
./BuildScripts/Build-Native.ps1 -WithVideo
./BuildScripts/Start-LocalRtsp.ps1 -Width 1920 -Height 1080 -FrameRate 25 -Gop 25
./BuildScripts/Test-NativeVideoBenchmark.ps1 -Seconds 15
# Build VideoWall.unity for Windows Mono first:
./BuildScripts/Test-VideoWall.ps1 -PlayerPath <player.exe> -Seconds 15
./BuildScripts/Stop-LocalRtsp.ps1
```

`VideoBenchmark <url> <count> <seconds> <report.json> probe` forces full stream probing for A/B measurements. Use a fresh output directory per run. URLs/passwords are intentionally not written into the JSON/CSV reports.

Next work: profile GPU conversion/copy/mutex timing and per-session devices; improve steady display throughput and startup/stop stalls; repeat with independent cameras, longer controlled runs and thermal monitoring. Collect GPU video-engine utilization and glass-to-glass latency before setting a published capacity/latency guarantee.

## Final native matrix

| Streams | Video FPS range | CPU % of machine | Working set MB | Process local VRAM MB | Present loop CPU P95 ms | Stop all ms |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 24.531–24.531 | 0.193 | 103.215 | 80.4 | 0.386 | 99.014 |
| 4 | 22.998–23.132 | 1.047 | 274.453 | 302.1 | 0.553 | 123.69 |
| 9 | 23.597–23.73 | 1.229 | 537.129 | 671.5 | 0.961 | 257.956 |
| 16 | 24.795–24.995 | 2.572 | 562.883 | 1188.7 | 1.374 | 425.727 |

Reports: Artifacts/native-1080p25-final. The native 16-way throughput exceeds the Unity wall result, so the native-only benchmark must not be used as the Unity capacity claim.
