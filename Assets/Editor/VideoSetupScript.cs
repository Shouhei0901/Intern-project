using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;

namespace VideoControl.Editor
{
    public static class VideoSetupScript
    {
        [MenuItem("PseudoHaptics/Add Video Player Screen to ApplicationSystemMain")]
        public static void AddVideoScreenToApplicationScene()
        {
            Debug.Log("[VideoSetupScript] Setting up Video Screen in ApplicationSystemMain...");

            string scenePath = "Assets/Scenes/ApplicationSystemMain.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // 1. 動画クリップの探索
            VideoClip targetClip = null;
            string[] clipGuids = AssetDatabase.FindAssets("t:VideoClip");
            if (clipGuids.Length > 0)
            {
                string clipPath = AssetDatabase.GUIDToAssetPath(clipGuids[0]);
                targetClip = AssetDatabase.LoadAssetAtPath<VideoClip>(clipPath);
                Debug.Log($"[VideoSetupScript] Found VideoClip: {targetClip.name} ({clipPath})");
            }

            // 2. シーン内ルートオブジェクトの検索/作成
            GameObject videoScreenRoot = GameObject.Find("VideoScreen_Display");
            if (videoScreenRoot == null)
            {
                videoScreenRoot = new GameObject("VideoScreen_Display");
            }

            // 見やすい位置・角度（X=0.65, Y=1.45, Z=1.1）
            videoScreenRoot.transform.position = new Vector3(0.65f, 1.45f, 1.1f);
            videoScreenRoot.transform.rotation = Quaternion.Euler(0, -30f, 0);
            videoScreenRoot.transform.localScale = Vector3.one;

            // 3. 額縁 (MonitorFrame)
            Transform frameTrans = videoScreenRoot.transform.Find("MonitorFrame");
            GameObject frameObj;
            if (frameTrans == null)
            {
                frameObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frameObj.name = "MonitorFrame";
                frameObj.transform.SetParent(videoScreenRoot.transform, false);
            }
            else
            {
                frameObj = frameTrans.gameObject;
            }

            frameObj.transform.localPosition = Vector3.zero;
            frameObj.transform.localRotation = Quaternion.identity;
            frameObj.transform.localScale = new Vector3(0.96f, 0.56f, 0.02f);

            var frameRenderer = frameObj.GetComponent<Renderer>();
            if (frameRenderer != null)
            {
                Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Material frameMat = new Material(litShader)
                {
                    name = "MonitorFrame_Mat",
                    color = new Color(0.12f, 0.12f, 0.14f)
                };
                frameRenderer.material = frameMat;
            }

            // 4. 画面面 (ScreenSurface)
            Transform screenTrans = videoScreenRoot.transform.Find("ScreenSurface");
            GameObject screenObj;
            if (screenTrans == null)
            {
                screenObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                screenObj.name = "ScreenSurface";
                screenObj.transform.SetParent(videoScreenRoot.transform, false);
            }
            else
            {
                screenObj = screenTrans.gameObject;
            }

            screenObj.transform.localPosition = new Vector3(0, 0, -0.011f); // 額縁の手前に配置
            screenObj.transform.localRotation = Quaternion.identity;
            screenObj.transform.localScale = new Vector3(0.92f, 0.52f, 1.0f);

            var col = screenObj.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            // マテリアルの設定（必ずBaseColorを白にする）
            var screenRenderer = screenObj.GetComponent<Renderer>();
            if (screenRenderer != null)
            {
                Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
                Material screenMat = new Material(unlitShader)
                {
                    name = "VideoScreen_Mat",
                    color = Color.white
                };
                if (screenMat.HasProperty("_BaseColor")) screenMat.SetColor("_BaseColor", Color.white);
                screenRenderer.material = screenMat;
            }

            // 5. VideoPlayer の設定
            VideoPlayer videoPlayer = screenObj.GetComponent<VideoPlayer>();
            if (videoPlayer == null) videoPlayer = screenObj.AddComponent<VideoPlayer>();

            videoPlayer.source = VideoSource.VideoClip;
            if (targetClip != null) videoPlayer.clip = targetClip;
            videoPlayer.playOnAwake = true;
            videoPlayer.isLooping = true;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.aspectRatio = VideoAspectRatio.FitInside;

            // 6. AudioSource の設定
            AudioSource audioSource = screenObj.GetComponent<AudioSource>();
            if (audioSource == null) audioSource = screenObj.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1.0f; // 3D音響
            audioSource.minDistance = 0.5f;
            audioSource.maxDistance = 5.0f;

            videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            videoPlayer.EnableAudioTrack(0, true);
            videoPlayer.SetTargetAudioSource(0, audioSource);

            // 7. VideoPlaybackController の設定
            VideoPlaybackController playbackController = screenObj.GetComponent<VideoPlaybackController>();
            if (playbackController == null) playbackController = screenObj.AddComponent<VideoPlaybackController>();

            SerializedObject serializedController = new SerializedObject(playbackController);
            serializedController.FindProperty("videoPlayer").objectReferenceValue = videoPlayer;
            serializedController.FindProperty("playOnStart").boolValue = true;
            serializedController.FindProperty("loop").boolValue = true;
            serializedController.FindProperty("pauseInsteadOfStop").boolValue = true;
            serializedController.FindProperty("autoBindRenderTexture").boolValue = true;
            serializedController.ApplyModifiedProperties();

            // 8. シーン保存
            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[VideoSetupScript] ✅ Setup successfully completed for {scenePath}!");
            Selection.activeGameObject = screenObj;
        }
    }
}
