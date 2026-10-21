# Third-party dependencies

Original StreamVideoPlayer code is MIT. Unity SDK headers, FFmpeg and other dependencies retain their licenses. The package has no dependency on Unity MCP; MCP belongs only to the development host project.

## Pinned local FFmpeg development SDK

BuildScripts/ffmpeg.lock.json locks BtbN's Windows x64 LGPL shared build `n8.1.3-18-g46d899d7a0-20261020` to SHA-256 `453a30a5019676e805a85d4845e3aad31234dfed4ae0ea2b1d4e97254dc44b0a`.
The build uses `--enable-shared --disable-static --enable-version3`, without `--enable-gpl` or `--enable-nonfree`; therefore its FFmpeg license is LGPL-3.0-or-later. Get-FFmpeg.ps1 verifies the archive and saves `ffmpeg -version` including configure flags in ignored ThirdParty~/ffmpeg-build-configuration.txt. The archive includes LICENSE.txt and development headers/import libraries.

This is a development convenience, not a minimal or fully audited binary release recipe. The upstream build includes many optional external libraries beyond the three directly linked by our backend (avformat, avcodec, avutil). Binary redistribution requires a complete transitive-dependency/source/license inventory. Upstream autobuild retention may remove old assets; the checksum prevents silently changing dependencies, but long-term release reproducibility needs archived inputs or a project-owned source build.

## Test fixture

Start-LocalRtsp.ps1 downloads checksum-pinned MediaMTX v1.21.2 Windows amd64 (MIT), creates an H.264 test video using the downloaded FFmpeg/libopenh264 encoder, and exposes RTSP only on 127.0.0.1:18554. Test tools are not package runtime dependencies or committed binaries. Stop-LocalRtsp.ps1 stops only recorded process IDs whose executable path and creation time still match.

## Binary release requirements (not completed)

- Archive exact FFmpeg and dependency source revisions, patches, toolchain and configure options.
- Include license texts/notices and preserve users' applicable LGPL modification/relinking rights.
- Audit every optional/transitive library; keep baseline free of GPL/nonfree components.
- Prepare project-owned reproducible minimal builds and release checksums.
- Review mobile/static-link packaging separately when Android/iOS work starts.
- Do not label the combined binary distribution as MIT. Codec patent considerations are separate from source licensing.

Unity PluginAPI headers are read from the installed Editor/Data/PluginAPI and are not redistributed in the source repository. Review their accompanying Unity Companion License for release artifacts.

References: https://ffmpeg.org/legal.html ; https://github.com/BtbN/FFmpeg-Builds ; https://github.com/bluenviron/mediamtx
