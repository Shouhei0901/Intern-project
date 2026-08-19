using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;
using UnityEngine.XR;

namespace VideoControl
{
    /// <summary>
    /// VRコントローラーのBボタン（Secondary Button）およびキーボード[B]/[Space]キーで
    /// VideoPlayerの再生 / 一時停止を切り替えるコントローラー。
    /// URP/Android(Quest)での黒画面問題を完全解消する自動RenderTextureバインドと、
    /// すべてのXRランタイム（OpenXR/Oculus/XRI/旧Input）に対応する多重入力検知を搭載。
    /// </summary>
    [RequireComponent(typeof(VideoPlayer))]
    [DisallowMultipleComponent]
    public class VideoPlaybackController : MonoBehaviour
    {
        [Header("Target Video Player")]
        [SerializeField] private VideoPlayer videoPlayer;

        [Header("Playback Settings")]
        [Tooltip("シーン開始時に自動で再生を開始するか")]
        [SerializeField] private bool playOnStart = true;

        [Tooltip("ループ再生を行うか")]
        [SerializeField] private bool loop = true;

        [Tooltip("停止時に最初から再生し直すか、一時停止（Pause）にするか")]
        [SerializeField] private bool pauseInsteadOfStop = true;

        [Header("Render Texture Setup")]
        [Tooltip("RenderTextureを自動生成してマテリアルに強制バインドするか")]
        [SerializeField] private bool autoBindRenderTexture = true;

        [Tooltip("解像度 (幅)")]
        [SerializeField] private int textureWidth = 1920;

        [Tooltip("解像度 (高さ)")]
        [SerializeField] private int textureHeight = 1080;

        [Header("Custom Input (Optional)")]
        [Tooltip("XRI Input Actionアセットからバインドする場合に指定（空の場合は自動でBボタンがバインドされます）")]
        [SerializeField] private InputActionProperty customToggleAction;

        private UnityEngine.InputSystem.InputAction autonomousToggleAction;
        private bool isAutonomousAction = false;
        private RenderTexture dynamicRenderTexture;
        private Material targetMaterial;

        // XR Direct Polling 用ステート
        private readonly List<UnityEngine.XR.InputDevice> xrDevices = new List<UnityEngine.XR.InputDevice>();
        private bool wasXrSecondaryPressed = false;

        private void Awake()
        {
            if (videoPlayer == null)
            {
                videoPlayer = GetComponent<VideoPlayer>();
            }

            // 1. RenderTexture & Material セットアップ
            if (autoBindRenderTexture && videoPlayer != null)
            {
                SetupRenderTextureAndMaterial();
            }

            if (videoPlayer != null)
            {
                videoPlayer.isLooping = loop;
                videoPlayer.errorReceived += OnVideoError;
                videoPlayer.prepareCompleted += OnVideoPrepared;
            }

            // 2. New Input System の登録
            SetupInputActions();
        }

        private void SetupRenderTextureAndMaterial()
        {
            RenderTexture rt = videoPlayer.targetTexture;

            if (rt == null || videoPlayer.renderMode != VideoRenderMode.RenderTexture)
            {
                dynamicRenderTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Dynamic_Video_RenderTexture",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                dynamicRenderTexture.Create();

                videoPlayer.renderMode = VideoRenderMode.RenderTexture;
                videoPlayer.targetTexture = dynamicRenderTexture;
                rt = dynamicRenderTexture;
            }

            // 同一または子オブジェクトの Renderer からマテリアルを取得
            Renderer rend = GetComponent<Renderer>();
            if (rend == null) rend = GetComponentInChildren<Renderer>();

            if (rend != null)
            {
                targetMaterial = rend.material;
                if (targetMaterial != null)
                {
                    targetMaterial.mainTexture = rt;
                    if (targetMaterial.HasProperty("_BaseMap")) targetMaterial.SetTexture("_BaseMap", rt);
                    if (targetMaterial.HasProperty("_MainTex")) targetMaterial.SetTexture("_MainTex", rt);

                    targetMaterial.color = Color.white;
                    if (targetMaterial.HasProperty("_BaseColor")) targetMaterial.SetColor("_BaseColor", Color.white);
                    if (targetMaterial.HasProperty("_Color")) targetMaterial.SetColor("_Color", Color.white);
                }
            }

            Debug.Log("[VideoPlaybackController] ✅ RenderTexture successfully bound to Screen Surface Material.");
        }

