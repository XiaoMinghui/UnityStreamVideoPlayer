# StreamVideoPlayer

面向 Unity 的低延迟、多路视频流播放器。Windows 优先，后续规划 Android 和 iOS。

**当前 `0.1.0-preview.3` 已接通 Windows D3D11 + FFmpeg 的 RTSP/TCP H.264 硬件播放。**
这是开发预览：提供 1／4／9／16 路视频墙及原生、Unity 独立程序性能测试。短时本机结果见 Benchmark.md；不代表所有硬件、真实摄像头或生产稳定性保证。

## 从源码运行

环境：Unity 2022.3（本机 2022.3.62f3c1 / URP 14.0.12）、Windows x64、Direct3D11、Visual Studio 2022 C++ 桌面开发 / Windows SDK / CMake。

1. 克隆仓库，用 Unity Hub 打开根目录。将 Windows Graphics APIs 设置为 Direct3D11。
2. 在仓库根目录构建。脚本下载固定版本、验证 SHA-256、运行原生测试并安装 DLL：

   ```powershell
   ./BuildScripts/Build-Native.ps1 -WithVideo
   ```

   Unity 不在默认安装目录时传入 `-UnityEditorPath '.../Editor/Unity.exe'`。
   已加载且发生变化的 DLL 必须在关闭使用它的 Editor/Player 后替换；相同文件会跳过。
   不加 `-WithVideo` 只构建 GPU 诊断模块。
3. 打开 `Assets/StreamVideoPlayerDev/Scenes/RtspPlayer.unity`，或执行菜单 `Tools > StreamVideoPlayer > Create RTSP Player Scene`。
4. 进入 Play，输入 RTSP 地址，点击示例中的 **Play**。本阶段仅支持 H.264 8-bit 4:2:0 SDR，无音频。
5. 没有摄像头时可使用本机测试流（仅监听 127.0.0.1）：

   ```powershell
   ./BuildScripts/Start-LocalRtsp.ps1
   # 示例默认地址 rtsp://127.0.0.1:18554/test
   # 用完后停止测试服务器和推流进程：
   ./BuildScripts/Stop-LocalRtsp.ps1
   ```

测试流是 FFmpeg 生成的移动图案，通过 MediaMTX 传送真实 RTSP 数据。播放端直接调用 FFmpeg 动态库，不为每路视频启动 ffmpeg.exe。

## Package 与 API

包名 `com.xmh.streamvideoplayer`，作者 `xmh`，自研代码 MIT。

推荐先克隆并构建，然后用 Package Manager 的 **Add package from disk** 选择 `Packages/com.xmh.streamvideoplayer/package.json`。
也可在源代码推送后使用 Git URL，但 Git 包不含原生 DLL，不能单独完成运行环境安装：

```text
https://github.com/XiaoMinghui/UnityStreamVideoPlayer.git?path=/Packages/com.xmh.streamvideoplayer
```

`StreamVideoPlayerComponent` 提供 `Play()`、`Stop()`、`Texture`、`LastError`、`GetStatistics()`。
把 `Texture` 交给 RawImage 或材质显示，同时应用 `TextureCoordinates` 修正 D3D11 视频纹理的上下方向。RawImage 设置 `uvRect`；材质设置纹理 scale=(1,-1)、offset=(0,1)。示例使用 IMGUI，所以包本身不依赖 UGUI/URP。
需要直接控制生命周期时使用 `VideoStreamSession`，在 Unity 主线程创建、调用 `Render()`、查询并 `Dispose()`。
每个实例对应独立连接、解码器和输出纹理。输出大小固定，可指定为小窗口分辨率；源分辨率变化后 GPU 处理器会重建。
多路显示可创建 `VideoStreamRenderBatch(sessions)`，每帧调用一次 `batch.Render()`，将各路渲染事件合并到一个可复用的 Unity 命令缓冲中。批次不拥有会话，释放时先 Dispose 批次，再释放会话；增加或替换会话后重建批次。视频墙示例默认使用批量提交。
`TryGetPerformanceStatistics` 提供输入视图创建次数、同步争用及 CPU 侧提交耗时，适合定位性能瓶颈。解码输入视图与不变的色彩配置会复用；纹理池或分辨率改变后重建缓存。
不要在场景、预制体或提交的配置中保存摄像头密码。

