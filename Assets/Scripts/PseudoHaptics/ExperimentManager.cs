using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PseudoHapticsCore
{
    /// <summary>
    /// 実験シーケンス制御・試行パラメータの動的注入・状態遷移・各モジュールの統合を統括するマネージャー。
    /// [Start Trial] -> [Show Fixation Point] -> [Grab & Lift Task] -> [Target Height Reached] -> [Input Score UI] -> [Next Trial]
    /// </summary>
    [DisallowMultipleComponent]
    public class ExperimentManager : MonoBehaviour
    {
        [System.Serializable]
        public struct TrialCondition
        {
            public float cdRatio;
            public float targetHeight;
            public string customTag;
        }

        public enum TrialState
        {
            Idle,
            Fixation,
            TaskRunning,
            TaskCompleted,
            ScoreInput,
            Interval,
            Finished
        }

        [Header("System Components")]
        [SerializeField] private PseudoHapticsController pseudoHapticsController;
        [SerializeField] private EyeTrackingValidator eyeTrackingValidator;
        [SerializeField] private DataLogger dataLogger;
        [SerializeField] private ScoreUIController scoreUIController;

        [Header("Experiment Configuration")]
        [Tooltip("実験ターゲットオブジェクト")]
        [SerializeField] private Transform targetObject;

        [Tooltip("オブジェクトの初期化リセット位置")]
        [SerializeField] private Vector3 objectStartPos = new Vector3(0, 1.0f, 0.5f);

        [Tooltip("右手コントローラーTransform")]
        [SerializeField] private Transform rightControllerTransform;

        [Tooltip("左手コントローラーTransform")]
        [SerializeField] private Transform leftControllerTransform;

        [Tooltip("互換用コントローラーTransform (右または左手)")]
        [SerializeField] private Transform controllerTransform;

        [Tooltip("ターゲット持ち上げ目標相対高度 (メートル)")]
        [SerializeField] private float defaultTargetHeight = 0.3f;

        [Tooltip("試行間のインターバル時間 (秒)")]
        [SerializeField] private float trialIntervalSeconds = 2.0f;

        [Header("Input System (XRI / New Input System)")]
        [SerializeField] private InputActionProperty grabAction;
        [Tooltip("右手コントローラーのAボタン (データ収集開始/停止トグル)")]
        [SerializeField] private InputActionProperty toggleRecordAction;
        [SerializeField] private InputActionProperty eyeGazePositionAction;
        [SerializeField] private InputActionProperty eyeGazeRotationAction;

        [Header("Default Test Conditions")]
        [Tooltip("Trueの場合、実験管理による自動C/D比変更を行わず、PseudoHapticsControllerのInspector設定値を優先します")]
        [SerializeField] private bool useManualCdRatio = true;

        [SerializeField] private List<TrialCondition> defaultConditions = new List<TrialCondition>
        {
            new TrialCondition { cdRatio = 1.0f, targetHeight = 0.3f, customTag = "Normal (CD=1.0)" },
            new TrialCondition { cdRatio = 0.5f, targetHeight = 0.3f, customTag = "Heavy (CD=0.5)" },
            new TrialCondition { cdRatio = 2.0f, targetHeight = 0.3f, customTag = "Light (CD=2.0)" }
        };

        public TrialState CurrentState { get; private set; } = TrialState.Idle;
        public int CurrentTrialIndex { get; private set; } = -1;
        public TrialCondition CurrentCondition => (CurrentTrialIndex >= 0 && CurrentTrialIndex < trialConditions.Count) 
            ? trialConditions[CurrentTrialIndex] 
            : default;

        private List<TrialCondition> trialConditions = new List<TrialCondition>();
        private float trialStartTime;
        private bool wasGrabPressed = false;
        private bool wasToggleRecordPressed = false;
        private float recordingDuration = 0f;
        private InputAction autonomousToggleAction;

        private void Awake()
        {
            // 右手Aボタン（PrimaryButton）およびエディタ用キー入力に対応する自律型InputActionを作成・登録
            autonomousToggleAction = new InputAction("AutonomousToggleRecord", InputActionType.Button);
            
            // 各種XRコントローラー / OpenXR / Vive / Quest / Pico の右手Aボタン（Primary Button）パスを網羅
            autonomousToggleAction.AddBinding("<XRController>{RightHand}/primaryButton");
            autonomousToggleAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
            autonomousToggleAction.AddBinding("<XRController>{RightHand}/primary");
            autonomousToggleAction.AddBinding("<XRController>{RightHand}/primaryAction");
            autonomousToggleAction.AddBinding("<XRController>{RightHand}/buttonSouth");
            autonomousToggleAction.AddBinding("<XRInputDevice>{RightHand}/primaryButton");
            autonomousToggleAction.AddBinding("<XRInputDevice>{RightHand}/{PrimaryButton}");
            autonomousToggleAction.AddBinding("<OculusTouchController>{RightHand}/primaryButton");
            autonomousToggleAction.AddBinding("<ViveFocus3Controller>{RightHand}/primaryButton");
            autonomousToggleAction.AddBinding("<ViveFocus3Profile>/rightHand/primary");
            autonomousToggleAction.AddBinding("<Gamepad>/buttonSouth");
            autonomousToggleAction.AddBinding("*/{PrimaryButton}");

            // エディタ用キーボード
            autonomousToggleAction.AddBinding("<Keyboard>/space");
            autonomousToggleAction.AddBinding("<Keyboard>/a");

            autonomousToggleAction.Enable();

            // コンポーネント参照の自動フォールバック探索
            if (dataLogger == null) dataLogger = UnityEngine.Object.FindAnyObjectByType<DataLogger>();
            if (scoreUIController == null) scoreUIController = UnityEngine.Object.FindAnyObjectByType<ScoreUIController>();
            if (pseudoHapticsController == null) pseudoHapticsController = UnityEngine.Object.FindAnyObjectByType<PseudoHapticsController>();
            if (eyeTrackingValidator == null) eyeTrackingValidator = UnityEngine.Object.FindAnyObjectByType<EyeTrackingValidator>();
        }

        private void Start()
        {
            if (scoreUIController != null)
            {
                scoreUIController.OnScoreSubmitted += OnScoreSubmitted;
                scoreUIController.SetLoggingStatus("IDLE", Color.gray);
            }

            if (trialConditions == null || trialConditions.Count == 0)
            {
                InitializeExperiment(defaultConditions);
            }

            StartExperiment();
        }

        private void OnDestroy()
        {
            if (autonomousToggleAction != null)
            {
                autonomousToggleAction.Disable();
                autonomousToggleAction.Dispose();
            }

            if (scoreUIController != null)
            {
                scoreUIController.OnScoreSubmitted -= OnScoreSubmitted;
            }
        }

        public void InitializeExperiment(List<TrialCondition> conditions)
        {
            trialConditions = new List<TrialCondition>(conditions);
            CurrentTrialIndex = -1;
            CurrentState = TrialState.Idle;
            Debug.Log($"[ExperimentManager] Experiment initialized with {trialConditions.Count} trial conditions.");
        }

        public void StartExperiment()
        {
            if (trialConditions.Count == 0)
            {
                Debug.LogWarning("[ExperimentManager] Cannot start experiment: No trial conditions configured.");
                return;
            }

            CurrentTrialIndex = -1;
            StartNextTrial();
        }

        public void StartNextTrial()
        {
            CurrentTrialIndex++;

            if (CurrentTrialIndex >= trialConditions.Count)
            {
                FinishExperiment();
                return;
            }

            TrialCondition cond = CurrentCondition;
            Debug.Log($"[ExperimentManager] Starting Trial #{CurrentTrialIndex + 1}/{trialConditions.Count} (C/D Ratio: {cond.cdRatio})");

            if (pseudoHapticsController != null)
            {
                if (!useManualCdRatio)
                {
                    pseudoHapticsController.CurrentCdRatio = cond.cdRatio;
                }
                pseudoHapticsController.ResetObjectPosition(objectStartPos);
            }

            if (eyeTrackingValidator != null)
            {
                eyeTrackingValidator.ResetValidation();
            }

            if (dataLogger != null)
            {
                dataLogger.SetCurrentTrialId(CurrentTrialIndex + 1);
            }

            CurrentState = TrialState.Fixation;
            if (scoreUIController != null)
            {
                scoreUIController.ShowFixationCross(true);
            }

            StartCoroutine(FixationPhaseRoutine());
        }

        private IEnumerator FixationPhaseRoutine()
        {
            yield return new WaitForSeconds(1.5f);

            CurrentState = TrialState.TaskRunning;
            trialStartTime = Time.time;
            Debug.Log("[ExperimentManager] Task Started: Grab & Lift object.");
        }

        private void OnEnable()
        {
            if (autonomousToggleAction != null && !autonomousToggleAction.enabled)
            {
                autonomousToggleAction.Enable();
            }
            if (toggleRecordAction.action != null && !toggleRecordAction.action.enabled)
            {
                toggleRecordAction.action.Enable();
            }
        }

        private void OnDisable()
        {
            if (autonomousToggleAction != null && autonomousToggleAction.enabled)
            {
                autonomousToggleAction.Disable();
            }
            if (toggleRecordAction.action != null && toggleRecordAction.action.enabled)
            {
                toggleRecordAction.action.Disable();
            }
        }

        private void Update()
        {
            // 1. 右手Aボタン（またはキーボード）によるデータ収集トグル入力の監視
            HandleToggleRecordInput();

            // 2. 視線ベクトルの取得 (OpenXR Eye Gaze または Main Camera 前方)
            Vector3 gazeOrigin = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Vector3 gazeDir = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;

            if (eyeGazePositionAction.action != null && eyeGazeRotationAction.action != null && eyeGazePositionAction.action.enabled)
            {
                gazeOrigin = eyeGazePositionAction.action.ReadValue<Vector3>();
                Quaternion gazeRot = eyeGazeRotationAction.action.ReadValue<Quaternion>();
                gazeDir = gazeRot * Vector3.forward;
            }

            // 視線妥当性の検証
            if (eyeTrackingValidator != null)
            {
                eyeTrackingValidator.CheckGazeDeviation(gazeOrigin, gazeDir, targetObject != null ? targetObject.position : Vector3.zero);
            }

            // 3. データ記録中（Aボタントグルによるロギング）の毎フレーム収集
            if (dataLogger != null && dataLogger.IsRecording)
            {
                recordingDuration += Time.deltaTime;

                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                float currentCd = pseudoHapticsController != null ? pseudoHapticsController.CurrentCdRatio : (CurrentCondition.cdRatio > 0 ? CurrentCondition.cdRatio : 1.0f);
                bool isGrabbed = pseudoHapticsController != null && pseudoHapticsController.IsGrabbed;

                // 左右コントローラーの座標・姿勢を取得
                GetControllerPose(false, leftControllerTransform, out Vector3 leftCtrlPos, out Quaternion leftCtrlRot);
                GetControllerPose(true, rightControllerTransform != null ? rightControllerTransform : controllerTransform, out Vector3 rightCtrlPos, out Quaternion rightCtrlRot);

                Vector3 headPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                Quaternion headRot = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;

                Vector3 objPos = targetObject != null ? targetObject.position : Vector3.zero;
                Quaternion objRot = targetObject != null ? targetObject.rotation : Quaternion.identity;

                float devAngle = eyeTrackingValidator != null ? eyeTrackingValidator.LastCalculatedDeviationAngle : 0f;
                bool isGazeValid = eyeTrackingValidator != null ? eyeTrackingValidator.IsCurrentTrialValid : true;

                dataLogger.LogDetailedFrameData(
                    sceneName,
                    currentCd,
                    isGrabbed,
                    leftCtrlPos,
                    leftCtrlRot,
                    rightCtrlPos,
                    rightCtrlRot,
                    headPos,
                    headRot,
                    objPos,
                    objRot,
                    gazeOrigin,
                    gazeDir,
                    devAngle,
                    isGazeValid
                );
            }

            // 4. 実験試行シーケンス実行中の処理
            if (CurrentState == TrialState.TaskRunning)
            {
                UpdateTaskRunningPhase(gazeOrigin, gazeDir);
            }
        }

        /// <summary>
        /// コントローラーの姿勢をTransformおよびInputDevicesから取得
        /// </summary>
        private void GetControllerPose(bool isRight, Transform explicitTransform, out Vector3 position, out Quaternion rotation)
        {
            if (explicitTransform != null && explicitTransform.position != Vector3.zero)
            {
                position = explicitTransform.position;
                rotation = explicitTransform.rotation;
                return;
            }

            var charac = (isRight ? UnityEngine.XR.InputDeviceCharacteristics.Right : UnityEngine.XR.InputDeviceCharacteristics.Left) | UnityEngine.XR.InputDeviceCharacteristics.Controller;
            var devices = new List<UnityEngine.XR.InputDevice>();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(charac, devices);

            if (devices.Count > 0)
            {
                var device = devices[0];
                if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 devPos) &&
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion devRot))
                {
                    if (Camera.main != null && Camera.main.transform.parent != null)
                    {
                        position = Camera.main.transform.parent.TransformPoint(devPos);
                        rotation = Camera.main.transform.parent.rotation * devRot;
                    }
                    else
                    {
                        position = devPos;
                        rotation = devRot;
                    }
                    return;
                }
            }

            if (explicitTransform != null)
            {
                position = explicitTransform.position;
                rotation = explicitTransform.rotation;
                return;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
        }

        /// <summary>
        /// 右手Aボタン（Primary Button）およびキーボードからのトグル入力を監視
        /// </summary>
        private void HandleToggleRecordInput()
        {
            bool isTogglePressed = false;

            // 1. 自律型 InputAction からの判定 (右手PrimaryButton, Space, A)
            if (autonomousToggleAction != null && autonomousToggleAction.enabled)
            {
                isTogglePressed = autonomousToggleAction.IsPressed();
            }

            // 2. インスペクター設定 InputAction からの判定
            if (!isTogglePressed && toggleRecordAction.action != null && toggleRecordAction.action.enabled)
            {
                isTogglePressed = toggleRecordAction.action.IsPressed();
            }

            // 3. XRNode.RightHand / InputDevices からの判定（右手Aボタン / primaryButton のみ）
            if (!isTogglePressed)
            {
                var rightDevices = new List<UnityEngine.XR.InputDevice>();
                UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                    UnityEngine.XR.InputDeviceCharacteristics.Right, 
                    rightDevices
                );

                // XRNode.RightHand デバイスも追加
                var nodeDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
                if (nodeDevice.isValid && !rightDevices.Contains(nodeDevice))
                {
                    rightDevices.Add(nodeDevice);
                }

                foreach (var device in rightDevices)
                {
                    if (!device.isValid) continue;

                    // CommonUsages.primaryButton
                    if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool btnPrimary) && btnPrimary)
                    {
                        isTogglePressed = true;
                        Debug.Log("[Input Detected] CommonUsages.primaryButton triggered.");
                        break;
                    }

                    // カスタムFeatureUsage名での走査 (OpenXR / Vive / Oculus)
                    if (device.TryGetFeatureValue(new UnityEngine.XR.InputFeatureUsage<bool>("PrimaryButton"), out bool btnPB) && btnPB)
                    {
                        isTogglePressed = true;
                        Debug.Log("[Input Detected] PrimaryButton triggered.");
                        break;
                    }
                    if (device.TryGetFeatureValue(new UnityEngine.XR.InputFeatureUsage<bool>("primaryButton"), out bool btnpb) && btnpb)
                    {
                        isTogglePressed = true;
                        Debug.Log("[Input Detected] primaryButton triggered.");
                        break;
                    }
                    if (device.TryGetFeatureValue(new UnityEngine.XR.InputFeatureUsage<bool>("A"), out bool btnA) && btnA)
                    {
                        isTogglePressed = true;
                        Debug.Log("[Input Detected] Button A triggered.");
                        break;
                    }
                    if (device.TryGetFeatureValue(new UnityEngine.XR.InputFeatureUsage<bool>("ButtonSouth"), out bool btnSouth) && btnSouth)
                    {
                        isTogglePressed = true;
                        Debug.Log("[Input Detected] ButtonSouth triggered.");
                        break;
                    }
                }
            }

            // 4. キーボードフォールバック（エディタテスト用: Spaceキー, Aキー）
            if (!isTogglePressed && Keyboard.current != null)
            {
                if (Keyboard.current.spaceKey.isPressed || Keyboard.current.aKey.isPressed)
                {
                    isTogglePressed = true;
                }
            }

            // ボタンが押された瞬間（立ち上がりエッジ）のみトグルを実行
            if (isTogglePressed && !wasToggleRecordPressed)
            {
                ToggleDataRecording();
            }

            wasToggleRecordPressed = isTogglePressed;
        }

        /// <summary>
        /// データロギングの開始 / 停止＆CSV保存のトグル実行
        /// </summary>
        public void ToggleDataRecording()
        {
            if (dataLogger == null)
            {
                dataLogger = UnityEngine.Object.FindAnyObjectByType<DataLogger>();
            }

            if (dataLogger == null)
            {
                Debug.LogError("[ExperimentManager] DataLogger component not found!");
                return;
            }

            if (!dataLogger.IsRecording)
            {
                // 記録開始
                recordingDuration = 0f;
                dataLogger.StartRecording();
                if (scoreUIController != null)
                {
                    scoreUIController.SetLoggingStatus("RECORDING", Color.red);
                }
                Debug.LogWarning("[ExperimentManager] === Data Recording STARTED (Right A Button) ===");
            }
            else
            {
                // 記録終了＆CSV保存
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                float currentCd = pseudoHapticsController != null ? pseudoHapticsController.CurrentCdRatio : (CurrentCondition.cdRatio > 0 ? CurrentCondition.cdRatio : 1.0f);
                string savedPath = dataLogger.StopRecordingAndSave(sceneName, currentCd);

                if (scoreUIController != null)
                {
                    scoreUIController.SetLoggingStatus("SAVED", Color.green);
                }
                Debug.LogWarning($"[ExperimentManager] === Data Recording STOPPED & SAVED ===\nPath: {savedPath}");
            }
        }

        private void UpdateTaskRunningPhase(Vector3 gazeOrigin, Vector3 gazeDir)
        {
            Vector3 ctrlPos = controllerTransform != null ? controllerTransform.position : Vector3.zero;

            // 掴み入力の取得 (New Input System のみを使用し、InvalidOperationException を防止)
            bool isGrabPressed = false;
            if (grabAction.action != null && grabAction.action.enabled)
            {
                isGrabPressed = grabAction.action.IsPressed();
            }
            else
            {
                if (Mouse.current != null && Mouse.current.leftButton.isPressed)
                {
                    isGrabPressed = true;
                }
                if (Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
                {
                    isGrabPressed = true;
                }
            }

            // キーボード手動操作（エディタデバッグ用）
            if (controllerTransform != null && Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
                {
                    controllerTransform.position += Vector3.up * Time.deltaTime * 0.5f;
                }
                else if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
                {
                    controllerTransform.position -= Vector3.up * Time.deltaTime * 0.5f;
                }
            }

            if (isGrabPressed && !wasGrabPressed)
            {
                if (pseudoHapticsController != null)
                {
                    pseudoHapticsController.OnGrabStart(ctrlPos);
                }
            }
            else if (isGrabPressed && wasGrabPressed)
            {
                if (pseudoHapticsController != null)
                {
                    pseudoHapticsController.OnGrabUpdate(ctrlPos);
                }
            }
            else if (!isGrabPressed && wasGrabPressed)
            {
                if (pseudoHapticsController != null)
                {
                    pseudoHapticsController.OnGrabRelease();
                }
            }

            wasGrabPressed = isGrabPressed;

            // 既存の試行データロギング
            if (dataLogger != null && pseudoHapticsController != null && !dataLogger.IsRecording)
            {
                dataLogger.LogFrameData(
                    CurrentCondition.cdRatio,
                    ctrlPos,
                    targetObject != null ? targetObject.position : Vector3.zero,
                    gazeOrigin,
                    gazeDir,
                    eyeTrackingValidator != null ? eyeTrackingValidator.LastCalculatedDeviationAngle : 0f,
                    eyeTrackingValidator != null ? eyeTrackingValidator.IsCurrentTrialValid : true
                );
            }

            // タスク完了判定
            float currentHeight = targetObject != null ? targetObject.position.y - objectStartPos.y : 0f;
            float requiredHeight = CurrentCondition.targetHeight > 0 ? CurrentCondition.targetHeight : defaultTargetHeight;

            if (currentHeight >= requiredHeight)
            {
                CompleteCurrentTrial();
            }
        }

        public void CompleteCurrentTrial()
        {
            if (CurrentState != TrialState.TaskRunning) return;

            CurrentState = TrialState.TaskCompleted;
            float completionTime = Time.time - trialStartTime;
            Debug.Log($"[ExperimentManager] Trial #{CurrentTrialIndex + 1} Target Height Reached in {completionTime:F2} seconds.");

            if (pseudoHapticsController != null)
            {
                pseudoHapticsController.OnGrabRelease();
            }

            if (scoreUIController != null)
            {
                scoreUIController.ShowFixationCross(false);
                scoreUIController.ShowScoreUI("今回の動作で感じた重さ（抵抗感）を評価してください");
            }

            CurrentState = TrialState.ScoreInput;
        }

        private void OnScoreSubmitted(int score)
        {
            if (CurrentState != TrialState.ScoreInput) return;

            float completionTime = Time.time - trialStartTime;
            bool isValid = eyeTrackingValidator != null ? eyeTrackingValidator.IsCurrentTrialValid : true;

            if (dataLogger != null)
            {
                dataLogger.LogTrialSummary(CurrentCondition.cdRatio, completionTime, score, isValid);
            }

            StartCoroutine(IntervalRoutine());
        }

        private IEnumerator IntervalRoutine()
        {
            CurrentState = TrialState.Interval;
            if (pseudoHapticsController != null)
            {
                pseudoHapticsController.ResetObjectPosition(objectStartPos);
            }

            yield return new WaitForSeconds(trialIntervalSeconds);

            StartNextTrial();
        }

        private void FinishExperiment()
        {
            CurrentState = TrialState.Finished;
            Debug.Log("[ExperimentManager] All experiment trials completed.");

            if (dataLogger != null)
            {
                dataLogger.ExportToCSV("PseudoHaptics_Experiment");
            }

            if (scoreUIController != null)
            {
                scoreUIController.ShowScoreUI("全実験試行が完了しました。ご協力ありがとうございました。");
            }
        }
    }
}
