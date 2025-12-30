using UnityEngine;
using UnityEditor;
using Prism;

namespace Prism.Editor
{
    [CustomEditor(typeof(PrismPlayer))]
    public class PrismPlayerEditor : UnityEditor.Editor
    {
        private SerializedProperty _sourceType;
        private SerializedProperty _videoClip;
        private SerializedProperty _url;
        private SerializedProperty _playOnAwake;
        private SerializedProperty _loop;
        private SerializedProperty _volume;
        private SerializedProperty _playbackSpeed;
        private SerializedProperty _targetTexture;
        private SerializedProperty _resolution;

        private bool _showPlaybackControls = true;

        private void OnEnable()
        {
            _sourceType = serializedObject.FindProperty("_sourceType");
            _videoClip = serializedObject.FindProperty("_videoClip");
            _url = serializedObject.FindProperty("_url");
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
                    EditorGUILayout.PropertyField(_url);
                    break;
                case PrismSourceType.WebRTC:
                    EditorGUILayout.HelpBox("WebRTC source requires additional setup. See PrismWebRTCReceiver.", MessageType.Info);
                    break;
            }

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

            // Runtime controls (only in play mode)
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Runtime Controls", EditorStyles.boldLabel);

                EditorGUILayout.LabelField($"State: {player.State}");
                EditorGUILayout.LabelField($"Duration: {player.Duration:F1}s");
                EditorGUILayout.LabelField($"Time: {player.Time:F1}s ({player.NormalizedTime:P0})");

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
                float newTime = EditorGUILayout.Slider("Seek", (float)player.Time, 0f, (float)player.Duration);
                if (Mathf.Abs(newTime - (float)player.Time) > 0.5f)
                {
                    player.Seek(newTime);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
