using UnityEngine;
using ChronoWild.Core;
namespace ChronoWild.Interaction
{
    // The player's CharacterController enters the collectible trigger. No input or aiming is required.
    public sealed class ItemInteractor : MonoBehaviour
    {
        public Camera viewCamera;
        public ChronoWild.Inventory.Inventory inventory;
        [Min(0.5f)] public float reach = 3f;
        readonly System.Collections.Generic.HashSet<Collectible> highlighted = new System.Collections.Generic.HashSet<Collectible>();
        void Awake()
        {
            if (!viewCamera) viewCamera = GetComponentInChildren<Camera>();
            if (!inventory) inventory = GetComponent<ChronoWild.Inventory.Inventory>();
        }
        void Update()
        {
            ClearHighlights();
            if (!viewCamera) return;
            foreach (var hit in Physics.OverlapSphere(transform.position + Vector3.up * 0.9f, reach))
            {
                var item = hit.GetComponentInParent<Collectible>();
                if (item && item.item) { item.SetNearby(true); highlighted.Add(item); }
            }
        }
        void OnDisable() => ClearHighlights();
        void ClearHighlights() { foreach (var item in highlighted) if (item) item.SetNearby(false); highlighted.Clear(); }
        public bool TryCollect(Collectible target)
        {
            var state = GameStateController.Instance;
            if (state && (state.State == GameState.Dead || state.State == GameState.Paused)) return false;
            if (!target || !target.gameObject.activeInHierarchy || !inventory || !target.item) return false;
            // A trigger near a thin wall must not allow collection through the wall.
            var from = transform.position + Vector3.up * 0.9f;
            foreach (var hit in Physics.RaycastAll(from, target.transform.position - from, Vector3.Distance(from, target.transform.position), ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(transform) && !hit.collider.transform.IsChildOf(target.transform)) return false;
            if (!inventory.TryCollect(target.item)) return false;
            target.Consume(); return true;
        }
    }
}
