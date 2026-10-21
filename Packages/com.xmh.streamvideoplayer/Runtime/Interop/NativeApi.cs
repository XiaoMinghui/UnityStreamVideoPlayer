using System;
using System.Runtime.InteropServices;

namespace StreamVideoPlayer.Interop
{
    internal enum NativeResult : int
    {
        Ok = 0,
        InvalidArgument = 1,
        UnsupportedGraphicsApi = 2,
        InvalidHandle = 3,
        GraphicsError = 4,
        InternalError = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInfo
    {
        public uint AbiVersion;
        public uint Capabilities;
        public uint ActiveProbes;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeProbeStats
    {
        public ulong RenderedFrames;
        public NativeResult Status;
        public uint Reserved;
    }

    // All managed calls use cdecl. The Unity render callback uses Unity's own ABI.
    internal static class NativeApi
    {
        internal const uint AbiVersion = 1;
        internal const string Library = "StreamVideoPlayerNative";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint svp_get_abi_version();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeResult svp_get_info(out NativeInfo info, uint size);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeResult svp_probe_register_texture(IntPtr texture, out int handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeResult svp_probe_unregister(int handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeResult svp_probe_get_stats(int handle, out NativeProbeStats stats, uint size);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr svp_get_render_event_func();
    }
}
