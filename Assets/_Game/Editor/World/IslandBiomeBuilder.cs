using System;
using System.Collections.Generic;
using System.IO;
using ChronoWild.Rendering;
using ChronoWild.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using N = ChronoWild.Editor.World.TileableNoise;
using F = ChronoWild.Editor.World.BiomeAssetFactory;

namespace ChronoWild.Editor.World
{
    /// <summary>
    /// Fills the island around the existing jungle: terrain relief everywhere, a Jurassic heath
    /// (moor) plateau in the north-west with granite tors and peat bogs, a coastal swamp in the
    /// east with murky water, horsetail reeds and dead snags, open woodland elsewhere, and a
    /// dressed-up beach. The jungle block and the ocean are left untouched; every existing object
    /// is re-seated on the new ground.
    /// </summary>
    public static class IslandBiomeBuilder
    {
        // World layout (metres). The terrain starts at x = z = 0 and spans 600 x 600.
        static readonly Rect JungleBox = new Rect(190f, 145f, 215f, 210f);
        static readonly Vector2 MoorCenter = new Vector2(195f, 440f);
        static readonly Vector2 MoorRadii = new Vector2(115f, 80f);
        static readonly Vector2 SwampCenter = new Vector2(462f, 215f);
        static readonly Vector2 SwampRadii = new Vector2(54f, 74f);
        static readonly Vector2 VolcanoCenter = new Vector2(390f, 420f);
        // Scatter rectangles: objects are sampled inside their biome, not over the whole island.
        static readonly Rect MoorArea = new Rect(60f, 335f, 290f, 195f);
        static readonly Rect SwampArea = new Rect(395f, 125f, 125f, 180f);
        static readonly Rect VolcanoArea = new Rect(290f, 320f, 200f, 200f);
        public const float SwampWaterLevel = 1.4f;
        const int Seed = 2026;

        const string BiomeMapPath = F.Root + "/T_IslandBiomeMap.png";
        const string VolcanoMapPath = F.Root + "/T_VolcanoMap.png";
        // Untouched copy of the terrain before any biome work; every build starts from it, so the
        // builder can be re-run without stacking relief on relief.
        const string SourcePath = F.Root + "/Source/TD_IslandSource.asset";

        // UnityEngine.Terrain layer slots after the rebuild.
        const int LSoil = 0, LLitter = 1, LMud = 2, LMoss = 3, LSandDry = 4, LSandWet = 5, LRock = 6,
                  LMoor = 7, LPath = 8, LPeat = 9, LMuck = 10, LShell = 11;

        [MenuItem("CHRONO WILD/World/Build Island Biomes (Moor, Swamp, Beach, Fill)")]
        public static void Build()
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain == null)
                throw new InvalidOperationException("No active terrain.");
            var data = terrain.terrainData;
            BackupTerrain(data);

