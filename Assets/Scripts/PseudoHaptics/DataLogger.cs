using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PseudoHapticsCore
{
    /// <summary>
    /// 実験データのフレーム毎ロギングおよび試行サマリーの収集とCSV書き出しを管理するクラス。
    /// Application.persistentDataPath 下に ExperimentLogs フォルダを作成して保存する。
    /// 右手Aボタン等によるトグル記録にも対応。
    /// </summary>
    [DisallowMultipleComponent]
    public class DataLogger : MonoBehaviour
    {
        private readonly List<string> frameLogBuffer = new List<string>();
        private readonly List<string> summaryLogBuffer = new List<string>();

        private const string DetailedFrameHeader = 
            "Timestamp,SceneName,CD_Ratio,IsGrabbed," +
            "LeftCtrl_PosX,LeftCtrl_PosY,LeftCtrl_PosZ,LeftCtrl_RotX,LeftCtrl_RotY,LeftCtrl_RotZ," +
            "RightCtrl_PosX,RightCtrl_PosY,RightCtrl_PosZ,RightCtrl_RotX,RightCtrl_RotY,RightCtrl_RotZ," +
            "Head_PosX,Head_PosY,Head_PosZ,Head_RotX,Head_RotY,Head_RotZ," +
            "TargetObj_PosX,TargetObj_PosY,TargetObj_PosZ,TargetObj_RotX,TargetObj_RotY,TargetObj_RotZ," +
            "EyeGaze_OriginX,EyeGaze_OriginY,EyeGaze_OriginZ,EyeGaze_DirX,EyeGaze_DirY,EyeGaze_DirZ,Gaze_DeviationAngle,IsGazeValid";

        private const string LegacyFrameHeader = 
            "Timestamp,TrialID,Current_CD,Controller_PosY,Object_PosY,Controller_VelocityY," +
            "EyeGaze_OriginX,EyeGaze_OriginY,EyeGaze_OriginZ,EyeGaze_DirX,EyeGaze_DirY,EyeGaze_DirZ,Gaze_DeviationAngle,IsGazeValid";

        private const string SummaryHeader = "TrialID,Current_CD,TaskCompletionTime,SubjectiveScore,IsTrialValid";

        public bool IsRecording { get; private set; } = false;
        public int RecordedFrameCount => Mathf.Max(0, frameLogBuffer.Count - 1);

        private int currentTrialId = 0;
        private Vector3 lastControllerPos;
        private float lastTimestamp;
        private float recordingStartTime;

        private void Awake()
        {
            ResetBuffers();
        }

        public void ResetBuffers()
        {
            frameLogBuffer.Clear();
            summaryLogBuffer.Clear();
            frameLogBuffer.Add(DetailedFrameHeader);
            summaryLogBuffer.Add(SummaryHeader);
        }

        public void SetCurrentTrialId(int trialId)
        {
            currentTrialId = trialId;
        }

        /// <summary>
        /// Aボタン等でのトグル記録を開始
        /// </summary>
        public void StartRecording()
        {
            ResetBuffers();
            IsRecording = true;
            recordingStartTime = Time.time;
            lastTimestamp = Time.time;
            Debug.Log("[DataLogger] === Data Recording Started ===");
        }

        /// <summary>
        /// 視線トラッキング、左右コントローラー・頭部姿勢、オブジェクト変位、C/D比等の詳細データを記録
        /// </summary>
        public void LogDetailedFrameData(
            string sceneName,
            float cdRatio,
            bool isGrabbed,
            Vector3 leftCtrlPos,
            Quaternion leftCtrlRot,
            Vector3 rightCtrlPos,
            Quaternion rightCtrlRot,
            Vector3 headPos,
            Quaternion headRot,
            Vector3 objPos,
            Quaternion objRot,
            Vector3 gazeOrigin,
            Vector3 gazeDir,
            float deviationAngle,
            bool isGazeValid)
        {
            if (!IsRecording) return;

            float currentTime = Time.time - recordingStartTime;
            lastControllerPos = rightCtrlPos;
            lastTimestamp = Time.time;

            Vector3 leftEuler = leftCtrlRot.eulerAngles;
            Vector3 rightEuler = rightCtrlRot.eulerAngles;
            Vector3 headEuler = headRot.eulerAngles;
            Vector3 objEuler = objRot.eulerAngles;

            string line = string.Format(
                "{0:F4},{1},{2:F3},{3}," +
                "{4:F4},{5:F4},{6:F4},{7:F2},{8:F2},{9:F2}," +
                "{10:F4},{11:F4},{12:F4},{13:F2},{14:F2},{15:F2}," +
                "{16:F4},{17:F4},{18:F4},{19:F2},{20:F2},{21:F2}," +
                "{22:F4},{23:F4},{24:F4},{25:F2},{26:F2},{27:F2}," +
                "{28:F4},{29:F4},{30:F4},{31:F4},{32:F4},{33:F4},{34:F2},{35}",
                currentTime,
                sceneName,
                cdRatio,
                isGrabbed ? 1 : 0,
                leftCtrlPos.x, leftCtrlPos.y, leftCtrlPos.z,
                leftEuler.x, leftEuler.y, leftEuler.z,
                rightCtrlPos.x, rightCtrlPos.y, rightCtrlPos.z,
                rightEuler.x, rightEuler.y, rightEuler.z,
                headPos.x, headPos.y, headPos.z,
                headEuler.x, headEuler.y, headEuler.z,
                objPos.x, objPos.y, objPos.z,
                objEuler.x, objEuler.y, objEuler.z,
                gazeOrigin.x, gazeOrigin.y, gazeOrigin.z,
                gazeDir.x, gazeDir.y, gazeDir.z,
                deviationAngle,
                isGazeValid ? 1 : 0
            );

            frameLogBuffer.Add(line);
        }

        /// <summary>
        /// Aボタン等でのトグル記録を終了し、指定されたフォーマットのファイル名でCSV出力して保存する。
        /// ファイル名仕様: {SceneName}_CD_{CDRatio:F2}_{DateTime:yyyyMMdd_HHmmss}.csv
        /// persistentDataPath および プロジェクト直下 ExperimentLogs の両方に安全に二重保存されます。
        /// </summary>
        public string StopRecordingAndSave(string sceneName, float cdRatio)
        {
            if (!IsRecording)
            {
                Debug.LogWarning("[DataLogger] Recording is not active.");
                return null;
            }

            IsRecording = false;

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeSceneName = string.IsNullOrEmpty(sceneName) ? "Scene" : sceneName;
            string fileName = $"{safeSceneName}_CD_{cdRatio:F2}_{timestamp}.csv";

            // 1. persistentDataPath / ExperimentLogs
            string persistentDir = Path.Combine(Application.persistentDataPath, "ExperimentLogs");
            if (!Directory.Exists(persistentDir))
            {
                Directory.CreateDirectory(persistentDir);
            }
            string primaryFilePath = Path.Combine(persistentDir, fileName);

            // 2. プロジェクト直下の ExperimentLogs (エディタ実行時のアクセス性向上のため)
            string projectDir = Path.Combine(Directory.GetCurrentDirectory(), "ExperimentLogs");
            try
            {
                if (!Directory.Exists(projectDir))
                {
                    Directory.CreateDirectory(projectDir);
                }
            }
            catch { /* Android等で書き込み権限がない場合はスキップ */ }

            string secondaryFilePath = Path.Combine(projectDir, fileName);

            try
            {
                // Primary (persistentDataPath) に保存
                File.WriteAllLines(primaryFilePath, frameLogBuffer, Encoding.UTF8);

                // Secondary (Project Root) にも保存
                if (Directory.Exists(projectDir))
                {
                    File.WriteAllLines(secondaryFilePath, frameLogBuffer, Encoding.UTF8);
                }

                Debug.LogWarning($"[DataLogger] ★★★ CSV LOG SAVED SUCCESSFULLY ★★★\n" +
                                 $"Primary Path: {primaryFilePath}\n" +
                                 $"Project Path: {secondaryFilePath}\n" +
                                 $"Total Recorded Frames: {RecordedFrameCount}");

                return primaryFilePath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DataLogger] Failed to export CSV: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 既存の試行フレームロギング（後方互換用）
        /// </summary>
        public void LogFrameData(
            float cdRatio, 
            Vector3 ctrlPos, 
            Vector3 objPos, 
            Vector3 gazeOrigin, 
            Vector3 gazeDir, 
            float deviationAngle, 
            bool isGazeValid)
        {
            float currentTime = Time.time;
            float deltaTime = currentTime - lastTimestamp;
            float velocityY = deltaTime > 0 ? (ctrlPos.y - lastControllerPos.y) / deltaTime : 0f;

            lastControllerPos = ctrlPos;
            lastTimestamp = currentTime;

            string line = string.Format(
                "{0:F4},{1},{2:F3},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F2},{13}",
                currentTime,
                currentTrialId,
                cdRatio,
                ctrlPos.y,
                objPos.y,
                velocityY,
                gazeOrigin.x, gazeOrigin.y, gazeOrigin.z,
                gazeDir.x, gazeDir.y, gazeDir.z,
                deviationAngle,
                isGazeValid
            );

            frameLogBuffer.Add(line);
        }

        /// <summary>
        /// 試行完了時のサマリーデータを記録
        /// </summary>
        public void LogTrialSummary(float cdRatio, float completionTime, int score, bool isTrialValid)
        {
            string line = string.Format(
                "{0},{1:F3},{2:F2},{3},{4}",
                currentTrialId,
                cdRatio,
                completionTime,
                score,
                isTrialValid
            );

            summaryLogBuffer.Add(line);
        }

        /// <summary>
        /// 蓄積されたログデータをCSVファイルとしてエクスポート（手動/試行シーケンス用）
        /// </summary>
        public string ExportToCSV(string filePrefix = "Experiment")
        {
            string directoryPath = Path.Combine(Application.persistentDataPath, "ExperimentLogs");
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string frameFilePath = Path.Combine(directoryPath, $"{filePrefix}_Frames_{timestamp}.csv");
            string summaryFilePath = Path.Combine(directoryPath, $"{filePrefix}_Summary_{timestamp}.csv");

            try
            {
                File.WriteAllLines(frameFilePath, frameLogBuffer, Encoding.UTF8);
                File.WriteAllLines(summaryFilePath, summaryLogBuffer, Encoding.UTF8);
                Debug.Log($"[DataLogger] Logs successfully exported to:\n{frameFilePath}\n{summaryFilePath}");
                return frameFilePath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DataLogger] Failed to export CSV: {ex.Message}");
                return null;
            }
        }
    }
}

