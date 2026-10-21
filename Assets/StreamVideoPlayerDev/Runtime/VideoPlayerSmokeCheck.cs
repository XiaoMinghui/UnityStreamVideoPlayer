using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreamVideoPlayer.Development
{
    public sealed class VideoPlayerSmokeCheck : MonoBehaviour
    {
        [Serializable] private sealed class Result
        {
            public bool success;
            public string gpu;
            public string error;
            public ulong decodedFrames;
            public ulong presentedFrames;
            public int pixelContrast;
            public double firstFrameMs;
            public double stopMs;
        }
        private IEnumerator Start()
        {
            if (Application.isEditor) yield break;
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-svp-video-test");
            if (flag < 0 || flag + 1 >= args.Length) yield break;
            string output = args[flag + 1];
            int sourceFlag = Array.IndexOf(args, "-svp-source");
            string source = sourceFlag >= 0 && sourceFlag + 1 < args.Length ? args[sourceFlag + 1] : "rtsp://127.0.0.1:18554/test";
            Application.runInBackground = true;
            var result = new Result { gpu = SystemInfo.graphicsDeviceName };
            VideoStreamSession session = null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { session = new VideoStreamSession(source, 320, 180); }
            catch (Exception ex) { result.error = ex.Message; }
            while (session != null && result.error == null && watch.Elapsed.TotalSeconds < 15)
            {
                try
                {
                    session.Render();
                    var stats = session.GetStatistics();
                    result.decodedFrames = stats.DecodedFrames;
                    result.presentedFrames = stats.PresentedFrames;
                    if (stats.State == VideoPlaybackState.Failed) result.error = session.GetError();
                    if (stats.PresentedFrames > 0 && result.firstFrameMs == 0) result.firstFrameMs = watch.Elapsed.TotalMilliseconds;
                }
                catch (Exception ex) { result.error = ex.Message; }
                if (result.presentedFrames >= 15) break;
                yield return null;
            }
            if (result.error == null && result.presentedFrames < 15) result.error = "Video first-frame timeout.";
            if (result.error == null)
            {
                var readable = new RenderTexture(320, 180, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                readable.Create();
                Graphics.Blit(session.Texture, readable);
                var request = AsyncGPUReadback.Request(readable, 0, TextureFormat.RGBA32);
                float until = Time.realtimeSinceStartup + 5;
                while (!request.done && Time.realtimeSinceStartup < until) yield return null;
                if (!request.done || request.hasError) result.error = "Video pixel readback failed.";
                else
                {
                    int min = 765, max = 0;
                    var pixels = request.GetData<Color32>();
                    for (int i = 0; i < pixels.Length; i += 17)
                    {
                        int sum = pixels[i].r + pixels[i].g + pixels[i].b;
                        min = Math.Min(min, sum); max = Math.Max(max, sum);
                    }
                    result.pixelContrast = max - min;
                    if (result.pixelContrast <= 100) result.error = "Expected a nonuniform test pattern.";
                }
                readable.Release(); Destroy(readable);
            }
            watch.Restart(); session?.Dispose(); result.stopMs = watch.Elapsed.TotalMilliseconds;
            result.success = result.error == null;
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            Application.Quit(result.success ? 0 : 1);
        }
    }
}
