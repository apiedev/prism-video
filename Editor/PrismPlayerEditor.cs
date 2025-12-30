using UnityEngine;
using UnityEditor;
using Prism;
using Prism.Streaming;

namespace Prism.Editor
{
    [CustomEditor(typeof(PrismPlayer))]
    public class PrismPlayerEditor : UnityEditor.Editor
    {
        private SerializedProperty _sourceType;
        private SerializedProperty _videoClip;
        private SerializedProperty _url;
        private SerializedProperty _streamQuality;
        private SerializedProperty _autoResolveUrls;
        private SerializedProperty _playOnAwake;
        private SerializedProperty _loop;
        private SerializedProperty _volume;
        private SerializedProperty _playbackSpeed;
        private SerializedProperty _targetTexture;
        private SerializedProperty _resolution;

        private bool _showStreamingInfo = true;

        private void OnEnable()
        {
            _sourceType = serializedObject.FindProperty("_sourceType");
            _videoClip = serializedObject.FindProperty("_videoClip");
            _url = serializedObject.FindProperty("_url");
            _streamQuality = serializedObject.FindProperty("_streamQuality");
            _autoResolveUrls = serializedObject.FindProperty("_autoResolveUrls");
            _playOnAwake = serializedObject.FindProperty("_playOnAwake");
            _loop = serializedObject.FindProperty("_loop");
            _volume = serializedObject.FindProperty("_volume");
            _playbackSpeed = serializedObject.FindProperty("_playbackSpeed");
            _targetTexture = serializedObject.FindProperty("_targetTexture");
            _resolution = serializedObject.FindProperty("_resolution");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PrismPlayer player = (PrismPlayer)target;

            // Source section
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_sourceType);

            PrismSourceType sourceType = (PrismSourceType)_sourceType.enumValueIndex;
            switch (sourceType)
            {
                case PrismSourceType.VideoClip:
                    EditorGUILayout.PropertyField(_videoClip);
                    break;

                case PrismSourceType.Url:
                    EditorGUILayout.PropertyField(_url, new GUIContent("Direct URL"));
                    EditorGUILayout.HelpBox("Use direct video URLs (.mp4, .webm, etc.)", MessageType.Info);
                    break;

                case PrismSourceType.Stream:
                    EditorGUILayout.PropertyField(_url, new GUIContent("Stream URL"));
                    EditorGUILayout.PropertyField(_streamQuality);

                    // Show supported platforms
                    EditorGUILayout.HelpBox(
                        "Supported: YouTube, Twitch, Vimeo, Facebook, Twitter, TikTok, and 1000+ more via yt-dlp",
                        MessageType.Info);

                    // Show yt-dlp status
                    DrawYtdlpStatus();
                    break;

                case PrismSourceType.WebRTC:
                    EditorGUILayout.HelpBox("WebRTC source requires additional setup. See PrismWebRTCReceiver.", MessageType.Info);
                    break;
            }

            EditorGUILayout.Space();

            // Streaming section
            EditorGUILayout.LabelField("Streaming", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_autoResolveUrls, new GUIContent("Auto-Resolve URLs",
                "Automatically detect and resolve YouTube/Twitch URLs when using SetSource()"));

            EditorGUILayout.Space();

            // Playback section
            EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_playOnAwake);
            EditorGUILayout.PropertyField(_loop);
            EditorGUILayout.PropertyField(_volume);
            EditorGUILayout.PropertyField(_playbackSpeed);

            EditorGUILayout.Space();

