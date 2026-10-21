using System;
using System.Runtime.InteropServices;
using StreamVideoPlayer.Interop;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreamVideoPlayer
{
    public readonly struct RuntimeStatus
    {
        public bool NativeLibraryLoaded { get; }
        public bool GpuProbeSupported { get; }
        public uint ActiveProbes { get; }
        public string Message { get; }

        internal RuntimeStatus(bool loaded, bool supported, uint active, string message)
        {
            NativeLibraryLoaded = loaded;
            GpuProbeSupported = supported;
            ActiveProbes = active;
            Message = message;
        }
    }

    /// <summary>Call from Unity's main thread. Does not imply video playback is available.</summary>
    public static class RuntimeDiagnostics
    {
        public static RuntimeStatus GetStatus()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor &&
                Application.platform != RuntimePlatform.WindowsPlayer)
                return new RuntimeStatus(false, false, 0, "This preview supports Windows x64 only.");

            if (IntPtr.Size != 8)
                return new RuntimeStatus(false, false, 0, "The native plugin requires a 64-bit process.");

            try
            {
                uint abi = NativeApi.svp_get_abi_version();
                if (abi != NativeApi.AbiVersion)
                    return new RuntimeStatus(true, false, 0, $"Native ABI {abi} does not match managed ABI {NativeApi.AbiVersion}. Rebuild the native plugin and restart Unity.");

                var result = NativeApi.svp_get_info(out var info, (uint)Marshal.SizeOf<NativeInfo>());
                if (result != NativeResult.Ok)
                    return new RuntimeStatus(true, false, 0, $"Native diagnostics failed: {result}.");

                bool supported = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 &&
                                 (info.Capabilities & 1u) != 0;
                return new RuntimeStatus(true, supported, info.ActiveProbes, supported
                    ? "D3D11 GPU interop ready. FFmpeg/RTSP playback is not implemented in this preview."
                    : "Use Direct3D11 on Windows and restart Unity to enable the GPU diagnostic.");
            }
            catch (DllNotFoundException)
            {
                return new RuntimeStatus(false, false, 0, "Native DLL is missing. Run BuildScripts/Build-Native.ps1 from the source repository, then restart Unity if needed.");
            }
            catch (EntryPointNotFoundException)
            {
                return new RuntimeStatus(true, false, 0, "Native DLL is incompatible. Rebuild it and restart Unity.");
            }
            catch (BadImageFormatException)
            {
                return new RuntimeStatus(false, false, 0, "Native DLL has an incompatible architecture. Build Windows x64.");
            }
        }
    }
}
