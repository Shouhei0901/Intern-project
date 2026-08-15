using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using TMPro;

public class SceneSetupScript
{
    [MenuItem("PseudoHaptics/Generate Core System Main Scene with XRI Rig")]
    public static void GenerateCoreSystemScene()
    {
        Debug.Log("[SceneSetupScript] Generating CoreSystemMain scene with XRI Controllers...");

        // 0. Project Validation & OpenXR プロファイルの修復
        FixProjectValidation.FixAllValidationIssues();

        // 1. 新規シーンを作成
        var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 2. XR Origin / Main Camera のセットアップ
        GameObject xrOriginObj = new GameObject("XR Origin");
        
        GameObject cameraOffsetObj = new GameObject("Camera Offset");
        cameraOffsetObj.transform.SetParent(xrOriginObj.transform);

        GameObject mainCameraObj = new GameObject("Main Camera");
        mainCameraObj.transform.SetParent(cameraOffsetObj.transform);
        mainCameraObj.tag = "MainCamera";
        Camera camera = mainCameraObj.AddComponent<Camera>();
        camera.nearClipPlane = 0.01f;
        mainCameraObj.AddComponent<AudioListener>();
        mainCameraObj.transform.localPosition = new Vector3(0, 1.6f, 0);

        // 3. XRI コントローラー (Left & Right Hand Controller) の自動構築
        // Left Hand Controller
        GameObject leftControllerObj = new GameObject("Left Hand Controller");
        leftControllerObj.transform.SetParent(cameraOffsetObj.transform);
        leftControllerObj.transform.localPosition = new Vector3(-0.2f, 1.2f, 0.5f);

        ActionBasedController leftControllerComp = leftControllerObj.AddComponent<ActionBasedController>();
        XRRayInteractor leftRayInteractor = leftControllerObj.AddComponent<XRRayInteractor>();
        UnityEngine.InputSystem.XR.TrackedPoseDriver leftPoseDriver = leftControllerObj.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        leftPoseDriver.positionInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("LeftPosition", binding: "<XRController>{LeftHand}/devicePosition"));
        leftPoseDriver.rotationInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("LeftRotation", binding: "<XRController>{LeftHand}/deviceRotation"));

        // Right Hand Controller
        GameObject rightControllerObj = new GameObject("Right Hand Controller");
        rightControllerObj.transform.SetParent(cameraOffsetObj.transform);
        rightControllerObj.transform.localPosition = new Vector3(0.2f, 1.2f, 0.5f);

        ActionBasedController rightControllerComp = rightControllerObj.AddComponent<ActionBasedController>();
        XRRayInteractor rightRayInteractor = rightControllerObj.AddComponent<XRRayInteractor>();
        UnityEngine.InputSystem.XR.TrackedPoseDriver rightPoseDriver = rightControllerObj.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        rightPoseDriver.positionInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("RightPosition", binding: "<XRController>{RightHand}/devicePosition"));
        rightPoseDriver.rotationInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("RightRotation", binding: "<XRController>{RightHand}/deviceRotation"));

        UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual lineVisual = rightControllerObj.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
        LineRenderer lineRenderer = rightControllerObj.GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            lineRenderer.startWidth = 0.01f;
            lineRenderer.endWidth = 0.005f;
            lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            lineRenderer.material.color = Color.cyan;
        }

        // Main Camera TrackedPoseDriver
        UnityEngine.InputSystem.XR.TrackedPoseDriver headPoseDriver = mainCameraObj.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        headPoseDriver.positionInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("HeadPosition", binding: "<XRHMD>/centerEyePosition"));
        headPoseDriver.rotationInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("HeadRotation", binding: "<XRHMD>/centerEyeRotation"));

        // 4. ライティング
        GameObject lightObj = new GameObject("Directional Light");
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);

        // 5. 床プレーン
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(10, 1, 10);

        // 6. 実験対象オブジェクト (Pseudo-Haptics Target Cube)
        GameObject targetObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        targetObj.name = "PseudoHapticTargetCube";
        targetObj.transform.position = new Vector3(0, 1.2f, 0.5f);
        targetObj.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);

        // 鮮やかな赤色のマテリアルを作成して割り当て
        Renderer cubeRenderer = targetObj.GetComponent<Renderer>();
        if (cubeRenderer != null)
        {
            Material redMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            redMaterial.name = "TargetCubeRedMaterial";
            redMaterial.color = new Color(1.0f, 0.05f, 0.05f); // 鮮やかな赤色
            cubeRenderer.material = redMaterial;
        }
        
        // 物理演算（重力）の有効化
        Rigidbody rb = targetObj.GetComponent<Rigidbody>();
        if (rb == null) rb = targetObj.AddComponent<Rigidbody>();
        rb.isKinematic = false; // スタート時に重力で落下させる
        rb.useGravity = true;

        // XR Grab Interactable（コントローラーでの掴み機能）の自動追加
        targetObj.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        // 擬似触覚コントローラーのアタッチ
        PseudoHapticsCore.PseudoHapticsController phController = targetObj.AddComponent<PseudoHapticsCore.PseudoHapticsController>();

        // 7. 視線固定ターゲット (Fixation Cross Target) - Main Camera の直下に配置
        GameObject fixationCrossObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fixationCrossObj.name = "FixationCrossTarget";
        fixationCrossObj.transform.SetParent(mainCameraObj.transform); // Main Camera の直下に配置
        fixationCrossObj.transform.localPosition = new Vector3(0, 0, 1.5f); // カメラ前方 1.5m
        fixationCrossObj.transform.localRotation = Quaternion.identity;
        fixationCrossObj.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
        Object.DestroyImmediate(fixationCrossObj.GetComponent<Collider>());

        Renderer crossRenderer = fixationCrossObj.GetComponent<Renderer>();
        if (crossRenderer != null)
        {
            crossRenderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            crossRenderer.sharedMaterial.color = Color.red;
        }

        // 8. 実験マネージャー & ロガー & UI
        GameObject managerObj = new GameObject("ExperimentManager");
        PseudoHapticsCore.ExperimentManager experimentManager = managerObj.AddComponent<PseudoHapticsCore.ExperimentManager>();
        PseudoHapticsCore.EyeTrackingValidator eyeValidator = managerObj.AddComponent<PseudoHapticsCore.EyeTrackingValidator>();
        PseudoHapticsCore.DataLogger dataLogger = managerObj.AddComponent<PseudoHapticsCore.DataLogger>();

        // VR World Space UI Canvas (Logging 専用シンプルUI)
        GameObject canvasObj = new GameObject("ScoreUICanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        canvasObj.transform.position = new Vector3(0, 1.8f, 1.2f); // 見やすい位置（高さ1.8m, 前方1.2m）
        canvasObj.transform.localScale = new Vector3(0.002f, 0.002f, 0.002f);

        // RectTransform のサイズ設定
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        if (canvasRect != null) canvasRect.sizeDelta = new Vector2(800, 240);

        // 背景半透明ダークパネル
        GameObject bgPanelObj = new GameObject("BackgroundPanel");
        bgPanelObj.transform.SetParent(canvasObj.transform, false);
        Image bgImage = bgPanelObj.AddComponent<Image>();
        bgImage.color = new Color(0.05f, 0.05f, 0.08f, 0.85f); // 濃いダーク半透明
        RectTransform bgRect = bgPanelObj.GetComponent<RectTransform>();
        if (bgRect != null)
        {
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
        }

        // データ収集ステータス表示テキスト (単一・大型表示: IDLE / RECORDING / SAVED)
        GameObject logStatusTextObj = new GameObject("LoggingStatusDisplayText");
        logStatusTextObj.transform.SetParent(canvasObj.transform, false);
        TextMeshProUGUI logStatusText = logStatusTextObj.AddComponent<TextMeshProUGUI>();
        logStatusText.fontSize = 44;
        logStatusText.fontStyle = FontStyles.Bold;
        logStatusText.alignment = TextAlignmentOptions.Center;
        logStatusText.color = Color.gray;
        logStatusText.text = "IDLE";
        RectTransform logStatusRect = logStatusTextObj.GetComponent<RectTransform>();
        if (logStatusRect != null)
        {
            logStatusRect.anchorMin = Vector2.zero;
            logStatusRect.anchorMax = Vector2.one;
            logStatusRect.sizeDelta = new Vector2(-40, -40);
            logStatusRect.anchoredPosition = Vector2.zero;
        }

        PseudoHapticsCore.ScoreUIController scoreUI = canvasObj.AddComponent<PseudoHapticsCore.ScoreUIController>();
        SerializedObject scoreUISerialized = new SerializedObject(scoreUI);
        scoreUISerialized.FindProperty("loggingStatusText").objectReferenceValue = logStatusText;
        scoreUISerialized.FindProperty("pseudoHapticsController").objectReferenceValue = phController;
        scoreUISerialized.ApplyModifiedProperties();

        // コンポーネント参照の完全バインド
        SerializedObject managerSerialized = new SerializedObject(experimentManager);
        managerSerialized.FindProperty("pseudoHapticsController").objectReferenceValue = phController;
        managerSerialized.FindProperty("eyeTrackingValidator").objectReferenceValue = eyeValidator;
        managerSerialized.FindProperty("dataLogger").objectReferenceValue = dataLogger;
        managerSerialized.FindProperty("scoreUIController").objectReferenceValue = scoreUI;
        managerSerialized.FindProperty("targetObject").objectReferenceValue = targetObj.transform;
        managerSerialized.FindProperty("rightControllerTransform").objectReferenceValue = rightControllerObj.transform;
        managerSerialized.FindProperty("leftControllerTransform").objectReferenceValue = leftControllerObj.transform;
        managerSerialized.FindProperty("controllerTransform").objectReferenceValue = rightControllerObj.transform;
        
        SerializedProperty manualProp = managerSerialized.FindProperty("useManualCdRatio");
        if (manualProp != null) manualProp.boolValue = true;

        managerSerialized.ApplyModifiedProperties();

        SerializedObject validatorSerialized = new SerializedObject(eyeValidator);
        validatorSerialized.FindProperty("fixationTarget").objectReferenceValue = fixationCrossObj.transform;
        validatorSerialized.FindProperty("eyeOriginTransform").objectReferenceValue = mainCameraObj.transform;
        validatorSerialized.ApplyModifiedProperties();

        // シーン保存
        string scenePath = "Assets/Scenes/CoreSystemMain.unity";
        EditorSceneManager.SaveScene(newScene, scenePath);
        Debug.Log($"[SceneSetupScript] CoreSystemMain scene updated with XRI Rig at: {scenePath}");
    }

    [MenuItem("PseudoHaptics/Open Experiment Logs Folder (Project Root)")]
    public static void OpenProjectLogsFolder()
    {
        string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "ExperimentLogs");
        if (!System.IO.Directory.Exists(path)) System.IO.Directory.CreateDirectory(path);
        EditorUtility.RevealInFinder(path);
    }

    [MenuItem("PseudoHaptics/Open Experiment Logs Folder (Persistent Data)")]
    public static void OpenPersistentLogsFolder()
    {
        string path = System.IO.Path.Combine(Application.persistentDataPath, "ExperimentLogs");
        if (!System.IO.Directory.Exists(path)) System.IO.Directory.CreateDirectory(path);
        EditorUtility.RevealInFinder(path);
    }
}
