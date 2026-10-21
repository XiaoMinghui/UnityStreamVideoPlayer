using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using StreamVideoPlayer.Interop;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreamVideoPlayer
{
    /// <summary>
    /// Diagnostic only: a native render event writes animated colors into a Unity-owned texture.
    /// This does not decode video. Create, render and dispose on Unity's main thread.
    /// </summary>
    public sealed class GpuProbeSession : IDisposable
    {
        private static readonly HashSet<GpuProbeSession> LiveSessions = new HashSet<GpuProbeSession>();
        private int handle;
        private CommandBuffer commands;
        private RenderTexture texture;

        public RenderTexture Texture => texture;
        public bool IsDisposed => handle == 0;

        public GpuProbeSession(int width = 640, int height = 360)
        {
            if (width < 1 || height < 1 || width > 4096 || height > 4096)
                throw new ArgumentOutOfRangeException(nameof(width), "Diagnostic dimensions must be between 1 and 4096.");

            var status = RuntimeDiagnostics.GetStatus();
            if (!status.GpuProbeSupported)
                throw new NotSupportedException(status.Message);

            try
            {
                texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "StreamVideoPlayer GPU diagnostic",
                    useMipMap = false,
                    autoGenerateMips = false,
                    antiAliasing = 1,
                    hideFlags = HideFlags.DontSave
                };
                if (!texture.Create())
                    throw new InvalidOperationException("Could not allocate the GPU diagnostic texture.");

                Check(NativeApi.svp_probe_register_texture(texture.GetNativeTexturePtr(), out handle));
                var callback = NativeApi.svp_get_render_event_func();
                if (callback == IntPtr.Zero)
                    throw new InvalidOperationException("Native render callback is unavailable.");

                commands = new CommandBuffer { name = "StreamVideoPlayer GPU diagnostic" };
                commands.IssuePluginEvent(callback, handle);
                LiveSessions.Add(this);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Render()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(GpuProbeSession));
            if (!texture.IsCreated())
                throw new InvalidOperationException("The graphics texture was lost. Dispose and recreate the diagnostic session.");
            Graphics.ExecuteCommandBuffer(commands);
        }

        public ulong GetRenderedFrameCount()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(GpuProbeSession));
            Check(NativeApi.svp_probe_get_stats(handle, out var stats, (uint)Marshal.SizeOf<NativeProbeStats>()));
            Check(stats.Status);
            return stats.RenderedFrames;
        }

        public void Dispose()
        {
            // Invalidate the native handle before releasing the Unity-owned texture.
            // Any already queued render event becomes a harmless lookup miss.
            if (handle != 0)
            {
                NativeApi.svp_probe_unregister(handle);
                handle = 0;
            }
            commands?.Dispose();
            commands = null;
            if (texture != null)
            {
                texture.Release();
                if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                else UnityEngine.Object.DestroyImmediate(texture);
                texture = null;
            }
            LiveSessions.Remove(this);
        }

        // Also called before an Editor assembly reload, including sessions created by editor tools.
        public static void DisposeAll()
        {
            var sessions = new GpuProbeSession[LiveSessions.Count];
            LiveSessions.CopyTo(sessions);
            foreach (var session in sessions) session.Dispose();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessions() => DisposeAll();

        private static void Check(NativeResult result)
        {
            if (result != NativeResult.Ok)
                throw new InvalidOperationException($"StreamVideoPlayer native diagnostic: {result}.");
        }
    }
}
