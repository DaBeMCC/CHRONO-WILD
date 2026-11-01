using System.Collections.Generic;
using ChronoWild.World;
using UnityEngine;
using Layer = ChronoWild.Editor.Terrain.PlaceholderAssetFactory.Layer;

namespace ChronoWild.Editor.Terrain
{
    /// <summary>
    /// Distributes surfaces (alphamaps) and placeholder vegetation over an already sculpted TerrainData.
    /// Surface rules: altitude, slope, beach mask (proximity to the south coast),
    /// moisture (noise + low areas) and path mask. Thresholds are perturbed with noise
    /// so transitions are not perfect contour lines.
    /// </summary>
    public static class IslandSurfacePainter
    {
        public static float[,,] BuildAlphamaps(IslandTerrainSettings s, TerrainData data, IslandHeightGenerator.Result gen)
        {
            var noise = new IslandNoise(s.seed);
            int res = data.alphamapResolution;
            int layers = PlaceholderAssetFactory.LayerCount;
            var maps = new float[res, res, layers];
            var w = new float[layers];
            float inv = 1f / (res - 1);

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = x * inv, v = z * inv;
                    float xm = u * s.terrainSize, zm = v * s.terrainSize;
                    float h = data.GetInterpolatedHeight(u, v) - s.seaFloorDepth;
                    float slope = data.GetSteepness(u, v);
                    var p = new Vector2(u, v);

                    float jitter = (Mathf.PerlinNoise(u * 45f + noise.jitter.x, v * 45f + noise.jitter.y) - 0.5f) * 2f;
                    float moisture = Mathf.PerlinNoise(u * 5f + noise.moisture.x, v * 5f + noise.moisture.y);
                    float beach = gen.SampleBeach(xm, zm);
                    float path = gen.SamplePath(xm, zm);
                    float forest = ForestDensity(s, noise, p);

                    float wet = 1f - IslandNoise.Smooth01((h - 0.3f + jitter * 0.25f) / 0.9f);
                    float sandTop = Mathf.Lerp(0.8f, s.beachHeight + 1.2f, beach);
                    float sand = (1f - IslandNoise.Smooth01((h - sandTop + jitter * 0.6f) / 1.2f)) * (1f - IslandNoise.Smooth01((slope - 18f) / 8f));
                    float rock = IslandNoise.Smooth01((slope - 30f + jitter * 5f) / 10f);
                    float cliff = IslandNoise.Smooth01((slope - 44f + jitter * 4f) / 8f);
                    float lowland = 1f - IslandNoise.Smooth01((h - 7f) / 4f);
                    float mud = IslandNoise.Smooth01((moisture - 0.62f) / 0.12f) * lowland * (1f - IslandNoise.Smooth01((slope - 10f) / 6f));
                    float leaf = IslandNoise.Smooth01((forest + jitter * 0.15f - 0.35f) / 0.3f);

                    for (int i = 0; i < layers; i++) w[i] = 0f;
                    w[(int)Layer.ForestSoil] = 1f - leaf;
                    w[(int)Layer.LeafLitter] = leaf;
                    Overlay(w, Layer.Mud, mud);
                    Overlay(w, Layer.PathDirt, path * (1f - sand));
                    Overlay(w, Layer.SandDry, sand);
                    Overlay(w, Layer.SandWet, wet * Mathf.Max(sand, 0.6f));
                    Overlay(w, Layer.Rock, rock * (1f - path));
                    Overlay(w, Layer.CliffRock, cliff * (1f - path));

                    float sum = 0f;
                    for (int i = 0; i < layers; i++) sum += w[i];
                    for (int i = 0; i < layers; i++) maps[z, x, i] = w[i] / Mathf.Max(1e-4f, sum);
                }
            }
            return maps;
        }

        /// <summary>Overlays a layer with a given coverage, scaling the rest down proportionally.</summary>
        static void Overlay(float[] w, Layer layer, float amount)
        {
            amount = Mathf.Clamp01(amount);
            for (int i = 0; i < w.Length; i++)
                w[i] *= 1f - amount;
            w[(int)layer] += amount;
        }

        /// <summary>Forest density 0..1: central mass with clusters and natural clearings.</summary>
        public static float ForestDensity(IslandTerrainSettings s, in IslandNoise n, Vector2 p)
        {
            float core = 1f - IslandNoise.Smooth01(((p - s.forestCenter).magnitude / Mathf.Max(0.01f, s.forestRadius) - 0.55f) / 0.45f);
            float cluster = IslandNoise.Smooth01((Mathf.PerlinNoise(p.x * s.clusterFrequency + n.cluster.x, p.y * s.clusterFrequency + n.cluster.y) - 0.32f) / 0.3f);
            return Mathf.Max(core, s.scatteredTreeDensity) * cluster;
        }

        public static TreeInstance[] PlaceTrees(IslandTerrainSettings s, TerrainData data, IslandHeightGenerator.Result gen, int prototypeCount)
        {
            var noise = new IslandNoise(s.seed);
            var rng = new System.Random(s.seed * 7919 + 17);
            var trees = new List<TreeInstance>();
            int cells = Mathf.FloorToInt(s.terrainSize / s.treeSpacing);

            for (int cz = 0; cz < cells && trees.Count < s.maxTrees; cz++)
            {
                for (int cx = 0; cx < cells && trees.Count < s.maxTrees; cx++)
                {
                    // Jittered grid: natural spread without gross overlaps.
                    float xm = (cx + 0.15f + 0.7f * (float)rng.NextDouble()) * s.treeSpacing;
                    float zm = (cz + 0.15f + 0.7f * (float)rng.NextDouble()) * s.treeSpacing;
                    float roll = (float)rng.NextDouble();
                    float speciesRoll = (float)rng.NextDouble();
                    float scaleRoll = (float)rng.NextDouble();
                    float rotRoll = (float)rng.NextDouble();

                    var p = new Vector2(xm / s.terrainSize, zm / s.terrainSize);
                    float density = ForestDensity(s, noise, p);
                    if (roll > density)
                        continue;

                    float h = data.GetInterpolatedHeight(p.x, p.y) - s.seaFloorDepth;
                    if (h < s.minTreeHeightAboveSea)
                        continue;
                    if (data.GetSteepness(p.x, p.y) > s.treeSlopeLimit)
                        continue;
                    if (gen.SamplePath(xm, zm) > 0.01f || NearPath(s, gen, xm, zm))
                        continue;
                    if (InClearing(s, xm, zm))
                        continue;

                    // Species: cycads on edges and low areas, conifers at altitude, ginkgos mixed in.
                    int proto;
                    if (h > 28f || speciesRoll < 0.45f) proto = 0;
                    else if (speciesRoll < 0.75f || density > 0.7f) proto = 1;
                    else proto = 2;
                    proto = Mathf.Min(proto, prototypeCount - 1);

                    float scale = Mathf.Lerp(s.treeScaleRange.x, s.treeScaleRange.y, scaleRoll);
                    trees.Add(new TreeInstance
                    {
                        position = new Vector3(p.x, 0f, p.y),
                        prototypeIndex = proto,
                        widthScale = scale,
                        heightScale = scale * Mathf.Lerp(0.9f, 1.1f, speciesRoll),
                        rotation = rotRoll * Mathf.PI * 2f,
                        color = Color.white,
                        lightmapColor = Color.white,
                    });
                }
            }
            return trees.ToArray();
        }

        static bool NearPath(IslandTerrainSettings s, IslandHeightGenerator.Result gen, float xm, float zm)
        {
            float c = s.pathClearance;
            return gen.SamplePath(xm + c, zm) > 0.01f || gen.SamplePath(xm - c, zm) > 0.01f
                || gen.SamplePath(xm, zm + c) > 0.01f || gen.SamplePath(xm, zm - c) > 0.01f;
        }

        static bool InClearing(IslandTerrainSettings s, float xm, float zm)
        {
            foreach (var area in s.flatAreas)
            {
                if (!area.treeClearing) continue;
                float d = Vector2.Distance(new Vector2(xm, zm), area.center * s.terrainSize);
                if (d < area.radius + 2f)
                    return true;
            }
            return false;
        }
    }
}
