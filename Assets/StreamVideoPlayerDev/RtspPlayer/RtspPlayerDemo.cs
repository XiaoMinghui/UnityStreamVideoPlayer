using UnityEngine;

namespace StreamVideoPlayer.Samples
{
    [RequireComponent(typeof(StreamVideoPlayerComponent))]
    public sealed class RtspPlayerDemo : MonoBehaviour
    {
        private StreamVideoPlayerComponent player;
        private string input;
        private string status = "Stopped";
        private float nextStatusUpdate;
        private void Awake() { player = GetComponent<StreamVideoPlayerComponent>(); input = player.source; }
        private void Update()
        {
            if (Time.unscaledTime < nextStatusUpdate) return;
            nextStatusUpdate = Time.unscaledTime + 0.25f;
            var stats = player.GetStatistics();
            status = player.LastError ?? (player.Texture == null ? "Stopped" :
                $"{stats.State} | {stats.SourceWidth} x {stats.SourceHeight} | decoded {stats.DecodedFrames} | displayed {stats.PresentedFrames} | dropped {stats.DroppedFrames}");
        }
        private void OnGUI()
        {
            GUI.Label(new Rect(16, 8, Screen.width - 32, 24), "StreamVideoPlayer | RTSP / H.264 preview");
            input = GUI.TextField(new Rect(16, 38, Mathf.Max(100, Screen.width - 205), 28), input);
            if (GUI.Button(new Rect(Screen.width - 180, 38, 76, 28), "Play")) { player.source = input; player.Play(); }
            if (GUI.Button(new Rect(Screen.width - 96, 38, 76, 28), "Stop")) player.Stop();
            GUI.Label(new Rect(16, 72, Screen.width - 32, 40), status);
            if (player.Texture != null && player.GetStatistics().PresentedFrames > 0)
            {
                var area = new Rect(16, 114, Mathf.Max(1, Screen.width - 32), Mathf.Max(1, Screen.height - 130));
                float scale = Mathf.Min(area.width / player.Texture.width, area.height / player.Texture.height);
                float width = player.Texture.width * scale, height = player.Texture.height * scale;
                var fitted = new Rect(area.center.x - width * 0.5f, area.center.y - height * 0.5f, width, height);
                GUI.DrawTextureWithTexCoords(fitted, player.Texture, player.TextureCoordinates, false);
            }
        }
    }
}
