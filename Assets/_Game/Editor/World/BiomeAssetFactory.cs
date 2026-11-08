using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using N = ChronoWild.Editor.World.TileableNoise;

namespace ChronoWild.Editor.World
{
    /// <summary>
    /// Bakes the procedural textures, terrain layers, meshes and materials used by the moor, the
    /// swamp and the beach. Textures are modest (1K) on purpose: realism comes from the shaders
    /// (height blending, wetness, parallax, macro variation) rather than from texel density.
    /// </summary>
    public static class BiomeAssetFactory
    {
        public const string Root = "Assets/_Game/Art/Biomes";
        public const string TextureDir = Root + "/Textures";
        public const string LayerDir = Root + "/TerrainLayers";
        public const string MeshDir = Root + "/Meshes";
        public const string MaterialDir = Root + "/Materials";
        public const string PrefabDir = Root + "/Prefabs";

        const int Size = 1024;

        public static void EnsureFolders()
        {
            foreach (var path in new[] { Root, TextureDir, LayerDir, MeshDir, MaterialDir, PrefabDir })
            {
                if (AssetDatabase.IsValidFolder(path))
                    continue;
                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
        }

        // ------------------------------------------------------------------ Surface textures

        delegate void SurfaceSample(float u, float v, out float height, out Color albedo, out float smoothness, out float ao);

        /// <summary>Writes albedo, normal and mask (R metal, G AO, B height, A smoothness) PNGs.</summary>
        static void BakeSurface(string name, SurfaceSample sample, float normalStrength)
        {
            var height = new float[Size * Size];
            var albedo = new Color32[Size * Size];
            var mask = new Color32[Size * Size];
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
                    sample(u, v, out float h, out Color c, out float s, out float ao);
                    int i = x + y * Size;
                    height[i] = h;
                    albedo[i] = c;
                    mask[i] = new Color(0f, ao, Mathf.Clamp01(h), s);
                }
            });
            var normal = new Color32[Size * Size];
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    float l = height[((x - 1 + Size) % Size) + y * Size], r = height[((x + 1) % Size) + y * Size];
                    float d = height[x + ((y - 1 + Size) % Size) * Size], t = height[x + ((y + 1) % Size) * Size];
                    var n = new Vector3((l - r) * normalStrength, (d - t) * normalStrength, 1f).normalized;
                    normal[x + y * Size] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            });
            WritePng(TextureDir + $"/T_{name}_Albedo.png", albedo, Size, true, false);
            WritePng(TextureDir + $"/T_{name}_Normal.png", normal, Size, false, true);
            WritePng(TextureDir + $"/T_{name}_Mask.png", mask, Size, false, false);
        }

        public static void WritePng(string path, Color32[] pixels, int size, bool srgb, bool normalMap, bool alpha = false)
        {
            if (pixels.Length != size * size)
                throw new ArgumentException("Only square textures are supported: " + path);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, !srgb);
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = alpha;
            importer.wrapMode = alpha ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        static float Saturate(float v) => Mathf.Clamp01(v);
        static float Smooth(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        /// <summary>Dry Jurassic heath floor: club-moss cushions, fern litter, lichen and peat gaps.</summary>
        static void MoorFloor(float u, float v, out float h, out Color c, out float s, out float ao)
        {
            var w = N.Worley(u, v, 18, 11, out float id);
            float cushion = Saturate(1f - w.x * 1.15f);
            cushion = cushion * cushion * (3f - 2f * cushion);
            float fine = N.Fbm(u, v, 64, 3, 12) * 0.5f + 0.5f;
            // Fern litter: thin streaks along both axes, periodic so the texture tiles seamlessly.
            float warpU = N.Fbm(u, v, 4, 2, 19) * 0.04f, warpV = N.Fbm(u, v, 4, 2, 18) * 0.04f;
            float sa = N.PerlinXY((u + warpU) * 160f, (v + warpV) * 14f, 160, 14, 20) * 0.5f + 0.5f;
            float sb = N.PerlinXY((u + warpU) * 14f, (v + warpV) * 160f, 14, 160, 21) * 0.5f + 0.5f;
            float strands = Mathf.Max(Smooth(0.6f, 0.9f, sa), Smooth(0.62f, 0.92f, sb)) * 0.7f;
            var lw = N.Worley(u, v, 70, 13, out float lid);
            float lichen = Smooth(0.42f, 0.18f, lw.x) * Smooth(0.55f, 0.75f, N.Fbm(u, v, 6, 3, 14) * 0.5f + 0.5f);
            float gaps = Smooth(0.35f, 0.08f, cushion) * Smooth(0.45f, 0.7f, N.Fbm(u, v, 8, 3, 15) * 0.5f + 0.5f);

            var olive = Color.Lerp(new Color(0.24f, 0.25f, 0.12f), new Color(0.36f, 0.33f, 0.16f), id);
            var straw = new Color(0.47f, 0.37f, 0.2f);
            var rust = new Color(0.42f, 0.22f, 0.1f);
            var peat = new Color(0.12f, 0.085f, 0.06f);
            var lichenCol = new Color(0.58f, 0.6f, 0.5f);
            c = Color.Lerp(olive * (0.7f + 0.5f * fine), straw * (0.8f + 0.3f * fine), strands * 0.65f);
            c = Color.Lerp(c, rust, Smooth(0.6f, 0.9f, id) * strands * 0.5f);
            c = Color.Lerp(c, peat, gaps);
            c = Color.Lerp(c, lichenCol, lichen * cushion);
            h = cushion * 0.7f + fine * 0.2f + strands * 0.1f - gaps * 0.3f;
            s = 0.12f + gaps * 0.25f;
            ao = Mathf.Lerp(0.55f, 1f, Saturate(h * 1.3f));
        }

        /// <summary>Peat bog: dark wet peat with red/green sphagnum hummocks and standing water films.</summary>
        static void PeatBog(float u, float v, out float h, out Color c, out float s, out float ao)
        {
            // Domain-warped cells so the sphagnum hummocks merge into irregular lawns.
            float wu = u + N.Fbm(u, v, 5, 3, 34) * 0.06f, wv = v + N.Fbm(u, v, 5, 3, 35) * 0.06f;
            var w = N.Worley(wu, wv, 8, 31, out float id);
            float large = N.Fbm(u, v, 4, 4, 36) * 0.5f + 0.5f;
            float hummock = Saturate(1f - w.x * 0.95f) * 0.7f + large * 0.5f;
            float cover = Smooth(0.35f, 0.62f, hummock);
            float micro = N.Fbm(u, v, 96, 2, 32) * 0.5f + 0.5f;
            var mw = N.Worley(u, v, 160, 33, out float mid);
            float heads = Smooth(0.55f, 0.1f, mw.x); // sphagnum capitula speckle
            float hue = Smooth(0.3f, 0.75f, N.Fbm(u, v, 6, 3, 37) * 0.5f + 0.5f);
            var sphagnum = Color.Lerp(new Color(0.34f, 0.38f, 0.12f), new Color(0.42f, 0.16f, 0.09f), hue);
            sphagnum = Color.Lerp(sphagnum, new Color(0.5f, 0.42f, 0.2f), Smooth(0.7f, 0.95f, id) * 0.4f);
            var peat = new Color(0.12f, 0.09f, 0.065f) * (0.8f + 0.4f * micro);
            c = Color.Lerp(peat, sphagnum * (0.7f + 0.45f * heads), cover);
            h = hummock * 0.7f + heads * 0.15f * cover + micro * 0.1f;
            s = Mathf.Lerp(0.8f, 0.25f, cover);
            ao = Mathf.Lerp(0.6f, 1f, Saturate(h * 1.3f));
        }

        /// <summary>Swamp muck: soaked organic mud with green algae films and decayed leaves.</summary>
        static void SwampMuck(float u, float v, out float h, out Color c, out float s, out float ao)
        {
            float large = N.Fbm(u, v, 5, 4, 41) * 0.5f + 0.5f;
            float micro = N.Fbm(u, v, 80, 3, 42) * 0.5f + 0.5f;
            var lw = N.Worley(u, v, 46, 43, out float lid);
            float leaf = Smooth(0.3f, 0.12f, lw.x) * Smooth(0.72f, 0.95f, lid) * Smooth(0.4f, 0.65f, N.Fbm(u, v, 7, 2, 45) * 0.5f + 0.5f);
            float algae = Smooth(0.5f, 0.68f, N.Fbm(u, v, 9, 4, 44) * 0.5f + 0.5f);
            var mud = Color.Lerp(new Color(0.11f, 0.09f, 0.065f), new Color(0.2f, 0.16f, 0.11f), large);
            c = mud * (0.8f + 0.35f * micro);
            c = Color.Lerp(c, new Color(0.2f, 0.25f, 0.08f), algae * 0.8f);
            c = Color.Lerp(c, new Color(0.3f, 0.19f, 0.09f), leaf * 0.85f);
            h = large * 0.5f + micro * 0.3f + leaf * 0.2f;
            s = Mathf.Lerp(0.55f, 0.82f, algae) * (1f - leaf * 0.5f);
            ao = Mathf.Lerp(0.65f, 1f, h);
        }

        /// <summary>Shell grit sand for the upper beach: pale sand with shell fragments and dark heavy minerals.</summary>
        static void ShellSand(float u, float v, out float h, out Color c, out float s, out float ao)
        {
            float ripple = Mathf.Sin((u * 26f + N.Fbm(u, v, 4, 3, 51) * 1.4f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            float grain = N.Fbm(u, v, 128, 2, 52) * 0.5f + 0.5f;
            var sw = N.Worley(u, v, 90, 53, out float sid);
            float shell = Smooth(0.28f, 0.12f, sw.x) * Smooth(0.65f, 0.9f, sid);
            float mineral = Smooth(0.6f, 0.8f, N.Fbm(u, v, 40, 2, 54) * 0.5f + 0.5f) * (1f - ripple);
            var sand = Color.Lerp(new Color(0.66f, 0.58f, 0.44f), new Color(0.78f, 0.7f, 0.55f), grain);
            c = Color.Lerp(sand, new Color(0.3f, 0.27f, 0.24f), mineral * 0.45f);
            c = Color.Lerp(c, new Color(0.92f, 0.86f, 0.78f), shell);
            h = ripple * 0.55f + grain * 0.25f + shell * 0.2f;
            s = 0.18f + shell * 0.2f;
            ao = Mathf.Lerp(0.7f, 1f, h);
        }

        public static void BakeTextures()
        {
            EnsureFolders();
            BakeSurface("MoorFloor", MoorFloor, 9f);
            BakeSurface("PeatBog", PeatBog, 7f);
            BakeSurface("SwampMuck", SwampMuck, 5f);
            BakeSurface("ShellSand", ShellSand, 6f);
            BakeWrack();
            BakeGranite();
        }

        /// <summary>
        /// Lichen-crusted granite for the moor tors, derived from the existing rock photo so the
        /// rock keeps its real detail: luminance only, cool grey, mineral flecks and lichen.
        /// </summary>
        public static void BakeGranite()
        {
            var src = new Texture2D(2, 2);
            src.LoadImage(File.ReadAllBytes("Assets/_Game/Art/Textures/Geology/T_RockHigh_Albedo.jpg"));
            int w = src.width, h = src.height;
            var input = src.GetPixels32();
            UnityEngine.Object.DestroyImmediate(src);
            double mean = 0;
            foreach (var c in input) mean += (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) / 255f;
            float avg = (float)(mean / input.Length);
            var output = new Color32[w * h];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = x + y * w;
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    var c = input[i];
                    float l = (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) / 255f;
                    l = (l - avg) * 1.25f + 0.47f;
                    var col = new Color(l * 0.94f, l * 0.96f, l);
                    float fleck = N.Hash01(x, y, 61);
                    if (fleck < 0.05f) col *= 0.45f;
                    else if (fleck > 0.975f) col = Color.Lerp(col, new Color(0.86f, 0.83f, 0.79f), 0.6f);
                    float grain = N.Hash01(x, y, 62);
                    float lichen = Saturate((N.Fbm(u, v, 6, 5, 63) * 0.5f + 0.5f - 0.62f) * 7f) * Saturate(grain * 1.6f - 0.2f);
                    float orange = Saturate((N.Fbm(u, v, 5, 5, 64) * 0.5f + 0.5f - 0.7f) * 9f) * Saturate(grain * 1.4f - 0.3f);
                    col = Color.Lerp(col, new Color(0.58f, 0.61f, 0.5f), lichen * 0.7f);
                    col = Color.Lerp(col, new Color(0.69f, 0.44f, 0.2f), orange * 0.6f);
                    col.a = 1f;
                    output[i] = col;
                }
            });
            WritePng(TextureDir + "/T_Granite_Albedo.png", output, w, true, false);
        }

        /// <summary>Tide-line wrack: tangled seaweed strands, sea-grass and shell bits with an alpha cutout.</summary>
        static void BakeWrack()
        {
            const int S = 512;
            var px = new Color32[S * S];
            var height = new float[S * S];
            var rng = new System.Random(7);
            for (int strand = 0; strand < 260; strand++)
            {
                // Strands concentrate along the middle line, like a wrack line left by the tide.
                float x = (float)rng.NextDouble() * S, y = S * 0.5f + (float)(rng.NextDouble() - 0.5) * S * 0.55f * (float)rng.NextDouble();
                float angle = (float)(rng.NextDouble() * Math.PI * 2);
                int length = 30 + rng.Next(120);
                float width = 1.5f + (float)rng.NextDouble() * 4.5f;
                float tone = (float)rng.NextDouble();
                var col = tone < 0.45f ? new Color(0.17f, 0.15f, 0.06f) : tone < 0.8f ? new Color(0.27f, 0.22f, 0.09f) : new Color(0.36f, 0.33f, 0.2f);
                for (int i = 0; i < length; i++)
                {
                    angle += (float)(rng.NextDouble() - 0.5) * 0.35f;
                    x += Mathf.Cos(angle);
                    y += Mathf.Sin(angle) * 0.6f;
                    float w = width * (1f - i / (float)length * 0.6f);
                    for (int oy = -4; oy <= 4; oy++)
                    for (int ox = -4; ox <= 4; ox++)
                    {
                        float d = Mathf.Sqrt(ox * ox + oy * oy);
                        if (d > w) continue;
                        int ix = Mathf.FloorToInt(x) + ox, iy = Mathf.FloorToInt(y) + oy;
                        if (ix < 2 || iy < 2 || ix >= S - 2 || iy >= S - 2) continue;
                        int k = ix + iy * S;
                        float hh = 0.5f + 0.5f * (1f - d / w) + height[k] * 0.3f;
                        height[k] = Mathf.Min(1f, hh);
                        px[k] = col * (0.8f + 0.35f * (1f - d / w));
                        px[k].a = 255;
                    }
                }
            }
            for (int shell = 0; shell < 90; shell++)
            {
                int cx = rng.Next(20, S - 20), cy = S / 2 + rng.Next(-120, 120);
                float r = 2f + (float)rng.NextDouble() * 4f;
                var col = Color.Lerp(new Color(0.9f, 0.85f, 0.76f), new Color(0.75f, 0.6f, 0.55f), (float)rng.NextDouble());
                for (int oy = -6; oy <= 6; oy++)
                for (int ox = -6; ox <= 6; ox++)
                {
                    float d = Mathf.Sqrt(ox * ox + oy * oy * 1.6f);
                    if (d > r) continue;
                    int k = (cx + ox) + (cy + oy) * S;
                    px[k] = col * (0.85f + 0.2f * (1f - d / r));
                    px[k].a = 255;
                    height[k] = 0.8f + 0.2f * (1f - d / r);
                }
            }
            var normal = new Color32[S * S];
            for (int y = 1; y < S - 1; y++)
            for (int x = 1; x < S - 1; x++)
            {
                var n = new Vector3((height[x - 1 + y * S] - height[x + 1 + y * S]) * 4f, (height[x + (y - 1) * S] - height[x + (y + 1) * S]) * 4f, 1f).normalized;
                normal[x + y * S] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            WritePng(TextureDir + "/T_Wrack_Albedo.png", px, S, true, false, alpha: true);
            WritePng(TextureDir + "/T_Wrack_Normal.png", normal, S, false, true);
        }

        // ------------------------------------------------------------------ Terrain layers

        public static TerrainLayer Layer(string name, float tile, float smoothness = 0.3f)
        {
            string path = LayerDir + $"/TL_{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + $"/T_{name}_Albedo.png");
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + $"/T_{name}_Normal.png");
            layer.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureDir + $"/T_{name}_Mask.png");
            layer.tileSize = new Vector2(tile, tile);
            layer.normalScale = 1f;
            layer.smoothness = smoothness;
            layer.maskMapRemapMin = Vector4.zero;
            layer.maskMapRemapMax = Vector4.one;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        // ------------------------------------------------------------------ Meshes

        /// <summary>
        /// A dead, bleached snag: tapered, leaning trunk with a splintered top and a few broken limbs.
        /// Two LODs. Vertex alpha stores height for the bark shader's moss line.
        /// </summary>
        public static Mesh SnagMesh(int seed, int sides, int rings, string name)
        {
            var rng = new System.Random(seed);
            float height = 6f + (float)rng.NextDouble() * 8f;
            float baseRadius = 0.28f + (float)rng.NextDouble() * 0.25f;
            var lean = new Vector2((float)(rng.NextDouble() - 0.5) * 0.25f, (float)(rng.NextDouble() - 0.5) * 0.25f);
            var verts = new System.Collections.Generic.List<Vector3>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();

            void Tube(Func<float, Vector3> center, Func<float, float> radius, int ringCount, float splinter)
            {
                int start = verts.Count;
                for (int r = 0; r <= ringCount; r++)
                {
                    float t = r / (float)ringCount;
                    Vector3 c = center(t);
                    Vector3 forward = (center(Mathf.Min(1f, t + 0.02f)) - center(Mathf.Max(0f, t - 0.02f))).normalized;
                    Vector3 side = Vector3.Cross(forward, Mathf.Abs(forward.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                    Vector3 up = Vector3.Cross(side, forward);
                    for (int s = 0; s <= sides; s++)
                    {
                        float a = s / (float)sides * Mathf.PI * 2f;
                        float rad = radius(t) * (1f + 0.12f * Mathf.Sin(a * 3f + seed) + 0.06f * Mathf.Sin(a * 7f + r));
                        Vector3 dir = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                        Vector3 p = c + dir * rad;
                        // Splintered top: the last ring is jagged.
                        if (r == ringCount && splinter > 0f)
                            p += forward * (float)rng.NextDouble() * splinter;
                        verts.Add(p);
                        norms.Add(dir);
                        uvs.Add(new Vector2(s / (float)sides * 2f, p.y * 0.35f));
                    }
                }
                for (int r = 0; r < ringCount; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + r * (sides + 1) + s, b = a + sides + 1;
                    tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            }

            Vector3 Trunk(float t) => new Vector3(lean.x * t * t * height, t * height, lean.y * t * t * height);
            // Flared root base plus taper; the splintered top stops well before a living crown.
            Tube(Trunk, t => baseRadius * (1f - 0.75f * t) * (1f + 0.9f * Mathf.Pow(1f - Mathf.Clamp01(t * 6f), 2f)), rings, 0.6f);
            int limbs = 2 + rng.Next(4);
            for (int i = 0; i < limbs; i++)
            {
                float at = 0.35f + (float)rng.NextDouble() * 0.5f;
                Vector3 origin = Trunk(at);
                float yaw = (float)(rng.NextDouble() * Math.PI * 2);
                var dir = new Vector3(Mathf.Cos(yaw), 0.35f + (float)rng.NextDouble() * 0.5f, Mathf.Sin(yaw)).normalized;
                float len = 0.8f + (float)rng.NextDouble() * 2.2f;
                float r0 = baseRadius * (1f - 0.75f * at) * 0.45f;
                Tube(t => origin + dir * (t * len) + Vector3.down * (t * t * 0.25f), t => r0 * (1f - 0.7f * t), Mathf.Max(2, rings / 3), 0.15f);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A clump of real tapered blades. Vertex colour r runs root to tip and g is random per blade,
        /// which is what the ground cover shader builds its colour and motion from.
        /// </summary>
        public static Mesh BladeClumpMesh(string name, int seed, int blades, float height, float width, float spread, float lean)
        {
            const int segments = 3;
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var verts = new System.Collections.Generic.List<Vector3>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color>();
            var tris = new System.Collections.Generic.List<int>();
            for (int b = 0; b < blades; b++)
            {
                float angle = R() * Mathf.PI * 2f, ring = spread * Mathf.Sqrt(R());
                var root = new Vector3(Mathf.Cos(angle) * ring, 0f, Mathf.Sin(angle) * ring);
                float yaw = R() * Mathf.PI * 2f;
                var facing = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                var side = new Vector3(-facing.z, 0f, facing.x);
                float h = height * Mathf.Lerp(0.55f, 1.15f, R());
                float bend = lean * Mathf.Lerp(0.5f, 1.4f, R()) * h;
                float w = width * Mathf.Lerp(0.7f, 1.2f, R());
                var blade = new Color(0f, R(), 0f, 1f);
                int start = verts.Count;
                for (int i = 0; i <= segments; i++)
                {
                    // Vertices crowd toward the tip, where the curve needs them.
                    float t = 1f - Mathf.Pow(1f - i / (float)segments, 1.5f);
                    Vector3 c = root + Vector3.up * (h * t) + facing * (bend * t * t);
                    float halfWidth = w * 0.5f * (1f - t * t);
                    // Tilted outward so a flat blade reads as slightly rounded.
                    Vector3 n = (Vector3.up * 0.25f + facing).normalized;
                    var col = blade; col.r = t;
                    if (i == segments)
                    {
                        verts.Add(c); norms.Add(n); colors.Add(col);
                    }
                    else
                    {
                        verts.Add(c - side * halfWidth); norms.Add((n - side * 0.5f).normalized); colors.Add(col);
                        verts.Add(c + side * halfWidth); norms.Add((n + side * 0.5f).normalized); colors.Add(col);
                    }
                }
                for (int i = 0; i < segments - 1; i++)
                {
                    int s0 = start + i * 2;
                    tris.AddRange(new[] { s0, s0 + 2, s0 + 1, s0 + 1, s0 + 2, s0 + 3 });
                }
                int last = start + (segments - 1) * 2;
                tris.AddRange(new[] { last, last + 2, last + 1 });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Weathered carcass: curved spine, a rib cage and a long skull, half sunk in the ground.</summary>
        public static Mesh CarcassMesh(string name, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var builder = new TubeBuilder(6);
            float length = 5f + R() * 4f;
            Vector3 Spine(float t) => new Vector3(Mathf.Sin(t * 2.2f + seed) * 0.5f, 0.75f + Mathf.Sin(t * Mathf.PI) * 0.25f, (t - 0.5f) * length);
            builder.Tube(Spine, t => 0.09f * (1f - 0.55f * t), 16);
            int ribs = 9 + (int)(R() * 5f);
            for (int i = 0; i < ribs; i++)
            {
                float at = 0.14f + 0.46f * i / (ribs - 1f);
                Vector3 o = Spine(at);
                float size = Mathf.Sin(Mathf.PI * Mathf.Clamp01(i / (ribs - 1f) * 0.9f + 0.1f)) * (1f + R() * 0.2f);
                for (int side = -1; side <= 1; side += 2)
                {
                    if (R() < 0.12f)
                        continue; // some ribs were dragged away
                    float broken = R() < 0.25f ? 0.55f + R() * 0.3f : 1f;
                    int s = side;
                    builder.Tube(t =>
                    {
                        float a = t * broken * 2.35f;
                        return o + new Vector3(s * Mathf.Sin(a) * 0.95f * size, (Mathf.Cos(a) - 1f) * 0.55f * size, 0.35f * t * size);
                    }, t => 0.05f * (1f - 0.65f * t), 8);
                }
            }
            Vector3 head = Spine(0f);
            builder.Tube(t => head + new Vector3(0.25f * t, -0.08f - 0.3f * t, -1.3f * t), t => Mathf.Lerp(0.26f, 0.05f, t) * (1f + 0.35f * Mathf.Sin(t * 9f)), 10);
            builder.Tube(t => head + new Vector3(0.3f * t, -0.5f - 0.1f * t, -0.2f - 1.05f * t), t => Mathf.Lerp(0.07f, 0.03f, t), 6);
            return builder.Build(name);
        }

        /// <summary>Generic tube mesher for bones and similar props.</summary>
        class TubeBuilder
        {
            readonly int sides;
            readonly System.Collections.Generic.List<Vector3> verts = new System.Collections.Generic.List<Vector3>();
            readonly System.Collections.Generic.List<Vector3> norms = new System.Collections.Generic.List<Vector3>();
            readonly System.Collections.Generic.List<Vector2> uvs = new System.Collections.Generic.List<Vector2>();
            readonly System.Collections.Generic.List<int> tris = new System.Collections.Generic.List<int>();
            public TubeBuilder(int sides) { this.sides = sides; }
            public void Tube(Func<float, Vector3> center, Func<float, float> radius, int rings)
            {
                int start = verts.Count;
                for (int r = 0; r <= rings; r++)
                {
                    float t = r / (float)rings;
                    Vector3 c = center(t);
                    Vector3 f = (center(Mathf.Min(1f, t + 0.02f)) - center(Mathf.Max(0f, t - 0.02f))).normalized;
                    Vector3 side = Vector3.Cross(f, Mathf.Abs(f.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                    Vector3 up = Vector3.Cross(side, f);
                    for (int s = 0; s <= sides; s++)
                    {
                        float a = s / (float)sides * Mathf.PI * 2f;
                        Vector3 d = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                        verts.Add(c + d * radius(t)); norms.Add(d); uvs.Add(new Vector2(s / (float)sides, t));
                    }
                }
                for (int r = 0; r < rings; r++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + r * (sides + 1) + s, b = a + sides + 1;
                    tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            }
            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
                mesh.RecalculateTangents(); mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>Cluster of hexagonal basalt columns of uneven height, flat shaded.</summary>
        public static Mesh BasaltColumnsMesh(string name, int seed, int columns, float radius)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var verts = new System.Collections.Generic.List<Vector3>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
            const float cell = 0.62f;
            int placed = 0;
            for (int q = -8; q <= 8 && placed < columns; q++)
            for (int r = -8; r <= 8 && placed < columns; r++)
            {
                var c = new Vector2(cell * 1.5f * q, cell * 1.732f * (r + q * 0.5f));
                if (c.magnitude > radius || R() < 0.12f)
                    continue;
                placed++;
                float falloff = 1f - c.magnitude / (radius + 0.5f);
                float h = (1.2f + R() * 1.2f) * (0.5f + falloff * 3.4f);
                float size = cell * (0.86f + R() * 0.1f);
                var ring = new Vector3[6];
                float tilt = (R() - 0.5f) * 0.25f;
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f;
                    ring[i] = new Vector3(c.x + Mathf.Cos(a) * size, h + tilt * Mathf.Cos(a) * size, c.y + Mathf.Sin(a) * size);
                }
                for (int i = 0; i < 6; i++)
                {
                    Vector3 a = ring[i], b = ring[(i + 1) % 6];
                    Vector3 a0 = new Vector3(a.x, -1.5f, a.z), b0 = new Vector3(b.x, -1.5f, b.z);
                    Vector3 n = Vector3.Cross(b - a, a0 - a).normalized;
                    if (Vector3.Dot(n, new Vector3(a.x + b.x - 2f * c.x, 0f, a.z + b.z - 2f * c.y)) < 0f) n = -n;
                    int v = verts.Count;
                    verts.AddRange(new[] { a0, b0, b, a }); norms.AddRange(new[] { n, n, n, n });
                    uvs.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, h), new Vector2(0, h) });
                    tris.AddRange(new[] { v, v + 2, v + 1, v, v + 3, v + 2 });
                }
                int top = verts.Count;
                for (int i = 0; i < 6; i++) { verts.Add(ring[i]); norms.Add(Vector3.up); uvs.Add(new Vector2(ring[i].x, ring[i].z)); }
                for (int i = 1; i < 5; i++) tris.AddRange(new[] { top, top + i + 1, top + i });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flying reptile silhouette: body, head crest and two membrane wings. Colour r marks the wing span.</summary>
        public static Mesh FlyerMesh(string name)
        {
            var v = new[]
            {
                new Vector3(0, 0, 0.9f), new Vector3(0.1f, 0, 0.1f), new Vector3(-0.1f, 0, 0.1f), new Vector3(0, 0, -0.75f), // body
                new Vector3(0, 0.03f, 1.55f), new Vector3(0, 0.3f, 0.65f),                                                  // beak, crest
                new Vector3(0.55f, 0, 0.3f), new Vector3(1.25f, 0, 0.2f), new Vector3(2.1f, 0, -0.15f), new Vector3(1.1f, 0, -0.42f), new Vector3(0.4f, 0, -0.42f),
                new Vector3(-0.55f, 0, 0.3f), new Vector3(-1.25f, 0, 0.2f), new Vector3(-2.1f, 0, -0.15f), new Vector3(-1.1f, 0, -0.42f), new Vector3(-0.4f, 0, -0.42f)
            };
            var colors = new Color[v.Length];
            var normals = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) { colors[i] = new Color(Mathf.Clamp01(Mathf.Abs(v[i].x) / 2.1f), 0, 0, 1); normals[i] = Vector3.up; }
            var mesh = new Mesh { name = name, vertices = v, normals = normals, colors = colors };
            mesh.triangles = new[]
            {
                0,1,2, 1,3,2, 0,2,4, 0,4,1, 0,5,4, 0,4,5,
                1,6,10, 6,7,9, 6,9,10, 7,8,9, 1,10,3,
                2,15,11, 11,14,12, 11,15,14, 12,14,13, 2,3,15
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Tiling sheet of thin, broken rain streaks for the distant curtains.</summary>
        public static Texture2D BakeRainStreaks()
        {
            EnsureFolders();
            string path = TextureDir + "/T_RainStreaks.png";
            const int S = 512;
            var alpha = new float[S * S];
            var rng = new System.Random(19);
            for (int n = 0; n < 900; n++)
            {
                int x = rng.Next(S), y = rng.Next(S), length = 30 + rng.Next(120);
                float strength = 0.25f + (float)rng.NextDouble() * 0.75f;
                for (int i = 0; i < length; i++)
                {
                    float taper = Mathf.Sin(i / (float)length * Mathf.PI);
                    int index = x + ((y + i) % S) * S;
                    alpha[index] = Mathf.Max(alpha[index], taper * strength);
                }
            }
            var px = new Color32[S * S];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color(1f, 1f, 1f, alpha[i]);
            WritePng(path, px, S, true, false);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>One thin tapered drop, drawn stretched along its motion.</summary>
        public static Texture2D BakeRainDrop()
        {
            EnsureFolders();
            string path = TextureDir + "/T_RainDrop.png";
            const int S = 128;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S - 0.5f, v = (y + 0.5f) / S;
                float across = Mathf.Exp(-u * u * 900f);
                float along = Mathf.Sin(Mathf.Pow(v, 0.7f) * Mathf.PI);
                px[x + y * S] = new Color(1f, 1f, 1f, across * along * along);
            }
            WritePng(path, px, S, true, false, alpha: true);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Soft puff with an eroded edge for smoke, mist and motes.</summary>
        public static Texture2D BakeSmokeTexture()
        {
            EnsureFolders();
            string path = TextureDir + "/T_SoftPuff.png";
            const int S = 256;
            var px = new Color32[S * S];
            Parallel.For(0, S, y =>
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                    float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
                    float n = N.Fbm(u, v, 5, 5, 71) * 0.5f + 0.5f;
                    float a = Saturate(1f - d * (1.15f - n * 0.5f));
                    a = a * a * (3f - 2f * a) * Mathf.Lerp(0.55f, 1f, n);
                    px[x + y * S] = new Color(1f, 1f, 1f, a);
                }
            });
            WritePng(path, px, S, true, false, alpha: true);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Flat ground card that follows a gentle wave, for wrack lines and debris.</summary>
        public static Mesh CardMesh(string name, float width, float depth)
        {
            var mesh = new Mesh { name = name };
            const int segments = 6;
            var verts = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var norms = new Vector3[verts.Length];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float x = (t - 0.5f) * width;
                float y = 0.02f + Mathf.Sin(t * Mathf.PI * 3f) * 0.015f;
                verts[i * 2] = new Vector3(x, y, -depth * 0.5f);
                verts[i * 2 + 1] = new Vector3(x, y, depth * 0.5f);
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
                norms[i * 2] = norms[i * 2 + 1] = Vector3.up;
            }
            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                tris[i * 6] = a; tris[i * 6 + 1] = a + 1; tris[i * 6 + 2] = a + 2;
                tris[i * 6 + 3] = a + 2; tris[i * 6 + 4] = a + 1; tris[i * 6 + 5] = a + 3;
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = norms;
            mesh.triangles = tris;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Unit disc in XZ for small round water surfaces.</summary>
        public static Mesh DiscMesh(string name, int segments)
        {
            var verts = new Vector3[segments + 1];
            var colors = new Color[segments + 1];
            var tris = new int[segments * 3];
            verts[0] = Vector3.zero;
            colors[0] = Color.white;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                colors[i + 1] = Color.white;
                tris[i * 3] = 0; tris[i * 3 + 1] = i == segments - 1 ? 1 : i + 2; tris[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = name, vertices = verts, colors = colors, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Regular grid in XZ, used for the swamp water surface.</summary>
        public static Mesh GridMesh(string name, Vector2 size, int cells)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            int n = cells + 1;
            var verts = new Vector3[n * n];
            var colors = new Color[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                verts[x + z * n] = new Vector3((x / (float)cells - 0.5f) * size.x, 0f, (z / (float)cells - 0.5f) * size.y);
                colors[x + z * n] = Color.white;
            }
            var tris = new int[cells * cells * 6];
            int k = 0;
            for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                int a = x + z * n;
                tris[k++] = a; tris[k++] = a + n; tris[k++] = a + 1;
                tris[k++] = a + 1; tris[k++] = a + n; tris[k++] = a + n + 1;
            }
            mesh.vertices = verts;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static T SaveAsset<T>(T asset, string path) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static Material MaterialVariant(Material source, string name, Action<Material> configure)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.CopyPropertiesFromMaterial(source);
                material.shader = source.shader;
            }
            configure(material);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
