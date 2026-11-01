using System.Collections.Generic;
using ChronoWild.World;
using UnityEngine;

namespace ChronoWild.Editor.Terrain
{
    /// <summary>
    /// Genera el heightmap por capas:
    /// 1) forma de isla con costa deformada (domain warping moderado),
    /// 2) perfil costero: playa suave al sur, acantilados al noreste,
    /// 3) relieve interior: fBm de detalle + rasgos diseñados (monte, colinas, valle),
    /// 4) áreas planas, 5) suavizado, 6) caminos tallados sobre splines.
    /// Las alturas se expresan en metros respecto al nivel del mar, indexadas [z, x].
    /// </summary>
    public static class IslandHeightGenerator
    {
        public sealed class Result
        {
            public int resolution;
            public float size;
            public float[,] heights;
            public float[,] pathMask;
            public float[,] cliffMask;
            public float[,] beachMask;

            public float MetersPerSample => size / (resolution - 1);

            public float SampleHeight(float xMeters, float zMeters) => Bilinear(heights, resolution, size, xMeters, zMeters);
            public float SamplePath(float xMeters, float zMeters) => Bilinear(pathMask, resolution, size, xMeters, zMeters);
            public float SampleBeach(float xMeters, float zMeters) => Bilinear(beachMask, resolution, size, xMeters, zMeters);
            public float SampleCliff(float xMeters, float zMeters) => Bilinear(cliffMask, resolution, size, xMeters, zMeters);
        }

        public static Result Generate(IslandTerrainSettings s, int resolution)
        {
            var noise = new IslandNoise(s.seed);
            var r = new Result
            {
                resolution = resolution,
                size = s.terrainSize,
                heights = new float[resolution, resolution],
                pathMask = new float[resolution, resolution],
                cliffMask = new float[resolution, resolution],
                beachMask = new float[resolution, resolution],
            };

            float inv = 1f / (resolution - 1);
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    var p = new Vector2(x * inv, z * inv);
                    r.heights[z, x] = BaseHeight(s, noise, p, out float beachW, out float cliffW);
                    r.beachMask[z, x] = beachW;
                    r.cliffMask[z, x] = cliffW;
                }
            }

            ApplyFlatAreas(s, r);
            for (int i = 0; i < s.smoothingPasses; i++)
                Smooth(r.heights, resolution, r.cliffMask);
            foreach (var path in s.paths)
                CarvePath(path, r);

