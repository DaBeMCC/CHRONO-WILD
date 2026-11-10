using System;
using System.Collections.Generic;
using UnityEngine;
namespace ChronoWild.Inventory
{
    public sealed class Inventory : MonoBehaviour
    {
        [SerializeField] ItemDefinition[] catalog;
        readonly HashSet<string> collected = new HashSet<string>();
        public event Action<ItemDefinition> ItemCollected;
        public IReadOnlyList<ItemDefinition> Catalog => catalog;
        public int Count => collected.Count;
        public bool Has(string id) => collected.Contains(id);
        public bool TryCollect(ItemDefinition item)
        {
            if (!item || string.IsNullOrEmpty(item.id) || collected.Count >= 15 || Has(item.id)) return false;
            bool inCatalog = false;
            foreach (var candidate in catalog) if (candidate == item) { inCatalog = true; break; }
            if (!inCatalog) return false;
            collected.Add(item.id); ItemCollected?.Invoke(item); return true;
        }
#if UNITY_EDITOR
        public void SetCatalog(ItemDefinition[] items) => catalog = items;
#endif
    }
}
