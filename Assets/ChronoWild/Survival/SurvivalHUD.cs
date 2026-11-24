using ChronoWild.Core;
using ChronoWild.Diagnostics;
using ChronoWild.Health;
using ChronoWild.Story;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ChronoWild.Survival
{
    /// <summary>
    /// Survival interface: compact needs bar (TAB expands it), prompts, the tool bar and the
    /// backpack with three pages — blueprints (B), crafting and food.
    /// </summary>
    internal sealed class SurvivalHUD : MonoBehaviour
    {
        static readonly Color Panel = new Color32(19, 29, 27, 250);
        static readonly Color Secondary = new Color32(38, 51, 47, 255);
        static readonly Color Accent = new Color32(232, 184, 111, 255);
        static readonly Color Ink = new Color32(25, 27, 21, 255);
        static readonly Color Foreground = new Color32(242, 239, 227, 255);
        static readonly Color Muted = new Color32(176, 192, 179, 255);
        static readonly Color Warning = new Color32(241, 130, 105, 255);
        static readonly Color[] MeterColors = { new Color32(219, 107, 100, 255), Accent,
            new Color32(117, 181, 206, 255), new Color32(165, 190, 112, 255), new Color32(196, 160, 222, 255) };
        static readonly string[] MeterNames = { "Salud", "Alimento", "Hidratación", "Resistencia", "Energía" };
        static readonly string[] MeterIcons = { "health", "food", "water", "stamina", "energy" };
        static readonly string[] TabNames = { "Planos  B", "Fabricar", "Comida" };
        const int ListSlots = 7;

        SurvivalRun run;
        PlayerHealth health;
        GameStateController states;
        PerformanceOverlay diagnostics;
        Font font, titleFont;
        RectTransform dashboard, inventory, card;
        CanvasGroup details;
        Text prompt, notice, toolbar, supplies, recipeTitle, recipeDescription, tabLabel, chipsTitle;
        Button close, action;
        RawImage preview;
        readonly Button[] tabs = new Button[TabNames.Length];
        readonly Button[] entries = new Button[ListSlots];
        readonly Text[] chips = new Text[4];
        readonly Image[] meters = new Image[MeterNames.Length];
        readonly Text[] meterValues = new Text[MeterNames.Length];
        readonly int[] shownPercent = { 100, 100, 100, 100, 100 };
        readonly RawImage[] icons = new RawImage[MeterNames.Length];
        int tab, selected;
        bool detailsOpen;
        float refreshAt;

        internal void Initialize(SurvivalRun survival, GameStateController controller)
        {
            run = survival;
            states = controller;
            health = run.GetComponent<PlayerHealth>();
            diagnostics = run.GetComponent<PerformanceOverlay>();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleFont = Resources.Load<Font>("SurvivalGuide/NotoSans-CondensedBlack") ?? font;
            BuildDashboard();
            BuildInventory();
            states.StateChanged += OnState;
            run.OpenPage += ShowTab;
            OnState(states.State);
        }

        // ------------------------------------------------------------------ layout
        void BuildDashboard()
        {
            dashboard = Rect((RectTransform)transform, "Supervivencia", 0, 0, 0, 0);
            Stretch(dashboard);

            var compact = Rect(dashboard, "Iconos de supervivencia", 0, 0, 350, 57);
            PinToBottom(compact, new Vector2(42, 33));
            Background(compact, Panel, false);
            for (int i = 0; i < icons.Length; i++)
            {
                var icon = Graphic(Rect(compact, MeterNames[i], -146 + 48 * i, 0, 27, 27));
                icon.texture = Resources.Load<Texture2D>("SurvivalGuide/" + MeterIcons[i]);
                icon.color = MeterColors[i];
                icons[i] = icon;
            }
            Background(Rect(compact, "Separador", 104, 0, 1, 30), Secondary, false);
            tabLabel = Label(compact, "TAB  +", 140, 0, 58, 31, 18, Muted, TextAnchor.MiddleCenter);
            tabLabel.font = titleFont;

            var expanded = Rect(dashboard, "Medidores", 0, 0, 350, 196);
            PinToBottom(expanded, new Vector2(42, 99));
            Background(expanded, Panel, false);
            details = expanded.gameObject.AddComponent<CanvasGroup>();
            details.alpha = 0;
            details.blocksRaycasts = false;
            for (int i = 0; i < meters.Length; i++)
                meters[i] = BuildMeter(expanded, i);

            prompt = Label(dashboard, "", 0, -137, 1180, 46, 25, Foreground, TextAnchor.MiddleCenter);
            notice = Label(dashboard, "", 0, -188, 1000, 34, 22, Accent, TextAnchor.MiddleCenter);
            toolbar = Label(dashboard, "", 0, 0, 760, 34, 19, Muted, TextAnchor.MiddleRight);
            toolbar.rectTransform.anchorMin = toolbar.rectTransform.anchorMax = new Vector2(1, 0);
            toolbar.rectTransform.pivot = new Vector2(1, 0);
            toolbar.rectTransform.anchoredPosition = new Vector2(-42, 44);
            AddShadow(prompt);
            AddShadow(notice);
            AddShadow(toolbar);
        }

        void BuildInventory()
        {
            inventory = Rect((RectTransform)transform, "Mochila de supervivencia", 0, 0, 0, 0);
            Stretch(inventory);
            Background(inventory, new Color(0, 0, 0, .68f), true);
            card = Rect(inventory, "Mochila", 0, 0, 1092, 696);
            Background(card, Panel, true);
            var title = Label(card, "Mochila", -166, 292, 680, 70, 58, Foreground);
            title.font = titleFont;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            close = Action(card, "Cerrar   P", 421, 293, 148, 48, states.Resume);

            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                tabs[i] = Action(card, TabNames[i], -428 + 146 * i, 232, 136, 42, () => ShowTab(index));
            }
            Background(Rect(card, "Reservas", 0, 182, 1008, 42), Secondary, false);
            supplies = Label(card, "", -2, 182, 974, 38, 20, Foreground);

            for (int i = 0; i < ListSlots; i++)
            {
                int index = i;
                entries[i] = Action(card, "", -361, 124 - 52 * i, 278, 46, () => Select(index));
            }

            var photo = Rect(card, "Vista previa", 157, 30, 694, 232);
            Background(photo, new Color32(23, 31, 28, 255), false);
            preview = Graphic(Rect(photo, "Modelo", 0, 0, 0, 0));
            Stretch(preview.rectTransform);
            var frame = preview.gameObject.AddComponent<AspectRatioFitter>();
            frame.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            frame.aspectRatio = 560f / 318;

            recipeTitle = Label(card, "", 157, -116, 694, 52, 40, Foreground);
            recipeTitle.font = titleFont;
            recipeTitle.verticalOverflow = VerticalWrapMode.Overflow;
            recipeDescription = Label(card, "", 157, -161, 694, 48, 18, Muted);
            recipeDescription.lineSpacing = .95f;
            Background(Rect(card, "Separador", 157, -186, 694, 1), Secondary, false);
            chipsTitle = Label(card, "Materiales", 157, -210, 694, 32, 22, Foreground);
            chipsTitle.font = titleFont;
            for (int i = 0; i < chips.Length; i++)
            {
                var chip = Rect(card, "Material", -105 + 174 * i, -252, 164, 40);
                Background(chip, Secondary, false);
                chips[i] = Label(chip, "", 0, 0, 156, 36, 19, Accent, TextAnchor.MiddleCenter);
            }

            action = Action(card, "Colocar", 379, -312, 250, 46, Act);
            action.image.color = Accent;
            action.GetComponentInChildren<Text>().color = Ink;
        }

        void OnDestroy()
        {
            if (states != null)
                states.StateChanged -= OnState;
            if (run != null)
                run.OpenPage -= ShowTab;
            if (diagnostics != null)
                diagnostics.HiddenByInterface = false;
            if (MissionDirector.Instance != null)
                MissionDirector.Instance.InterfaceOpen = false;
        }

        void OnState(GameState state)
        {
            bool open = state == GameState.Backpack;
            inventory.gameObject.SetActive(open);
            if (diagnostics != null)
                diagnostics.HiddenByInterface = open;
            if (MissionDirector.Instance != null)
                MissionDirector.Instance.InterfaceOpen = open;
            if (open)
            {
                Refresh();
                FitCard();
                EventSystem.current?.SetSelectedGameObject(entries[selected].gameObject);
            }
        }

        void ShowTab(int index)
        {
            tab = index;
            selected = 0;
            Refresh();
        }

        void Select(int index)
        {
            selected = index;
            Refresh();
        }

        void OnRectTransformDimensionsChange()
        {
            if (card != null)
                FitCard();
        }

        void FitCard()
        {
            var size = ((RectTransform)transform).rect.size;
            float scale = Mathf.Max(0, Mathf.Min(size.x / 1190, size.y / 790));
            card.localScale = new Vector3(scale, scale, 1);
        }

        // ------------------------------------------------------------------ live HUD
        void Update()
        {
            if (run == null)
                return;

            bool playing = states.State == GameState.Playing && !IntroCinematic.Playing;
            dashboard.gameObject.SetActive(playing);
            if (!playing)
                return;

            if (Keyboard.current?.tabKey.wasPressedThisFrame == true)
                ToggleDetails();
            details.alpha = Mathf.MoveTowards(details.alpha, detailsOpen ? 1 : 0, Time.unscaledDeltaTime * 7);
            ((RectTransform)details.transform).anchoredPosition = new Vector2(42, Mathf.Lerp(91, 99, details.alpha));

            UpdateMeter(0, health.Current / (float)PlayerHealth.Maximum);
            UpdateMeter(1, run.Hunger);
            UpdateMeter(2, run.Hydration);
            UpdateMeter(3, run.Stamina);
            UpdateMeter(4, run.Energy);
            if (Time.unscaledTime < refreshAt)
                return;
            refreshAt = Time.unscaledTime + .08f;
            prompt.text = run.Hint ?? "";
            notice.text = Time.time < run.NoticeUntil ? run.Notice : "";
            string logs = run.CarriedLogs > 0 ? $"Troncos {run.CarriedLogs}/{SurvivalRun.MaxCarriedLogs} · G soltar     " : "";
            toolbar.text = logs + run.ToolKeys();
        }

        void ToggleDetails()
        {
            detailsOpen = !detailsOpen;
            tabLabel.text = detailsOpen ? "TAB  −" : "TAB  +";
        }

        void UpdateMeter(int i, float value)
        {
            var anchor = meters[i].rectTransform.anchorMax;
            anchor.x = Mathf.MoveTowards(anchor.x, value, Time.unscaledDeltaTime * 2.5f);
            meters[i].rectTransform.anchorMax = anchor;
            icons[i].color = Color.Lerp(Warning, MeterColors[i], Mathf.SmoothStep(0, 1, value * 4));
            int percent = Mathf.RoundToInt(value * 100);
            if (details.alpha > .001f && shownPercent[i] != percent)
            {
                shownPercent[i] = percent;
                meterValues[i].text = $"{percent}%";
            }
        }

        // ------------------------------------------------------------------ backpack pages
        int EntryCount => tab == 0 ? SurvivalRun.Recipes.Length : tab == 1 ? SurvivalRun.ToolRecipes.Length : SurvivalRun.Consumables.Length;

        void Act()
        {
            switch (tab)
            {
                case 0: run.BeginBuild(SurvivalRun.Recipes[selected]); break;
                case 1: run.Craft(SurvivalRun.ToolRecipes[selected]); break;
                default: run.Consume(SurvivalRun.Consumables[selected].Supply); break;
            }
            if (states.State == GameState.Backpack)
                Refresh();
        }

        void Refresh()
        {
            selected = Mathf.Clamp(selected, 0, EntryCount - 1);
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].image.color = i == tab ? Accent : Secondary;
                tabs[i].GetComponentInChildren<Text>().color = i == tab ? Ink : Foreground;
            }
            supplies.text = $"Al hombro {run.CarriedLogs}/{SurvivalRun.MaxCarriedLogs}     Madera {run.Count(SurvivalRun.Supply.Wood)}     " +
                            $"Piedra {run.Count(SurvivalRun.Supply.Stone)}     Fibra {run.Count(SurvivalRun.Supply.Fiber)}     " +
                            $"Brotes {run.Count(SurvivalRun.Supply.Shoots)}     Energía {Mathf.RoundToInt(run.Energy * 100)}%";

            for (int i = 0; i < ListSlots; i++)
            {
                bool used = i < EntryCount;
                entries[i].gameObject.SetActive(used);
                if (!used)
                    continue;
                bool isSelected = i == selected;
                entries[i].image.color = isSelected ? Accent : Secondary;
                var label = entries[i].GetComponentInChildren<Text>();
                label.text = EntryLabel(i);
                label.color = isSelected ? Ink : EntryAvailable(i) ? Foreground : Muted;
            }

            for (int i = 0; i < chips.Length; i++)
                chips[i].transform.parent.gameObject.SetActive(false);
            int slot = 0;
            var actionText = action.GetComponentInChildren<Text>();
            switch (tab)
            {
                case 0:
                {
                    var r = SurvivalRun.Recipes[selected];
                    Show(r.Preview, r.Name, r.Description + (r.Total == 0 ? "" : "  Coloca el plano y complétalo llevando los materiales."));
                    chipsTitle.text = "Materiales";
                    Chip(ref slot, "Troncos", run.CarriedLogs, r.Logs);
                    Chip(ref slot, "Madera", run.Count(SurvivalRun.Supply.Wood), r.Wood);
                    Chip(ref slot, "Piedra", run.Count(SurvivalRun.Supply.Stone), r.Stone);
                    Chip(ref slot, "Fibra", run.Count(SurvivalRun.Supply.Fiber), r.Fiber);
                    actionText.text = "Colocar plano";
                    action.interactable = true;
                    break;
                }
                case 1:
                {
                    var r = SurvivalRun.ToolRecipes[selected];
                    Show(r.Preview, r.Name, r.Description + (r.Requires != SurvivalRun.Tool.None ? $"  Requiere: {SurvivalRun.ToolName(r.Requires)}." : ""));
                    chipsTitle.text = "Materiales";
                    Chip(ref slot, "Madera", run.Count(SurvivalRun.Supply.Wood), r.Wood);
                    Chip(ref slot, "Piedra", run.Count(SurvivalRun.Supply.Stone), r.Stone);
                    Chip(ref slot, "Fibra", run.Count(SurvivalRun.Supply.Fiber), r.Fiber);
                    actionText.text = run.Owns(r.Tool) && r.Tool != SurvivalRun.Tool.Torch ? "Ya la tienes" : "Fabricar";
                    action.interactable = run.CanCraft(r);
                    break;
                }
                default:
                {
                    var c = SurvivalRun.Consumables[selected];
                    Show(c.Preview, c.Name, c.Description);
                    chipsTitle.text = "En la mochila";
                    Chip(ref slot, "Tienes", run.Count(c.Supply), 0, true);
                    actionText.text = c.Verb;
                    action.interactable = run.Count(c.Supply) > 0;
                    break;
                }
            }
        }

        string EntryLabel(int i)
        {
            if (tab == 0)
                return SurvivalRun.Recipes[i].Name;
            if (tab == 1)
            {
                var r = SurvivalRun.ToolRecipes[i];
                return run.Owns(r.Tool) ? r.Name + " · tienes" : r.Name;
            }
            var c = SurvivalRun.Consumables[i];
            return $"{c.Name} · {run.Count(c.Supply)}";
        }

        bool EntryAvailable(int i) => tab == 0 || (tab == 1 ? run.CanCraft(SurvivalRun.ToolRecipes[i]) || run.Owns(SurvivalRun.ToolRecipes[i].Tool)
            : run.Count(SurvivalRun.Consumables[i].Supply) > 0);

        void Show(string photo, string title, string description)
        {
            preview.texture = Resources.Load<Texture2D>("SurvivalGuide/" + photo);
            preview.enabled = preview.texture != null;
            recipeTitle.text = title;
            recipeDescription.text = description;
        }

        void Chip(ref int slot, string name, int available, int needed, bool plain = false)
        {
            if (!plain && needed == 0)
                return;
            var chip = chips[slot++];
            chip.transform.parent.gameObject.SetActive(true);
            chip.text = plain ? $"{name}   {available}" : $"{name}   {available}/{needed}";
            chip.color = plain || available >= needed ? Foreground : Warning;
        }

        // ------------------------------------------------------------------ widgets
        Image BuildMeter(RectTransform parent, int index)
        {
            float y = 72 - 36 * index;
            Label(parent, MeterNames[index], -90, y, 120, 29, 18, Foreground);
            var track = Rect(parent, MeterNames[index] + " · barra", 40, y, 120, 6);
            Background(track, Secondary, false);
            var fill = Background(Rect(track, "Cantidad", 0, 0, 0, 0), MeterColors[index], false);
            Stretch(fill.rectTransform);
            meterValues[index] = Label(parent, "100%", 136, y, 49, 25, 18, Muted, TextAnchor.MiddleRight);
            return fill;
        }

        Button Action(RectTransform parent, string title, float x, float y, float width, float height,
            UnityEngine.Events.UnityAction invoke)
        {
            var rect = Rect(parent, title, x, y, width, height);
            var image = Background(rect, Secondary, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.88f, .93f, .86f);
            colors.selectedColor = new Color(.84f, .89f, .80f);
            colors.disabledColor = new Color(.48f, .51f, .48f);
            button.colors = colors;
            Label(rect, title, 0, 0, width, height, 20, Foreground, TextAnchor.MiddleCenter);
            button.onClick.AddListener(invoke);
            return button;
        }

        Text Label(RectTransform parent, string value, float x, float y, float width, float height,
            int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var text = Rect(parent, string.IsNullOrEmpty(value) ? "Texto" : value, x, y, width, height).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.text = value;
            return text;
        }

        static RectTransform Rect(RectTransform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static void PinToBottom(RectTransform rect, Vector2 offset)
        {
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = offset;
        }

        static Image Background(RectTransform rect, Color color, bool raycast)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        static RawImage Graphic(RectTransform rect)
        {
            var image = rect.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            return image;
        }

        static void AddShadow(Text label)
        {
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectDistance = new Vector2(2, -2);
            shadow.effectColor = new Color(0, 0, 0, .9f);
        }
    }
}
