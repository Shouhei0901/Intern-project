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
    /// </summary>
    [DisallowMultipleComponent]
    public class DataLogger : MonoBehaviour
    {
        private readonly List<string> frameLogBuffer = new List<string>();
        private readonly List<string> summaryLogBuffer = new List<string>();

        private const string FrameHeader = "Timestamp,TrialID,Current_CD,Controller_PosY,Object_PosY,Controller_VelocityY,EyeGaze_OriginX,EyeGaze_OriginY,EyeGaze_OriginZ,EyeGaze_DirX,EyeGaze_DirY,EyeGaze_DirZ,Gaze_DeviationAngle,IsGazeValid";
        private const string SummaryHeader = "TrialID,Current_CD,TaskCompletionTime,SubjectiveScore,IsTrialValid";

        private int currentTrialId = 0;
        private Vector3 lastControllerPos;
        private float lastTimestamp;

        private void Awake()
        {
            ResetBuffers();
        }

        public void ResetBuffers()
        {
            frameLogBuffer.Clear();
            summaryLogBuffer.Clear();
            frameLogBuffer.Add(FrameHeader);
            summaryLogBuffer.Add(SummaryHeader);
        }

        public void SetCurrentTrialId(int trialId)
        {
            currentTrialId = trialId;
        }

        /// <summary>
        /// 毎フレームの実験・アイトラッキングデータをバッファに記録
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
        /// 蓄積されたログデータをCSVファイルとしてエクスポート
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
