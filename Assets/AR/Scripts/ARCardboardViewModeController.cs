using UnityEngine;

namespace Ucen.AR
{
    [DisallowMultipleComponent]
    public sealed class ARCardboardViewModeController : MonoBehaviour
    {
        private const float ButtonWidth = 220f;
        private const float ButtonHeight = 64f;
        private const float ButtonMargin = 18f;
        private const float SeparatorWidth = 4f;

        [SerializeField] private Camera arCamera;
        [SerializeField] private bool startInCardboardMode;
        [SerializeField, Range(0.5f, 1f)] private float renderScale = 1f;

        private RenderTexture cardboardTexture;
        private RenderTexture originalTargetTexture;
        private Rect originalCameraRect;
        private bool isCardboardMode;
        private int textureWidth;
        private int textureHeight;
        private GUIStyle buttonStyle;
        private GUIStyle labelStyle;

        private void Awake()
        {
            if (arCamera == null)
            {
                arCamera = Camera.main;
            }

            if (arCamera != null)
            {
                originalTargetTexture = arCamera.targetTexture;
                originalCameraRect = arCamera.rect;
            }
        }

        private void Start()
        {
            SetCardboardMode(startInCardboardMode);
        }

        private void Update()
        {
            if (isCardboardMode)
            {
                EnsureCardboardTexture();
            }
        }

        private void OnDisable()
        {
            SetCardboardMode(false);
            ReleaseCardboardTexture();
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (isCardboardMode)
            {
                DrawCardboardView();
                DrawToggleButton("Vista normal", new Rect((Screen.width - ButtonWidth) * 0.5f, ButtonMargin, ButtonWidth, ButtonHeight));
                return;
            }

            DrawToggleButton("Cardboard", new Rect(Screen.width - ButtonWidth - ButtonMargin, ButtonMargin, ButtonWidth, ButtonHeight));
        }

        private void DrawCardboardView()
        {
            if (cardboardTexture == null)
            {
                return;
            }

            Rect leftEye = new Rect(0f, 0f, Screen.width * 0.5f, Screen.height);
            Rect rightEye = new Rect(Screen.width * 0.5f, 0f, Screen.width * 0.5f, Screen.height);
            GUI.DrawTexture(leftEye, cardboardTexture, ScaleMode.StretchToFill, false);
            GUI.DrawTexture(rightEye, cardboardTexture, ScaleMode.StretchToFill, false);

            Rect separator = new Rect((Screen.width - SeparatorWidth) * 0.5f, 0f, SeparatorWidth, Screen.height);
            GUI.Box(separator, GUIContent.none);

            Rect labelRect = new Rect(0f, Screen.height - 48f, Screen.width, 36f);
            GUI.Label(labelRect, "Modo Cardboard", labelStyle);
        }

        private void DrawToggleButton(string text, Rect rect)
        {
            if (GUI.Button(rect, text, buttonStyle))
            {
                SetCardboardMode(!isCardboardMode);
            }
        }

        private void SetCardboardMode(bool enabled)
        {
            if (arCamera == null)
            {
                return;
            }

            isCardboardMode = enabled;

            if (enabled)
            {
                EnsureCardboardTexture();
                arCamera.rect = new Rect(0f, 0f, 1f, 1f);
                arCamera.targetTexture = cardboardTexture;
                return;
            }

            arCamera.targetTexture = originalTargetTexture;
            arCamera.rect = originalCameraRect;
        }

        private void EnsureCardboardTexture()
        {
            int desiredWidth = Mathf.Max(64, Mathf.RoundToInt(Screen.width * 0.5f * renderScale));
            int desiredHeight = Mathf.Max(64, Mathf.RoundToInt(Screen.height * renderScale));

            if (cardboardTexture != null && textureWidth == desiredWidth && textureHeight == desiredHeight)
            {
                return;
            }

            ReleaseCardboardTexture();
            textureWidth = desiredWidth;
            textureHeight = desiredHeight;
            cardboardTexture = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "AR Cardboard View",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            cardboardTexture.Create();

            if (isCardboardMode && arCamera != null)
            {
                arCamera.targetTexture = cardboardTexture;
            }
        }

        private void ReleaseCardboardTexture()
        {
            if (cardboardTexture == null)
            {
                return;
            }

            if (arCamera != null && arCamera.targetTexture == cardboardTexture)
            {
                arCamera.targetTexture = originalTargetTexture;
            }

            cardboardTexture.Release();
            Destroy(cardboardTexture);
            cardboardTexture = null;
            textureWidth = 0;
            textureHeight = 0;
        }

        private void EnsureStyles()
        {
            if (buttonStyle != null)
            {
                return;
            }

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.active.textColor = Color.white;

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            labelStyle.normal.textColor = Color.white;
        }
    }
}
