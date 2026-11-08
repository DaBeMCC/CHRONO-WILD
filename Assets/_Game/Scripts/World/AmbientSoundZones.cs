using UnityEngine;

namespace ChronoWild.World
{
    /// <summary>
    /// Blends looping ambience beds by where the listener stands, and now and then plays a one-shot
    /// call from a random direction. Beds are mixed in, never swapped, so crossing between biomes
    /// has no audible seam.
    /// </summary>
    public class AmbientSoundZones : MonoBehaviour
    {
        public enum Gate { Always, Day, Night, Raining, DryDay }

        [System.Serializable]
        public class Zone
        {
            public string name;
            public AudioClip bed;
            [Tooltip("When this zone is audible.")]
            public Gate gate = Gate.Always;
            [Range(0f, 1f)] public float volume = 0.6f;
            public Vector2 center;
            public float radius = 80f;
            public float falloff = 60f;
            [Tooltip("Plays everywhere; the area settings are ignored.")]
            public bool global;
            [Tooltip("Follows the real distance to the sea and comes from the sea's direction.")]
            public bool coastal;
            [Tooltip("Extra loudness while it rains (trees thrash, water stirs).")]
            public float rainBoost;
            [Tooltip("Muted around this point (useful for low ground that is not really shore).")]
            public Vector2 quietCenter;
            [Tooltip("Radius of the muted area (m); 0 disables.")]
            public float quietRadius;
            [Tooltip("Only above this height (m); -1000 disables.")]
            public float minHeight = -1000f;
            public AudioClip[] calls;
            public Vector2 callInterval = new Vector2(8f, 22f);
            [HideInInspector] public AudioSource source;
            [HideInInspector] public float nextCall;
        }

        public Transform listener;
        public Terrain terrain;
        public Zone[] zones = new Zone[0];
        [Tooltip("Distance to the sea (m) at which the surf is at full volume.")]
        public float surfFullDistance = 12f;
        [Tooltip("Distance to the sea (m) at which the surf can no longer be heard.")]
        public float surfSilentDistance = 75f;
        [Tooltip("Seconds taken to follow a change of zone.")]
        public float smoothing = 1.5f;

        AudioSource oneShot;
        const float CoastCell = 4f;
        float[,] coastDistance;
        int coastN;

        void Start()
        {
            BuildCoastField();
            foreach (var zone in zones)
            {
                zone.source = gameObject.AddComponent<AudioSource>();
                zone.source.clip = zone.bed;
                zone.source.loop = true;
                zone.source.playOnAwake = false;
                zone.source.spatialBlend = 0f;
                zone.source.volume = 0f;
                zone.source.time = Random.value * (zone.bed != null ? zone.bed.length * 0.9f : 0f);
                if (zone.bed != null)
                    zone.source.Play();
                zone.nextCall = Time.time + Random.Range(zone.callInterval.x, zone.callInterval.y);
            }
            var go = new GameObject("Distant Calls");
            go.transform.SetParent(transform, false);
            oneShot = go.AddComponent<AudioSource>();
            oneShot.spatialBlend = 1f;
            oneShot.minDistance = 30f;
            oneShot.maxDistance = 400f;
            oneShot.rolloffMode = AudioRolloffMode.Linear;
        }

        /// <summary>Distance from every point of the map to the nearest sea, by a two-pass sweep over a coarse grid.</summary>
        void BuildCoastField()
        {
            var data = terrain.terrainData;
            coastN = Mathf.CeilToInt(data.size.x / CoastCell) + 1;
            coastDistance = new float[coastN, coastN];
            float originY = terrain.transform.position.y;
            for (int z = 0; z < coastN; z++)
            for (int x = 0; x < coastN; x++)
            {
                float h = data.GetInterpolatedHeight(x / (coastN - 1f), z / (coastN - 1f)) + originY;
                coastDistance[z, x] = h < 0f ? 0f : 1e6f;
            }
            const float straight = CoastCell, diagonal = CoastCell * 1.414f;
            float At(int z, int x) => z < 0 || x < 0 || z >= coastN || x >= coastN ? 1e6f : coastDistance[z, x];
            for (int z = 0; z < coastN; z++)
            for (int x = 0; x < coastN; x++)
                coastDistance[z, x] = Mathf.Min(coastDistance[z, x], Mathf.Min(At(z, x - 1) + straight, Mathf.Min(At(z - 1, x) + straight, Mathf.Min(At(z - 1, x - 1) + diagonal, At(z - 1, x + 1) + diagonal))));
            for (int z = coastN - 1; z >= 0; z--)
            for (int x = coastN - 1; x >= 0; x--)
                coastDistance[z, x] = Mathf.Min(coastDistance[z, x], Mathf.Min(At(z, x + 1) + straight, Mathf.Min(At(z + 1, x) + straight, Mathf.Min(At(z + 1, x + 1) + diagonal, At(z + 1, x - 1) + diagonal))));
        }