            // Output section
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_targetTexture);
            EditorGUILayout.PropertyField(_resolution);

            // Resolution presets
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Presets");
            if (GUILayout.Button("720p", EditorStyles.miniButtonLeft))
            {
                _resolution.vector2IntValue = new Vector2Int(1280, 720);
            }
            if (GUILayout.Button("1080p", EditorStyles.miniButtonMid))
            {
                _resolution.vector2IntValue = new Vector2Int(1920, 1080);
            }
            if (GUILayout.Button("4K", EditorStyles.miniButtonMid))
            {
                _resolution.vector2IntValue = new Vector2Int(3840, 2160);
            }
            if (GUILayout.Button("8K", EditorStyles.miniButtonRight))
            {
                _resolution.vector2IntValue = new Vector2Int(7680, 4320);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Runtime info (only in play mode)
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);

                // State
                EditorGUILayout.LabelField("State", player.State.ToString());

                // Stream info
                if (player.CurrentStreamInfo != null && player.CurrentStreamInfo.Success)
                {
                    _showStreamingInfo = EditorGUILayout.Foldout(_showStreamingInfo, "Stream Info");
                    if (_showStreamingInfo)
                    {
                        EditorGUI.indentLevel++;
                        if (!string.IsNullOrEmpty(player.CurrentStreamInfo.Title))
                            EditorGUILayout.LabelField("Title", player.CurrentStreamInfo.Title);
                        EditorGUILayout.LabelField("Format", player.CurrentStreamInfo.Format);
                        EditorGUILayout.LabelField("Live Stream", player.IsLiveStream ? "Yes" : "No");
                        if (!string.IsNullOrEmpty(player.ResolvedUrl))
                        {
                            EditorGUILayout.LabelField("Resolved URL");
                            EditorGUILayout.SelectableLabel(player.ResolvedUrl, GUILayout.Height(40));
                        }
                        EditorGUI.indentLevel--;
                    }
                }

                // Playback info
                if (!player.IsLiveStream)
                {
                    EditorGUILayout.LabelField($"Time: {player.Time:F1}s / {player.Duration:F1}s ({player.NormalizedTime:P0})");
                }

                // Controls
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Play"))
                {
                    player.Play();
                }
                if (GUILayout.Button("Pause"))
                {
                    player.Pause();
                }
                if (GUILayout.Button("Stop"))
                {
                    player.Stop();
                }
                EditorGUILayout.EndHorizontal();

                // Seek slider (not for live streams)
                if (!player.IsLiveStream && player.Duration > 0)
                {
                    float newTime = EditorGUILayout.Slider("Seek", (float)player.Time, 0f, (float)player.Duration);
                    if (Mathf.Abs(newTime - (float)player.Time) > 0.5f)
                    {
                        player.Seek(newTime);
                    }
                }

                // Force repaint for live updates
                Repaint();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawYtdlpStatus()
        {
            bool ytdlpAvailable = StreamResolverFactory.IsYtdlpAvailable;
            bool isInstalled = YtdlpDownloader.IsInstalled();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (ytdlpAvailable)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("yt-dlp Status:", "Installed", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("Path:", StreamResolverFactory.YtdlpResolver.YtdlpPath, EditorStyles.miniLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    StreamResolverFactory.YtdlpResolver.RefreshPath();
                }
                if (isInstalled && GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    if (EditorUtility.DisplayDialog("Delete yt-dlp",
                        "Are you sure you want to delete the bundled yt-dlp?", "Delete", "Cancel"))
                    {
                        YtdlpDownloader.Delete();
                        StreamResolverFactory.YtdlpResolver.RefreshPath();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("yt-dlp Status:", "Not Found", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("yt-dlp will be downloaded automatically on first use, or you can download it now:",
                    EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button("Download yt-dlp Now"))
                {
                    DownloadYtdlpWithProgress();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DownloadYtdlpWithProgress()
        {
            EditorUtility.DisplayProgressBar("Downloading yt-dlp", "Starting download...", 0f);

            try
            {
                DownloadResult result = YtdlpDownloader.DownloadSync((progress) =>
                {
                    EditorUtility.DisplayProgressBar("Downloading yt-dlp",
                        "Downloading... " + (int)(progress * 100) + "%", progress);
                });

                if (result.Success)
                {
                    StreamResolverFactory.YtdlpResolver.RefreshPath();
                    EditorUtility.DisplayDialog("Download Complete",
                        "yt-dlp has been downloaded successfully to:\n" + result.InstallPath, "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Download Failed",
                        "Failed to download yt-dlp:\n" + result.Error, "OK");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
