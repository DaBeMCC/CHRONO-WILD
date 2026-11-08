using UnityEngine;

namespace ChronoWild.Editor.World
{
    /// <summary>Deterministic, periodic 2D noise used to bake seamless textures and to shape terrain.</summary>
    public static class TileableNoise
    {
        public static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        public static float Hash01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777215f;

        static int Wrap(int v, int p) => p <= 0 ? v : ((v % p) + p) % p;
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>Gradient noise in [-1, 1]. With period > 0 it tiles every `period` cells.</summary>
        public static float Perlin(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float G(int ox, int oy)
            {
                float a = Hash01(Wrap(x0 + ox, period), Wrap(y0 + oy, period), seed) * Mathf.PI * 2f;
                return Mathf.Cos(a) * (fx - ox) + Mathf.Sin(a) * (fy - oy);
            }
            float u = Fade(fx), v = Fade(fy);
            return Mathf.Lerp(Mathf.Lerp(G(0, 0), G(1, 0), u), Mathf.Lerp(G(0, 1), G(1, 1), u), v) * 1.41f;
        }

        /// <summary>Gradient noise with separate periods per axis (for directional, tileable streaks).</summary>
        public static float PerlinXY(float x, float y, int periodX, int periodY, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float G(int ox, int oy)
            {
                float a = Hash01(Wrap(x0 + ox, periodX), Wrap(y0 + oy, periodY), seed) * Mathf.PI * 2f;
                return Mathf.Cos(a) * (fx - ox) + Mathf.Sin(a) * (fy - oy);
            }
            float u = Fade(fx), v = Fade(fy);
            return Mathf.Lerp(Mathf.Lerp(G(0, 0), G(1, 0), u), Mathf.Lerp(G(0, 1), G(1, 1), u), v) * 1.41f;
        }

        /// <summary>fBm in roughly [-1, 1]; tileable when uv is in [0,1) and `cells` is the base period.</summary>
        public static float Fbm(float u, float v, int cells, int octaves, int seed, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                int p = cells << i;
                sum += Perlin(u * p, v * p, p, seed + i * 31) * amp;
                norm += amp;
                amp *= gain;
            }
            return sum / norm;
        }

        /// <summary>Distances to the nearest (F1) and second nearest (F2) feature points, in cell units.</summary>
        public static Vector2 Worley(float u, float v, int cells, int seed, out float cellId)
        {
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float f1 = 9f, f2 = 9f;
            cellId = 0f;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gy = cy + dy;
                int wx = Wrap(gx, cells), wy = Wrap(gy, cells);
                float px = gx + Hash01(wx, wy, seed), py = gy + Hash01(wx, wy, seed + 1);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1)
                {
                    f2 = f1;
                    f1 = d;
                    cellId = Hash01(wx, wy, seed + 2);
                }
                else if (d < f2)
                {
                    f2 = d;
                }
            }
            return new Vector2(f1, f2);
        }

        /// <summary>Non-periodic world-space fBm in [-1, 1] for terrain shaping.</summary>
        public static float WorldFbm(float x, float z, float frequency, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f, f = frequency;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin(x * f, z * f, 0, seed + i * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                f *= 2.03f;
            }
            return sum / norm;
        }

        /// <summary>Ridged multifractal in [0, 1]: sharp crests, used for ridges and rock tors.</summary>
        public static float WorldRidged(float x, float z, float frequency, int octaves, int seed)
        {
            float sum = 0f, amp = 0.6f, f = frequency, weight = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Perlin(x * f, z * f, 0, seed + i * 23));
                n *= n * weight;
                weight = Mathf.Clamp01(n * 1.6f);
                sum += n * amp;
                norm += amp;
                amp *= 0.5f;
                f *= 2.1f;
            }
            return sum / norm;
        }
    }
}