            F.EnsureFolders();
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(F.TextureDir + "/T_MoorFloor_Albedo.png") == null)
                F.BakeTextures();

            var heightsBefore = RecordObjectHeights(terrain);
            RestoreSource(terrain);
            SculptHeights(terrain);
            ReseatObjects(terrain, heightsBefore);
            PaintLayers(terrain);
            var biomeMap = BakeBiomeMap(terrain);
            var volcanoMap = BakeVolcanoMap(terrain);
            PaintDetails(terrain);
            PlaceObjects(terrain);
            BuildSwampWater();
            BuildBogPools(terrain);
            SetupAtmosphere(terrain, biomeMap, volcanoMap);

            terrain.Flush();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            EditorSceneManager.SaveScene(terrain.gameObject.scene);
        }

        static void BackupTerrain(TerrainData data)
        {
            string src = AssetDatabase.GetAssetPath(data);
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Backups");
            Directory.CreateDirectory(dir);
            string dst = Path.Combine(dir, Path.GetFileNameWithoutExtension(src) + "_preBiomes_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".asset");
            File.Copy(Path.Combine(Directory.GetCurrentDirectory(), src), dst, true);
            Debug.Log("CHRONO WILD: terrain backed up to " + dst);
        }

        static void RestoreSource(UnityEngine.Terrain terrain)
        {
            var data = terrain.terrainData;
            AssetDatabase.ImportAsset(SourcePath);
            var src = AssetDatabase.LoadAssetAtPath<TerrainData>(SourcePath);
            if (src == null)
            {
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(data), SourcePath);
                return; // first build: the live terrain is the source
            }
            int n = src.heightmapResolution;
            data.SetHeights(0, 0, src.GetHeights(0, 0, n, n));
            data.terrainLayers = src.terrainLayers;
            int a = src.alphamapResolution;
            data.SetAlphamaps(0, 0, src.GetAlphamaps(0, 0, a, a));
            data.detailPrototypes = src.detailPrototypes;
            int d = src.detailResolution;
            for (int l = 0; l < src.detailPrototypes.Length; l++)
                data.SetDetailLayer(0, 0, l, src.GetDetailLayer(0, 0, d, d, l));
        }

        // ------------------------------------------------------------------ Masks

        static float Smooth(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        static float EllipseDistance(float x, float z, Vector2 c, Vector2 r, float warp, int seed)
        {
            float dx = (x - c.x) / r.x, dz = (z - c.y) / r.y;
            return Mathf.Sqrt(dx * dx + dz * dz) + N.WorldFbm(x, z, 1f / 55f, 3, seed) * warp;
        }

        /// <summary>1 inside the jungle block, fading to 0 over `feather` metres outside it.</summary>
        static float Protect(float x, float z, float feather)
        {
            float dx = Mathf.Max(JungleBox.xMin - x, 0f, x - JungleBox.xMax);
            float dz = Mathf.Max(JungleBox.yMin - z, 0f, z - JungleBox.yMax);
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            return 1f - Smooth(0f, feather, d);
        }

        static float Volcano(float x, float z) => 1f - Smooth(55f, 95f, Vector2.Distance(new Vector2(x, z), VolcanoCenter));

        static float MoorMask(float x, float z)
        {
            float d = EllipseDistance(x, z, MoorCenter, MoorRadii, 0.22f, Seed + 1);
            return (1f - Smooth(0.5f, 1f, d)) * (1f - Protect(x, z, 18f)) * (1f - Volcano(x, z));
        }

        static float SwampMask(float x, float z, float inner = 0.6f)
        {
            float d = EllipseDistance(x, z, SwampCenter, SwampRadii, 0.2f, Seed + 2);
            return (1f - Smooth(inner, 1f, d)) * (1f - Protect(x, z, 12f));
        }

        static float SwampInner(float x, float z) => SwampMaskRaw(x, z, 0.42f, 0.8f);

        static float SwampMaskRaw(float x, float z, float a, float b)
        {
            float d = EllipseDistance(x, z, SwampCenter, SwampRadii, 0.2f, Seed + 2);
            return (1f - Smooth(a, b, d)) * (1f - Protect(x, z, 12f));
        }

        static List<Vector3> pools;

        /// <summary>Peat pool sites (x, z, radius), fixed by the seed so every rebuild digs the same ones.</summary>
        static List<Vector3> BogPools() => pools ?? (pools = new List<Vector3>());

        /// <summary>Picks pool sites where the plateau really forms: the original ground must be high enough all around.</summary>
        static void ChoosePools(System.Func<float, float, float> originalHeight)
        {
            var list = new List<Vector3>();
            var rng = new System.Random(Seed + 77);
            for (int attempt = 0; attempt < 12000 && list.Count < 14; attempt++)
            {
                float x = MoorArea.x + (float)rng.NextDouble() * MoorArea.width, z = MoorArea.y + (float)rng.NextDouble() * MoorArea.height;
                if (MoorMask(x, z) < 0.85f)
                    continue;
                float radius = 5f + (float)rng.NextDouble() * 6f;
                bool onPlateau = true;
                for (int a = 0; a < 8 && onPlateau; a++)
                {
                    float angle = a * Mathf.PI / 4f, far = radius * 2.4f;
                    onPlateau = MoorMask(x + Mathf.Cos(angle) * far, z + Mathf.Sin(angle) * far) > 0.85f && originalHeight(x + Mathf.Cos(angle) * far, z + Mathf.Sin(angle) * far) > 9f;
                }
                if (!onPlateau)
                    continue;
                bool crowded = false;
                foreach (var q in list)
                    if (Vector2.Distance(new Vector2(q.x, q.y), new Vector2(x, z)) < 26f) { crowded = true; break; }
                if (!crowded)
                    list.Add(new Vector3(x, z, radius));
            }
            pools = list;
        }

        static float PlateauHeight(float x, float z) => 31f + N.WorldFbm(x, z, 1f / 70f, 4, Seed + 13) * 5f;

        static float PoolBlend(float x, float z)
        {
            float best = 0f;
            foreach (var q in BogPools())
                best = Mathf.Max(best, 1f - Smooth(q.z, q.z * 1.5f, Vector2.Distance(new Vector2(x, z), new Vector2(q.x, q.y))));
            return best;
        }

        static float Tor(float x, float z) => Mathf.Pow(N.WorldRidged(x, z, 1f / 40f, 3, Seed + 5), 3f);
        static float BogHollow(float x, float z) => Smooth(0.6f, 0.78f, N.WorldFbm(x, z, 1f / 48f, 3, Seed + 6) * 0.5f + 0.5f);
        static float SwampChannel(float x, float z)
        {
            float ridge = 1f - Mathf.Abs(N.WorldFbm(x, z, 1f / 70f, 3, Seed + 7));
            float pool = Smooth(0.58f, 0.8f, N.WorldFbm(x, z, 1f / 26f, 3, Seed + 8) * 0.5f + 0.5f);
            return Mathf.Max(Smooth(0.82f, 0.95f, ridge), pool);
        }

        // ------------------------------------------------------------------ Heights

        static float OriginY(UnityEngine.Terrain t) => t.transform.position.y;

        static void SculptHeights(UnityEngine.Terrain terrain)
        {
            var data = terrain.terrainData;
            int n = data.heightmapResolution;
            float sizeY = data.size.y, originY = OriginY(terrain);
            var h = data.GetHeights(0, 0, n, n);
            var result = new float[n, n];
            float sizeX = data.size.x, sizeZ = data.size.z;
            // Chosen here because the sculpt below runs on worker threads.
            ChoosePools((x, z) => h[Mathf.Clamp(Mathf.RoundToInt(z / sizeZ * (n - 1)), 0, n - 1), Mathf.Clamp(Mathf.RoundToInt(x / sizeX * (n - 1)), 0, n - 1)] * sizeY + originY);
            System.Threading.Tasks.Parallel.For(0, n, j =>
            {
                for (int i = 0; i < n; i++)
                {
                    float x = i / (float)(n - 1) * sizeX, z = j / (float)(n - 1) * sizeZ;
                    float h0 = h[j, i] * sizeY + originY;
                    result[j, i] = (SculptAt(x, z, h0) - originY) / sizeY;
                }
            });
            data.SetHeights(0, 0, result);
        }

        static float SculptAt(float x, float z, float h0)
        {
            if (h0 < -1f)
                return h0; // sea floor stays as it is
            float protect = Protect(x, z, 24f);
            float volcano = Volcano(x, z);
            float land = Smooth(1f, 6f, h0);
            float swamp = SwampMask(x, z);
            float moor = MoorMask(x, z);

            // Rolling relief and long ridges everywhere outside the jungle, the swamp and the coast.
            float relief = N.WorldFbm(x, z, 1f / 85f, 5, Seed + 10) * 4.5f
                         + (N.WorldRidged(x, z, 1f / 150f, 4, Seed + 11) - 0.35f) * 7f;
            float h = h0 + relief * land * (1f - protect) * (1f - volcano * 0.6f) * (1f - swamp);

            // Beach backshore: low dunes with wind ripples between the berm and the vegetation.
            float dune = Smooth(1.2f, 2.5f, h0) * (1f - Smooth(4f, 7f, h0)) * (1f - protect * 0.5f) * (1f - swamp);
            h += (N.WorldFbm(x, z, 1f / 22f, 3, Seed + 12) * 0.5f + 0.5f) * 1.6f * dune;

            // Moor plateau: rolling top, granite tors, peat hollows, fading where the coast is low.
            if (moor > 0f)
            {
                float plateau = PlateauHeight(x, z) + Tor(x, z) * 4.5f - BogHollow(x, z) * 1.8f;
                foreach (var q in BogPools())
                {
                    float d = Vector2.Distance(new Vector2(x, z), new Vector2(q.x, q.y));
                    if (d > q.z * 3.2f)
                        continue;
                    // A level shelf around the pool holds the water; the bowl is dug inside it.
                    plateau = Mathf.Lerp(plateau, PlateauHeight(q.x, q.y) + 0.6f, 1f - Smooth(q.z * 2f, q.z * 3.2f, d));
                    plateau -= (1f - Smooth(q.z * 0.3f, q.z, d)) * 1.7f;
                }
                float w = moor * Smooth(0.5f, 9f, h0);
                h = Mathf.Lerp(h, Mathf.Max(h, plateau), w);
            }

            h = SculptVolcano(x, z, h);

            // Swamp: flat waterlogged ground just above the water, cut by channels and pools.
            if (swamp > 0f)
            {
                float hummock = N.WorldFbm(x, z, 1f / 13f, 3, Seed + 14) * 0.45f;
                float inner = SwampInner(x, z);
                float target = SwampWaterLevel + 0.45f + hummock - SwampChannel(x, z) * 1.25f * inner;
                h = Mathf.Lerp(h, target, swamp * Smooth(-0.5f, 2f, h0));
            }
            return h;
        }

        static float VolcanoRadius(float x, float z) => Vector2.Distance(new Vector2(x, z), VolcanoCenter);

        /// <summary>0..1 angle around the mountain, warped so gullies wander instead of radiating straight.</summary>
        static float VolcanoAngle(float x, float z)
        {
            float a = Mathf.Atan2(z - VolcanoCenter.y, x - VolcanoCenter.x) / (Mathf.PI * 2f) + 0.5f;
            return a + N.WorldFbm(x, z, 1f / 38f, 3, Seed + 40) * 0.035f;
        }

        /// <summary>Crest-shaped ridges running down the mountain; 1 on a ridge line, 0 in a gully.</summary>
        static float VolcanoRidge(float x, float z, float cells, int seed)
        {
            float n = N.PerlinXY(VolcanoAngle(x, z) * cells, VolcanoRadius(x, z) * 0.018f, Mathf.RoundToInt(cells), 0, seed);
            float ridge = 1f - Mathf.Abs(n);
            return ridge * ridge;
        }

        const float CraterRim = 106f;

        static float SculptVolcano(float x, float z, float h)
        {
            float r = VolcanoRadius(x, z);
            if (r > 125f || h < 3f)
                return h;
            float reach = 1f - Smooth(70f, 125f, r);
            float slope = Smooth(12f, 50f, h) * reach;
            // Lava-carved gullies at two scales, fading out at the very top and on the foot hills.
            float gullies = (VolcanoRidge(x, z, 34f, Seed + 41) - 0.55f) * 9f + (VolcanoRidge(x, z, 90f, Seed + 42) - 0.55f) * 2.6f;
            h += gullies * slope * (1f - Smooth(92f, 108f, h));
            // Broken surface, so the big slopes are never perfectly smooth.
            h += N.WorldFbm(x, z, 1f / 11f, 3, Seed + 43) * 1.5f * slope;
            // Old flows: terraced steps wrapping the middle of the cone.
            h += Mathf.Sin(r * 0.28f + N.WorldFbm(x, z, 1f / 45f, 3, Seed + 44) * 6f) * 1.1f * Smooth(25f, 45f, h) * (1f - Smooth(70f, 90f, h));
            // Truncated summit with a crater bowl.
            if (r < 38f)
            {
                float rim = CraterRim + N.WorldFbm(x, z, 1f / 16f, 3, Seed + 45) * 3f;
                h = Mathf.Lerp(h, Mathf.Min(h, rim), 1f - Smooth(20f, 38f, r));
                h -= (1f - Smooth(4f, 18f, r)) * 25f;
            }
            return h;
        }

        // ------------------------------------------------------------------ Existing objects

        static readonly HashSet<string> SkipRoots = new HashSet<string> { "Review Terrain", "16 Coastal Ocean", "Sun(Clone)", "Graphics Tier", "Volumetric Sky", "Island Biomes", "Jungle Probe Volume", "ProbeVolumePerSceneData", "Coast Exposure Volume", "Forest Atmosphere" };
        static readonly string[] BuiltRoots = { "20 Moor", "21 Swamp", "22 Island Woodland", "23 Beach Dressing", "24 Swamp Water", "25 Water Physics", "26 Ambience", "27 Volcano", "28 Bog Pools" };

        static IEnumerable<Transform> SeatableObjects()
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (SkipRoots.Contains(root.name) || Array.IndexOf(BuiltRoots, root.name) >= 0)
                    continue;
                foreach (var t in Leaves(root.transform))
                    yield return t;
            }
        }

        static IEnumerable<Transform> Leaves(Transform t)
        {
            if (t.GetComponent<LODGroup>() != null || t.GetComponent<MeshRenderer>() != null || t.childCount == 0 || t.GetComponent<CharacterController>() != null)
            {
                yield return t;
                yield break;
            }
            foreach (Transform c in t)
            foreach (var l in Leaves(c))
                yield return l;
        }

        static Dictionary<Transform, float> RecordObjectHeights(UnityEngine.Terrain terrain)
        {
            var map = new Dictionary<Transform, float>();
            foreach (var t in SeatableObjects())
                map[t] = terrain.SampleHeight(t.position);
            return map;
        }

        static void ReseatObjects(UnityEngine.Terrain terrain, Dictionary<Transform, float> before)
        {
            int moved = 0;
            foreach (var pair in before)
            {
                if (pair.Key == null) continue;
                float delta = terrain.SampleHeight(pair.Key.position) - pair.Value;
                if (Mathf.Abs(delta) < 0.005f) continue;
                Undo.RecordObject(pair.Key, "Reseat");
                pair.Key.position += Vector3.up * delta;
                moved++;
            }
            Debug.Log($"CHRONO WILD: re-seated {moved} existing objects on the new terrain.");
        }

        // ------------------------------------------------------------------ Surface helpers

        static float HeightAt(UnityEngine.Terrain t, float x, float z) => t.SampleHeight(new Vector3(x, 0f, z)) + OriginY(t);

        static float SlopeAt(UnityEngine.Terrain t, float x, float z) =>
            t.terrainData.GetSteepness(x / t.terrainData.size.x, z / t.terrainData.size.z);

        static Vector3 NormalAt(UnityEngine.Terrain t, float x, float z) =>
            t.terrainData.GetInterpolatedNormal(x / t.terrainData.size.x, z / t.terrainData.size.z);

        static float BeachMask(UnityEngine.Terrain t, float x, float z, float h, float slope)
        {
            float low = 1f - Smooth(2.2f, 4.2f, h);
            float above = Smooth(-1.2f, -0.3f, h);
            float gentle = 1f - Smooth(10f, 18f, slope);
            return low * above * gentle * (1f - SwampMask(x, z, 0.3f)) * (1f - MoorMask(x, z) * 0.8f) * (1f - Protect(x, z, 14f) * 0.85f);
        }

        // ------------------------------------------------------------------ Splat layers

        static void PaintLayers(UnityEngine.Terrain terrain)
        {
            var data = terrain.terrainData;
            var old = data.terrainLayers;
            var layers = new TerrainLayer[12];
            for (int i = 0; i < 7; i++) layers[i] = old[i];
            layers[LMoor] = F.Layer("MoorFloor", 2.6f, 0.15f);
            layers[LPath] = old[8];
            layers[LPeat] = F.Layer("PeatBog", 3.2f, 0.5f);
            layers[LMuck] = F.Layer("SwampMuck", 3f, 0.6f);
            layers[LShell] = F.Layer("ShellSand", 4f, 0.2f);
            // Sunlit dry sand blew out to white under the island's strong sun; tone it down a bit.
            layers[LSandDry].diffuseRemapMax = new Vector4(0.74f, 0.72f, 0.72f, 1f);
            layers[LShell].diffuseRemapMax = new Vector4(0.8f, 0.78f, 0.76f, 1f);
            EditorUtility.SetDirty(layers[LSandDry]);

            int res = data.alphamapResolution;
            var src = data.GetAlphamaps(0, 0, res, res);
            int srcLayers = src.GetLength(2);
            var dst = new float[res, res, 12];
            float originY = OriginY(terrain);
            for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float x = (i + 0.5f) / res * data.size.x, z = (j + 0.5f) / res * data.size.z;
                float h = terrain.SampleHeight(new Vector3(x, 0f, z)) + originY;
                float slope = data.GetSteepness(x / data.size.x, z / data.size.z);
                var orig = new float[12];
                // Old slot 7 (cliff rock, same texture as rock) folds into rock; path moves to 8.
                for (int l = 0; l < srcLayers && l < 9; l++)
                {
                    int slot = l == 7 ? LRock : l;
                    orig[slot] += src[j, i, l];
                }

                var w = BiomeWeights(terrain, x, z, h, slope, orig);
                float keep = Protect(x, z, 16f);
                if (h < -0.6f) keep = 1f;               // sea floor keeps its painting
                float pathKeep = Mathf.Clamp01(orig[LPath] * 2f);

                float sum = 0f;
                for (int l = 0; l < 12; l++)
                {
                    float v = Mathf.Lerp(w[l], orig[l], keep);
                    if (l == LPath) v = Mathf.Max(v, orig[l] * pathKeep);
                    dst[j, i, l] = v;
                    sum += v;
                }
                for (int l = 0; l < 12; l++)
                    dst[j, i, l] = sum > 0f ? dst[j, i, l] / sum : (l == LSoil ? 1f : 0f);
            }
            data.terrainLayers = layers;
            data.SetAlphamaps(0, 0, dst);
        }

        static float[] BiomeWeights(UnityEngine.Terrain terrain, float x, float z, float h, float slope, float[] orig)
        {
            var w = new float[12];
            float n1 = N.WorldFbm(x, z, 1f / 18f, 3, Seed + 20) * 0.5f + 0.5f;
            float n2 = N.WorldFbm(x, z, 1f / 7f, 2, Seed + 21) * 0.5f + 0.5f;
            // Rock on steep ground, plus broken outcrops on moderate slopes so hills are not bare.
            float rocky = Mathf.Max(Smooth(25f, 34f, slope), Smooth(15f, 24f, slope) * Smooth(0.55f, 0.75f, n2) * 0.8f);

            float moor = MoorMask(x, z);
            float swamp = SwampMask(x, z, 0.5f);
            float beach = BeachMask(terrain, x, z, h, slope);
            float fill = Mathf.Clamp01(1f - moor - swamp - beach);

            // Open woodland: soil and litter patches, moss in damp lows, rock on steep ground,
            // heath creeping in on the higher ground.
            // Jurassic open ground is a fern prairie: olive heath floor and litter dominate, bare soil
            // only shows in worn patches, moss in damp hollows.
            w[LSoil] += fill * Mathf.Lerp(0.22f, 0.04f, n1);
            w[LLitter] += fill * Mathf.Lerp(0.2f, 0.45f, n1);
            w[LMoor] += fill * (0.45f + 0.4f * Smooth(10f, 22f, h));
            w[LMoss] += fill * Smooth(0.5f, 0.8f, n2) * (0.5f - 0.3f * Smooth(6f, 16f, h));

            // Moor: heath floor, peat in hollows, granite on tors.
            float bog = Mathf.Max(BogHollow(x, z), PoolBlend(x, z));
            float tor = Smooth(0.25f, 0.5f, Tor(x, z));
            // Tors read as granite through the boulders placed on them; the ground between stays
            // heath with thin soil (litter), so the tan terrain rock never shows up here.
            w[LMoor] += moor * (1f - bog) * (1f - tor * 0.5f);
            w[LPeat] += moor * bog;
            w[LLitter] += moor * tor * 0.5f;
            w[LMoss] += moor * Smooth(0.7f, 0.9f, n2) * 0.25f;

            // Swamp: muck below and around the water, mud banks, moss on hummocks.
            float above = h - SwampWaterLevel;
            w[LMuck] += swamp * (1f - Smooth(0.15f, 0.6f, above));
            w[LMud] += swamp * Smooth(0.1f, 0.4f, above) * (1f - Smooth(0.7f, 1.4f, above)) * (0.5f + 0.5f * n1);
            w[LMoss] += swamp * Smooth(0.4f, 0.9f, above) * 0.9f;
            w[LLitter] += swamp * Smooth(0.6f, 1.5f, above) * 0.4f;

            // Beach: wet sand at the waterline, shell grit on the berm, dry sand on the dunes.
            w[LSandWet] += beach * (1f - Smooth(0.6f, 1.3f, h));
            w[LShell] += beach * Smooth(0.6f, 1.3f, h) * (1f - Smooth(1.8f, 2.8f, h)) * (0.6f + 0.4f * n2);
            w[LSandDry] += beach * Smooth(1.5f, 2.6f, h);
            w[LMoor] += beach * Smooth(3.5f, 5.5f, h) * 0.5f;

            // The mountain: bare rock above the tree line (recoloured as basalt by the terrain shader),
            // heath and litter on the foot hills.
            float cone = 1f - Smooth(65f, 118f, VolcanoRadius(x, z));
            if (cone > 0f)
            {
                float bare = Smooth(16f, 34f, h);
                for (int l = 0; l < 12; l++) w[l] *= 1f - cone;
                w[LRock] += cone * bare;
                w[LMoor] += cone * (1f - bare) * 0.65f;
                w[LLitter] += cone * (1f - bare) * 0.35f;
            }

            // Steep ground becomes rock (tan sandstone, matching the island's cliffs). On the moor the
            // escarpment stays heath and litter: its rock comes from the granite boulders instead.
            rocky *= 1f - moor * 0.85f;
            for (int l = 0; l < 12; l++) w[l] *= 1f - rocky;
            w[LRock] += rocky;
            w[LLitter] += moor * Smooth(25f, 40f, slope) * 0.6f;
            return w;
        }

        // ------------------------------------------------------------------ Biome map

        static Texture2D BakeBiomeMap(UnityEngine.Terrain terrain)
        {
            const int res = 512;
            var px = new Color32[res * res];
            var data = terrain.terrainData;
            for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float x = (i + 0.5f) / res * data.size.x, z = (j + 0.5f) / res * data.size.z;
                float h = HeightAt(terrain, x, z);
                float slope = SlopeAt(terrain, x, z);
                float moor = MoorMask(x, z);
                float swamp = SwampMask(x, z, 0.45f);
                float beach = BeachMask(terrain, x, z, h, slope);
                float wet = swamp * Mathf.Lerp(0.95f, 0.55f, Smooth(0.4f, 1.4f, h - SwampWaterLevel));
                wet = Mathf.Max(wet, moor * BogHollow(x, z) * 0.85f);
                wet = Mathf.Max(wet, (1f - Smooth(2f, 4.5f, h)) * Smooth(-0.5f, 0.5f, h) * 0.35f * (1f - beach));
                float water = SwampInner(x, z);
                px[i + j * res] = new Color(wet, beach, water, moor);
            }
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(BiomeMapPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(BiomeMapPath, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(BiomeMapPath);
            imp.sRGBTexture = false;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(BiomeMapPath);
        }

        static Texture2D BakeVolcanoMap(UnityEngine.Terrain terrain)
        {
            const int res = 512;
            var px = new Color32[res * res];
            var data = terrain.terrainData;
            for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float x = (i + 0.5f) / res * data.size.x, z = (j + 0.5f) / res * data.size.z;
                float r = VolcanoRadius(x, z);
                if (r > 125f)
                {
                    px[i + j * res] = new Color32(0, 0, 0, 255);
                    continue;
                }
                float h = HeightAt(terrain, x, z);
                float cone = (1f - Smooth(75f, 125f, r)) * Smooth(10f, 26f, h);
                float streaks = Smooth(0.5f, 0.9f, VolcanoRidge(x, z, 150f, Seed + 46)) * Smooth(0.4f, 0.75f, N.WorldFbm(x, z, 1f / 30f, 3, Seed + 47) * 0.5f + 0.5f);
                float ash = (1f - Smooth(0.15f, 0.55f, VolcanoRidge(x, z, 34f, Seed + 41))) * Smooth(0.35f, 0.6f, N.WorldFbm(x, z, 1f / 12f, 3, Seed + 48) * 0.5f + 0.5f);
                ash = Mathf.Max(ash, (1f - Smooth(22f, 50f, r)) * 0.55f);
                px[i + j * res] = new Color(cone, streaks, ash, 1f);
            }
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(VolcanoMapPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(VolcanoMapPath, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(VolcanoMapPath);
            imp.sRGBTexture = false;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(VolcanoMapPath);
        }

        // ------------------------------------------------------------------ Materials and prefabs

        static T Load<T>(string path) where T : UnityEngine.Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) throw new FileNotFoundException(path);
            return a;
        }

        const string Veg = "Assets/_Game/Art/Prefabs/Vegetation/";
        const string Geo = "Assets/_Game/Art/Prefabs/Geology/";

        static Material FindMaterial(GameObject prefab, string name)
        {
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m != null && m.name == name) return m;
            throw new FileNotFoundException("Material " + name + " in " + prefab.name);
        }

        class Palette
        {
            public Material dryFrond, swampHorsetail, granite, deadWood, swampLog, wrack, swampConiferBark, swampWater, gustFrond, basalt, charred, bone, bogWater;
            public GameObject grassMeadow, grassDry, grassSedge, grassTussock;
            public GameObject[] basaltColumns, carcasses, charredSnags;
            public GameObject[] snags;
            public Mesh wrackMesh;
        }

        static Palette palette;

        static Palette BuildPalette()
        {
            var p = new Palette();
            var fern = Load<GameObject>(Veg + "JurassicRealism/PF_JR_GroundFern_A.prefab");
            var frond = FindMaterial(fern, "M_JR_Frond");
            p.dryFrond = F.MaterialVariant(frond, "M_Frond_Dry", m =>
            {
                m.SetColor("_BaseColor", new Color(0.86f, 0.66f, 0.38f));
                m.SetFloat("_Age", 0.9f);
                m.SetFloat("_Variation", 0.45f);
                m.SetFloat("_GustStrength", 0.32f);
                m.SetFloat("_WindStrength", 0.07f);
            });
            p.gustFrond = F.MaterialVariant(frond, "M_Frond_Windy", m =>
            {
                m.SetFloat("_GustStrength", 0.25f);
                m.SetFloat("_Variation", 0.35f);
            });

            var tuftPrefab = Load<GameObject>(Veg + "JurassicRealism/PF_JR_GroundTuft_ClubMoss.prefab");
            var tuft = FindMaterial(tuftPrefab, "M_JR_GroundTuft");
            var groundShader = Shader.Find("CHRONO WILD/Vegetation/Ground Cover");
            Material Ground(string name, Color root, Color tip, Color dry, float amount) => F.MaterialVariant(new Material(groundShader), name, m =>
            {
                m.SetColor("_RootColor", root);
                m.SetColor("_TipColor", tip);
                m.SetColor("_DryColor", dry);
                m.SetFloat("_DryAmount", amount);
            });
            var meadow = Ground("M_Grass_Meadow", new Color(0.06f, 0.085f, 0.035f), new Color(0.2f, 0.29f, 0.095f), new Color(0.4f, 0.37f, 0.15f), 0.3f);
            var dry = Ground("M_Grass_Moor", new Color(0.09f, 0.08f, 0.04f), new Color(0.46f, 0.38f, 0.16f), new Color(0.3f, 0.29f, 0.11f), 0.5f);
            var sedge = Ground("M_Grass_Sedge", new Color(0.05f, 0.075f, 0.03f), new Color(0.2f, 0.3f, 0.09f), new Color(0.38f, 0.33f, 0.12f), 0.2f);
            GameObject Clump(string name, Material m, int seed, int blades, float height, float width, float spread, float lean)
                => MeshPrefab(name, F.SaveAsset(F.BladeClumpMesh("SM_" + name, seed, blades, height, width, spread, lean), F.MeshDir + "/SM_" + name + ".asset"), m, false);
            p.grassMeadow = Clump("Grass_Meadow", meadow, 21, 11, 0.42f, 0.034f, 0.34f, 0.5f);
            p.grassDry = Clump("Grass_Moor", dry, 22, 11, 0.36f, 0.028f, 0.34f, 0.65f);
            p.grassSedge = Clump("Grass_Sedge", sedge, 23, 9, 0.85f, 0.04f, 0.26f, 0.4f);
            p.grassTussock = Clump("Grass_Tussock", dry, 24, 26, 0.72f, 0.03f, 0.2f, 0.85f);

            var horsetail = Load<GameObject>(Veg + "JurassicRealism/PF_JR_Horsetail_A.prefab");
            p.swampHorsetail = FindMaterial(horsetail, "M_JR_Horsetail");

            var rock = Load<GameObject>(Geo + "PF_Rock1.prefab");
            var rockMat = rock.GetComponentInChildren<Renderer>().sharedMaterial;
            p.granite = F.MaterialVariant(rockMat, "M_Granite_Lichen", m =>
            {
                m.SetTexture("_BaseMap", Load<Texture2D>(F.TextureDir + "/T_Granite_Albedo.png"));
                m.SetColor("_BaseColor", Color.white);
                m.SetColor("_MossColor", new Color(0.36f, 0.38f, 0.26f));
                m.SetFloat("_Moss", 0.55f);
                m.SetFloat("_Wetness", 0.08f);
            });

            var buttress = Load<GameObject>(Veg + "HeroForest/PF_ButtressConifer.prefab");
            var bark = FindMaterial(buttress, "M_HeroBark");
            p.deadWood = F.MaterialVariant(bark, "M_DeadWood", m =>
            {
                m.SetColor("_BaseColor", new Color(0.78f, 0.74f, 0.68f));
                m.SetFloat("_Moss", 0.35f);
                m.SetFloat("_Wetness", 0.15f);
                m.SetFloat("_Triplanar", 1f);
                m.EnableKeyword("_TRIPLANAR");
            });
            p.swampLog = F.MaterialVariant(bark, "M_SwampLog", m =>
            {
                m.SetColor("_BaseColor", new Color(0.55f, 0.5f, 0.42f));
                m.SetFloat("_Moss", 0.75f);
                m.SetFloat("_Wetness", 0.7f);
                m.SetFloat("_Triplanar", 1f);
                m.EnableKeyword("_TRIPLANAR");
            });
            p.swampConiferBark = F.MaterialVariant(bark, "M_SwampBark", m =>
            {
                m.SetFloat("_Moss", 0.7f);
                m.SetFloat("_Wetness", 0.55f);
                m.SetColor("_BaseColor", new Color(0.8f, 0.78f, 0.72f));
            });

            p.wrack = F.MaterialVariant(tuft, "M_Wrack", m =>
            {
                m.SetTexture("_BaseMap", Load<Texture2D>(F.TextureDir + "/T_Wrack_Albedo.png"));
                m.SetTexture("_BumpMap", Load<Texture2D>(F.TextureDir + "/T_Wrack_Normal.png"));
                m.SetTextureScale("_BaseMap", Vector2.one);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Cutoff", 0.5f);
                m.SetFloat("_WindStrength", 0f);
                m.SetFloat("_GustStrength", 0f);
                m.SetFloat("_Transmission", 0.05f);
                m.SetFloat("_Smoothness", 0.45f);
                m.SetFloat("_Age", 0f);
                m.SetFloat("_Variation", 0.3f);
            });
            p.wrackMesh = F.SaveAsset(F.CardMesh("SM_WrackCard", 2.6f, 1.3f), F.MeshDir + "/SM_WrackCard.asset");

            var graniteSource = p.granite;
            p.basalt = F.MaterialVariant(graniteSource, "M_Basalt", m =>
            {
                m.SetColor("_BaseColor", new Color(0.42f, 0.39f, 0.39f));
                m.SetColor("_MossColor", new Color(0.3f, 0.17f, 0.08f));
                m.SetFloat("_Moss", 0.18f);
            });
            p.charred = F.MaterialVariant(p.deadWood, "M_CharredWood", m =>
            {
                m.SetColor("_BaseColor", new Color(0.16f, 0.14f, 0.13f));
                m.SetFloat("_Moss", 0f);
                m.SetFloat("_Wetness", 0f);
            });
            p.bone = F.MaterialVariant(new Material(Shader.Find("Universal Render Pipeline/Lit")), "M_Bone", m =>
            {
                m.SetColor("_BaseColor", new Color(0.72f, 0.66f, 0.52f));
                m.SetFloat("_Smoothness", 0.22f);
            });
            p.basaltColumns = new GameObject[3];
            for (int i = 0; i < 3; i++)
                p.basaltColumns[i] = MeshPrefab("PF_BasaltColumns_" + i, F.SaveAsset(F.BasaltColumnsMesh("SM_BasaltColumns_" + i, 40 + i, 16 + i * 10, 2.6f + i), F.MeshDir + $"/SM_BasaltColumns_{i}.asset"), p.basalt, true);
            p.carcasses = new GameObject[3];
            for (int i = 0; i < 3; i++)
                p.carcasses[i] = MeshPrefab("PF_Carcass_" + i, F.SaveAsset(F.CarcassMesh("SM_Carcass_" + i, 60 + i), F.MeshDir + $"/SM_Carcass_{i}.asset"), p.bone, true);

            p.swampWater = AssetDatabase.LoadAssetAtPath<Material>(F.MaterialDir + "/M_SwampWater.mat");
            if (p.swampWater == null)
            {
                p.swampWater = new Material(Shader.Find("CHRONO WILD/Water/Swamp Water")) { name = "M_SwampWater" };
                AssetDatabase.CreateAsset(p.swampWater, F.MaterialDir + "/M_SwampWater.mat");
            }
            // Pale, desaturated murk: sediment near the surface bounces light back.
            p.swampWater.SetVector("_Absorption", new Vector4(4f, 3f, 2.2f, 0f));
            p.swampWater.SetColor("_ScatterColor", new Color(0.3f, 0.31f, 0.19f));
            p.swampWater.SetColor("_FernColor", new Color(0.075f, 0.13f, 0.03f));
            p.swampWater.SetColor("_FernColorB", new Color(0.15f, 0.12f, 0.04f));
            p.swampWater.SetFloat("_FernCoverage", 0.33f);
            EditorUtility.SetDirty(p.swampWater);

            p.snags = new GameObject[6];
            for (int i = 0; i < p.snags.Length; i++)
                p.snags[i] = SnagPrefab(i, p.deadWood);
            p.bogWater = F.MaterialVariant(p.swampWater, "M_BogWater", m =>
            {
                m.SetFloat("_UseBiomeExtent", 0f);
                m.SetVector("_Absorption", new Vector4(7f, 8f, 9f, 0f));
                m.SetColor("_ScatterColor", new Color(0.07f, 0.05f, 0.03f));
                m.SetFloat("_FernCoverage", 0.08f);
                m.SetFloat("_ReflectionStrength", 0.95f);
            });
            p.charredSnags = new GameObject[p.snags.Length];
            for (int i = 0; i < p.snags.Length; i++)
                p.charredSnags[i] = SnagPrefab(i, p.charred, "PF_CharredSnag_");
            AssetDatabase.SaveAssets();
            return p;
        }

        static GameObject PrefabWithMaterial(GameObject source, string name, Material material)
        {
            string path = F.PrefabDir + "/" + name + ".prefab";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                r.sharedMaterials = mats;
            }
            instance.name = name;
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            UnityEngine.Object.DestroyImmediate(instance);
            return prefab;
        }

        static GameObject SnagPrefab(int index, Material wood, string prefix = "PF_Snag_")
        {
            string path = F.PrefabDir + "/" + prefix + index + ".prefab";
            var lod0 = F.SaveAsset(F.SnagMesh(500 + index * 7, 10, 12, $"SM_Snag_{index}_L0"), F.MeshDir + $"/SM_Snag_{index}_L0.asset");
            var lod1 = F.SaveAsset(F.SnagMesh(500 + index * 7, 5, 6, $"SM_Snag_{index}_L1"), F.MeshDir + $"/SM_Snag_{index}_L1.asset");
            var root = new GameObject($"PF_Snag_{index}");
            var renderers = new Renderer[2];
            var meshes = new[] { lod0, lod1 };
            for (int l = 0; l < 2; l++)
            {
                var go = new GameObject($"Snag_LOD{l}");
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[l];
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = wood;
                renderers[l] = mr;
            }
            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.12f, new[] { renderers[0] }), new LOD(0.015f, new[] { renderers[1] }) });
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ Detail layers

        static void PaintDetails(UnityEngine.Terrain terrain)
        {
            palette = BuildPalette();
            var data = terrain.terrainData;
            var protos = new List<DetailPrototype>(data.detailPrototypes);
            int AddGrass(GameObject prefab, float minW, float maxW, float minH, float maxH)
            {
                protos.Add(new DetailPrototype
                {
                    prototype = prefab, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit,
                    minWidth = minW, maxWidth = maxW, minHeight = minH, maxHeight = maxH,
                    noiseSpread = 0.3f, alignToGround = 0.85f, positionJitter = 1f, density = 1f
                });
                return protos.Count - 1;
            }
            int meadow = AddGrass(palette.grassMeadow, 0.9f, 1.5f, 0.75f, 1.45f);
            int dry = AddGrass(palette.grassDry, 0.9f, 1.5f, 0.7f, 1.3f);
            int sedge = AddGrass(palette.grassSedge, 0.9f, 1.4f, 0.7f, 1.4f);
            int tussock = AddGrass(palette.grassTussock, 0.8f, 1.5f, 0.8f, 1.5f);
            data.detailPrototypes = protos.ToArray();
            const int clubMoss = 0, mixed = 1, horsetail = 2;
            int res = data.detailResolution;
            var layers = new int[protos.Count][,];
            for (int l = 0; l < protos.Count; l++)
                layers[l] = l < 3 ? data.GetDetailLayer(0, 0, res, res, l) : new int[res, res];
            float sizeX = data.size.x, sizeZ = data.size.z;
            var rng = new System.Random(Seed);
            for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                float x = (i + 0.5f) / res * sizeX, z = (j + 0.5f) / res * sizeZ;
                if (Protect(x, z, 4f) > 0.5f) continue; // the jungle keeps its hand-tuned ground cover
                // The old spiky tufts are gone from everywhere outside the jungle.
                layers[clubMoss][j, i] = 0;
                layers[mixed][j, i] = 0;
                layers[horsetail][j, i] = 0;
                float h = HeightAt(terrain, x, z);
                if (h < 0.3f) continue;
                float slope = SlopeAt(terrain, x, z);
                if (slope > 36f) continue;
                float patch = N.WorldFbm(x, z, 1f / 9f, 3, Seed + 30) * 0.5f + 0.5f;
                float moor = MoorMask(x, z);
                float swamp = SwampMask(x, z, 0.5f);
                float beach = BeachMask(terrain, x, z, h, slope);
                float cone = (1f - Smooth(55f, 100f, VolcanoRadius(x, z))) * Smooth(18f, 34f, h);
                float fill = Mathf.Clamp01(1f - moor - swamp - beach) * (1f - cone);
                float r = (float)rng.NextDouble();
                float r2 = (float)rng.NextDouble();
                float sparse = Smooth(0.22f, 0.5f, patch);

                float above = h - SwampWaterLevel;
                float swampEdge = swamp * Smooth(-0.1f, 0.12f, above);
                float duneDensity = beach * Smooth(2.2f, 3.5f, h) * Smooth(0.45f, 0.75f, patch);
                float moorDensity = moor * (1f - BogHollow(x, z) * 0.55f) * (1f - Smooth(0.3f, 0.5f, Tor(x, z)) * 0.6f);

                layers[meadow][j, i] = Count(fill * sparse * 2.3f + swamp * Smooth(0.5f, 1.2f, above) * 1.2f, r);
                layers[dry][j, i] = Count(moorDensity * (1.4f + sparse * 1.8f) + duneDensity * 1.1f, r);
                layers[sedge][j, i] = Count(swampEdge * (1f - Smooth(0.6f, 1.3f, above)) * (0.7f + patch * 1.6f) + moor * BogHollow(x, z) * 1.2f, r);
                layers[tussock][j, i] = Count(moorDensity * Smooth(0.55f, 0.8f, patch) * 0.22f, r2);
                layers[horsetail][j, i] = Count(swampEdge * (1f - Smooth(0.5f, 1.1f, above)) * 0.35f, r2);
            }
            for (int l = 0; l < protos.Count; l++)
                data.SetDetailLayer(0, 0, l, layers[l]);
        }

        static int Count(float expected, float r)
        {
            int whole = Mathf.FloorToInt(expected);
            return whole + (r < expected - whole ? 1 : 0);
        }

        // ------------------------------------------------------------------ Object scattering

        delegate float Probability(float x, float z, float h, float slope);

        class ScatterSpec
        {
            public GameObject[] prefabs;
            public int count;
            public Probability probability;
            public float minDistance = 2f;
            public Vector2 scale = new Vector2(0.8f, 1.2f);
            public float align = 0f;          // 0 upright, 1 follow terrain normal
            public float sink = 0f;           // metres pushed into the ground per unit scale
            public float tilt = 0f;           // random lean in degrees
            public Material[] materials;      // optional override (all slots)
            public Func<Material, Material> remap; // optional per-slot remap
            public bool noShadows;
            public Rect area;                 // empty = the whole terrain
        }

        static readonly List<Vector3> placed = new List<Vector3>();

        static int Scatter(UnityEngine.Terrain terrain, Transform parent, ScatterSpec spec, System.Random rng)
        {
            var data = terrain.terrainData;
            int made = 0;
            var local = new List<Vector2>();
            for (int attempt = 0; attempt < spec.count * 40 && made < spec.count; attempt++)
            {
                var region = spec.area.width > 0f ? spec.area : new Rect(0f, 0f, data.size.x, data.size.z);
                float x = region.x + (float)rng.NextDouble() * region.width, z = region.y + (float)rng.NextDouble() * region.height;
                float h = HeightAt(terrain, x, z);
                float slope = SlopeAt(terrain, x, z);
                float p = spec.probability(x, z, h, slope);
                if (p <= 0f || rng.NextDouble() > p) continue;
                bool tooClose = false;
                foreach (var q in local)
                    if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < spec.minDistance * spec.minDistance) { tooClose = true; break; }
                if (tooClose) continue;
                local.Add(new Vector2(x, z));

                var prefab = spec.prefabs[rng.Next(spec.prefabs.Length)];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                float s = Mathf.Lerp(spec.scale.x, spec.scale.y, (float)rng.NextDouble());
                go.transform.localScale = prefab.transform.localScale * s;
                Vector3 up = Vector3.Slerp(Vector3.up, NormalAt(terrain, x, z), spec.align);
                var rot = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                if (spec.tilt > 0f)
                    rot = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 2f * spec.tilt, 0f, (float)(rng.NextDouble() - 0.5) * 2f * spec.tilt) * rot;
                go.transform.SetPositionAndRotation(new Vector3(x, h - spec.sink * s, z), rot);
                if (spec.materials != null || spec.remap != null)
                {
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++)
                            mats[i] = spec.materials != null ? spec.materials[Mathf.Min(i, spec.materials.Length - 1)] : spec.remap(mats[i]);
                        r.sharedMaterials = mats;
                    }
                }
                if (spec.noShadows)
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                made++;
            }
            return made;
        }

        static Transform FreshRoot(string name)
        {
            var existing = GameObject.Find(name);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
            return new GameObject(name).transform;
        }

        static GameObject[] Prefabs(string folder, params string[] names)
        {
            var list = new GameObject[names.Length];
            for (int i = 0; i < names.Length; i++) list[i] = Load<GameObject>(folder + names[i] + ".prefab");
            return list;
        }

        static GameObject MeshPrefab(string name, Mesh mesh, Material material, bool shadows)
        {
            string path = F.PrefabDir + "/" + name + ".prefab";
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject[] DriftwoodPrefabs(string name, Material material)
        {
            var result = new GameObject[3];
            string[] ids = { "A", "B", "C" };
            for (int i = 0; i < 3; i++)
            {
                var model = Load<GameObject>($"Assets/_Game/Art/OptimizedAssets/Environment/SM_Driftwood_{ids[i]}.fbx");
                var mesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
                result[i] = MeshPrefab($"PF_{name}_{ids[i]}", mesh, material, true);
            }
            return result;
        }

        static void PlaceObjects(UnityEngine.Terrain terrain)
        {
            var p = palette ?? BuildPalette();
            var rng = new System.Random(Seed + 99);
            var rocks = Prefabs(Geo, "PF_Rock1", "PF_Rock2", "PF_Rock3", "PF_Rock4", "PF_Rock5", "PF_Rock6");
            var cliffs = Prefabs(Geo, "PF_Cliff7", "PF_Cliff8");
            var groundFerns = Prefabs(Veg, "JurassicRealism/PF_JR_GroundFern_A", "JurassicRealism/PF_JR_GroundFern_B", "JurassicRealism/PF_JR_GroundFern_C");
            var cycads = Prefabs(Veg, "JurassicRealism/PF_JR_Cycad_A", "JurassicRealism/PF_JR_Cycad_B");
            var treeFerns = Prefabs(Veg, "JurassicRealism/PF_JR_TreeFern_A", "JurassicRealism/PF_JR_TreeFern_B", "JurassicRealism/PF_JR_TreeFern_C");
            var horsetails = Prefabs(Veg, "JurassicRealism/PF_JR_Horsetail_A", "JurassicRealism/PF_JR_Horsetail_B");
            var conifers = Prefabs(Veg, "JungleHigh/PF_ConiferHigh1", "JungleHigh/PF_ConiferHigh2", "JungleHigh/PF_ConiferHigh3", "DepthStudy/PF_ConiferDepth1", "DepthStudy/PF_ConiferDepth2", "DepthStudy/PF_ConiferDepth3");
            var tallConifers = Prefabs(Veg, "DepthStudy/PF_ConiferDepth1", "DepthStudy/PF_ConiferDepth2");
            var ginkgo = Prefabs(Veg, "Jungle/PF_Ginkgo_Jungle", "JurassicWetland/PF_GinkgoSapling1", "JurassicWetland/PF_GinkgoSapling2");
            var buttress = Prefabs(Veg, "HeroForest/PF_ButtressConifer");
            var swampLogs = DriftwoodPrefabs("SwampLog", p.swampLog);
            var beachDrift = DriftwoodPrefabs("BeachDrift", FindMaterial(Load<GameObject>("Assets/_Game/Art/Prefabs/Environment/Pickups/PF_Pickup_Driftwood_A.prefab"), "M_JR_Driftwood"));
            var wrack = new[] { MeshPrefab("PF_WrackLine", p.wrackMesh, p.wrack, false) };

            Material Dry(Material m) => m != null && m.name.StartsWith("M_JR_Frond") ? p.dryFrond : m;
            Material Windy(Material m) => m != null && m.name.StartsWith("M_JR_Frond") ? p.gustFrond : m;
            Material Granite(Material m) => m != null && m.shader.name.Contains("Geology") ? p.granite : m;
            Material SwampBark(Material m) => m != null && m.name == "M_HeroBark" ? p.swampConiferBark : m;

            // ---------------- Moor
            var moorRoot = FreshRoot("20 Moor");
            Scatter(terrain, moorRoot, new ScatterSpec { area = MoorArea,
                prefabs = cliffs, count = 26, minDistance = 14f, scale = new Vector2(0.9f, 1.7f), sink = 1.2f, tilt = 8f, remap = Granite,
                probability = (x, z, h, s) => MoorMask(x, z) * Smooth(0.35f, 0.6f, Tor(x, z))
            }, rng);
            Scatter(terrain, moorRoot, new ScatterSpec { area = MoorArea,
                prefabs = rocks, count = 760, minDistance = 2.6f, scale = new Vector2(0.6f, 3.2f), sink = 0.35f, tilt = 25f, align = 0.6f, remap = Granite,
                probability = (x, z, h, s) => MoorMask(x, z) * (0.06f + Smooth(0.15f, 0.45f, Tor(x, z)) + Smooth(18f, 32f, s) * 0.8f)
            }, rng);
            Scatter(terrain, moorRoot, new ScatterSpec { area = MoorArea,
                prefabs = groundFerns, count = 2300, minDistance = 1.5f, scale = new Vector2(0.55f, 1.1f), align = 0.7f, remap = Dry,
                probability = (x, z, h, s) => MoorMask(x, z) * (1f - BogHollow(x, z)) * (s < 30f ? 1f : 0f) * (N.WorldFbm(x, z, 1f / 25f, 2, 77) * 0.5f + 0.6f)
            }, rng);
            Scatter(terrain, moorRoot, new ScatterSpec { area = MoorArea,
                prefabs = cycads, count = 260, minDistance = 5f, scale = new Vector2(0.55f, 0.9f), align = 0.3f, remap = Dry,
                probability = (x, z, h, s) => MoorMask(x, z) * (1f - BogHollow(x, z)) * (s < 25f ? 0.6f : 0f)
            }, rng);
            Scatter(terrain, moorRoot, new ScatterSpec { area = MoorArea,
                // Lone, wind-stunted conifers leaning away from the prevailing wind.
                prefabs = tallConifers, count = 14, minDistance = 30f, scale = new Vector2(0.45f, 0.65f), tilt = 6f,
                probability = (x, z, h, s) => MoorMask(x, z) * (s < 20f ? 0.5f : 0f) * (1f - BogHollow(x, z))
            }, rng);

            Scatter(terrain, moorRoot, new ScatterSpec { area = MoorArea, prefabs = tallConifers, count = 26, minDistance = 16f, scale = new Vector2(0.28f, 0.42f), tilt = 9f,
                probability = (x, z, h, s) => MoorMask(x, z) * (s < 22f ? 0.7f : 0f) * (1f - BogHollow(x, z)) }, rng);
            Scatter(terrain, moorRoot, new ScatterSpec { area = MoorArea, prefabs = p.snags, count = 90, minDistance = 7f, scale = new Vector2(0.28f, 0.7f), sink = 0.2f, tilt = 16f,
                probability = (x, z, h, s) => MoorMask(x, z) * (s < 28f ? 0.8f : 0f) }, rng);
            Scatter(terrain, moorRoot, new ScatterSpec { area = MoorArea, prefabs = DriftwoodPrefabs("MoorLog", p.deadWood), count = 70, minDistance = 7f, scale = new Vector2(1.2f, 2.4f), sink = 0.08f, align = 1f,
                probability = (x, z, h, s) => MoorMask(x, z) * (s < 25f ? 0.8f : 0f) }, rng);
            Scatter(terrain, moorRoot, new ScatterSpec { area = MoorArea, prefabs = p.carcasses, count = 7, minDistance = 40f, scale = new Vector2(0.8f, 1.5f), sink = 0.25f, align = 1f,
                probability = (x, z, h, s) => MoorMask(x, z) * (s < 14f ? 0.8f : 0f) * (1f - BogHollow(x, z)) }, rng);

            // ---------------- Swamp
            var swampRoot = FreshRoot("21 Swamp");
            Probability hummock = (x, z, h, s) => SwampMask(x, z, 0.5f) * Smooth(0.15f, 0.45f, h - SwampWaterLevel);
            Probability edge = (x, z, h, s) => SwampMask(x, z, 0.5f) * Smooth(-0.35f, -0.05f, h - SwampWaterLevel) * (1f - Smooth(0.3f, 0.8f, h - SwampWaterLevel));
            Scatter(terrain, swampRoot, new ScatterSpec { area = SwampArea, prefabs = buttress, count = 46, minDistance = 8f, scale = new Vector2(0.7f, 1.05f), sink = 0.2f, remap = SwampBark, probability = hummock }, rng);
            Scatter(terrain, swampRoot, new ScatterSpec { area = SwampArea,
                prefabs = p.snags, count = 130, minDistance = 3.5f, scale = new Vector2(0.8f, 1.3f), sink = 0.3f, tilt = 9f,
                probability = (x, z, h, s) => SwampMask(x, z, 0.5f) * Smooth(-0.6f, 0f, h - SwampWaterLevel)
            }, rng);
            Scatter(terrain, swampRoot, new ScatterSpec { area = SwampArea, prefabs = treeFerns, count = 150, minDistance = 3.5f, scale = new Vector2(0.7f, 1.15f), remap = Windy, probability = hummock }, rng);
            Scatter(terrain, swampRoot, new ScatterSpec { area = SwampArea, prefabs = horsetails, count = 520, minDistance = 1.3f, scale = new Vector2(0.6f, 1.3f), sink = 0.1f, probability = edge }, rng);
            Scatter(terrain, swampRoot, new ScatterSpec { area = SwampArea,
                prefabs = swampLogs, count = 90, minDistance = 4.5f, scale = new Vector2(1.4f, 3.4f), sink = 0.12f, align = 1f,
                probability = (x, z, h, s) => SwampMask(x, z, 0.5f) * Smooth(-0.5f, 0f, h - SwampWaterLevel) * (1f - Smooth(0.4f, 0.9f, h - SwampWaterLevel))
            }, rng);
            Scatter(terrain, swampRoot, new ScatterSpec { area = SwampArea,
                prefabs = groundFerns, count = 520, minDistance = 1.6f, scale = new Vector2(0.7f, 1.2f), align = 0.6f, remap = Windy, probability = hummock
            }, rng);

            var roots = new[] { MeshPrefab("PF_SwampRoots", Load<Mesh>("Assets/_Game/Art/OptimizedAssets/Geology/SM_Exposed Root Network.asset"), p.swampLog, true) };
            Scatter(terrain, swampRoot, new ScatterSpec { area = SwampArea, prefabs = roots, count = 120, minDistance = 3.5f, scale = new Vector2(1f, 2.2f), sink = 0.04f, align = 1f,
                probability = (x, z, h, s) => SwampMask(x, z, 0.5f) * Smooth(-0.3f, 0.1f, h - SwampWaterLevel) * (1f - Smooth(0.6f, 1.2f, h - SwampWaterLevel)) }, rng);
            Scatter(terrain, swampRoot, new ScatterSpec { area = SwampArea, prefabs = p.carcasses, count = 5, minDistance = 25f, scale = new Vector2(1f, 1.7f), sink = 0.35f, align = 1f,
                probability = (x, z, h, s) => SwampMask(x, z, 0.5f) * Smooth(-0.2f, 0.2f, h - SwampWaterLevel) }, rng);
            Scatter(terrain, swampRoot, new ScatterSpec { area = SwampArea, prefabs = rocks, count = 120, minDistance = 3f, scale = new Vector2(0.3f, 1.3f), sink = 0.2f, tilt = 25f, align = 0.7f, remap = m => m != null && m.shader.name.Contains("Geology") ? p.swampLog : m,
                probability = (x, z, h, s) => SwampMask(x, z, 0.5f) * Smooth(-0.3f, 0.3f, h - SwampWaterLevel) }, rng);

            // ---------------- The mountain
            var volcanoRoot = FreshRoot("27 Volcano");
            Probability cone = (x, z, h, s) => (1f - Smooth(70f, 112f, VolcanoRadius(x, z))) * Smooth(14f, 26f, h) * (VolcanoRadius(x, z) > 24f ? 1f : 0f);
            Material Basalt(Material m) => m != null && m.shader.name.Contains("Geology") ? p.basalt : m;
            Scatter(terrain, volcanoRoot, new ScatterSpec { area = VolcanoArea, prefabs = p.basaltColumns, count = 18, minDistance = 22f, scale = new Vector2(0.55f, 1.05f), sink = 1.2f, tilt = 7f,
                probability = (x, z, h, s) => cone(x, z, h, s) * Smooth(0.55f, 0.85f, VolcanoRidge(x, z, 34f, Seed + 41)) }, rng);
            Scatter(terrain, volcanoRoot, new ScatterSpec { area = VolcanoArea, prefabs = cliffs, count = 22, minDistance = 20f, scale = new Vector2(0.7f, 1.4f), sink = 1.6f, tilt = 12f, remap = Basalt, probability = cone }, rng);
            Scatter(terrain, volcanoRoot, new ScatterSpec { area = VolcanoArea, prefabs = rocks, count = 800, minDistance = 2.2f, scale = new Vector2(0.15f, 1.5f), sink = 0.2f, tilt = 30f, align = 0.8f, remap = Basalt, probability = cone }, rng);
            Scatter(terrain, volcanoRoot, new ScatterSpec { area = VolcanoArea, prefabs = p.charredSnags, count = 80, minDistance = 7f, scale = new Vector2(0.5f, 1.15f), sink = 0.25f, tilt = 11f,
                probability = (x, z, h, s) => (1f - Smooth(80f, 118f, VolcanoRadius(x, z))) * Smooth(10f, 20f, h) * (1f - Smooth(42f, 62f, h)) }, rng);
            Scatter(terrain, volcanoRoot, new ScatterSpec { area = VolcanoArea, prefabs = cycads, count = 90, minDistance = 5f, scale = new Vector2(0.55f, 1f), remap = Dry,
                probability = (x, z, h, s) => (1f - Smooth(85f, 120f, VolcanoRadius(x, z))) * Smooth(8f, 15f, h) * (1f - Smooth(28f, 42f, h)) * (s < 32f ? 1f : 0f) }, rng);
            Scatter(terrain, volcanoRoot, new ScatterSpec { area = VolcanoArea, prefabs = p.carcasses, count = 4, minDistance = 30f, scale = new Vector2(1.1f, 1.8f), sink = 0.2f, align = 1f,
                probability = (x, z, h, s) => cone(x, z, h, s) * (s < 28f ? 1f : 0f) }, rng);

            // ---------------- Woodland fill and volcano flanks
            var fillRoot = FreshRoot("22 Island Woodland");
            Probability fill = (x, z, h, s) =>
            {
                if (h < 2.5f || Protect(x, z, 6f) > 0.2f) return 0f;
                float free = Mathf.Clamp01(1f - MoorMask(x, z) - SwampMask(x, z, 0.4f) * 1.5f);
                return free * (1f - Volcano(x, z) * 0.85f) * (s < 30f ? 1f : 0.15f);
            };
            Probability grove = (x, z, h, s) => fill(x, z, h, s) * Smooth(0.05f, 0.35f, N.WorldFbm(x, z, 1f / 60f, 3, 501));
            Scatter(terrain, fillRoot, new ScatterSpec { prefabs = conifers, count = 170, minDistance = 9f, scale = new Vector2(0.65f, 1.05f), probability = grove }, rng);
            Scatter(terrain, fillRoot, new ScatterSpec { prefabs = ginkgo, count = 40, minDistance = 9f, scale = new Vector2(0.8f, 1.2f), probability = grove }, rng);
            Scatter(terrain, fillRoot, new ScatterSpec { prefabs = cycads, count = 160, minDistance = 4f, scale = new Vector2(0.7f, 1.2f), remap = Windy, probability = fill }, rng);
            Scatter(terrain, fillRoot, new ScatterSpec
            {
                prefabs = treeFerns, count = 70, minDistance = 5f, scale = new Vector2(0.7f, 1.1f), remap = Windy,
                probability = (x, z, h, s) => grove(x, z, h, s) * (1f - Smooth(8f, 18f, h))
            }, rng);
            Scatter(terrain, fillRoot, new ScatterSpec { prefabs = groundFerns, count = 1700, minDistance = 1.4f, scale = new Vector2(0.6f, 1.15f), align = 0.7f, remap = Windy, probability = fill }, rng);
            Scatter(terrain, fillRoot, new ScatterSpec
            {
                prefabs = rocks, count = 260, minDistance = 4f, scale = new Vector2(0.5f, 2.4f), sink = 0.3f, tilt = 20f, align = 0.7f,
                probability = (x, z, h, s) => fill(x, z, h, s) * 0.25f * (0.4f + Smooth(15f, 35f, s))
            }, rng);
            Scatter(terrain, fillRoot, new ScatterSpec
            {
                prefabs = cliffs, count = 40, minDistance = 16f, scale = new Vector2(0.8f, 1.4f), sink = 1.5f, tilt = 6f,
                probability = (x, z, h, s) => (h > 2.5f && Protect(x, z, 6f) < 0.2f ? 1f : 0f) * Smooth(18f, 30f, s) * (1f - MoorMask(x, z))
            }, rng);

            // ---------------- Beach
            var beachRoot = FreshRoot("23 Beach Dressing");
            Probability beach = (x, z, h, s) => BeachMask(terrain, x, z, h, s);
            Scatter(terrain, beachRoot, new ScatterSpec { prefabs = p.carcasses, count = 3, minDistance = 60f, scale = new Vector2(1.2f, 1.8f), sink = 0.3f, align = 1f,
                probability = (x, z, h, s) => beach(x, z, h, s) * Smooth(1.3f, 2f, h) }, rng);
            Scatter(terrain, beachRoot, new ScatterSpec
            {
                // Wrack line: the debris band the last high tide left behind.
                prefabs = wrack, count = 420, minDistance = 1.4f, scale = new Vector2(1f, 1.9f), align = 1f, noShadows = true,
                probability = (x, z, h, s) => beach(x, z, h, s) * Smooth(0.55f, 0.85f, h) * (1f - Smooth(1.5f, 1.9f, h))
            }, rng);
            Scatter(terrain, beachRoot, new ScatterSpec
            {
                prefabs = rocks, count = 700, minDistance = 0.6f, scale = new Vector2(0.06f, 0.22f), sink = 0.02f, align = 1f, tilt = 30f,
                probability = (x, z, h, s) => beach(x, z, h, s) * Smooth(0.1f, 0.4f, h) * (1f - Smooth(0.9f, 1.4f, h)) * Smooth(0.2f, 0.6f, N.WorldFbm(x, z, 1f / 15f, 2, 601) * 0.5f + 0.5f)
            }, rng);
            Scatter(terrain, beachRoot, new ScatterSpec
            {
                prefabs = beachDrift, count = 28, minDistance = 6f, scale = new Vector2(0.9f, 2.2f), sink = 0.05f, align = 1f,
                probability = (x, z, h, s) => beach(x, z, h, s) * Smooth(1f, 1.5f, h) * (1f - Smooth(2.2f, 3f, h))
            }, rng);
            Scatter(terrain, beachRoot, new ScatterSpec
            {
                prefabs = groundFerns, count = 170, minDistance = 2f, scale = new Vector2(0.5f, 0.9f), align = 0.6f, remap = Dry,
                probability = (x, z, h, s) => beach(x, z, h, s) * Smooth(2.4f, 3.6f, h)
            }, rng);
            Scatter(terrain, beachRoot, new ScatterSpec
            {
                prefabs = cycads, count = 26, minDistance = 7f, scale = new Vector2(0.6f, 0.95f), remap = Windy,
                probability = (x, z, h, s) => beach(x, z, h, s) * Smooth(3.2f, 4.5f, h)
            }, rng);
            Scatter(terrain, beachRoot, new ScatterSpec
            {
                // Rocky points: outcrops where the shore steepens, with tide pools between them.
                prefabs = cliffs, count = 14, minDistance = 16f, scale = new Vector2(0.6f, 1.2f), sink = 1f, tilt = 12f,
                probability = (x, z, h, s) => h > -0.6f && h < 3f && Protect(x, z, 0f) < 0.5f ? Smooth(9f, 20f, s) * 0.8f : 0f
            }, rng);
            Scatter(terrain, beachRoot, new ScatterSpec
            {
                prefabs = rocks, count = 90, minDistance = 2.5f, scale = new Vector2(0.4f, 1.6f), sink = 0.25f, tilt = 25f, align = 0.8f,
                probability = (x, z, h, s) => h > -0.8f && h < 2.5f && Protect(x, z, 0f) < 0.5f ? Smooth(7f, 16f, s) * 0.9f : 0f
            }, rng);
        }

        // ------------------------------------------------------------------ Swamp water and atmosphere

        static void BuildSwampWater()
        {
            var p = palette ?? BuildPalette();
            var root = FreshRoot("24 Swamp Water");
            var size = SwampRadii * 2.6f;
            var mesh = F.SaveAsset(F.GridMesh("SM_SwampWaterGrid", size, 96), F.MeshDir + "/SM_SwampWaterGrid.asset");
            var go = new GameObject("Swamp Water Surface");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(SwampCenter.x, SwampWaterLevel, SwampCenter.y);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = p.swampWater;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Dark peat pools in the moor's hollows. A pool is only placed where its whole rim stays above the water.</summary>
        static void BuildBogPools(UnityEngine.Terrain terrain)
        {
            var p = palette ?? BuildPalette();
            var root = FreshRoot("28 Bog Pools");
            var mesh = F.SaveAsset(F.DiscMesh("SM_BogPool", 32), F.MeshDir + "/SM_BogPool.asset");
            var placed = new List<Vector2>();
            foreach (var q in BogPools())
            {
                float half = q.z * 1.2f;
                float centre = HeightAt(terrain, q.x, q.y);
                float rimLow = float.MaxValue;
                // A pool only exists where the rim all the way around stays above its surface.
                for (int a = 0; a < 24; a++)
                {
                    float angle = a * Mathf.PI / 12f;
                    rimLow = Mathf.Min(rimLow, HeightAt(terrain, q.x + Mathf.Cos(angle) * half * 1.03f, q.y + Mathf.Sin(angle) * half * 1.03f));
                }
                float level = rimLow - 0.08f;
                if (level < centre + 0.35f)
                    continue;
                placed.Add(new Vector2(q.x, q.y));
                var go = new GameObject("Bog Pool " + placed.Count);
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(q.x, level, q.y);
                go.transform.localScale = new Vector3(half, 1f, half);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = p.bogWater;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Debug.Log($"CHRONO WILD: placed {placed.Count} bog pools.");
        }

        static void SetupAtmosphere(UnityEngine.Terrain terrain, Texture2D biomeMap, Texture2D volcanoMap)
        {
            var biomes = UnityEngine.Object.FindFirstObjectByType<IslandBiomes>();
            if (biomes == null)
                biomes = new GameObject("Island Biomes").AddComponent<IslandBiomes>();
            biomes.terrain = terrain;
            biomes.biomeMap = biomeMap;
            biomes.volcanoMap = volcanoMap;
            biomes.seaLevel = 0f;
            biomes.Push();
            EditorUtility.SetDirty(biomes);

            var atmosphere = UnityEngine.Object.FindFirstObjectByType<JungleAtmosphere>();
            if (atmosphere != null)
            {
                atmosphere.extraBanks = new[]
                {
                    new JungleAtmosphere.FogBank { name = "Swamp ground mist", center = SwampCenter, radius = 55f, falloff = 40f, density = 0.028f, baseHeight = SwampWaterLevel + 0.4f, heightFalloff = 0.85f },
                    new JungleAtmosphere.FogBank { name = "Moor haze", center = MoorCenter, radius = 95f, falloff = 55f, density = 0.006f, baseHeight = 33f, heightFalloff = 0.06f },
                };
                atmosphere.Push();
                EditorUtility.SetDirty(atmosphere);
            }
        }
    }
}
