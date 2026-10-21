using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreamVideoPlayer
{
    /// <summary>
    /// Records a fixed group of sessions once and submits it with one command buffer per Render call.
    /// Create, Render and Dispose on Unity's main thread. Does not own the sessions; closed sessions
    /// are safely skipped by their native handles. Create a new batch when adding/replacing sessions.
    /// </summary>
    public sealed class VideoStreamRenderBatch : IDisposable
    {
        private static readonly HashSet<VideoStreamRenderBatch> LiveBatches = new HashSet<VideoStreamRenderBatch>();
        private VideoStreamSession[] sessions;
        private CommandBuffer commands;

        public VideoStreamRenderBatch(IReadOnlyList<VideoStreamSession> sessions)
        {
            if (sessions == null) throw new ArgumentNullException(nameof(sessions));
            if (sessions.Count == 0) throw new ArgumentException("At least one session is required.", nameof(sessions));
            this.sessions = new VideoStreamSession[sessions.Count];
            try
            {
                commands = new CommandBuffer { name = "StreamVideoPlayer batch present" };
                for (int i = 0; i < sessions.Count; ++i)
                {
                    var session = sessions[i] ?? throw new ArgumentException("A batch cannot contain a null session.", nameof(sessions));
                    session.AppendRenderCommand(commands);
                    this.sessions[i] = session;
                }
                LiveBatches.Add(this);
            }
            catch { Dispose(); throw; }
        }

        public void Render()
        {
            if (commands == null) throw new ObjectDisposedException(nameof(VideoStreamRenderBatch));
            bool active = false;
            foreach (var session in sessions)
            {
                if (session.IsDisposed) continue;
                session.ValidateRenderTarget();
                active = true;
            }
            if (active) Graphics.ExecuteCommandBuffer(commands);
        }

        public void Dispose()
        {
            commands?.Dispose(); commands = null;
            sessions = null;
            LiveBatches.Remove(this);
        }

        internal static void DisposeAll()
        {
            var batches = new VideoStreamRenderBatch[LiveBatches.Count];
            LiveBatches.CopyTo(batches);
            foreach (var batch in batches) batch.Dispose();
        }
    }
}
