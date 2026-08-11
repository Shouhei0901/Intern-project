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
        [SerializeField] private Slider scoreSlider;
        [SerializeField] private Button submitButton;
        [SerializeField] private PseudoHapticsController pseudoHapticsController;

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
