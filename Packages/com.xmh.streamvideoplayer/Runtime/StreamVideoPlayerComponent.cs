using System;
using UnityEngine;

namespace StreamVideoPlayer
{
    [AddComponentMenu("StreamVideoPlayer/Video Player")]
    public sealed class StreamVideoPlayerComponent : MonoBehaviour
    {
        [Tooltip("RTSP/TCP H.264 source. Avoid saving camera passwords in scenes or prefabs.")]
        public string source = "rtsp://127.0.0.1:18554/test";
        public bool playOnEnable;
        [Range(16, 4096)] public int outputWidth = 1280;
        [Range(16, 4096)] public int outputHeight = 720;
        public RenderTexture Texture => session?.Texture;
        public Rect TextureCoordinates => new Rect(0, 1, 1, -1);
        public string LastError { get; private set; }
        private VideoStreamSession session;

        private void OnEnable() { if (playOnEnable) Play(); }
        public void Play()
        {
            Stop(); LastError = null;
            try { session = new VideoStreamSession(source, outputWidth, outputHeight); }
            catch (Exception ex) { LastError = ex.Message; }
        }
        public void Stop() { session?.Dispose(); session = null; }
        public VideoStatistics GetStatistics() => session == null ? default : session.GetStatistics();
        public bool TryGetStartupTimings(out VideoStartupTimings timings)
        {
            timings = default;
            return session != null && session.TryGetStartupTimings(out timings);
        }
        private void Update()
        {
            if (session == null) return;
            try
            {
                session.Render();
                if (LastError == null && session.GetStatistics().State == VideoPlaybackState.Failed)
                    LastError = session.GetError();
            }
            catch (Exception ex) { LastError = ex.Message; Stop(); }
        }
        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
    }
}
