using System.Collections.Generic;
using UnityEngine;
using ChronoWild.Core;
using ChronoWild.Inventory;
namespace ChronoWild.UI
{
    public sealed class PickupMessageUI : MonoBehaviour
    {
        public ChronoWild.Inventory.Inventory inventory;
        public GameStateController states;
        public UnityEngine.UI.Text label;
        public CanvasGroup group;
        public float duration = 5f;
        readonly Queue<string> pending = new Queue<string>();
        readonly string[] phrases = {
            "Bien, encontré {0}. Me será útil para {1}.",
            "Qué suerte: {0}. Puede ayudarme a {1}.",
            "Encontré {0}. Esto me ayudará a {1}.",
            "Un hallazgo oportuno: {0}. Me servirá para {1}."
        };
        float startedAt;
        bool showing;
        int lastPhrase = -1;
        void OnEnable() { if (inventory) inventory.ItemCollected += OnCollected; if (states) states.StateChanged += OnState; }
        void OnDisable() { if (inventory) inventory.ItemCollected -= OnCollected; if (states) states.StateChanged -= OnState; }
        void OnState(GameState state) { if (state == GameState.Dead) { pending.Clear(); showing=false; group.alpha=0; } }
        void OnCollected(ItemDefinition item)
        {
            int phrase = Random.Range(0,lastPhrase < 0 ? phrases.Length : phrases.Length-1); if (lastPhrase >= 0 && phrase >= lastPhrase) phrase++;
            lastPhrase = phrase;
            string use = item.survivalUses != null && item.survivalUses.Length > 0 ? item.survivalUses[Random.Range(0,item.survivalUses.Length)] : "seguir sobreviviendo cerca del portal";
            pending.Enqueue(string.Format(phrases[phrase],string.IsNullOrEmpty(item.pickupName) ? item.displayName : item.pickupName,use));
        }
        void Update()
        {
            if (!showing && pending.Count > 0) { label.text=pending.Dequeue(); startedAt=Time.unscaledTime; showing=true; }
            if (!showing) { group.alpha=0; return; }
            float t=Time.unscaledTime-startedAt;
            group.alpha=Mathf.Min(Mathf.Clamp01(t/0.25f),Mathf.Clamp01((duration-t)/0.5f));
            if (t >= duration) showing=false;
        }
    }
}
