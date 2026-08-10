using UnityEngine;

namespace PseudoHapticsCore
{
    /// <summary>
    /// C/D比（Control/Display Ratio）に基づき、現実のコントローラー移動量に対して
    /// 仮想オブジェクトの変位を動的に制御するクラス。
    /// 式: Y_obj = Y_obj_start + (Y_ctrl - Y_ctrl_start) * R_CD
    /// </summary>
    [DisallowMultipleComponent]
    public class PseudoHapticsController : MonoBehaviour
    {
        [Header("Pseudo-Haptics Settings")]
        [Tooltip("適用されるC/D比 (Control/Display Ratio)")]
        [SerializeField] private float currentCdRatio = 1.0f;

        [Tooltip("Y軸方向のみにPseudo-Haptics効果を適用するか (True: Y軸のみスケール, XZは手と同等)")]
        [SerializeField] private bool lockXzTranslation = true;

        [Tooltip("手放した際に物理演算 (Rigidbody) を有効化して落下させるか")]
        [SerializeField] private bool enablePhysicsOnRelease = true;

        public float CurrentCdRatio
        {
            get => currentCdRatio;
            set => currentCdRatio = Mathf.Max(0.01f, value);
        }

        public bool IsGrabbed => isGrabbed;

        private bool isGrabbed = false;
        private Vector3 initialControllerPos;
        private Vector3 initialObjectPos;
        private Rigidbody rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
        }

        /// <summary>
        /// 把持（Grab）開始時の初期化処理
        /// </summary>
        /// <param name="controllerPos">掴み開始時点のコントローラー世界座標</param>
        public void OnGrabStart(Vector3 controllerPos)
        {
            isGrabbed = true;
            initialControllerPos = controllerPos;
            initialObjectPos = transform.position;

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// 把持中の毎フレーム更新処理。C/D比に基づく仮想変位の適用。
        /// </summary>
        /// <param name="controllerPos">現在のコントローラー世界座標</param>
        public void OnGrabUpdate(Vector3 controllerPos)
        {
            if (!isGrabbed) return;

            Vector3 controllerDelta = controllerPos - initialControllerPos;

            // Y軸（高さ方向）へのC/D比スケール適用
            float deltaY = controllerDelta.y * currentCdRatio;

            Vector3 targetPosition = initialObjectPos;
            targetPosition.y += deltaY;

            if (!lockXzTranslation)
            {
                targetPosition.x += controllerDelta.x;
                targetPosition.z += controllerDelta.z;
            }
            else
            {
                // X, Z方向は手の相対移動にそのまま追従
                targetPosition.x += controllerDelta.x;
                targetPosition.z += controllerDelta.z;
            }

            transform.position = targetPosition;
        }

        /// <summary>
        /// 把持解除（Release）時の処理
        /// </summary>
        public void OnGrabRelease()
        {
            if (!isGrabbed) return;

            isGrabbed = false;

            if (rb != null && enablePhysicsOnRelease)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }
        }

        /// <summary>
        /// オブジェクトを初期状態・指定位置に安全にリセット
        /// </summary>
        public void ResetObjectPosition(Vector3 resetPosition)
        {
            isGrabbed = false;
            transform.position = resetPosition;
            initialObjectPos = resetPosition;

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}