            float minH = -s.seaFloorDepth;
            float maxH = s.HeightAboveSeaMax;
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++)
                    r.heights[z, x] = Mathf.Clamp(r.heights[z, x], minH, maxH);
            return r;
        }

        static float BaseHeight(IslandTerrainSettings s, in IslandNoise n, Vector2 p, out float beachW, out float cliffW)
        {
            var center = new Vector2(0.5f, 0.5f);

            // Capa 1: forma de isla. La costa se deforma desplazando el punto de muestreo.
            float f = s.coastWarpFrequency;
            var warp = new Vector2(
                IslandNoise.Fbm(p * f + n.warpA, 3) - 0.5f,
                IslandNoise.Fbm(p * f + n.warpB, 3) - 0.5f) * (2f * s.coastWarpStrength);
            Vector2 q = p + warp;
            Vector2 fromCenter = q - center;
            float angle = Mathf.Atan2(fromCenter.y, fromCenter.x) * Mathf.Rad2Deg;
            beachW = IslandNoise.AngleWeight(angle, s.beachDirection, s.beachArc);
            float cliffSector = IslandNoise.AngleWeight(angle, s.cliffDirection, s.cliffArc);

            // Metros tierra adentro desde la línea de costa (negativo = mar).
            float inland = (s.islandRadius - fromCenter.magnitude) * s.terrainSize;
            // La máscara de acantilado solo cubre la franja costera del sector, no el interior.
            cliffW = cliffSector * (1f - IslandNoise.Smooth01((Mathf.Abs(inland) - s.cliffWidth * 1.5f) / 8f));

            if (inland < 0f)
            {
                float offshore = s.offshoreSlopeWidth * Mathf.Lerp(1f, 0.35f, cliffSector) * Mathf.Lerp(1f, 1.6f, beachW);
                float seabed = -s.seaFloorDepth * 0.9f * IslandNoise.Smooth01(-inland / offshore);
                // Bajo los acantilados el agua se vuelve profunda casi de inmediato.
                float cliffDrop = -s.seaFloorDepth * 0.5f * IslandNoise.Smooth01(-inland / 4f) * cliffSector;
                return Mathf.Min(seabed, cliffDrop);
            }

            // Capa 2: perfil costero.
            float beachRamp = s.beachHeight * IslandNoise.Smooth01(inland / Mathf.Max(1f, s.beachWidth));
            float normalRamp = s.inlandBaseHeight * 0.5f * IslandNoise.Smooth01(inland / 15f);
            float coastal = Mathf.Lerp(normalRamp, beachRamp, beachW);
            float cliff = s.cliffHeight * IslandNoise.Smooth01(inland / Mathf.Max(1f, s.cliffWidth));
            coastal = Mathf.Lerp(coastal, cliff, cliffSector);

            // Capa 3: relieve interior, atenuado cerca de la costa para conservar la playa.
            float inlandStart = Mathf.Lerp(10f, s.beachWidth, beachW);
            float inlandMask = IslandNoise.Smooth01((inland - inlandStart) / Mathf.Max(1f, s.inlandRampWidth));

            float detail = (IslandNoise.Fbm(p * s.detailFrequency + n.detail, s.detailOctaves) - 0.5f) * 2f * s.detailAmplitude;
            float relief = s.inlandBaseHeight + detail;
            foreach (var feature in s.reliefFeatures)
                relief += FeatureHeight(feature, p, n);

            float h = coastal + inlandMask * relief;
            // Evita lagunas interiores accidentales: tierra adentro siempre queda algo sobre el mar.
            h = Mathf.Max(h, 1.5f * IslandNoise.Smooth01(inland / 20f));
            return h;
        }

        static float FeatureHeight(ReliefFeature feature, Vector2 p, in IslandNoise n)
        {
            if (feature.radius <= 0f)
                return 0f;
            float t = 1f - (p - feature.center).magnitude / feature.radius;
            if (t <= 0f)
                return 0f;
            float bump = IslandNoise.Smooth01(t);
            if (feature.ridged > 0f)
            {
                float ridge = IslandNoise.Ridged(p * 7f + n.ridge, 4);
                // Las crestas pesan más hacia la cima para no ensuciar las faldas.
                bump *= Mathf.Lerp(1f, 0.55f + ridge, feature.ridged * bump);
            }
            return feature.height * bump;
        }

        static void ApplyFlatAreas(IslandTerrainSettings s, Result r)
        {
            float mps = r.MetersPerSample;
            foreach (var area in s.flatAreas)
            {
                float cx = area.center.x * r.size;
                float cz = area.center.y * r.size;
                float target = area.autoHeight ? r.SampleHeight(cx, cz) + area.height : area.height;
                float outer = area.radius + area.falloff;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((cx - outer) / mps));
                int x1 = Mathf.Min(r.resolution - 1, Mathf.CeilToInt((cx + outer) / mps));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((cz - outer) / mps));
                int z1 = Mathf.Min(r.resolution - 1, Mathf.CeilToInt((cz + outer) / mps));
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x * mps, z * mps), new Vector2(cx, cz));
                        float w = 1f - IslandNoise.Smooth01((d - area.radius) / Mathf.Max(0.01f, area.falloff));
                        r.heights[z, x] = Mathf.Lerp(r.heights[z, x], target, w);
                    }
                }
            }
        }

        /// <summary>Suavizado 3x3 selectivo: no toca los acantilados para que sigan siendo verticales.</summary>
        static void Smooth(float[,] h, int res, float[,] protect)
        {
            var copy = (float[,])h.Clone();
            for (int z = 1; z < res - 1; z++)
            {
                for (int x = 1; x < res - 1; x++)
                {
                    float sum = 0f;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            sum += copy[z + dz, x + dx];
                    h[z, x] = Mathf.Lerp(sum / 9f, copy[z, x], protect[z, x]);
                }
            }
        }

        public static List<Vector2> SamplePathMeters(PathDefinition path, float size, float spacing)
        {
            var result = new List<Vector2>();
            var pts = path.points;
            if (pts == null || pts.Count < 2)
                return result;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 p0 = pts[Mathf.Max(i - 1, 0)] * size;
                Vector2 p1 = pts[i] * size;
                Vector2 p2 = pts[i + 1] * size;
                Vector2 p3 = pts[Mathf.Min(i + 2, pts.Count - 1)] * size;
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / spacing));
                for (int k = 0; k < steps; k++)
                    result.Add(CatmullRom(p0, p1, p2, p3, k / (float)steps));
            }
            result.Add(pts[pts.Count - 1] * size);
            return result;
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        static void CarvePath(PathDefinition path, Result r)
        {
            const float spacing = 1f;
            var pts = SamplePathMeters(path, r.size, spacing);
            if (pts.Count < 2)
                return;

            // Alturas a lo largo del camino, suavizadas longitudinalmente para evitar escalones.
            var original = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++)
                original[i] = r.SampleHeight(pts[i].x, pts[i].y);
            int window = Mathf.Max(1, Mathf.RoundToInt(path.smoothingRadius / spacing));
            var ph = original;
            for (int pass = 0; pass < 3; pass++)
                ph = BoxBlur(ph, window);
            // Los extremos vuelven a la altura existente para empalmar con otros caminos y claros.
            int pin = Mathf.Min(window * 2, ph.Length / 2);
            for (int i = 0; i < pin; i++)
            {
                float w = IslandNoise.Smooth01(1f - i / (float)pin);
                ph[i] = Mathf.Lerp(ph[i], original[i], w);
                int j = ph.Length - 1 - i;
                ph[j] = Mathf.Lerp(ph[j], original[j], w);
            }
            for (int i = 0; i < ph.Length; i++)
                ph[i] = Mathf.Max(ph[i], 0.6f);

            int res = r.resolution;
            float mps = r.MetersPerSample;
            float half = path.width * 0.5f;
            float reach = half + path.falloff;
            var bestDist = new float[res, res];
            var bestHeight = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    bestDist[z, x] = float.MaxValue;

            int minX = res, maxX = 0, minZ = res, maxZ = 0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / mps));
                int x1 = Mathf.Min(res - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach) / mps));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach) / mps));
                int z1 = Mathf.Min(res - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + reach) / mps));
                minX = Mathf.Min(minX, x0); maxX = Mathf.Max(maxX, x1);
                minZ = Mathf.Min(minZ, z0); maxZ = Mathf.Max(maxZ, z1);
                Vector2 ab = b - a;
                float len2 = Mathf.Max(1e-5f, ab.sqrMagnitude);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        var c = new Vector2(x * mps, z * mps);
                        float t = Mathf.Clamp01(Vector2.Dot(c - a, ab) / len2);
                        float d = Vector2.Distance(c, a + ab * t);
                        if (d < bestDist[z, x])
                        {
                            bestDist[z, x] = d;
                            bestHeight[z, x] = Mathf.Lerp(ph[i], ph[i + 1], t);
                        }
                    }
                }
            }

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float d = bestDist[z, x];
                    if (d > reach)
                        continue;
                    float w = 1f - IslandNoise.Smooth01((d - half) / Mathf.Max(0.01f, path.falloff));
                    // Sin tallar dentro del mar: el camino termina en la orilla.
                    if (r.heights[z, x] < 0f)
                        continue;
                    r.heights[z, x] = Mathf.Lerp(r.heights[z, x], bestHeight[z, x] - path.carveDepth, w);
                    float mask = 1f - IslandNoise.Smooth01((d - half * 0.7f) / Mathf.Max(0.5f, half * 0.6f));
                    r.pathMask[z, x] = Mathf.Max(r.pathMask[z, x], mask);
                    r.cliffMask[z, x] *= 1f - w;
                }
            }
        }

        static float[] BoxBlur(float[] src, int radius)
        {
            var dst = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                int a = Mathf.Max(0, i - radius), b = Mathf.Min(src.Length - 1, i + radius);
                float sum = 0f;
                for (int k = a; k <= b; k++)
                    sum += src[k];
                dst[i] = sum / (b - a + 1);
            }
            return dst;
        }

        public static float Bilinear(float[,] map, int res, float size, float xMeters, float zMeters)
        {
            float fx = Mathf.Clamp(xMeters / size * (res - 1), 0f, res - 1.001f);
            float fz = Mathf.Clamp(zMeters / size * (res - 1), 0f, res - 1.001f);
            int x0 = (int)fx, z0 = (int)fz;
            float tx = fx - x0, tz = fz - z0;
            float a = Mathf.Lerp(map[z0, x0], map[z0, x0 + 1], tx);
            float b = Mathf.Lerp(map[z0 + 1, x0], map[z0 + 1, x0 + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }
    }
}
