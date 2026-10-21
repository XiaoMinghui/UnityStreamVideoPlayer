using System;
using UnityEngine;

namespace StreamVideoPlayer.Samples
{
    /// <summary>Add to an empty GameObject. Colored tiles are generated on the GPU, not decoded video.</summary>
    public sealed class GpuInteropDemo : MonoBehaviour
    {
        [SerializeField, Range(1, 16)] private int tileCount = 4;
        private GpuProbeSession[] sessions;
        private string message;
        private ulong frames;
        private float nextStatsTime;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            try
            {
                sessions = new GpuProbeSession[Mathf.Clamp(tileCount, 1, 16)];
                for (int i = 0; i < sessions.Length; ++i) sessions[i] = new GpuProbeSession();
                message = "GPU texture diagnostic — animated colors, not video playback";
            }
            catch (Exception ex)
            {
                message = ex.Message;
                ReleaseSessions();
            }
        }

        private void Update()
        {
            if (sessions == null) return;
            try
            {
                foreach (var session in sessions) session.Render();
                if (Time.unscaledTime >= nextStatsTime)
                {
                    frames = sessions[0].GetRenderedFrameCount();
                    nextStatsTime = Time.unscaledTime + 0.5f;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                ReleaseSessions();
            }
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(16, 12, Screen.width - 32, 24), "StreamVideoPlayer / GPU Interop Diagnostic");
            GUI.Label(new Rect(16, 38, Screen.width - 32, 24), message);
            GUI.Label(new Rect(16, 64, Screen.width - 32, 24), $"Native frames (first tile): {frames}");
            if (sessions == null) return;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(sessions.Length));
            int rows = Mathf.CeilToInt(sessions.Length / (float)columns);
            float width = (Screen.width - 32f) / columns;
            float height = Mathf.Max(1, (Screen.height - 104f) / rows);
            for (int i = 0; i < sessions.Length; ++i)
            {
                if (sessions[i].Texture == null) continue;
                var rect = new Rect(16 + (i % columns) * width, 96 + (i / columns) * height, width - 8, height - 8);
                GUI.DrawTexture(rect, sessions[i].Texture, ScaleMode.ScaleToFit, false);
            }
        }

        private void OnDisable() => ReleaseSessions();

        private void ReleaseSessions()
        {
            if (sessions == null) return;
            foreach (var session in sessions) session?.Dispose();
            sessions = null;
        }
    }
}
