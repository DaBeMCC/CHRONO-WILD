using UnityEngine;

namespace ChronoWild.World
{
    /// <summary>
    /// Keeps a group of flying creatures wandering over an area: each follows its own looping path
    /// made of a few overlapping circles, so no two trace the same route and none ever lands.
    /// </summary>
    public class AmbientFlock : MonoBehaviour
    {
        public Mesh mesh;
        public Material material;
        public int count = 8;
        public Vector3 center;
        [Tooltip("Horizontal size of the area (m).")]
        public float radius = 90f;
        [Tooltip("Lowest and highest flight height above the center (m).")]
        public Vector2 height = new Vector2(20f, 60f);
        [Tooltip("Flight speed (m/s).")]
        public Vector2 speed = new Vector2(8f, 13f);
        public Vector2 scale = new Vector2(1f, 1.6f);
        [Tooltip("How sharply the creature banks into turns.")]
        public float banking = 30f;
        public int seed = 1;
        [Tooltip("Hides the creatures at night and in rain.")]
        public bool fairWeatherOnly = true;

        struct Agent
        {
            public Transform transform;
            public float phaseA, phaseB, rateA, rateB, radiusA, radiusB, heightPhase, heightRate;
        }

        Agent[] agents;
        MeshRenderer[] renderers;
        bool visible = true;

        void Start()
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            agents = new Agent[count];
            renderers = new MeshRenderer[count];
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Creature " + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderers[i] = renderer;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.transform.localScale = Vector3.one * Mathf.Lerp(scale.x, scale.y, R());
                float radiusA = radius * Mathf.Lerp(0.35f, 0.7f, R()), radiusB = radius * Mathf.Lerp(0.1f, 0.3f, R());
                float v = Mathf.Lerp(speed.x, speed.y, R());
                float rateA = v / radiusA * (R() < 0.5f ? 1f : -1f);
                agents[i] = new Agent
                {
                    transform = go.transform,
                    phaseA = R() * Mathf.PI * 2f,
                    phaseB = R() * Mathf.PI * 2f,
                    rateA = rateA,
                    rateB = rateA * Mathf.Lerp(1.7f, 2.9f, R()),
                    radiusA = radiusA,
                    radiusB = radiusB,
                    heightPhase = R() * Mathf.PI * 2f,
                    heightRate = Mathf.Lerp(0.08f, 0.2f, R())
                };
            }
        }

        Vector3 PositionOf(in Agent a, float t)
        {
            float ta = a.phaseA + a.rateA * t, tb = a.phaseB + a.rateB * t;
            float h = Mathf.Lerp(height.x, height.y, Mathf.Sin(a.heightPhase + a.heightRate * t) * 0.5f + 0.5f);
            return center + new Vector3(Mathf.Cos(ta) * a.radiusA + Mathf.Cos(tb) * a.radiusB, h, Mathf.Sin(ta) * a.radiusA + Mathf.Sin(tb) * a.radiusB);
        }

        void Update()
        {
            var clock = DayNightCycle.Instance;
            var weather = WeatherSystem.Instance;
            bool show = !fairWeatherOnly || ((clock == null || clock.Daylight > 0.3f) && (weather == null || weather.Rain < 0.25f));
            if (show != visible)
            {
                visible = show;
                foreach (var r in renderers)
                    r.enabled = show;
            }
            if (!visible)
                return;
            float t = Time.timeSinceLevelLoad;
            for (int i = 0; i < agents.Length; i++)
            {
                ref Agent a = ref agents[i];
                Vector3 now = PositionOf(a, t), next = PositionOf(a, t + 0.25f), after = PositionOf(a, t + 0.5f);
                Vector3 forward = (next - now).normalized;
                Vector3 turn = (after - next).normalized - forward;
                float roll = Mathf.Clamp(Vector3.Dot(turn, Vector3.Cross(Vector3.up, forward)) * banking * 8f, -55f, 55f);
                a.transform.SetPositionAndRotation(now, Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, -roll));
            }
        }
    }
}
