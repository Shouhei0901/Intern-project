using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PseudoHapticsCore
{
    /// <summary>
    /// VR空間内での試行状態提示、評価スコア入力UI、注視ターゲット(Fixation Cross)の制御を行うクラス。
    /// </summary>
    [DisallowMultipleComponent]
    public class ScoreUIController : MonoBehaviour
    {
        [Header("UI Canvas & Panels")]
        [SerializeField] private GameObject scoreCanvas;
        [SerializeField] private GameObject fixationCrossCanvas;

        [Header("UI Text & Components")]
        [SerializeField] private TextMeshProUGUI instructionText;
        [SerializeField] private TextMeshProUGUI scoreValueText;
        [SerializeField] private TextMeshProUGUI cdRatioDisplayText;
        [SerializeField] private TextMeshProUGUI loggingStatusText;
        [SerializeField] private Slider scoreSlider;
        [SerializeField] private Button submitButton;
        [SerializeField] private PseudoHapticsController pseudoHapticsController;

        [Header("Display Settings")]
        [Tooltip("Trueの場合、キャンバスが常にメインカメラの方向を向きます")]
        [SerializeField] private bool faceCameraAlways = true;

        [Tooltip("Trueの場合、画面左上に常時確実なGUIオーバーレイを表示します")]
        [SerializeField] private bool enableOnGuiOverlay = true;

        private string currentStatusMessage = "IDLE";
        private Color currentStatusColor = Color.gray;

        public event Action<int> OnScoreSubmitted;

        private void Awake()
        {
            if (submitButton != null)
            {
                submitButton.onClick.AddListener(OnSubmitClicked);
            }

            if (scoreSlider != null)
            {
                scoreSlider.onValueChanged.AddListener(OnSliderValueChanged);
            }

            SetLoggingStatus("IDLE", Color.gray);
            HideAllUI();
        }

        private void Update()
        {
            // pseudoHapticsController が未割り当ての場合は自動探索
            if (pseudoHapticsController == null)
            {
                pseudoHapticsController = UnityEngine.Object.FindAnyObjectByType<PseudoHapticsController>();
            }

            // リアルタイムに現在の C/D 比を表示更新
            if (cdRatioDisplayText != null)
            {
                float currentCd = pseudoHapticsController != null ? pseudoHapticsController.CurrentCdRatio : 1.0f;
                cdRatioDisplayText.text = $"C/D Ratio: {currentCd:F2}";
            }
        }

        private void LateUpdate()
        {
            // ビルボード処理: カメラの方向を向かせて裏返りや見失いを防止
            if (faceCameraAlways && Camera.main != null)
            {
                Vector3 forward = transform.position - Camera.main.transform.position;
                if (forward.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                }
            }
        }

        private void OnGUI()
        {
            if (!enableOnGuiOverlay) return;

            // 画面左上にステータス（IDLE / RECORDING / SAVED）のみを描画
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = 20;
            boxStyle.fontStyle = FontStyle.Bold;
            boxStyle.normal.textColor = currentStatusColor;
            boxStyle.alignment = TextAnchor.MiddleCenter;

            GUI.Box(new Rect(20, 20, 180, 48), currentStatusMessage, boxStyle);
        }

        public void SetLoggingStatus(string message, Color? color = null)
        {
            currentStatusMessage = message;
            if (color.HasValue)
            {
                currentStatusColor = color.Value;
            }

            if (loggingStatusText != null)
            {
                loggingStatusText.text = currentStatusMessage;
                loggingStatusText.color = currentStatusColor;
            }
        }

        public void UpdateCdRatioDisplay(float cdRatio)
        {
            if (cdRatioDisplayText != null)
            {
                cdRatioDisplayText.text = $"C/D Ratio: {cdRatio:F2}";
            }
        }

        public void ShowFixationCross(bool show)
        {
            if (fixationCrossCanvas != null)
            {
                fixationCrossCanvas.SetActive(show);
            }
        }

        public void ShowScoreUI(string message = "感知された重量・抵抗感を評価してください")
        {
            if (scoreCanvas != null)
            {
                scoreCanvas.SetActive(true);
            }

            if (instructionText != null)
            {
                instructionText.text = message;
            }

            if (scoreSlider != null)
            {
                scoreSlider.value = scoreSlider.minValue;
                UpdateScoreText((int)scoreSlider.value);
            }
        }

        public void HideAllUI()
        {
            if (scoreCanvas != null) scoreCanvas.SetActive(false);
            if (fixationCrossCanvas != null) fixationCrossCanvas.SetActive(false);
        }

        private void OnSliderValueChanged(float value)
        {
            UpdateScoreText((int)value);
        }

        private void UpdateScoreText(int score)
        {
            if (scoreValueText != null)
            {
                scoreValueText.text = $"評価スコア: {score}";
            }
        }

        private void OnSubmitClicked()
        {
            int score = scoreSlider != null ? (int)scoreSlider.value : 1;
            OnScoreSubmitted?.Invoke(score);
            HideAllUI();
        }
    }
}
