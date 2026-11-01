using UnityEngine;

namespace ChronoWild.Editor.Terrain
{
    /// <summary>
    /// Ruido determinista a partir de la semilla. Mathf.PerlinNoise no acepta semilla, así que
    /// cada capa usa un desplazamiento propio derivado de System.Random(seed).
    /// </summary>
    public readonly struct IslandNoise
    {
        public readonly Vector2 warpA;
        public readonly Vector2 warpB;
        public readonly Vector2 detail;
        public readonly Vector2 ridge;
        public readonly Vector2 cluster;
        public readonly Vector2 moisture;
        public readonly Vector2 jitter;

        public IslandNoise(int seed)
        {
            var rng = new System.Random(seed);
            Vector2 Next() => new Vector2((float)rng.NextDouble() * 900f + 50f, (float)rng.NextDouble() * 900f + 50f);
            warpA = Next();
            warpB = Next();
            detail = Next();
            ridge = Next();
            cluster = Next();
            moisture = Next();
            jitter = Next();
        }

        public static float Fbm(Vector2 p, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(p.x, p.y) * amp;
                norm += amp;
                p *= lacunarity;
                amp *= gain;
            }
            return sum / norm;
        }

        /// <summary>Ruido "ridged" 0..1: crestas afiladas para montañas.</summary>
        public static float Ridged(Vector2 p, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Mathf.PerlinNoise(p.x, p.y) * 2f - 1f);
                sum += n * n * amp;
                norm += amp;
                p *= 2f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        public static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>1 dentro del arco angular, 0 fuera, con transición suave en el 40% exterior.</summary>
        public static float AngleWeight(float angleDeg, float centerDeg, float arcDeg)
        {
            float delta = Mathf.Abs(Mathf.DeltaAngle(angleDeg, centerDeg));
            float half = arcDeg * 0.5f;
            return 1f - Smooth01((delta - half * 0.6f) / (half * 0.4f));
        }
    }
}
