using UnityEngine;
using UnityEngine.Video;

namespace Prism
{
    /// <summary>
    /// Minimal test to verify Unity VideoPlayer works on this system.
    /// Attach to any GameObject and press Play.
    /// </summary>
    public class MinimalVideoTest : MonoBehaviour
    {
        [SerializeField] private string _videoPath = "/home/pierce/test_video.mp4";
        [SerializeField] private bool _useUrl = false;

        private VideoPlayer _videoPlayer;

        private void Start()
        {
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.renderMode = VideoRenderMode.CameraFarPlane;
            _videoPlayer.targetCamera = Camera.main;

            _videoPlayer.errorReceived += (vp, msg) => Debug.LogError($"[MinimalTest] Error: {msg}");
            _videoPlayer.prepareCompleted += (vp) => {
                Debug.Log("[MinimalTest] Prepared! Starting playback...");
                _videoPlayer.Play();
            };
            _videoPlayer.started += (vp) => Debug.Log("[MinimalTest] Playing!");

            if (_useUrl)
            {
                Debug.Log($"[MinimalTest] Trying URL: file://{_videoPath}");
                _videoPlayer.source = VideoSource.Url;
                _videoPlayer.url = "file://" + _videoPath;
            }
            else
            {
                Debug.Log($"[MinimalTest] Trying VideoClip path (drag clip in inspector)");
                // For local files, Unity prefers using VideoClip assets
                // Try URL mode with file:// prefix
                Debug.Log($"[MinimalTest] Falling back to URL: file://{_videoPath}");
                _videoPlayer.source = VideoSource.Url;
                _videoPlayer.url = "file://" + _videoPath;
            }

            Debug.Log("[MinimalTest] Preparing...");
            _videoPlayer.Prepare();
        }

        [ContextMenu("Test With Direct Path")]
        public void TestDirectPath()
        {
            if (_videoPlayer == null)
            {
                _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            }

            _videoPlayer.source = VideoSource.Url;
            _videoPlayer.url = _videoPath; // Without file:// prefix
            _videoPlayer.Prepare();
        }
    }
}
