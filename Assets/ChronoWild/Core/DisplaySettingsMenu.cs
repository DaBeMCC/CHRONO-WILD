using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ChronoWild.Core
{
    internal sealed partial class DisplaySettingsMenu : MonoBehaviour
    {
        const string WidthKey = "Display.Width";
        const string HeightKey = "Display.Height";
        const string RateKey = "Display.Rate";

        static readonly Color PanelColor = new Color32(19, 29, 27, 255);
        static readonly Color ButtonColor = new Color32(38, 51, 47, 255);
        static readonly Color AccentColor = new Color32(232, 184, 111, 255);
        static readonly Color InkColor = new Color32(25, 27, 21, 255);
        static readonly Color TextColor = new Color32(242, 239, 227, 255);
        static readonly Color MutedColor = new Color32(175, 191, 181, 255);
        static readonly Color DividerColor = new Color32(68, 83, 75, 255);

        Resolution[] monitorModes;
        Vector2Int[] sizes;
        int selectedSize;
        int selectedRate;
        Font font;
        RectTransform overlay;
        RectTransform panel;
        CanvasGroup mainButtons;
        RectTransform mainActions;
        Button openButton;
        Text sizeText;
        Button rate60;
        Button rate120;
        Text rate60Text;
        Text rate120Text;
        Text activeModeText;
        Text statusText;

        static bool CanSwitchRefresh => Application.platform == RuntimePlatform.WindowsPlayer;

        void Awake() => font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        internal void InitializeMain()
        {
            BuildMenu();
            ReadModes();
            if (PlayerPrefs.HasKey(RateKey) && !Application.isEditor)
                ApplyDisplayMode();
            else
                ApplyFramePacing(false);
            RefreshActiveMode();
        }

        void ReadModes()
        {
            monitorModes = Screen.resolutions;
            var current = new Vector2Int(Screen.width, Screen.height);
            var supported = monitorModes.Select(mode => new Vector2Int(mode.width, mode.height));
            if (monitorModes.Length == 0 || Application.isEditor || Application.platform == RuntimePlatform.WebGLPlayer)
                supported = supported.Concat(new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) });

            sizes = supported.Append(current).Distinct().OrderBy(size => size.x).ThenBy(size => size.y).ToArray();
            var saved = new Vector2Int(PlayerPrefs.GetInt(WidthKey, current.x), PlayerPrefs.GetInt(HeightKey, current.y));
            selectedSize = System.Array.IndexOf(sizes, saved);
            if (selectedSize < 0)
                selectedSize = System.Array.IndexOf(sizes, current);
            selectedRate = PlayerPrefs.GetInt(RateKey, 60) == 120 ? 120 : 60;
        }

        Resolution FindExclusiveMode(int rate)
        {
            if (!CanSwitchRefresh)
                return default;

            var size = sizes[selectedSize];
            foreach (var mode in monitorModes)
            {
                if (mode.width == size.x && mode.height == size.y &&
                    System.Math.Abs(mode.refreshRateRatio.value - rate) < 0.6)
                    return mode;
            }
            return default;
        }

        void ApplyDisplayMode()
        {
            var size = sizes[selectedSize];
            var mode = FindExclusiveMode(selectedRate);
            if (mode.width > 0)
            {
                Screen.SetResolution(size.x, size.y, FullScreenMode.ExclusiveFullScreen, mode.refreshRateRatio);
            }
            else
            {
                var fullscreen = Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen
                    ? FullScreenMode.FullScreenWindow : Screen.fullScreenMode;
                Screen.SetResolution(size.x, size.y, fullscreen);
            }
            ApplyFramePacing(mode.width > 0);
        }

        void ApplyFramePacing(bool useVSync)
        {
            QualitySettings.vSyncCount = useVSync ? 1 : 0;
            Application.targetFrameRate = selectedRate;
        }

        void Save()
        {
            ApplyDisplayMode();
            var size = sizes[selectedSize];
            PlayerPrefs.SetInt(WidthKey, size.x);
            PlayerPrefs.SetInt(HeightKey, size.y);
            PlayerPrefs.SetInt(RateKey, selectedRate);
            PlayerPrefs.Save();
            if (pauseShell != null)
                statusText.text = $"Aplicado · {size.x} × {size.y} · {selectedRate} {(QualitySettings.vSyncCount == 1 ? "Hz" : "FPS")}";
            else
            {
                RefreshActiveMode();
                Close();
            }
        }

        void Open()
        {
            ReadModes();
            RefreshLabels();
            mainButtons.interactable = false;
            overlay.gameObject.SetActive(true);
            FitPanel();
            EventSystem.current?.SetSelectedGameObject((selectedRate == 120 ? rate120 : rate60).gameObject);
        }

        void Update()
        {
            if (overlay != null && overlay.gameObject.activeSelf && Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                Close();
        }

        void OnRectTransformDimensionsChange()
        {
            if (overlay != null)
                FitPanel();
            if (pauseShell != null)
                FitPause();
        }

        void FitPanel()
        {
            if (panel == null || !overlay.gameObject.activeSelf)
                return;

            var available = ((RectTransform)transform).rect.size - new Vector2(32, 32);
            var scale = Mathf.Clamp01(Mathf.Min(available.x / panel.sizeDelta.x, available.y / panel.sizeDelta.y));
            panel.localScale = new Vector3(scale, scale, 1f);
        }

        void Close()
        {
            if (pauseShell != null)
            {
                SetPauseTab(0);
                return;
            }

            overlay.gameObject.SetActive(false);
            mainButtons.interactable = true;
            EventSystem.current?.SetSelectedGameObject(openButton.gameObject);
        }

        void ChangeSize(int direction)
        {
            selectedSize = (selectedSize + direction + sizes.Length) % sizes.Length;
            RefreshLabels();
        }

        void SelectRate(int rate)
        {
            selectedRate = rate;
            RefreshLabels();
        }

        void RefreshLabels()
        {
            var size = sizes[selectedSize];
            bool exclusive = FindExclusiveMode(selectedRate).width > 0;
            sizeText.text = $"{size.x} × {size.y}";
            rate60Text.text = $"60 {(FindExclusiveMode(60).width > 0 ? "Hz" : "FPS")}";
            rate120Text.text = $"120 {(FindExclusiveMode(120).width > 0 ? "Hz" : "FPS")}";
            SetRateStyle(rate60, rate60Text, selectedRate == 60);
            SetRateStyle(rate120, rate120Text, selectedRate == 120);
            statusText.text = exclusive
                ? "Frecuencia del monitor · pantalla completa exclusiva"
                : "Límite de FPS · los Hz dependen del monitor y del sistema";
        }

        static void SetRateStyle(Button button, Text label, bool chosen)
        {
            button.image.color = chosen ? AccentColor : ButtonColor;
            label.color = chosen ? InkColor : TextColor;
        }

        void RefreshActiveMode()
        {
            int rate = Application.targetFrameRate;
            string unit = QualitySettings.vSyncCount == 1 ? "Hz" : "FPS";
            activeModeText.text = $"{rate} {unit}  ›";
        }

        void BuildMenu()
        {
            var canvas = (RectTransform)transform;
            var scaler = GetComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(960, 540);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            mainButtons = GetComponent<CanvasGroup>();
            if (mainButtons == null)
                mainButtons = gameObject.AddComponent<CanvasGroup>();
            var background = transform.Find("Background");
            var backdrop = CreateRect(canvas, "Isla jurásica", 0, 0, 0, 0);
            backdrop.SetSiblingIndex(background == null ? 0 : background.GetSiblingIndex() + 1);
            var art = backdrop.gameObject.AddComponent<RawImage>();
            art.texture = Resources.Load<Texture2D>("JurassicMenuBackdrop");
            art.raycastTarget = false;
            var cover = backdrop.gameObject.AddComponent<AspectRatioFitter>();
            cover.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            cover.aspectRatio = 16f / 9f;

            var actions = mainActions = CreateRect(canvas, "Menú principal", 56, 0, 408, 370);
            AnchorLeft(actions);
            var title = transform.Find("Title").GetComponent<Text>();
            title.rectTransform.SetParent(actions, false);
            SetRect(title.rectTransform, 0, 106, 408, 151);
            title.text = "CHRONO\nWILD";
            title.font = font;
            title.fontStyle = FontStyle.Bold;
            title.fontSize = 75;
            title.lineSpacing = .78f;
            title.color = TextColor;
            title.alignment = TextAnchor.MiddleLeft;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            CreateRule(actions, 0, 20, 54, 4, AccentColor, true);

            var play = transform.Find("Jugar").GetComponent<Button>();
            // A saved survival game (made by sleeping) adds "Continuar" on top; "Jugar" then starts over.
            var mainMenu = GetComponent<MainMenuController>();
            bool canContinue = mainMenu != null && ChronoWild.Survival.SurvivalRun.HasSave;
            float shift = canContinue ? -58 : 0;
            Button resume = null;
            if (canContinue)
            {
                resume = CreateButton(actions, "Continuar", 0, -29, 278, 48, mainMenu.Continue);
                StyleMainButton(resume, actions, -29, true);
                play.GetComponentInChildren<Text>().text = "Nueva partida";
            }
            StyleMainButton(play, actions, -29 + shift, !canContinue);
            openButton = CreateButton(actions, "Pantalla", 0, -87 + shift, 278, 48, Open);
            StyleMainButton(openButton, actions, -87 + shift, false);
            activeModeText = CreateText((RectTransform)openButton.transform, "", 74, 0, 99, 48, 15, TextAnchor.MiddleRight);
            activeModeText.color = MutedColor;
            var quit = transform.Find("Salir").GetComponent<Button>();
            StyleMainButton(quit, actions, -145 + shift, false);
            EventSystem.current?.SetSelectedGameObject((resume != null ? resume : play).gameObject);

            overlay = CreateRect(canvas, "Ajustes de pantalla", 0, 0, 0, 0);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.64f);
            overlay.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;

            BuildDisplayPanel(overlay);
            overlay.gameObject.SetActive(false);
        }

        void BuildDisplayPanel(RectTransform parent)
        {
            panel = CreateRect(parent, "Panel", 0, 0, 574, 378);
            panel.gameObject.AddComponent<Image>().color = PanelColor;
            CreateRule(panel, -268, 0, 3, 338, AccentColor);
            CreateText(panel, "Pantalla", 0, 138, 480, 52, 39, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            CreateRule(panel, 0, 99, 480, 1, DividerColor);
            CreateText(panel, "Resolución", -148, 53, 184, 48, 19, TextAnchor.MiddleLeft);
            CreateText(panel, "Fluidez", -148, -19, 184, 48, 19, TextAnchor.MiddleLeft);

            CreateButton(panel, "‹", -10, 53, 44, 48, () => ChangeSize(-1));
            sizeText = CreateText(panel, "", 104, 53, 180, 48, 25);
            sizeText.fontStyle = FontStyle.Bold;
            CreateButton(panel, "›", 218, 53, 44, 48, () => ChangeSize(1));

            rate60 = CreateButton(panel, "60", 37, -19, 128, 48, () => SelectRate(60));
            rate120 = CreateButton(panel, "120", 173, -19, 128, 48, () => SelectRate(120));
            rate60Text = rate60.GetComponentInChildren<Text>();
            rate120Text = rate120.GetComponentInChildren<Text>();
            rate60Text.fontStyle = rate120Text.fontStyle = FontStyle.Bold;

            statusText = CreateText(panel, "", 0, -68, 480, 31, 15, TextAnchor.MiddleLeft);
            statusText.color = MutedColor;
            CreateRule(panel, 0, -103, 480, 1, DividerColor);
            CreateButton(panel, "Volver", -145, -143, 190, 50, Close);
            var applyButton = CreateButton(panel, "Aplicar", 145, -143, 190, 50, Save);
            applyButton.image.color = AccentColor;
            applyButton.GetComponentInChildren<Text>().color = InkColor;
        }

        static void StyleMainButton(Button button, RectTransform parent, float y, bool primary)
        {
            var rect = (RectTransform)button.transform;
            rect.SetParent(parent, false);
            SetRect(rect, 0, y, 278, 48);
            button.image.color = primary ? AccentColor : ButtonColor;
            var label = button.GetComponentInChildren<Text>();
            label.color = primary ? InkColor : TextColor;
            label.alignment = TextAnchor.MiddleLeft;
            label.fontStyle = FontStyle.Bold;
            label.fontSize = 20;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(16, 0);
            label.rectTransform.offsetMax = new Vector2(-10, 0);
            var colors = button.colors;
            colors.normalColor = new Color(.85f, .85f, .85f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(.72f, .72f, .72f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
        }

        static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            AnchorLeft(rect);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);
        }

        static void AnchorLeft(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
            rect.pivot = new Vector2(0, .5f);
        }

        static void CreateRule(RectTransform parent, float x, float y, float width, float height,
            Color color, bool left = false)
        {
            var rect = CreateRect(parent, "Separador", x, y, width, height);
            if (left)
                AnchorLeft(rect);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        static RectTransform CreateRect(RectTransform parent, string name, float x, float y, float width, float height)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        Text CreateText(RectTransform parent, string value, float x, float y, float width, float height,
            int fontSize, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var text = CreateRect(parent, value, x, y, width, height).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = TextColor;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        Button CreateButton(RectTransform parent, string label, float x, float y, float width, float height,
            UnityEngine.Events.UnityAction action)
        {
            var rect = CreateRect(parent, label, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.colorMultiplier = 1f;
            colors.normalColor = new Color(.85f, .85f, .85f);
            colors.selectedColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.60f, 0.60f, 0.60f);
            button.colors = colors;
            CreateText(rect, label, 0, 0, width, height, 19);
            button.onClick.AddListener(action);
            return button;
        }
    }
}