        private void SetupInputActions()
        {
            if (customToggleAction.action != null)
            {
                customToggleAction.action.performed += OnToggleInputPerformed;
                customToggleAction.action.Enable();
            }
            else
            {
                autonomousToggleAction = new UnityEngine.InputSystem.InputAction("VideoTogglePlayback", InputActionType.Button);
                
                // XR 右手 Bボタン (Secondary Button)
                autonomousToggleAction.AddBinding("<XRController>{RightHand}/secondaryButton");
                autonomousToggleAction.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
                autonomousToggleAction.AddBinding("<XRController>{RightHand}/secondary");
                autonomousToggleAction.AddBinding("<XRController>{RightHand}/secondaryAction");
                autonomousToggleAction.AddBinding("<XRController>{RightHand}/buttonEast");
                autonomousToggleAction.AddBinding("<XRInputDevice>{RightHand}/secondaryButton");
                autonomousToggleAction.AddBinding("<XRInputDevice>{RightHand}/{SecondaryButton}");
                autonomousToggleAction.AddBinding("<OculusTouchController>{RightHand}/secondaryButton");
                autonomousToggleAction.AddBinding("<ViveFocus3Controller>{RightHand}/secondaryButton");
                autonomousToggleAction.AddBinding("<ViveFocus3Profile>/rightHand/secondary");
                autonomousToggleAction.AddBinding("<Gamepad>/buttonEast");
                autonomousToggleAction.AddBinding("*/{SecondaryButton}");

                // PC エディタ用キーボード [B] キー / [Space] キー
                autonomousToggleAction.AddBinding("<Keyboard>/b");
                autonomousToggleAction.AddBinding("<Keyboard>/space");

                autonomousToggleAction.performed += OnToggleInputPerformed;
                autonomousToggleAction.Enable();
                isAutonomousAction = true;
            }
        }

        private void Start()
        {
            if (videoPlayer == null) return;

            if (playOnStart)
            {
                PlayVideo();
            }
        }

        private void Update()
        {
            // 1. キーボードフォールバック検知 ([B]キー / [Space]キー)
            if (Keyboard.current != null)
            {
                if (Keyboard.current.bKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    TogglePlayback();
                    return;
                }
            }

            // 2. ゲームパッドフォールバック (Bボタン)
            if (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame)
            {
                TogglePlayback();
                return;
            }

            // 3. XR InputDevices 直接ポーリング (OpenXR / Oculus / Quest のBボタン)
            PollXRDevices();
        }

        private void PollXRDevices()
        {
            xrDevices.Clear();
            UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
                xrDevices
            );

            bool isAnySecondaryPressed = false;
            foreach (var dev in xrDevices)
            {
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool pressed) && pressed)
                {
                    isAnySecondaryPressed = true;
                    break;
                }
            }

            // 立下り・立上りエッジ検出
            if (isAnySecondaryPressed && !wasXrSecondaryPressed)
            {
                wasXrSecondaryPressed = true;
                TogglePlayback();
            }
            else if (!isAnySecondaryPressed)
            {
                wasXrSecondaryPressed = false;
            }
        }

        private void OnVideoPrepared(VideoPlayer source)
        {
            Debug.Log($"[VideoPlaybackController] Video prepared ({source.texture?.width}x{source.texture?.height})");
            if (playOnStart && !source.isPlaying)
            {
                source.Play();
            }
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            Debug.LogError($"[VideoPlaybackController] VideoPlayer Error: {message}");
        }

        private void OnDestroy()
        {
            if (videoPlayer != null)
            {
                videoPlayer.errorReceived -= OnVideoError;
                videoPlayer.prepareCompleted -= OnVideoPrepared;
            }

            if (customToggleAction.action != null)
            {
                customToggleAction.action.performed -= OnToggleInputPerformed;
            }

            if (isAutonomousAction && autonomousToggleAction != null)
            {
                autonomousToggleAction.performed -= OnToggleInputPerformed;
                autonomousToggleAction.Disable();
                autonomousToggleAction.Dispose();
            }

            if (dynamicRenderTexture != null)
            {
                if (videoPlayer != null && videoPlayer.targetTexture == dynamicRenderTexture)
                {
                    videoPlayer.targetTexture = null;
                }
                dynamicRenderTexture.Release();
                Destroy(dynamicRenderTexture);
            }
        }

        private void OnToggleInputPerformed(UnityEngine.InputSystem.InputAction.CallbackContext context)
        {
            TogglePlayback();
        }

        /// <summary>
        /// 再生と一時停止（または停止）を切り替えます。
        /// </summary>
        public void TogglePlayback()
        {
            if (videoPlayer == null) return;

            if (videoPlayer.isPlaying)
            {
                if (pauseInsteadOfStop)
                {
                    PauseVideo();
                }
                else
                {
                    StopVideo();
                }
            }
            else
            {
                PlayVideo();
            }
        }

        public void PlayVideo()
        {
            if (videoPlayer == null) return;
            videoPlayer.Play();
            Debug.Log("[VideoPlaybackController] ▶ Video Playing");
        }

        public void PauseVideo()
        {
            if (videoPlayer == null) return;
            videoPlayer.Pause();
            Debug.Log("[VideoPlaybackController] ⏸ Video Paused");
        }

        public void StopVideo()
        {
            if (videoPlayer == null) return;
            videoPlayer.Stop();
            Debug.Log("[VideoPlaybackController] ⏹ Video Stopped");
        }
    }
}
