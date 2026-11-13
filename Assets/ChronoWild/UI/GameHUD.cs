using UnityEngine;
using UnityEngine.UI;
using ChronoWild.Core;
using ChronoWild.Health;
using ChronoWild.Inventory;

namespace ChronoWild.UI
{
    public sealed class GameHUD : MonoBehaviour
    {
        public GameStateController states;
        public PlayerHealth health;
        public ChronoWild.Inventory.Inventory inventory;
        public CanvasGroup backpack, pause, options, death, damage;
        public CanvasGroup backpackContent;
        float backpackOpenedAt;
        bool openingBackpack;
        public UnityEngine.UI.Image backpackBackground, deathBackground, heart, claw1, claw2;
        public UnityEngine.UI.Text countText, tipText, deathText, healthText, tooltip;
        public UnityEngine.UI.Image[] slotIcons;
        public UnityEngine.UI.Text[] slotLabels;
        public UnityEngine.UI.Button[] slotButtons;
        public AudioSource damageAudio;
        public UnityEngine.Rendering.Volume pauseVolume;
        public string[] tips = { "La energía mantiene abierto el portal.", "Revisa las piezas antes de cruzar.", "Un acceso cerrado puede necesitar una tarjeta.", "Los dinosaurios siguen moviéndose mientras revisas la mochila." };
        float damageTime = -10f, deathStart;
        bool showOptions;
        void OnEnable()
        {
            if (states) states.StateChanged += OnState;
            if (health) { health.HealthChanged += OnHealth; health.Damaged += OnDamage; }
            if (inventory) inventory.ItemCollected += OnItem;
        }
        void OnDisable()
        {
            if (states) states.StateChanged -= OnState;
            if (health) { health.HealthChanged -= OnHealth; health.Damaged -= OnDamage; }
            if (inventory) inventory.ItemCollected -= OnItem;
        }
        void Start() { OnHealth(health ? health.Current : 100); RefreshSlots(); OnState(states ? states.State : GameState.Playing); }
        void OnState(GameState state)
        {
            openingBackpack = state == GameState.Backpack;
            backpackOpenedAt = Time.unscaledTime;
            SetPanel(backpack, false);
            SetPanel(backpackContent, false); SetPanel(pause, state == GameState.Paused); SetPanel(options, false); showOptions = false;
            SetPanel(death, state == GameState.Dead);
            if (pauseVolume) pauseVolume.enabled = state == GameState.Paused;
            if (state == GameState.Paused && tipText != null) tipText.text = tips[Random.Range(0, tips.Length)];
            if (state == GameState.Dead) deathStart = Time.unscaledTime;
            if (tooltip) tooltip.text = "";
            RefreshCount();
        }
        static void SetPanel(CanvasGroup panel, bool on) { if (!panel) return; panel.alpha = on ? 1 : 0; panel.blocksRaycasts = on; panel.interactable = on; }
        void OnHealth(int hp) { if (healthText) healthText.text = hp.ToString() + " HP"; }
        void OnDamage() { damageTime = Time.unscaledTime; if (damageAudio && damageAudio.clip) damageAudio.Play(); }
        void OnItem(ItemDefinition _) { RefreshSlots(); RefreshCount(); }
        void RefreshCount() { if (countText && inventory) countText.text = inventory.Count + "/15"; }
        void RefreshSlots()
        {
            if (!inventory || slotIcons == null) return;
            for (int i = 0; i < Mathf.Min(15, inventory.Catalog.Count, slotIcons.Length); i++)
            {
                var item = inventory.Catalog[i]; bool found = inventory.Has(item.id);
                if (slotIcons[i]) { slotIcons[i].sprite = found ? item.icon : null; slotIcons[i].color = found && item.icon ? Color.white : new Color(0,0,0,0.18f); }
                if (slotLabels != null && i < slotLabels.Length && slotLabels[i]) slotLabels[i].text = found ? item.displayName : "?";
            }
        }
        public void HoverSlot(int index) { if (!tooltip || !inventory || index < 0 || index >= inventory.Catalog.Count) return; var item = inventory.Catalog[index]; tooltip.text = inventory.Has(item.id) ? item.displayName : "?  " + item.hint; }
        public void ClearHover() { if (tooltip) tooltip.text = ""; }
        public void ToggleOptions()
        {
            showOptions = !showOptions; SetPanel(options, showOptions);
        }
        void Update()
        {
            if (openingBackpack && backpack && backpackContent)
            {
                float t = Time.unscaledTime - backpackOpenedAt;
                // Ease-in for the dimmer, then a separate 0.2 second content fade.
                float black = Mathf.Clamp01(t / 0.3f);
                backpack.alpha = black * black;
                backpackContent.alpha = Mathf.Clamp01((t - 0.3f) / 0.2f);
                backpack.blocksRaycasts = true;
                backpack.interactable = true;
                backpackContent.blocksRaycasts = t >= 0.5f;
                backpackContent.interactable = t >= 0.5f;
            }
            float elapsed = Time.unscaledTime - damageTime;
            if (damage) damage.alpha = elapsed < 0.55f ? Mathf.Sin(Mathf.Clamp01(elapsed / 0.55f) * Mathf.PI) * 0.75f : 0;
            if (heart && health) { float rate = health.Current <= 30 ? 4.5f : 1.2f; heart.rectTransform.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.unscaledTime * rate * 6.28f)); heart.color = health.Current <= 30 ? new Color(1,0.05f,0.05f) : new Color(1,0.35f,0.35f); }
            if (death && states && states.State == GameState.Dead) { death.alpha = Mathf.Clamp01((Time.unscaledTime - deathStart) / 1.2f); if (deathText) deathText.color = new Color(1,1,1,0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f)); }
        }
    }
}
