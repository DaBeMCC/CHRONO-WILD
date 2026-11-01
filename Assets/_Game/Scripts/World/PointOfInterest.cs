using UnityEngine;

namespace ChronoWild.World
{
    /// <summary>
    /// Point-of-interest marker in the gameplay scene. The generator creates them from
    /// IslandTerrainSettings and can read their positions back if they are moved by hand.
    /// </summary>
    public class PointOfInterest : MonoBehaviour
    {
        public string id;
        public string displayName;
        public PoiType type;
        public float radius = 5f;
        [TextArea] public string notes;

        public static Color ColorFor(PoiType type)
        {
            switch (type)
            {
                case PoiType.Spawn: return new Color(0.2f, 1f, 0.3f);
                case PoiType.Resource: return new Color(1f, 0.85f, 0.2f);
                case PoiType.BuildZone: return new Color(0.3f, 0.7f, 1f);
                case PoiType.Research: return new Color(0.6f, 1f, 0.6f);
                case PoiType.CreatureZone: return new Color(1f, 0.35f, 0.25f);
                case PoiType.Landmark: return new Color(0.85f, 0.85f, 0.85f);
                case PoiType.Viewpoint: return new Color(0.9f, 0.6f, 1f);
                case PoiType.Portal: return new Color(0.4f, 0.3f, 1f);
                default: return Color.white;
            }
        }

        void OnDrawGizmos()
        {
            var color = ColorFor(type);
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 6f);
#if UNITY_EDITOR
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 7f, string.IsNullOrEmpty(displayName) ? id : displayName);
#endif
        }
    }
}