## 当前边界

- 已实现：RTSP/TCP、H.264 D3D11VA、GPU NV12→BGRA、共享纹理同步、单帧输出、错误状态、读超时、停止/重开、测试用本地文件播放。
- 不静默回退软件解码；不支持的格式会报错。当前无音频、自动重连、UDP、H.265、HDR、旋转/去隔行支持。
- 输出像素不经过 CPU。显示仍包含 GPU 转色和一次 GPU 拷贝，不宣称零拷贝。
- 每路暂用独立 D3D11 解码设备；已建立 16 路短时压力测试，设备共享/调度优化、真实多源和长稳测试仍待完成。
- `Stop/Dispose` 目前在主线程等待原生线程结束；批量停止先对所有会话调用 `RequestStop()` 再释放，避免逐路叠加等待。DNS/驱动阻塞边界仍需专门验证。
- Android/iOS、D3D12、其他流协议尚未实现。首帧时间与端到端播放延迟是不同指标。

## 验证与路线

- 构建脚本执行 CTest；`VideoSmoke.exe <file-or-rtsp-url>` 验证硬解、GPU 输出和停止。
- Unity Test Runner 运行 PlayMode 的 `StreamVideoPlayer.Tests.PlayMode`。视频集成测试需在启动 Unity 前设置 `SVP_TEST_RTSP_URL` 为有动态图案的 H.264 RTSP 地址，未配置时会跳过。
- 独立程序验证：`./BuildScripts/Test-Player.ps1 -PlayerPath <exe> -Video`（RTSP 示例场景、默认本机测试流）。
- 消费工程运行包内测试时，把 `com.xmh.streamvideoplayer` 添加到 manifest 的 `testables`。

实测记录见 [Validation](Documentation/Validation.md)，设计见 [Architecture](Documentation/Architecture.md)，后续计划见 [Roadmap](Documentation/Roadmap.md)。
FFmpeg 开发依赖为 LGPL-3.0-or-later 动态构建，原始代码保持各自许可证；二进制不提交到 Git。发布含第三方二进制的 Release 前，仍需整理对应源码、构建资料和依赖许可证，详见 [ThirdParty](Documentation/ThirdParty.md)。




## 视频墙与性能复测

打开 `Assets/StreamVideoPlayerDev/Scenes/VideoWall.unity`，选择 1／4／9／16 后点击 Play。每个格子对应独立连接与解码器；默认重复连接同一测试源，也可以在 `sources` 数组填入不同摄像头。

```powershell
./BuildScripts/Start-LocalRtsp.ps1 -Width 1920 -Height 1080 -FrameRate 25 -Gop 25
./BuildScripts/Test-NativeVideoBenchmark.ps1 -Seconds 15
# 将 VideoWall 场景构建为 Windows Player 后：
./BuildScripts/Test-VideoWall.ps1 -PlayerPath <exe> -Seconds 15
./BuildScripts/Stop-LocalRtsp.ps1
```

输出 JSON/CSV 包含各路解码/显示帧率、丢帧、CPU、内存和停止时间。原生报告还包含进程本地显存；Unity 报告包含画面实际运行的 FPS 和 P95/P99 帧间隔。原生测试循环时间不等于 Unity 帧率，首帧时间不等于端到端延迟。

`TryGetStartupTimings` 用于查看启动各阶段耗时。RTSP SDP 已包含 SPS/PPS 时会跳过信息探测，否则保留原路径。摄像头的关键帧间隔仍然影响首帧速度。

测试方法与结果见 [Benchmark](Documentation/Benchmark.md)。
