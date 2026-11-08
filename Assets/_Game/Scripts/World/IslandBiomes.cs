using UnityEngine;

namespace ChronoWild.World
{
    /// <summary>
    /// Publishes the island biome map and the global wind to shaders.
    /// Biome map channels: R ground wetness, G beach (animated wave run-up), B swamp water extent,
    /// A moor. It is baked by the island biome builder and covers the whole terrain.
    /// </summary>
    [ExecuteAlways]
    public class IslandBiomes : MonoBehaviour
    {
        public Texture2D biomeMap;
        [Tooltip("R volcanic rock, G mineral streaks, B ash.")]
        public Texture2D volcanoMap;
        public Terrain terrain;
        public float seaLevel = 0f;

        [Header("Wind")]
        [Tooltip("Direction the wind blows towards (x, z).")]
        public Vector2 windDirection = new Vector2(0.8f, 0.45f);
        [Tooltip("Speed (m/s) at which gust fronts travel across vegetation and water.")]
        public float gustSpeed = 6f;
        [Tooltip("Distance (m) between gust fronts.")]
        public float gustWavelength = 28f;

        static readonly int BiomeMapId = Shader.PropertyToID("_CW_BiomeMap");
        static readonly int VolcanoMapId = Shader.PropertyToID("_CW_VolcanoMap");
        static readonly int BiomeParamsId = Shader.PropertyToID("_CW_BiomeParams");
        static readonly int WindId = Shader.PropertyToID("_CW_Wind");

        void OnEnable() => Push();
        void OnValidate() => Push();
        void Update() => Push();

        void OnDisable()
        {
            Shader.SetGlobalVector(BiomeParamsId, Vector4.zero);
            Shader.SetGlobalVector(WindId, Vector4.zero);
        }

        public void Push()
        {
            Vector2 size = terrain != null && terrain.terrainData != null
                ? new Vector2(terrain.terrainData.size.x, terrain.terrainData.size.z)
                : new Vector2(600f, 600f);
            Vector3 origin = terrain != null ? terrain.transform.position : Vector3.zero;
            if (biomeMap != null)
            {
                Shader.SetGlobalTexture(BiomeMapId, biomeMap);
                if (volcanoMap != null)
                    Shader.SetGlobalTexture(VolcanoMapId, volcanoMap);
                // The map is sampled with world xz / size, so the terrain is assumed to start at x = z = 0.
                Shader.SetGlobalVector(BiomeParamsId, new Vector4(size.x, size.y, origin.y, seaLevel));
            }
            else
            {
                Shader.SetGlobalVector(BiomeParamsId, Vector4.zero);
            }
            Vector2 dir = windDirection.sqrMagnitude > 1e-4f ? windDirection.normalized : Vector2.right;
            Shader.SetGlobalVector(WindId, new Vector4(dir.x, dir.y, gustSpeed, Mathf.Max(gustWavelength, 1f)));
        }
    }
}
