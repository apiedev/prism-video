using UnityEngine;
using UnityEditor;
using Prism.FFmpeg;
using Prism.Streaming;

namespace Prism.Editor
{
    [CustomEditor(typeof(PrismFFmpegPlayer))]
    public class PrismFFmpegPlayerEditor : UnityEditor.Editor
    {
        private SerializedProperty _url;
        private SerializedProperty _streamQuality;
        private SerializedProperty _autoResolveUrls;
        private SerializedProperty _playOnAwake;
        private SerializedProperty _loop;
        private SerializedProperty _volume;
        private SerializedProperty _playbackSpeed;
        private SerializedProperty _targetTexture;
        private SerializedProperty _targetRenderer;
        private SerializedProperty _texturePropertyName;
        private SerializedProperty _useHardwareAcceleration;

        private bool _showEvents = false;
        private bool _showStreamInfo = true;

        private void OnEnable()
        {
            _url = serializedObject.FindProperty("_url");
            _streamQuality = serializedObject.FindProperty("_streamQuality");
            _autoResolveUrls = serializedObject.FindProperty("_autoResolveUrls");
            _playOnAwake = serializedObject.FindProperty("_playOnAwake");
            _loop = serializedObject.FindProperty("_loop");
            _volume = serializedObject.FindProperty("_volume");
            _playbackSpeed = serializedObject.FindProperty("_playbackSpeed");
            _targetTexture = serializedObject.FindProperty("_targetTexture");
            _targetRenderer = serializedObject.FindProperty("_targetRenderer");
            _texturePropertyName = serializedObject.FindProperty("_texturePropertyName");
            _useHardwareAcceleration = serializedObject.FindProperty("_useHardwareAcceleration");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            PrismFFmpegPlayer player = (PrismFFmpegPlayer)target;

            // Header
            EditorGUILayout.LabelField("Prism FFmpeg Player", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Native FFmpeg-based video player. Supports HLS, RTMP, and all FFmpeg formats.", MessageType.Info);

            EditorGUILayout.Space();

            // Source section
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_url, new GUIContent("URL"));
            EditorGUILayout.PropertyField(_streamQuality);
            EditorGUILayout.PropertyField(_autoResolveUrls, new GUIContent("Auto-Resolve URLs",
                "Automatically resolve YouTube/Twitch URLs using yt-dlp"));

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
            EditorGUILayout.PropertyField(_targetRenderer);
            if (_targetRenderer.objectReferenceValue != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_texturePropertyName);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // Settings section
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_useHardwareAcceleration);

            EditorGUILayout.Space();

            // Events
            _showEvents = EditorGUILayout.Foldout(_showEvents, "Events");
            if (_showEvents)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnPrepared"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnStarted"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnPaused"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnStopped"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnFinished"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("OnError"));
            }

            EditorGUILayout.Space();

            // Native plugin status
            DrawPluginStatus();

            // Runtime info (play mode only)
            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                DrawRuntimeInfo(player);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPluginStatus()
        {
            EditorGUILayout.LabelField("Native Plugin", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            bool pluginFound = IsPluginAvailable();

            if (pluginFound)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Status:", "Available", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();

                if (Application.isPlaying)
                {
                    try
                    {
                        string ffmpegVersion = PrismFFmpegBridge.GetFFmpegVersion();
                        string pluginVersion = PrismFFmpegBridge.GetVersion();
                        EditorGUILayout.LabelField("FFmpeg:", ffmpegVersion);
                        EditorGUILayout.LabelField("Plugin:", pluginVersion);
                    }
                    catch
                    {
                        EditorGUILayout.LabelField("Version info available at runtime");
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Version info available at runtime");
                }
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Status:", "Not Found", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox(
                    "Native plugin not found. Please build the plugin:\n\n" +
                    "1. Navigate to Native/ directory\n" +
                    "2. Windows: Run build_windows.bat\n" +
                    "   Linux/Mac: Run ./build_unix.sh\n\n" +
                    "See Native/BUILD.md for detailed instructions.",
                    MessageType.Warning);

                if (GUILayout.Button("Open Build Instructions"))
                {
                    string path = System.IO.Path.Combine(Application.dataPath, "..", "Packages", "dev.apie.prism", "Native", "BUILD.md");
                    if (!System.IO.File.Exists(path))
                    {
                        // Try alternate path for local development
                        path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "prism-video", "Native", "BUILD.md"));
                    }
                    if (System.IO.File.Exists(path))
                    {
                        Application.OpenURL("file://" + path);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("File Not Found", "BUILD.md not found. Check the Native directory of the Prism package.", "OK");
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeInfo(PrismFFmpegPlayer player)
        {
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);

            // State
            EditorGUILayout.LabelField("State", player.State.ToString());

            // Video info
            if (player.VideoWidth > 0 && player.VideoHeight > 0)
            {
                EditorGUILayout.LabelField("Video", player.VideoWidth + "x" + player.VideoHeight);
            }

            // Stream info
            if (player.CurrentStreamInfo != null && player.CurrentStreamInfo.Success)
            {
                _showStreamInfo = EditorGUILayout.Foldout(_showStreamInfo, "Stream Info");
                if (_showStreamInfo)
                {
                    EditorGUI.indentLevel++;
                    if (!string.IsNullOrEmpty(player.CurrentStreamInfo.Title))
                        EditorGUILayout.LabelField("Title", player.CurrentStreamInfo.Title);
                    if (!string.IsNullOrEmpty(player.CurrentStreamInfo.Format))
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

            // Time
            if (!player.IsLiveStream && player.Duration > 0)
            {
                string timeStr = string.Format("{0:F1}s / {1:F1}s ({2:P0})",
                    player.Time, player.Duration, player.NormalizedTime);
                EditorGUILayout.LabelField("Time", timeStr);
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

            // Seek slider
            if (!player.IsLiveStream && player.Duration > 0)
            {
                float newTime = EditorGUILayout.Slider("Seek", (float)player.Time, 0f, (float)player.Duration);
                if (Mathf.Abs(newTime - (float)player.Time) > 0.5f)
                {
                    player.Seek(newTime);
                }
            }

            // Force repaint
            Repaint();
        }

        private bool IsPluginAvailable()
        {
            string pluginPath = null;

            #if UNITY_EDITOR_WIN
            pluginPath = "Plugins/Windows/x86_64/prism_ffmpeg.dll";
            #elif UNITY_EDITOR_OSX
            pluginPath = "Plugins/macOS/libprism_ffmpeg.dylib";
            #elif UNITY_EDITOR_LINUX
            pluginPath = "Plugins/Linux/x86_64/libprism_ffmpeg.so";
            #endif

            if (string.IsNullOrEmpty(pluginPath))
                return false;

            // Check in Assets
            string assetsPath = System.IO.Path.Combine(Application.dataPath, pluginPath);
            if (System.IO.File.Exists(assetsPath))
                return true;

            // Check in Packages
            string packagesPath = System.IO.Path.Combine(Application.dataPath, "..", "Packages", "dev.apie.prism", pluginPath);
            if (System.IO.File.Exists(packagesPath))
                return true;

            return false;
        }
    }
}
