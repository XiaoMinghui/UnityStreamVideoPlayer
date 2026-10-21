# Third-party notices

Original StreamVideoPlayer code is MIT licensed; see LICENSE.md.

Unity PluginAPI headers are supplied by the installed Unity Editor under its accompanying license. They are not copied into this repository. Unity and Windows SDK dependencies are not relicensed under MIT.

The source package contains an optional FFmpeg backend. BuildScripts/Get-FFmpeg.ps1 fetches a checksum-pinned BtbN shared Windows development SDK. That build enables version3 and is LGPL-3.0-or-later, not MIT or LGPL-2.1-only. Its binary archive, library headers and executables are not committed to this source package. The SDK includes LICENSE.txt; build flags are saved locally in ThirdParty~/ffmpeg-build-configuration.txt.

Do not redistribute the local development DLL folder as a release without corresponding source, build/patch information, license texts and notices for FFmpeg and its dependencies. See the source repository's Documentation/ThirdParty.md and BuildScripts/ffmpeg.lock.json. No FFmpeg binary release is provided yet.
