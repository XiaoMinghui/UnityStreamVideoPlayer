# StreamVideoPlayer preview

Windows x64 / Direct3D11 / Unity 2022.3. H.264 8-bit 4:2:0 SDR over RTSP/TCP; audio, H.265, reconnect, Android and iOS are not implemented yet.

Build the optional FFmpeg backend from the source repository with `BuildScripts/Build-Native.ps1 -WithVideo`. A source-only Git UPM install does not include native DLLs. Import **RTSP Player**, add `RtspPlayerDemo` to a GameObject, enter Play mode, supply a URL and click Play.

Use `StreamVideoPlayerComponent.Play()`, `.Stop()`, `.Texture`, `.LastError` and `.GetStatistics()` for a component-driven integration. Assign Texture to your RawImage/material after opening. Apply TextureCoordinates to RawImage.uvRect; for materials use texture scale (1,-1) and offset (0,1) to orient the D3D11 video correctly. The sample displays only after PresentedFrames is positive.

For explicit ownership:

```csharp
// Main thread only; keep session as a field.
session = new VideoStreamSession(sourceUrl, 1280, 720);
// Each Update:
session.Render();
var stats = session.GetStatistics();
// OnDisable / shutdown:
session.Dispose();
```

Do not retain Texture after disposal. Dispose/recreate after device loss. Stop joins the native worker; network I/O cancellation uses a 5-second deadline, but DNS/driver stalls still need validation. Do not put production passwords into committed scene assets.

Each session currently uses a separate hardware decoder device. Four-session reopen tests and short 1/4/9/16-session video-wall runs have passed functional checks against a local test source. The 16-session Unity run did not meet the 60 Unity FPS / 25 video FPS goals; capacity guarantees, end-to-end latency and long-term stability are not established. See the repository's Documentation/Benchmark.md for measured results and conditions. No CPU pixel upload/readback is used in production playback. GPU conversion and a GPU copy remain.

GPU Interop Diagnostic is a separate animated-color test, not decoded video. It can be built without FFmpeg.

Third-party FFmpeg is not MIT. See Third Party Notices.md and the repository's dependency documentation before redistributing binaries.

Project: https://github.com/XiaoMinghui/UnityStreamVideoPlayer


## Video wall and startup diagnostics (preview.3)

Import the **RTSP Video Wall** sample and add `VideoWallDemo`. Select 1/4/9/16 and click Play. `sources` can supply a different URL per tile; empty entries use the single input URL. Each tile opens its own RTSP connection and hardware decoder. The source project includes `Assets/StreamVideoPlayerDev/Scenes/VideoWall.unity`.

`VideoStreamSession.TryGetStartupTimings(out VideoStartupTimings)` reports monotonic microseconds from session construction: device ready, RTSP input open, stream metadata ready, decoder ready, first decoded and first submitted frame. Zero means not reached. This does not measure screen presentation or glass-to-glass latency. False indicates an older native DLL is still loaded; restart the Editor after installing the rebuilt library.

RTSP H.264 sources with Annex B SPS/PPS supplied in SDP bypass `avformat_find_stream_info`. Other sources retain probing. This removes metadata probing delay; it cannot remove waiting for a source keyframe. The native benchmark's `probe` argument forces the older path for an A/B comparison.

When closing many sessions, call `RequestStop()` on all first, then `Dispose()` on all. RequestStop is a cooperative signal and does not release resources or guarantee completed cancellation. Batch cleanup and the video-wall sample do this automatically. Dispose can still wait for network/driver work; asynchronous lifecycle/reconnect are future work.
