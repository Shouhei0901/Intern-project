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

        // 3. XRI コントローラー (Right Hand Controller) の自動構築
        GameObject rightControllerObj = new GameObject("Right Hand Controller");
        rightControllerObj.transform.SetParent(cameraOffsetObj.transform);
        rightControllerObj.transform.localPosition = new Vector3(0.2f, 1.2f, 0.5f);

        // XRI Controller & Ray Interactor のアタッチ
        ActionBasedController controllerComponent = rightControllerObj.AddComponent<ActionBasedController>();
        XRRayInteractor rayInteractor = rightControllerObj.AddComponent<XRRayInteractor>();
        UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual lineVisual = rightControllerObj.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
        LineRenderer lineRenderer = rightControllerObj.GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            lineRenderer.startWidth = 0.01f;
            lineRenderer.endWidth = 0.005f;
            lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            lineRenderer.material.color = Color.cyan;
        }

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
        targetObj.transform.position = new Vector3(0, 1.0f, 0.5f);
        targetObj.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
        
        Rigidbody rb = targetObj.GetComponent<Rigidbody>();
        if (rb == null) rb = targetObj.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        // XR Simple Grab Interactable 相当のコンポーネントアタッチ
        PseudoHapticsCore.PseudoHapticsController phController = targetObj.AddComponent<PseudoHapticsCore.PseudoHapticsController>();

        // 7. 視線固定ターゲット (Fixation Cross Target)
        GameObject fixationCrossObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fixationCrossObj.name = "FixationCrossTarget";
        fixationCrossObj.transform.position = new Vector3(0, 1.6f, 1.5f);
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

        // VR World Space UI Canvas
        GameObject canvasObj = new GameObject("ScoreUICanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        canvasObj.transform.position = new Vector3(0, 1.6f, 1.0f);
        canvasObj.transform.localScale = new Vector3(0.002f, 0.002f, 0.002f);

        PseudoHapticsCore.ScoreUIController scoreUI = canvasObj.AddComponent<PseudoHapticsCore.ScoreUIController>();

        // コンポーネント参照の完全バインド
        SerializedObject managerSerialized = new SerializedObject(experimentManager);
        managerSerialized.FindProperty("pseudoHapticsController").objectReferenceValue = phController;
        managerSerialized.FindProperty("eyeTrackingValidator").objectReferenceValue = eyeValidator;
        managerSerialized.FindProperty("dataLogger").objectReferenceValue = dataLogger;
        managerSerialized.FindProperty("scoreUIController").objectReferenceValue = scoreUI;
        managerSerialized.FindProperty("targetObject").objectReferenceValue = targetObj.transform;
        managerSerialized.FindProperty("controllerTransform").objectReferenceValue = rightControllerObj.transform;
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
}
