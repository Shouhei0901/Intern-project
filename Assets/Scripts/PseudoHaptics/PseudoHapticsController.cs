using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

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

        [Tooltip("Trueの場合、実験マネージャーからの自動上書きを防ぎ、Inspectorでの設定値を常に最優先します")]
        [SerializeField] private bool useInspectorCdRatioAlways = true;

        [Tooltip("Y軸方向のみにPseudo-Haptics効果を適用するか (True: Y軸のみスケール, XZは手と同等)")]
        [SerializeField] private bool lockXzTranslation = true;

        [Tooltip("手放した際に物理演算 (Rigidbody) を有効化して落下させるか")]
        [SerializeField] private bool enablePhysicsOnRelease = true;

        public float CurrentCdRatio
        {
            get => currentCdRatio;
            set
            {
                currentCdRatio = Mathf.Max(0.01f, value);
            }
        }

        public bool IsGrabbed => isGrabbed;

        private bool isGrabbed = false;
        private Vector3 initialControllerPos;
        private Vector3 initialObjectPos;
        private Rigidbody rb;
        private IXRInteractor activeInteractor;
        private XRGrabInteractable grabInteractable;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
        }

        private void OnEnable()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
            if (grabInteractable != null)
            {
                // XRGrabInteractable が1:1で位置を直接上書きするのを防止
                grabInteractable.trackPosition = false;
                grabInteractable.trackRotation = false;

                grabInteractable.selectEntered.AddListener(OnSelectEntered);
                grabInteractable.selectExited.AddListener(OnSelectExited);
            }
        }

        private void OnDisable()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
                grabInteractable.selectExited.RemoveListener(OnSelectExited);
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (args.interactorObject != null)
            {
                activeInteractor = args.interactorObject;
                Transform attachTransform = activeInteractor.GetAttachTransform(grabInteractable);
                Vector3 startPos = attachTransform != null ? attachTransform.position : activeInteractor.transform.position;
                OnGrabStart(startPos);
            }
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            activeInteractor = null;
            OnGrabRelease();
        }

        private void LateUpdate()
        {
            // XRGrabInteractable や手動デバッグ操作で掴まれている間、毎フレーム C/D比に基づいた位置計算を適用
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
                targetPosition.x += controllerDelta.x * currentCdRatio;
                targetPosition.z += controllerDelta.z * currentCdRatio;
            }
            else
            {
                // X, Z方向は手の相対移動にそのまま1:1追従
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
