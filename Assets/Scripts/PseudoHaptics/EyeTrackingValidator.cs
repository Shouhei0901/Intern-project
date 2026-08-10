using UnityEngine;

namespace PseudoHapticsCore
{
    /// <summary>
    /// アイトラッキングデータを取得・検証し、注視ターゲットからの逸脱角に基づいて
    /// 試行データの妥当性 (Valid/Invalid) を監視・判定するクラス。
    /// OpenXR Eye Gaze または Camera 前方への自動フォールバックに対応。
    /// </summary>
    [DisallowMultipleComponent]
    public class EyeTrackingValidator : MonoBehaviour
    {
        [Header("Validation Settings")]
        [Tooltip("許容される最大視線逸脱角度 (度数法)")]
        [SerializeField] private float maxAllowedDeviationAngle = 5.0f;

        [Header("References")]
        [Tooltip("注視ターゲット (Fixation Cross) のTransform")]
        [SerializeField] private Transform fixationTarget;

        [Tooltip("視線原点のTransform (カメラまたはHMD等)")]
        [SerializeField] private Transform eyeOriginTransform;

        public float MaxAllowedDeviationAngle
        {
            get => maxAllowedDeviationAngle;
            set => maxAllowedDeviationAngle = Mathf.Max(0.1f, value);
        }

        public bool IsCurrentTrialValid { get; private set; } = true;

        public float LastCalculatedDeviationAngle { get; private set; } = 0.0f;

        private void Start()
        {
            if (eyeOriginTransform == null && Camera.main != null)
            {
                eyeOriginTransform = Camera.main.transform;
            }
        }

        /// <summary>
        /// 試行開始時に検証ステータスをリセット
        /// </summary>
        public void ResetValidation()
        {
            IsCurrentTrialValid = true;
            LastCalculatedDeviationAngle = 0.0f;
        }

        /// <summary>
        /// 視線ベクトルとターゲット座標から逸脱角度を測定・検証
        /// </summary>
        /// <param name="gazeOrigin">視線原点の世界座標</param>
        /// <param name="gazeDirection">視線方向の単位ベクトル</param>
        /// <param name="targetWorldPos">注視ターゲットの世界座標</param>
        public void CheckGazeDeviation(Vector3 gazeOrigin, Vector3 gazeDirection, Vector3 targetWorldPos)
        {
            if (gazeDirection == Vector3.zero)
            {
                // フォールバック: カメラ前方ベクトル
                if (eyeOriginTransform != null)
                {
                    gazeOrigin = eyeOriginTransform.position;
                    gazeDirection = eyeOriginTransform.forward;
                }
                else return;
            }

            // 原点からターゲットへの理想の視線方向ベクトル
            Vector3 targetDirection = (targetWorldPos - gazeOrigin).normalized;

            // 実際の視線方向と理想方向との角度差 (度数法)
            float angle = Vector3.Angle(gazeDirection.normalized, targetDirection);
            LastCalculatedDeviationAngle = angle;

            // 許容限界角度を超えた場合は不整合 (Invalid) と判定
            if (angle > maxAllowedDeviationAngle)
            {
                IsCurrentTrialValid = false;
            }
        }

        /// <summary>
        /// インスペクター参照がアタッチされている場合の簡易オーバーロード
        /// </summary>
        public void CheckGazeDeviation(Vector3 gazeDirection)
        {
            Vector3 origin = eyeOriginTransform != null ? eyeOriginTransform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            Vector3 targetPos = fixationTarget != null ? fixationTarget.position : Vector3.forward * 2.0f;

            CheckGazeDeviation(origin, gazeDirection, targetPos);
        }
    }
}
