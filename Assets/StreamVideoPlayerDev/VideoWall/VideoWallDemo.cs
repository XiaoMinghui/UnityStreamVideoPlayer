using System;
using System.Collections.Generic;
using UnityEngine;

namespace StreamVideoPlayer.Samples
{
    /// <summary>Independent connections/decoders. Same source by default; override Sources for real cameras.</summary>
    public sealed class VideoWallDemo : MonoBehaviour
    {
        public string source = "rtsp://127.0.0.1:18554/test";
        [Range(1, 16)] public int streamCount = 4;
        public string[] sources = Array.Empty<string>();
        public int outputWidth = 640, outputHeight = 360;
        public string LastError { get; private set; }
        public IReadOnlyList<VideoStreamSession> Sessions => sessions;
        public bool BatchRendering { get; set; } = true;
        public double RenderSubmissionCpuMs { get; private set; }
        private readonly List<VideoStreamSession> sessions = new List<VideoStreamSession>();
        private VideoStreamRenderBatch renderBatch;
        private string input;
        private string[] labels = Array.Empty<string>();
        private bool[] visible = Array.Empty<bool>();
        private VideoStatistics[] previous = Array.Empty<VideoStatistics>();
        private float nextUpdate, previousTime;
        private static readonly int[] Counts = { 1, 4, 9, 16 };
        private void Awake() => input = source;

        public void Play()
        {
            Stop(); LastError = null;
            try
            {
                int count = Mathf.Clamp(streamCount, 1, 16);
                labels = new string[count]; visible = new bool[count]; previous = new VideoStatistics[count];
                for (int i = 0; i < count; ++i)
                {
                    string url = sources != null && i < sources.Length && !string.IsNullOrWhiteSpace(sources[i]) ? sources[i] : source;
                    sessions.Add(new VideoStreamSession(url, outputWidth, outputHeight));
                    labels[i] = $"{i + 1} | Opening";
                }
                previousTime = Time.realtimeSinceStartup;
                nextUpdate = previousTime + 0.5f;
                renderBatch = new VideoStreamRenderBatch(sessions);
            }
            catch (Exception ex) { LastError = ex.Message; Stop(); }
        }
        public void Stop()
        {
            renderBatch?.Dispose(); renderBatch = null;
            foreach (var session in sessions) session.RequestStop();
            foreach (var session in sessions) session.Dispose();
            sessions.Clear();
        }
        private void Update()
        {
            try
            {
                long begin = System.Diagnostics.Stopwatch.GetTimestamp();
                if (BatchRendering) renderBatch?.Render();
                else foreach (var session in sessions) session.Render();
                RenderSubmissionCpuMs += (System.Diagnostics.Stopwatch.GetTimestamp() - begin) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                float now = Time.realtimeSinceStartup;
                if (now < nextUpdate) return;
                for (int i = 0; i < sessions.Count; ++i)
                {
                    var stats = sessions[i].GetStatistics();
                    double fps = (stats.PresentedFrames - previous[i].PresentedFrames) / Math.Max(0.001, now - previousTime);
                    visible[i] = stats.PresentedFrames > 0;
                    labels[i] = $"{i + 1} | {stats.State} | {fps:F1} fps | drops {stats.DroppedFrames}";
                    if (stats.State == VideoPlaybackState.Failed) labels[i] = $"{i + 1} | {sessions[i].GetError()}";
                    previous[i] = stats;
                }
                previousTime = now; nextUpdate = now + 0.5f;
            }
            catch (Exception ex) { LastError = ex.Message; Stop(); }
        }
        private void OnGUI()
        {
            GUI.Label(new Rect(16, 8, Screen.width - 32, 24), "StreamVideoPlayer | Independent RTSP video wall");
            input = GUI.TextField(new Rect(16, 36, Mathf.Max(100, Screen.width - 210), 26), input);
            if (GUI.Button(new Rect(Screen.width - 180, 36, 76, 26), "Play")) { source = input; Play(); }
            if (GUI.Button(new Rect(Screen.width - 96, 36, 76, 26), "Stop")) Stop();
            for (int i = 0; i < Counts.Length; ++i)
                if (GUI.Toggle(new Rect(16 + i * 64, 68, 60, 24), streamCount == Counts[i], Counts[i].ToString())) streamCount = Counts[i];
            if (LastError != null) GUI.Label(new Rect(280, 68, Screen.width - 296, 36), LastError);
            int columns = Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, sessions.Count)));
            int rows = Mathf.CeilToInt(sessions.Count / (float)columns);
            float tileWidth = (Screen.width - 32f) / columns;
            float tileHeight = (Screen.height - 108f) / Mathf.Max(1, rows);
            for (int i = 0; i < sessions.Count; ++i)
            {
                float x = 16 + (i % columns) * tileWidth, y = 102 + (i / columns) * tileHeight;
                GUI.Label(new Rect(x + 4, y, tileWidth - 8, 24), labels[i]);
                if (!visible[i]) continue;
                var texture = sessions[i].Texture;
                float scale = Mathf.Min((tileWidth - 8) / texture.width, Mathf.Max(1, tileHeight - 30) / texture.height);
                float width = texture.width * scale, height = texture.height * scale;
                var rect = new Rect(x + (tileWidth - width) / 2, y + 26 + (tileHeight - 30 - height) / 2, width, height);
                GUI.DrawTextureWithTexCoords(rect, texture, sessions[i].TextureCoordinates, false);
            }
        }
        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
    }
}
