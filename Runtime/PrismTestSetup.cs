using UnityEngine;

namespace Prism
{
    public class PrismTestSetup : MonoBehaviour
    {
        [Header("Test Video")]
        [SerializeField] private string _testVideoUrl = "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4";

        [Header("Screen Settings")]
        [SerializeField] private Vector2 _screenSize = new Vector2(16f, 9f);
        [SerializeField] private float _screenDistance = 10f;

        [Header("Created References")]
        [SerializeField] private PrismPlayer _player;
        [SerializeField] private PrismRenderer _renderer;
        [SerializeField] private GameObject _screen;

        [ContextMenu("Create Test Setup")]
        public void CreateTestSetup()
        {
            // Create player
            GameObject playerObj = new GameObject("Prism Player");
            playerObj.transform.SetParent(transform);
            _player = playerObj.AddComponent<PrismPlayer>();

            // Set source to URL with test video
            _player.SetSource(_testVideoUrl);

            // Create screen quad
            _screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _screen.name = "Video Screen";
            _screen.transform.SetParent(transform);
            _screen.transform.localPosition = new Vector3(0, _screenSize.y / 2f, _screenDistance);
            _screen.transform.localScale = new Vector3(_screenSize.x, _screenSize.y, 1f);

            // Remove collider (not needed for display)
            DestroyImmediate(_screen.GetComponent<Collider>());

            // Add renderer component
            _renderer = _screen.AddComponent<PrismRenderer>();

            // Create unlit material for the screen (URP compatible)
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
            {
                // Fallback for built-in render pipeline
                unlitShader = Shader.Find("Unlit/Texture");
            }
            Material screenMat = new Material(unlitShader);
            _screen.GetComponent<MeshRenderer>().material = screenMat;

            // Link renderer to player using SerializedObject for proper persistence
#if UNITY_EDITOR
            var so = new UnityEditor.SerializedObject(_renderer);
            so.FindProperty("_player").objectReferenceValue = _player;
            so.ApplyModifiedPropertiesWithoutUndo();
#endif

            Debug.Log("[Prism] Test setup created! Enter Play mode to test.");
        }

        [ContextMenu("Play Test Video")]
        public void PlayTestVideo()
        {
            if (_player == null)
            {
                Debug.LogError("[Prism] No player found. Run 'Create Test Setup' first.");
                return;
            }

            _player.SetSource(_testVideoUrl);
            _player.Play();
        }

        private void Start()
        {
            if (_player != null && !string.IsNullOrEmpty(_testVideoUrl))
            {
                PlayTestVideo();
            }
        }
    }
}
