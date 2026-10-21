using UnityEditor;
using UnityEngine;

namespace StreamVideoPlayer.Editor
{
    [InitializeOnLoad]
    internal static class ProbeCleanup
    {
        static ProbeCleanup()
        {
            AssemblyReloadEvents.beforeAssemblyReload += GpuProbeSession.DisposeAll;
            EditorApplication.quitting += GpuProbeSession.DisposeAll;
            AssemblyReloadEvents.beforeAssemblyReload += VideoStreamSession.DisposeAll;
            EditorApplication.quitting += VideoStreamSession.DisposeAll;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) { GpuProbeSession.DisposeAll(); VideoStreamSession.DisposeAll(); }
            };
        }
    }

    internal sealed class DiagnosticsWindow : EditorWindow
    {
        private RuntimeStatus status;

        [MenuItem("Window/StreamVideoPlayer/Diagnostics")]
        private static void Open() => GetWindow<DiagnosticsWindow>("StreamVideoPlayer");

        private void OnEnable() => RefreshStatus();

        private void RefreshStatus() => status = RuntimeDiagnostics.GetStatus();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("StreamVideoPlayer", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Package", "com.xmh.streamvideoplayer");
            EditorGUILayout.LabelField("GPU", SystemInfo.graphicsDeviceName);
            EditorGUILayout.LabelField("Graphics API", SystemInfo.graphicsDeviceType.ToString());
            EditorGUILayout.LabelField("Color space", QualitySettings.activeColorSpace.ToString());
            EditorGUILayout.LabelField("Native DLL", status.NativeLibraryLoaded ? "Loaded" : "Unavailable");
            EditorGUILayout.LabelField("Active GPU probes", status.ActiveProbes.ToString());
            EditorGUILayout.HelpBox(status.Message ?? "Click Refresh.", MessageType.Info);
            EditorGUILayout.HelpBox("GPU probes verify texture interop. Video playback uses the separate optional FFmpeg module (Windows D3D11, H.264, RTSP/TCP).", MessageType.Info);
            if (GUILayout.Button("Refresh diagnostics")) RefreshStatus();
        }
    }
}
