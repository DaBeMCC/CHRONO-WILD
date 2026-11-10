using ChronoWild.World;
using UnityEngine;

namespace ChronoWild.Story
{
    /// <summary>
    /// The meteor over the sea. It starts high above the horizon and sinks toward it a little every
    /// day while it grows, so the time left can be read straight from the sky.
    /// </summary>
    public class ApproachingMeteor : MonoBehaviour
    {
        public Transform follow;
        [Tooltip("Compass direction of the meteor (degrees, 180 = south, over the sea from the beach).")]
        public float azimuth = 190f;
        [Tooltip("Height above the horizon (degrees) on the first day and on impact day.")]
        public Vector2 elevation = new Vector2(34f, 4f);
        public float distance = 600f;
        [Tooltip("Size (m at that distance) on the first day and on impact day.")]
        public Vector2 size = new Vector2(130f, 380f);

        void LateUpdate()
        {
            var mission = MissionDirector.Instance;
            var clock = DayNightCycle.Instance;
            float total = mission != null ? Mathf.Max(mission.impactDays, 1) : 15f;
            float elapsed = clock != null ? clock.day - 1 + clock.hour / 24f : 0f;
            float t = Mathf.Clamp01(elapsed / total);
            float el = Mathf.Lerp(elevation.x, elevation.y, t) * Mathf.Deg2Rad, az = azimuth * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
            Vector3 origin = follow != null ? follow.position : Vector3.zero;
            transform.position = origin + dir * distance;
            transform.localScale = Vector3.one * Mathf.Lerp(size.x, size.y, t * t);
        }
    }
}
