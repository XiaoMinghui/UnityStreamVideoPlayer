using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StreamVideoPlayer.Development
{
    public static class DevelopmentTools
    {
        public const string ScenePath = "Assets/StreamVideoPlayerDev/Scenes/GpuInterop.unity";

        [MenuItem("Tools/StreamVideoPlayer/Create Video Wall Scene")]
        public static void CreateWallScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < SceneManager.sceneCount; ++i)
                if (SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Save modified scenes first.");
            const string path = "Assets/StreamVideoPlayerDev/Scenes/VideoWall.unity";
            if (File.Exists(path)) { EditorSceneManager.OpenScene(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var demo = new GameObject("StreamVideoPlayer Video Wall");
            demo.AddComponent<Samples.VideoWallDemo>();
            demo.AddComponent<VideoWallBenchmark>();
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0.025f, 0.03f, 0.045f);
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/StreamVideoPlayer/Create RTSP Player Scene")]
        public static void CreateVideoScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < SceneManager.sceneCount; ++i)
                if (SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Save modified scenes first.");
            const string path = "Assets/StreamVideoPlayerDev/Scenes/RtspPlayer.unity";
            if (File.Exists(path)) { EditorSceneManager.OpenScene(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var demo = new GameObject("StreamVideoPlayer RTSP Player");
            demo.AddComponent<StreamVideoPlayerComponent>();
            demo.AddComponent<Samples.RtspPlayerDemo>();
            demo.AddComponent<VideoPlayerSmokeCheck>();
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0.025f, 0.03f, 0.045f);
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/StreamVideoPlayer/Create GPU Diagnostic Scene")]
        public static void CreateGpuScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Exit Play mode before creating a scene.");
            for (int i = 0; i < SceneManager.sceneCount; ++i)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new System.InvalidOperationException("Save modified scenes first.");
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var demo = new GameObject("StreamVideoPlayer GPU Diagnostic");
            demo.AddComponent<Samples.GpuInteropDemo>();
            demo.AddComponent<PlayerSmokeCheck>();
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0.035f, 0.045f, 0.065f);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }
    }
}
