using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChronoWild.Core
{
    internal sealed partial class DisplaySettingsMenu
    {
        RectTransform pauseShell;
        RectTransform[] pausePages;
        Button[] pauseTabs;
        Button resumeButton;
        int pauseTab;
        Text loadStatus;
        Image loadFill;

        internal void InitializePause(CanvasGroup pause, CanvasGroup oldOptions, GameStateController states, Text tip)
        {
            var host = (RectTransform)pause.transform;
            var volume = oldOptions == null ? null : oldOptions.transform.Find("Volumen Slider")?.GetComponent<Slider>();
            var sensitivity = oldOptions == null ? null : oldOptions.transform.Find("Sensibilidad Slider")?.GetComponent<Slider>();
            foreach (Transform child in host)
                child.gameObject.SetActive(false);

            if (pause.TryGetComponent<Image>(out var shade))
                shade.color = new Color(.025f, .04f, .035f, .73f);

            pauseShell = CreateRect(host, "Menú de pausa", 0, 0, 844, 465);
            pauseShell.gameObject.AddComponent<Image>().color = PanelColor;
            CreateText(pauseShell, "CHRONO WILD", -141, 178, 480, 30, 22, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            var heading = CreateText(pauseShell, "PAUSA", 263, 178, 232, 30, 18, TextAnchor.MiddleRight);
            heading.color = MutedColor;
            CreateRule(pauseShell, 0, 151, 772, 1, DividerColor);

            pausePages = new RectTransform[3];
            pauseTabs = new Button[3];
            string[] names = { "Partida", "Pantalla", "Audio y controles" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                pauseTabs[i] = CreateButton(pauseShell, names[i], -265 + 266 * i, 116, 244, 45, () => SetPauseTab(index));
                pausePages[i] = CreateRect(pauseShell, names[i] + " · contenido", 0, -55, 772, 304);
                pausePages[i].gameObject.SetActive(false);
            }

            var game = pausePages[0];
            CreateText(game, "Partida en curso", 0, 94, 698, 52, 39, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            CreateRule(game, 0, 55, 698, 1, DividerColor);
            resumeButton = CreateButton(game, "Reanudar", -172, -3, 354, 50, states.Resume);
            resumeButton.image.color = AccentColor;
            resumeButton.GetComponentInChildren<Text>().color = InkColor;
            CreateButton(game, "Volver al menú principal", -172, -66, 354, 50, states.MainMenu);
            if (tip != null)
            {
                tip.rectTransform.SetParent(game, false);
                tip.gameObject.SetActive(true);
                tip.rectTransform.anchorMin = tip.rectTransform.anchorMax = new Vector2(.5f, .5f);
                tip.rectTransform.pivot = new Vector2(.5f, .5f);
                tip.rectTransform.sizeDelta = new Vector2(698, 36);
                tip.rectTransform.anchoredPosition = new Vector2(0, -117);
                tip.alignment = TextAnchor.MiddleLeft;
                tip.font = font;
                tip.fontSize = 16;
                tip.color = MutedColor;
            }

            BuildDisplayPanel(pausePages[1]);
            panel.localScale = Vector3.one * .78f;

            var controls = pausePages[2];
            CreateText(controls, "Audio y controles", 0, 94, 698, 52, 39, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            CreateRule(controls, 0, 55, 698, 1, DividerColor);
            AddSlider(controls, "Volumen", 10, volume, 0, 1, AudioListener.volume, states.SetVolume);
            AddSlider(controls, "Sensibilidad", -64, sensitivity, .02f, .4f, .12f, states.SetSensitivity);

            FitPause();
        }

        void AddSlider(RectTransform page, string title, float y, Slider original,
            float minimum, float maximum, float value, UnityEngine.Events.UnityAction<float> apply)
        {
            CreateText(page, title, -240, y, 220, 40, 19, TextAnchor.MiddleLeft);
            var track = CreateRect(page, title + " Slider", 108, y, 466, 28);
            var backdrop = track.gameObject.AddComponent<Image>();
            backdrop.color = ButtonColor;
            var slider = track.gameObject.AddComponent<Slider>();
            slider.minValue = original != null ? original.minValue : minimum;
            slider.maxValue = original != null ? original.maxValue : maximum;
            slider.SetValueWithoutNotify(original != null ? original.value : value);
            var trackArea = CreateRect(track, "Pista", 0, 0, 444, 12);
            var fill = CreateRect(trackArea, "Nivel", 0, 0, 0, 12).gameObject.AddComponent<Image>();
            fill.color = AccentColor;
            fill.raycastTarget = false;
            var handle = CreateRect(trackArea, "Cursor", 0, 0, 16, 24).gameObject.AddComponent<Image>();
            handle.color = TextColor;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.onValueChanged.AddListener(v =>
            {
                if (original != null)
                    original.value = v;
                else
                    apply(v);
            });
        }

        void FitPause()
        {
            var available = ((RectTransform)transform).rect.size;
            float scale = Mathf.Min(available.x / 930f, available.y / 523f);
            pauseShell.localScale = new Vector3(scale, scale, 1f);
        }

        internal void SetPauseVisible(bool visible)
        {
            pauseShell.gameObject.SetActive(visible);
            if (visible)
            {
                FitPause();
                SetPauseTab(0);
            }
            else if (EventSystem.current != null &&
                     EventSystem.current.currentSelectedGameObject is GameObject selected &&
                     selected.transform.IsChildOf(pauseShell))
                EventSystem.current.SetSelectedGameObject(null);
        }

        internal void SetPauseTab(int tab)
        {
            if (pausePages == null)
                return;
            if (tab == 1 && (pauseTab != 1 || !pausePages[1].gameObject.activeSelf))
            {
                ReadModes();
                RefreshLabels();
            }
            pauseTab = tab;
            for (int i = 0; i < pausePages.Length; i++)
            {
                pausePages[i].gameObject.SetActive(i == tab);
                SetRateStyle(pauseTabs[i], pauseTabs[i].GetComponentInChildren<Text>(), i == tab);
            }
            EventSystem.current?.SetSelectedGameObject((tab == 0 ? resumeButton : pauseTabs[tab]).gameObject);
        }

        internal void ShowLoading()
        {
            if (overlay.gameObject.activeSelf)
                Close();
            EventSystem.current?.SetSelectedGameObject(null);
            mainButtons.interactable = false;
            foreach (Transform child in mainActions)
            {
                if (child.TryGetComponent<Button>(out _))
                    child.gameObject.SetActive(false);
            }

            var canvas = (RectTransform)transform;
            var transition = CreateRect(canvas, "Transición", 0, 0, 0, 0);
            transition.anchorMin = Vector2.zero;
            transition.anchorMax = Vector2.one;
            var shade = transition.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .035f, .03f, .34f);

            var track = CreateRect(transition, "Progreso", 0, 34, 0, 3);
            track.anchorMin = new Vector2(.058f, 0);
            track.anchorMax = new Vector2(.942f, 0);
            track.anchoredPosition = new Vector2(0, 34);
            track.gameObject.AddComponent<Image>().color = DividerColor;

            loadFill = CreateRect(track, "Avance", 0, 0, 0, 0).gameObject.AddComponent<Image>();
            loadFill.color = AccentColor;
            loadFill.rectTransform.anchorMin = Vector2.zero;
            loadFill.rectTransform.anchorMax = Vector2.up;

            loadStatus = CreateText(transition, "", 0, 0, 80, 26, 16, TextAnchor.MiddleRight);
            var counter = loadStatus.rectTransform;
            counter.anchorMin = counter.anchorMax = new Vector2(.942f, 0);
            counter.pivot = new Vector2(1, .5f);
            counter.anchoredPosition = new Vector2(0, 54);
            loadStatus.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -1);
            SetLoadingProgress(0);
        }

        internal void SetLoadingProgress(float progress)
        {
            loadFill.rectTransform.anchorMax = new Vector2(progress, 1);
            loadStatus.text = $"{Mathf.FloorToInt(progress * 100)} %";
        }
    }
}
