# Windows native plugins

BuildScripts/Build-Native.ps1 installs StreamVideoPlayerNative.dll (GPU diagnostic).
Add -WithVideo to build StreamVideoPlayerFFmpeg.dll and install its pinned FFmpeg shared dependencies.
Import settings restrict these DLLs to Windows x64 Editor/Player. Binaries and their generated importer metadata are ignored by Git.

Unity does not unload native libraries: close the consuming Editor/Player before installing changed DLLs. Identical files are skipped by the build script.

Downloaded FFmpeg is LGPL-3.0-or-later in this development configuration; it is not covered by the project's MIT license. This local DLL folder is not an audited release artifact. See Third Party Notices.md.
