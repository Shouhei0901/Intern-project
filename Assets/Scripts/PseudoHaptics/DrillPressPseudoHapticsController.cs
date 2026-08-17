using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace PseudoHapticsCore
{
    /// <summary>
    /// ボール盤（DrillPress）専用のPseudo-Hapticsコントローラー。
    /// ハンドル（DrillPress_V1_L2.001）を把持してコントローラーを上下に動かすと、
    /// C/D比に基づいて主軸（DrillPress_V1_L2.002）が垂直方向に移動し、
    /// ハンドルが昇降量に連動して回転します。
    /// </summary>
    [DisallowMultipleComponent]
    public class DrillPressPseudoHapticsController : PseudoHapticsController
    {
        [Header("DrillPress Components")]
        [Tooltip("把持および回転するハンドルTransform (DrillPress_V1_L2.001)")]
        [SerializeField] private Transform handleTransform;

        [Tooltip("垂直方向に昇降する主軸Transform (DrillPress_V1_L2.002)")]
        [SerializeField] private Transform spindleTransform;

        [Header("Stroke & Movement Settings")]
        [Tooltip("主軸の最大下降ストローク距離 (メートル, 正の値)")]
        [SerializeField] private float maxStrokeDistance = 0.15f;

        [Tooltip("手の移動量に対する主軸移動の倍率")]
        [SerializeField] private float movementScaleMultiplier = 1.0f;

        [Tooltip("Trueの場合、ワールド垂直軸(Vector3.up)に沿って昇降します")]
        [SerializeField] private bool useWorldVerticalMovement = true;

        [Tooltip("主軸のローカル移動軸 (useWorldVerticalMovementがFalseの場合に使用)")]
        [SerializeField] private Vector3 localSpindleMoveAxis = new Vector3(0, 1, 0);

        [Header("Handle Rotation Settings")]
        [Tooltip("主軸が最大ストローク下降した際のハンドルの最大回転角度 (度)")]
        [SerializeField] private float maxRotationAngle = 120.0f;

        [Tooltip("ハンドルの回転ローカル軸")]
        [SerializeField] private Vector3 handleRotationAxis = new Vector3(1, 0, 0);

        [Tooltip("ハンドルの回転方向を反転するか")]
        [SerializeField] private bool invertRotation = false;

        [Header("Spring Back (Release Behavior)")]
        [Tooltip("手を離した際に自動的に初期位置（最上部）へスムーズに戻るか")]
        [SerializeField] private bool springBackOnRelease = true;

        [Tooltip("初期位置へ戻る復帰速度")]
        [SerializeField] private float springBackSpeed = 6.0f;

        [Header("Debug & Monitor")]
        [Tooltip("現在のストローク進行度 (0.0: 初期最上位置, 1.0: 最下点)")]
        [Range(0f, 1f)]
        [SerializeField] private float currentStrokeProgress = 0f;

        [Tooltip("現在の主軸垂直変位 (メートル)")]
        [SerializeField] private float currentSpindleDisplacement = 0f;

        private Vector3 initialSpindleWorldPos;
        private Vector3 initialSpindleLocalPos;
        private Quaternion initialHandleLocalRot;
        private Quaternion initialHandleWorldRot;
        private Vector3 initialHandleLocalPos;

        private float targetStrokeDisplacement = 0f;
        private bool isInitialized = false;

        public Transform HandleTransform => handleTransform;
        public Transform SpindleTransform => spindleTransform;
        public float CurrentStrokeProgress => currentStrokeProgress;
        public float CurrentSpindleDisplacement => currentSpindleDisplacement;

        protected override void Awake()
        {
            // 基底のRigidbody初期化を抑制またはKinematic化
            rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;

            InitializeComponents();
        }

        private void Start()
        {
            CaptureInitialTransforms();
        }

        private void InitializeComponents()
        {
            if (handleTransform == null)
            {
                handleTransform = transform;
            }

            if (spindleTransform == null && transform.parent != null)
            {
                // 親や兄弟から 002 (主軸) を自動検索
                Transform found002 = transform.parent.Find("13604_Drill_Press_v1_L2.002");
                if (found002 != null)
                {
                    spindleTransform = found002;
                }
                else
                {
                    // 部分一致検索
                    foreach (Transform child in transform.parent)
                    {
                        if (child.name.Contains(".002") || child.name.ToLower().Contains("spindle"))
                        {
                            spindleTransform = child;
                            break;
                        }
                    }
                }
            }

            CaptureInitialTransforms();
        }

        /// <summary>
        /// 初期位置と回転の記録
        /// </summary>
        public void CaptureInitialTransforms()
        {
            if (handleTransform != null)
            {
                initialHandleLocalRot = handleTransform.localRotation;
                initialHandleWorldRot = handleTransform.rotation;
                initialHandleLocalPos = handleTransform.localPosition;
            }

            if (spindleTransform != null)
            {
                initialSpindleWorldPos = spindleTransform.position;
                initialSpindleLocalPos = spindleTransform.localPosition;
            }

            isInitialized = true;
        }

        protected override void LateUpdate()
        {
            if (!isInitialized)
            {
                CaptureInitialTransforms();
            }

            // 把持中の更新
            if (isGrabbed)
            {
                Vector3 currentCtrlPos = transform.position;
                if (activeInteractor != null)
                {
                    Transform attachTransform = activeInteractor.GetAttachTransform(grabInteractable);
                    currentCtrlPos = attachTransform != null ? attachTransform.position : activeInteractor.transform.position;
                }
                OnGrabUpdate(currentCtrlPos);
            }
            else if (springBackOnRelease && currentStrokeProgress > 0.0001f)
            {
                // 手を離した際のスプリングバック（初期位置・最上部へスムーズに復帰）
                targetStrokeDisplacement = Mathf.MoveTowards(targetStrokeDisplacement, 0f, springBackSpeed * maxStrokeDistance * Time.deltaTime);
                ApplyDisplacement(targetStrokeDisplacement);
            }

            // エディタ実行時でのインスペクターデバッグ用スライダーの変更検知
            #if UNITY_EDITOR
            if (!isGrabbed && !Application.isPlaying)
            {
                ApplyProgress(currentStrokeProgress);
            }
            #endif
        }

        /// <summary>
        /// 把持開始処理
        /// </summary>
        public override void OnGrabStart(Vector3 controllerPos)
        {
            isGrabbed = true;
            initialControllerPos = controllerPos;

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// 把持中のC/D比に基づいた主軸移動およびハンドル連動回転
        /// </summary>
        public override void OnGrabUpdate(Vector3 controllerPos)
        {
            if (!isGrabbed) return;

            // コントローラーの垂直上下移動量（メートル）
            float controllerDeltaY = controllerPos.y - initialControllerPos.y;

            // C/D比を適用した主軸の変位量（コントローラーを下げると deltaY < 0）
            float scaledDeltaY = controllerDeltaY * CurrentCdRatio * movementScaleMultiplier;

            // 変位量は 0（初期位置）〜 -maxStrokeDistance（最下点）の範囲にクランプ
            // 下方向に動かすと負の値
            targetStrokeDisplacement = Mathf.Clamp(scaledDeltaY, -maxStrokeDistance, 0f);

            ApplyDisplacement(targetStrokeDisplacement);
        }

        /// <summary>
        /// 把持解除処理
        /// </summary>
        public override void OnGrabRelease()
        {
            isGrabbed = false;
        }

        /// <summary>
        /// 主軸の垂直位置およびハンドルの回転を適用
        /// </summary>
        /// <param name="displacement">下方向への変位（0 〜 -maxStrokeDistance）</param>
        private void ApplyDisplacement(float displacement)
        {
            currentSpindleDisplacement = displacement;
            currentStrokeProgress = maxStrokeDistance > 0 ? Mathf.Clamp01(-displacement / maxStrokeDistance) : 0f;

            // 1. 主軸（DrillPress_V1_L2.002）の垂直移動
            if (spindleTransform != null)
            {
                if (useWorldVerticalMovement)
                {
                    // ワールド垂直軸（Vector3.up）に沿って上下移動
                    spindleTransform.position = initialSpindleWorldPos + Vector3.up * displacement;
                }
                else
                {
                    // ローカル移動軸に沿って移動
                    spindleTransform.localPosition = initialSpindleLocalPos + localSpindleMoveAxis.normalized * displacement;
                }
            }

            // 2. ハンドル（DrillPress_V1_L2.001）の連動回転
            if (handleTransform != null)
            {
                float rotationAngle = currentStrokeProgress * maxRotationAngle * (invertRotation ? -1f : 1f);
                handleTransform.localRotation = initialHandleLocalRot * Quaternion.AngleAxis(rotationAngle, handleRotationAxis.normalized);
            }
        }

        /// <summary>
        /// 進行度 (0.0〜1.0) を直接指定して状態を更新
        /// </summary>
        public void ApplyProgress(float progress)
        {
            float displacement = -Mathf.Clamp01(progress) * maxStrokeDistance;
            ApplyDisplacement(displacement);
        }

        /// <summary>
        /// オブジェクトを初期状態・最上部にリセット
        /// </summary>
        public override void ResetObjectPosition(Vector3 resetPosition)
        {
            isGrabbed = false;
            targetStrokeDisplacement = 0f;
            ApplyDisplacement(0f);
        }

        private void OnDrawGizmosSelected()
        {
            if (spindleTransform != null)
            {
                Vector3 startPos = Application.isPlaying ? initialSpindleWorldPos : spindleTransform.position;
                Vector3 endPos = startPos - Vector3.up * maxStrokeDistance;

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(startPos, endPos);
                Gizmos.DrawWireSphere(startPos, 0.015f);
                Gizmos.DrawWireSphere(endPos, 0.015f);
            }

            if (handleTransform != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(handleTransform.position, 0.03f);
            }
        }
    }
}
