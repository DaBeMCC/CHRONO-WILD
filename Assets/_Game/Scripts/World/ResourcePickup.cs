using UnityEngine;

namespace ChronoWild.World
{
    /// <summary>
    /// Marks a world object as a collectable resource (wood, stone, fiber...). It only holds data:
    /// the interaction and inventory systems read it and decide what happens on pickup.
    /// </summary>
    [DisallowMultipleComponent]
    public class ResourcePickup : MonoBehaviour
    {
        [Tooltip("Inventory item id, e.g. \"wood\" or \"stone\".")]
        public string itemId = "stone";
        public string displayName = "Stone";
        [Min(1)] public int amount = 1;
        [Tooltip("Seconds until the pickup reappears after being collected. 0 = never.")]
        [Min(0f)] public float respawnSeconds = 0f;
        [Tooltip("Meters from which the player can interact with it.")]
        [Min(0.5f)] public float interactDistance = 2.5f;

        public bool IsAvailable => gameObject.activeSelf;

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, interactDistance);
        }
    }
}
