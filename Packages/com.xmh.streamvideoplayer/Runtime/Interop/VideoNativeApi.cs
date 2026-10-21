using System;
using System.Runtime.InteropServices;
using System.Text;

namespace StreamVideoPlayer.Interop
{
    internal static class VideoNativeApi
    {
        private const string Library = "StreamVideoPlayerFFmpeg";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint svp_video_abi_version();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeResult svp_video_open(byte[] url, IntPtr texture, out int handle);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeResult svp_video_close(int handle);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeResult svp_video_request_stop(int handle);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeResult svp_video_get_stats(int handle, out VideoStatistics stats, uint size);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeResult svp_video_get_timings(int handle, out VideoStartupTimings timings, uint size);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern NativeResult svp_video_get_error(int handle, StringBuilder buffer, uint size);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr svp_video_render_event();
    }
}
