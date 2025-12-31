using UnityEngine;
using UnityEditor;
using Prism;
using Prism.FFmpeg;

namespace Prism.Editor
{
    public static class PrismMenuItems
    {
        [MenuItem("GameObject/Prism/Video Player", false, 10)]
        public static void CreateVideoPlayer()
        {
            GameObject playerObj = new GameObject("Prism Player");
            playerObj.AddComponent<PrismPlayer>();

            Selection.activeGameObject = playerObj;
            Undo.RegisterCreatedObjectUndo(playerObj, "Create Prism Player");

            Debug.Log("[Prism] Video Player created. Set a source and call Play().");
        }

        [MenuItem("GameObject/Prism/Video Screen", false, 11)]
        public static void CreateVideoScreen()
        {
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Prism Screen";
            screen.transform.localScale = new Vector3(16f, 9f, 1f);

            // Remove collider
            Object.DestroyImmediate(screen.GetComponent<Collider>());

            // Add unlit material (URP compatible)
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            Material mat = new Material(unlitShader);
            screen.GetComponent<MeshRenderer>().material = mat;

            // Add renderer component
            screen.AddComponent<PrismRenderer>();

            Selection.activeGameObject = screen;
            Undo.RegisterCreatedObjectUndo(screen, "Create Prism Screen");

            Debug.Log("[Prism] Video Screen created. Assign a PrismPlayer to the Renderer component.");
        }

        [MenuItem("GameObject/Prism/Complete Setup", false, 12)]
        public static void CreateCompleteSetup()
        {
            // Root object
            GameObject root = new GameObject("Prism Video Setup");

            // Player
            GameObject playerObj = new GameObject("Player");
            playerObj.transform.SetParent(root.transform);
            PrismPlayer player = playerObj.AddComponent<PrismPlayer>();

            // Screen
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            screen.transform.SetParent(root.transform);
            screen.transform.localPosition = new Vector3(0, 4.5f, 10f);
            screen.transform.localScale = new Vector3(16f, 9f, 1f);
            Object.DestroyImmediate(screen.GetComponent<Collider>());

            // Material (URP compatible)
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            Material mat = new Material(unlitShader);
            screen.GetComponent<MeshRenderer>().material = mat;

            // Renderer with player reference
            PrismRenderer renderer = screen.AddComponent<PrismRenderer>();
            SerializedObject so = new SerializedObject(renderer);
            so.FindProperty("_player").objectReferenceValue = player;
            so.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = root;
            Undo.RegisterCreatedObjectUndo(root, "Create Prism Complete Setup");

            Debug.Log("[Prism] Complete setup created! Set a video URL on the Player and enter Play mode.");
        }

        [MenuItem("GameObject/Prism/Test Setup (with sample video)", false, 13)]
        public static void CreateTestSetup()
        {
            GameObject root = new GameObject("Prism Test");
            root.AddComponent<PrismTestSetup>();

            // Trigger the setup creation
            PrismTestSetup setup = root.GetComponent<PrismTestSetup>();
            setup.CreateTestSetup();

            Selection.activeGameObject = root;
            Undo.RegisterCreatedObjectUndo(root, "Create Prism Test Setup");

            Debug.Log("[Prism] Test setup created with Big Buck Bunny sample video. Enter Play mode to test!");
        }

        // ============================================================================
        // FFmpeg Player Menu Items
        // ============================================================================

        [MenuItem("GameObject/Prism/FFmpeg Player", false, 20)]
        public static void CreateFFmpegPlayer()
        {
            GameObject playerObj = new GameObject("Prism FFmpeg Player");
            playerObj.AddComponent<PrismFFmpegPlayer>();

            Selection.activeGameObject = playerObj;
            Undo.RegisterCreatedObjectUndo(playerObj, "Create Prism FFmpeg Player");

            Debug.Log("[Prism] FFmpeg Player created. Set a URL and call Play().");
        }

        [MenuItem("GameObject/Prism/FFmpeg Player with Screen", false, 21)]
        public static void CreateFFmpegPlayerWithScreen()
        {
            // Root object
            GameObject root = new GameObject("Prism FFmpeg Setup");

            // Player
            GameObject playerObj = new GameObject("Player");
            playerObj.transform.SetParent(root.transform);
            PrismFFmpegPlayer player = playerObj.AddComponent<PrismFFmpegPlayer>();

            // Screen
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            screen.transform.SetParent(root.transform);
            screen.transform.localPosition = new Vector3(0, 4.5f, 10f);
            screen.transform.localScale = new Vector3(16f, 9f, 1f);
            Object.DestroyImmediate(screen.GetComponent<Collider>());

            // Material (URP compatible)
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            Material mat = new Material(unlitShader);
            screen.GetComponent<MeshRenderer>().material = mat;

            // Link player to renderer
            SerializedObject so = new SerializedObject(player);
            so.FindProperty("_targetRenderer").objectReferenceValue = screen.GetComponent<MeshRenderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = root;
            Undo.RegisterCreatedObjectUndo(root, "Create Prism FFmpeg Setup");

            Debug.Log("[Prism] FFmpeg Player with Screen created. Set a URL and enter Play mode.");
        }
    }
}