        /// <summary>Metres to the nearest sea from a world position.</summary>
        public float DistanceToSea(Vector3 p)
        {
            Vector3 o = terrain.transform.position;
            int x = Mathf.Clamp(Mathf.RoundToInt((p.x - o.x) / CoastCell), 0, coastN - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((p.z - o.z) / CoastCell), 0, coastN - 1);
            return coastDistance[z, x];
        }

        /// <summary>Left/right balance so the surf comes from where the sea actually is.</summary>
        float SeaPan(Vector3 p)
        {
            float left = DistanceToSea(p - listener.right * 8f), right = DistanceToSea(p + listener.right * 8f);
            return Mathf.Clamp((left - right) / 16f, -1f, 1f) * 0.6f;
        }

        static float GateWeight(Gate gate)
        {
            var clock = DayNightCycle.Instance;
            var weather = WeatherSystem.Instance;
            float day = clock != null ? clock.Daylight : 1f;
            float rain = weather != null ? weather.Rain : 0f;
            switch (gate)
            {
                case Gate.Day: return day;
                case Gate.Night: return 1f - day;
                case Gate.Raining: return rain;
                case Gate.DryDay: return day * (1f - Mathf.Clamp01(rain * 3f));
                default: return 1f;
            }
        }

        float Weight(Zone zone, Vector3 p)
        {
            var weather = WeatherSystem.Instance;
            float rain = weather != null ? weather.Rain : 0f;
            return AreaWeight(zone, p) * GateWeight(zone.gate) * (1f + zone.rainBoost * rain);
        }

        float AreaWeight(Zone zone, Vector3 p)
        {
            if (zone.global)
                return 1f;
            float weight;
            if (zone.coastal)
            {
                // Height counts as distance too: from up the mountain the shore is far away.
                float d = DistanceToSea(p), up = Mathf.Max(p.y, 0f) * 2.5f;
                float t = 1f - Mathf.InverseLerp(surfFullDistance, surfSilentDistance, Mathf.Sqrt(d * d + up * up));
                weight = t * t; // loudness falls away quickly, so it is really gone inland
            }
            else
            {
                float d = Vector2.Distance(new Vector2(p.x, p.z), zone.center);
                weight = 1f - Mathf.InverseLerp(zone.radius, zone.radius + zone.falloff, d);
            }
            if (zone.quietRadius > 0f)
                weight *= Mathf.InverseLerp(zone.quietRadius, zone.quietRadius * 1.5f, Vector2.Distance(new Vector2(p.x, p.z), zone.quietCenter));
            if (zone.minHeight > -999f)
                weight *= Mathf.InverseLerp(zone.minHeight, zone.minHeight + 14f, p.y);
            return weight;
        }

        void Update()
        {
            if (listener == null)
                return;
            Vector3 p = listener.position;
            float blend = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(smoothing, 0.01f));
            foreach (var zone in zones)
            {
                float weight = Weight(zone, p);
                zone.source.volume = Mathf.Lerp(zone.source.volume, Mathf.Clamp01(weight * zone.volume), blend);
                if (zone.coastal)
                    zone.source.panStereo = Mathf.Lerp(zone.source.panStereo, SeaPan(p), blend);
                if (zone.calls == null || zone.calls.Length == 0 || Time.time < zone.nextCall)
                    continue;
                zone.nextCall = Time.time + Random.Range(zone.callInterval.x, zone.callInterval.y);
                if (weight < 0.25f)
                    continue;
                Vector2 around = Random.insideUnitCircle.normalized * Random.Range(35f, 120f);
                oneShot.transform.position = p + new Vector3(around.x, Random.Range(0f, 30f), around.y);
                oneShot.pitch = Random.Range(0.86f, 1.14f);
                oneShot.PlayOneShot(zone.calls[Random.Range(0, zone.calls.Length)], zone.volume);
            }
        }
    }
}
