using UnityEngine;
namespace ChronoWild.Inventory
{
    [CreateAssetMenu(menuName = "Chrono Wild/Item")]
    public sealed class ItemDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public string pickupName;
        [TextArea] public string[] survivalUses;
        [TextArea] public string hint;
        public Sprite icon;
        public GameObject prefab;
    }
}
