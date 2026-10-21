using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using StreamVideoPlayer.Interop;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace StreamVideoPlayer
{
    public enum VideoPlaybackState { Opening, Playing, Ended, Failed }

    /// <summary>Microseconds from native session construction. Zero means not reached, not zero duration.
    /// Presented means submitted to Unity's GPU context; it is not a screen/optical timestamp.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VideoStartupTimings
    {
        public ulong DeviceReadyUs;
        public ulong InputOpenUs;
        public ulong StreamInfoUs;
        public ulong DecoderReadyUs;
        public ulong FirstDecodedUs;
        public ulong FirstPresentedUs;
        public uint SkippedStreamProbe;
        private uint reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VideoStatistics
    {
        public ulong DecodedFrames;
        public ulong PresentedFrames;
        public ulong DroppedFrames;
        public VideoPlaybackState State;
        public int ErrorCode;
        public int SourceWidth;
        public int SourceHeight;
    }

    /// <summary>
    /// Windows D3D11 / H.264 preview. Create, Render, query and Dispose on Unity's main thread.
    /// Texture dimensions are fixed; the GPU scales the source into this texture.
    /// Network I/O and decoding run on a native worker. Dispose interrupts I/O and joins it.
    /// </summary>
    public sealed class VideoStreamSession : IDisposable
    {
        private static readonly HashSet<VideoStreamSession> LiveSessions = new HashSet<VideoStreamSession>();
        private int handle;
        private CommandBuffer commands;
        private RenderTexture texture;
        private bool timingsUnavailable;
        private bool stopRequestUnavailable;
        public RenderTexture Texture => texture;
        /// <summary>Apply to RawImage.uvRect or material scale/offset when sampling Texture.</summary>
        public Rect TextureCoordinates => new Rect(0, 1, 1, -1);
        public bool IsDisposed => handle == 0;

        public VideoStreamSession(string source, int width = 1280, int height = 720)
        {
            if (string.IsNullOrWhiteSpace(source) || source.IndexOf('\0') >= 0)
                throw new ArgumentException("A valid RTSP URL or local video path is required.", nameof(source));
            if (width < 16 || height < 16 || width > 4096 || height > 4096)
                throw new ArgumentOutOfRangeException(nameof(width), "Output dimensions must be between 16 and 4096.");
            if ((Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer) ||
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                throw new NotSupportedException("This playback preview requires Windows x64 and Direct3D 11.");
            try
            {
                if (VideoNativeApi.svp_video_abi_version() != 1)
                    throw new InvalidOperationException("FFmpeg plugin ABI mismatch. Rebuild the native plugin.");
                var format = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? GraphicsFormat.B8G8R8A8_SRGB : GraphicsFormat.B8G8R8A8_UNorm;
                if (!SystemInfo.IsFormatSupported(format, FormatUsage.Render))
                    throw new NotSupportedException("BGRA8 render textures are unavailable on this GPU.");
                texture = new RenderTexture(new RenderTextureDescriptor(width, height)
                {
                    graphicsFormat = format, depthBufferBits = 0, msaaSamples = 1,
                    mipCount = 1, useMipMap = false, autoGenerateMips = false
                }) { name = "StreamVideoPlayer output", hideFlags = HideFlags.DontSave };
                if (!texture.Create()) throw new InvalidOperationException("Could not allocate video output texture.");
                Check(VideoNativeApi.svp_video_open(Encoding.UTF8.GetBytes(source + "\0"), texture.GetNativeTexturePtr(), out handle));
                var callback = VideoNativeApi.svp_video_render_event();
                if (callback == IntPtr.Zero) throw new InvalidOperationException("Native video render callback is unavailable.");
                commands = new CommandBuffer { name = "StreamVideoPlayer video present" };
                commands.IssuePluginEvent(callback, handle);
                LiveSessions.Add(this);
            }
            catch (DllNotFoundException ex)
            {
                Dispose();
                throw new InvalidOperationException("FFmpeg runtime is missing. Run BuildScripts/Build-Native.ps1 -WithVideo before using playback.", ex);
            }
            catch { Dispose(); throw; }
        }

        public void Render()
        {
            ThrowIfDisposed();
            if (!texture.IsCreated()) throw new InvalidOperationException("Video texture was lost. Dispose and recreate this session.");
            Graphics.ExecuteCommandBuffer(commands);
        }
        public VideoStatistics GetStatistics()
        {
            ThrowIfDisposed();
            Check(VideoNativeApi.svp_video_get_stats(handle, out var stats, (uint)Marshal.SizeOf<VideoStatistics>()));
            return stats;
        }
        public string GetError()
        {
            ThrowIfDisposed();
            var message = new StringBuilder(512);
            Check(VideoNativeApi.svp_video_get_error(handle, message, (uint)message.Capacity));
            return message.ToString();
        }
        /// <summary>False when an older native DLL is still loaded. Restart Unity after updating DLLs.</summary>
        public bool TryGetStartupTimings(out VideoStartupTimings timings)
        {
            ThrowIfDisposed();
            timings = default;
            if (timingsUnavailable) return false;
            try
            {
                Check(VideoNativeApi.svp_video_get_timings(handle, out timings, (uint)Marshal.SizeOf<VideoStartupTimings>()));
                return true;
            }
            catch (EntryPointNotFoundException) { timingsUnavailable = true; return false; }
        }
        public void Dispose()
        {
            if (handle != 0) { VideoNativeApi.svp_video_close(handle); handle = 0; }
            commands?.Dispose(); commands = null;
            if (texture != null)
            {
                texture.Release();
                if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                else UnityEngine.Object.DestroyImmediate(texture);
                texture = null;
            }
            LiveSessions.Remove(this);
        }
        /// <summary>Signals cancellation without waiting. Call on each session before disposing a batch.
        /// Dispose remains required. Older native libraries cancel during Dispose instead.</summary>
        public void RequestStop()
        {
            if (IsDisposed || stopRequestUnavailable) return;
            try
            {
                var result = VideoNativeApi.svp_video_request_stop(handle);
                if (result != NativeResult.InvalidHandle) Check(result); // Device shutdown already retired this session.
            }
            catch (EntryPointNotFoundException) { stopRequestUnavailable = true; }
        }
        public static void DisposeAll()
        {
            var sessions = new VideoStreamSession[LiveSessions.Count];
            LiveSessions.CopyTo(sessions);
            foreach (var session in sessions) session.RequestStop();
            foreach (var session in sessions) session.Dispose();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessions() => DisposeAll();
        private void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(VideoStreamSession));
        }
        private static void Check(NativeResult result)
        {
            if (result != NativeResult.Ok) throw new InvalidOperationException($"StreamVideoPlayer native playback: {result}.");
        }
    }
}
