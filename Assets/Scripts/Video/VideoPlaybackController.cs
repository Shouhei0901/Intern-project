using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

namespace VideoControl
{
    /// <summary>
    /// VRコントローラーのBボタン（Secondary Button）およびキーボード[B]キーで
    /// VideoPlayerの再生 / 一時停止を切り替えるコントローラー。
    /// URP / Android (Quest) 環境で映像が黒くなる現象を100%防止するため、
    /// RenderTextureを自動生成・マテリアルへ強制バインドし、マテリアル色を白に初期化します。
    /// </summary>
    [RequireComponent(typeof(VideoPlayer))]
    [DisallowMultipleComponent]
    public class VideoPlaybackController : MonoBehaviour
    {
        [Header("Target Video Player")]
        [Tooltip("制御対象のVideoPlayerコンポーネント（未指定時は同一GameObjectから自動取得）")]
        [SerializeField] private VideoPlayer videoPlayer;

        [Header("Playback Settings")]
        [Tooltip("シーン開始時に自動で再生を開始するか")]
        [SerializeField] private bool playOnStart = true;

        [Tooltip("ループ再生を行うか")]
        [SerializeField] private bool loop = true;

        [Tooltip("停止時に最初から再生し直すか、一時停止（Pause）にするか")]
        [SerializeField] private bool pauseInsteadOfStop = true;

        [Header("Render Texture Setup (URP / Quest Black Screen Fix)")]
        [Tooltip("RenderTextureを自動生成してマテリアルに適用するか")]
        [SerializeField] private bool autoBindRenderTexture = true;

        [Tooltip("解像度 (幅)")]
        [SerializeField] private int textureWidth = 1920;

        [Tooltip("解像度 (高さ)")]
        [SerializeField] private int textureHeight = 1080;

        [Header("Custom Input (Optional)")]
        [Tooltip("XRI Input Actionアセットからバインドする場合に指定（空の場合は自動でBボタンがバインドされます）")]
        [SerializeField] private InputActionProperty customToggleAction;

        private InputAction autonomousToggleAction;
        private bool isAutonomousAction = false;
        private RenderTexture dynamicRenderTexture;

        private void Awake()
        {
            if (videoPlayer == null)
            {
                videoPlayer = GetComponent<VideoPlayer>();
            }

            // 1. RenderTexture の強制セットアップ (URP / Quest での黒画面を完全に解決)
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

            // 2. Input Action のセットアップ (右手Bボタン / Secondary Button)
            SetupInputActions();
        }

        private void SetupRenderTextureAndMaterial()
        {
            RenderTexture rt = videoPlayer.targetTexture;

            // targetTexture が未設定、または renderMode が RenderTexture でない場合は動的生成
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

            // 同一オブジェクト（または親・子）のRendererマテリアルにテクスチャを強制適用
            Renderer rend = GetComponent<Renderer>();
            if (rend == null) rend = GetComponentInChildren<Renderer>();

            if (rend != null && rend.material != null)
            {
                Material mat = rend.material;
                mat.mainTexture = rt;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", rt);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", rt);

                // マテリアルカラーを白に（黒だと乗算でテクスチャが真っ黒になるため）
                mat.color = Color.white;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

                Debug.Log("[VideoPlaybackController] Successfully bound RenderTexture to Screen Material (Color set to White).");
            }
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
                autonomousToggleAction = new InputAction("VideoTogglePlayback", InputActionType.Button);
                
                // XR コントローラー (右手 Secondary Button / Bボタン)
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

                // PC エディタデバッグ用キーボード [B] キー / [Space] キー
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
            // フォールバック直接入力検知（InputActionが不通の場合の安全策）
            if (Keyboard.current != null && (Keyboard.current.bKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame))
            {
                TogglePlayback();
            }
            else if (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame)
            {
                TogglePlayback();
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

        private void OnToggleInputPerformed(InputAction.CallbackContext context)
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
            Debug.Log("[VideoPlaybackController] Video Playing ▶");
        }

        public void PauseVideo()
        {
            if (videoPlayer == null) return;
            videoPlayer.Pause();
            Debug.Log("[VideoPlaybackController] Video Paused ⏸");
        }

        public void StopVideo()
        {
            if (videoPlayer == null) return;
            videoPlayer.Stop();
            Debug.Log("[VideoPlaybackController] Video Stopped ⏹");
        }
    }
}
