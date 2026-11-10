using UnityEngine;
using UnityEngine.InputSystem;
using ChronoWild.Core;
using ChronoWild.Inventory;
namespace ChronoWild.Interaction
{
    public sealed class ItemInteractor : MonoBehaviour
    {
        public Camera viewCamera;
        public ChronoWild.Inventory.Inventory inventory;
        [Min(0.5f)] public float reach = 3f;
        Collectible nearby;
        void Update()
        {
            if (nearby) nearby.SetNearby(false);
            nearby = null;
            if (GameStateController.Instance && !GameStateController.Instance.CanControl) return;
            if (!viewCamera || !inventory) return;
            var hits = Physics.OverlapSphere(viewCamera.transform.position, reach);
            float closest = float.MaxValue;
            foreach (var hit in hits)
            {
                var c = hit.GetComponentInParent<Collectible>();
                if (!c || inventory.Has(c.item.id)) continue;
                float d = (c.transform.position - viewCamera.transform.position).sqrMagnitude;
                if (d < closest) { closest = d; nearby = c; }
            }
            if (nearby) nearby.SetNearby(true);
            if (Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame) return;
            var ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
            if (Physics.Raycast(ray, out var target, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                var c = target.collider.GetComponentInParent<Collectible>();
                if (c && inventory.TryCollect(c.item)) { c.SetNearby(false); Destroy(c.gameObject); }
            }
        }
        void OnDisable() { if (nearby) nearby.SetNearby(false); }
    }
}
