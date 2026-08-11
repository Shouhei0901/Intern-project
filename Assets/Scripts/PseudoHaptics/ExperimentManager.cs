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

        [Tooltip("コントローラーTransform (右または左手)")]
        [SerializeField] private Transform controllerTransform;

        [Tooltip("ターゲット持ち上げ目標相対高度 (メートル)")]
        [SerializeField] private float defaultTargetHeight = 0.3f;

        [Tooltip("試行間のインターバル時間 (秒)")]
        [SerializeField] private float trialIntervalSeconds = 2.0f;

        [Header("Input System (XRI / New Input System)")]
        [SerializeField] private InputActionProperty grabAction;
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

        private void Start()
        {
            if (scoreUIController != null)
            {
                scoreUIController.OnScoreSubmitted += OnScoreSubmitted;
            }

            if (trialConditions == null || trialConditions.Count == 0)
            {
                InitializeExperiment(defaultConditions);
            }

            StartExperiment();
        }

        private void OnDestroy()
        {
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

        private void Update()
        {
            if (CurrentState != TrialState.TaskRunning) return;

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

            // 視線ベクトルの取得 (OpenXR Eye Gaze または Main Camera 前方)
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

            // 毎フレームデータロギング
            if (dataLogger != null && pseudoHapticsController != null)
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
