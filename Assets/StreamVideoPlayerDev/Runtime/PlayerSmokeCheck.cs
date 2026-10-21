using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreamVideoPlayer.Development
{
    /// <summary>Source-project verification helper. Not part of the published UPM package.</summary>
    public sealed class PlayerSmokeCheck : MonoBehaviour
    {
        [Serializable]
        private sealed class Result
        {
            public bool success;
            public string gpu;
            public string graphicsApi;
            public string error;
            public ulong renderedFrames;
            public uint probesBefore;
            public uint probesAfter;
        }

        private IEnumerator Start()
        {
            if (Application.isEditor) yield break;
            var args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-svp-smoke-test");
            if (flag < 0 || flag + 1 >= args.Length) yield break;
            string resultPath = args[flag + 1];
            Application.runInBackground = true;
            var result = new Result { gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString() };
            GpuProbeSession probe = null;
            try
            {
                result.probesBefore = RuntimeDiagnostics.GetStatus().ActiveProbes;
                probe = new GpuProbeSession(32, 32);
                probe.Render();
            }
            catch (Exception ex) { result.error = ex.Message; }

            if (probe != null && result.error == null)
            {
                // Give the render thread time to process the native event.
                yield return null;
                yield return null;
                try { result.renderedFrames = probe.GetRenderedFrameCount(); }
                catch (Exception ex) { result.error = ex.Message; }

                if (result.error == null && SystemInfo.supportsAsyncGPUReadback)
                {
                    var readback = AsyncGPUReadback.Request(probe.Texture, 0, TextureFormat.RGBA32);
                    for (int i = 0; i < 300 && !readback.done; ++i) yield return null;
                    if (!readback.done || readback.hasError) result.error = "GPU verification readback failed.";
                    else
                    {
                        var pixel = readback.GetData<Color32>()[0];
                        if (pixel.a != 255 || pixel.r + pixel.g + pixel.b < 30)
                            result.error = "Native render did not produce the expected non-black opaque pixel.";
                    }
                }
                else if (result.error == null) result.error = "GPU readback unsupported; pixel verification could not run.";
            }
            probe?.Dispose();
            result.probesAfter = RuntimeDiagnostics.GetStatus().ActiveProbes;
            result.success = result.error == null && result.renderedFrames > 0 && result.probesBefore == result.probesAfter;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath)));
            File.WriteAllText(resultPath, JsonUtility.ToJson(result, true));
            Application.Quit(result.success ? 0 : 1);
        }
    }
}
