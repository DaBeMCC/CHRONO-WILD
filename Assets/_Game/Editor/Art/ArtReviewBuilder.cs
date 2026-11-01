using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ChronoWild.Editor.Art
{
    public static class ArtReviewBuilder
    {
        const string TextureRoot = "Assets/_Game/Art/Textures/Environment/Review";
        const string MaterialRoot = "Assets/_Game/Art/Materials/Environment/Review";
        const string MeshRoot = "Assets/_Game/Art/OptimizedAssets/Environment/Review";
        const string PrefabRoot = "Assets/_Game/Art/Prefabs/Environment/Review";
        const string LayerPath = "Assets/_Game/Art/Terrain/Generated/Layers/TL_ForestSoil.terrainlayer";
        const string TerrainPath = "Assets/_Game/Art/Terrain/Generated/Island_TerrainData.asset";
        public const string IslandScene = "Assets/_Game/Scenes/Art/Island_ArtReview.unity";
        public const string SandboxScene = "Assets/_Game/Scenes/Art/Art_AssetReview.unity";

        [MenuItem("CHRONO WILD/Arte/Crear muestra de entorno")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Sal de Play antes de preparar la muestra.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Guarda los cambios de las escenas abiertas antes de crear la muestra.");
            if (File.Exists(IslandScene) || File.Exists(SandboxScene))
                throw new InvalidOperationException("La muestra ya existe. Abre sus escenas para revisarla; no se sobrescriben.");
            Require<TerrainData>(TerrainPath);
            Require<TerrainLayer>(LayerPath);
            foreach (string name in new[] { "ForestSoil", "Rock", "Bark" })
                foreach (string map in new[] { "Albedo.jpg", "Normal.png", "Mask.png" })
                    if (!File.Exists($"{TextureRoot}/T_{name}_{map}"))
                        throw new FileNotFoundException("Ejecuta Tools/Art/prepare_review_textures.py antes de crear la muestra.");

            foreach (string folder in new[] { MaterialRoot, MeshRoot, PrefabRoot, "Assets/_Game/Scenes/Art" })
                Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            ConfigureTextures();
            var bark = SurfaceMaterial("Bark", new Vector2(1f, 2.5f));
            var rock = SurfaceMaterial("Rock", Vector2.one);
            var soil = SurfaceMaterial("ForestSoil", new Vector2(4f, 4f));
            var leaves = LeafMaterial();
            var tree = BuildTree(bark, leaves);
            var stone = BuildRock(rock);
            ApplyForestLayer();

            var active = SceneManager.GetActiveScene();
            CreateSandbox(tree, stone, soil);
            CreateIslandReview(tree, stone);
            if (active.IsValid() && active.isLoaded)
                SceneManager.SetActiveScene(active);
            AssetDatabase.SaveAssets();
            Debug.Log("Muestra preparada: ginkgo, roca y suelo forestal. TerrainData y gameplay conservados.");
        }

        static T Require<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException($"Falta el asset {path}");
            return asset;
        }

        static void ConfigureTextures()
        {
            foreach (string file in Directory.GetFiles(TextureRoot).Where(p => p.EndsWith(".jpg") || p.EndsWith(".png")))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(file);
                bool normal = file.Contains("_Normal");
                bool mask = file.Contains("_Mask");
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !normal && !mask;
                importer.mipmapEnabled = true;
                importer.isReadable = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = file.Contains("ForestSoil") ? 4 : 2;
                importer.maxTextureSize = file.Contains("ForestSoil") ? 1024 : 512;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Standalone", overridden = true, maxTextureSize = importer.maxTextureSize,
                    format = normal ? TextureImporterFormat.BC5 : mask ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1,
                    compressionQuality = 100
                });
                // DXT1/5 pertenece a la ruta de compresión DXT de WebGL; validar el fallback del navegador.
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "WebGL", overridden = true, maxTextureSize = importer.maxTextureSize,
                    format = normal || mask ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1,
                    compressionQuality = 100
                });
                importer.SaveAndReimport();
            }
        }

        static Material SurfaceMaterial(string name, Vector2 scale)
        {
            string path = $"{MaterialRoot}/M_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_" + name, enableInstancing = true };
            material.SetTexture("_BaseMap", Require<Texture2D>($"{TextureRoot}/T_{name}_Albedo.jpg"));
            material.SetTexture("_BumpMap", Require<Texture2D>($"{TextureRoot}/T_{name}_Normal.png"));
            var mask = Require<Texture2D>($"{TextureRoot}/T_{name}_Mask.png");
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetTexture("_OcclusionMap", mask);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_BumpScale", name == "ForestSoil" ? .65f : .8f);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTextureScale("_BumpMap", scale);
            material.SetTextureScale("_MetallicGlossMap", scale);
            material.SetTextureScale("_OcclusionMap", scale);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Material LeafMaterial()
        {
            string path = $"{MaterialRoot}/M_GinkgoLeaf.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_GinkgoLeaf", enableInstancing = true };
            material.SetTexture("_BaseMap", Require<Texture2D>($"{TextureRoot}/T_GinkgoLeaf_Albedo.png"));
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_Smoothness", .18f);
            material.doubleSidedGI = true;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void ApplyForestLayer()
        {
            Directory.CreateDirectory("Backups/ArtReview");
            string backup = "Backups/ArtReview/TL_ForestSoil_before.terrainlayer";
            if (!File.Exists(backup)) File.Copy(LayerPath, backup);
            var layer = Require<TerrainLayer>(LayerPath);
            Undo.RecordObject(layer, "Aplicar suelo forestal PBR");
            layer.diffuseTexture = Require<Texture2D>($"{TextureRoot}/T_ForestSoil_Albedo.jpg");
            layer.normalMapTexture = Require<Texture2D>($"{TextureRoot}/T_ForestSoil_Normal.png");
            layer.maskMapTexture = Require<Texture2D>($"{TextureRoot}/T_ForestSoil_Mask.png");
            layer.tileSize = new Vector2(3f, 3f);
            layer.normalScale = .65f;
            layer.metallic = 0f;
            layer.smoothness = 1f;
            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssetIfDirty(layer);
        }

        static GameObject BuildTree(Material bark, Material leaves)
        {
            string path = $"{PrefabRoot}/PF_Ginkgo_Review.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = new GameObject("PF_Ginkgo_Review");
            try
            {
                var collider = root.AddComponent<CapsuleCollider>();
                collider.center = new Vector3(0f, 2f, 0f);
                collider.height = 4f;
                collider.radius = .34f;
                var levels = new LOD[3];
                for (int lod = 0; lod < 3; lod++)
                {
                    var wood = new MeshDraft();
                    var foliage = new MeshDraft();
                    BuildGinkgoGeometry(wood, foliage, lod);
                    var group = new GameObject("LOD" + lod);
                    group.transform.SetParent(root.transform, false);
                    var trunk = MeshChild(group, "Tronco y ramas", SaveMesh(wood, "SM_Ginkgo_Wood_LOD" + lod), bark);
                    var crown = MeshChild(group, "Hojas en abanico", SaveMesh(foliage, "SM_Ginkgo_Leaves_LOD" + lod), leaves);
                    levels[lod] = new LOD(new[] { .18f, .065f, .012f }[lod], new Renderer[] { trunk, crown });
                }
                var lodGroup = root.AddComponent<LODGroup>();
                lodGroup.SetLODs(levels);
                lodGroup.fadeMode = LODFadeMode.None;
                lodGroup.RecalculateBounds();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void BuildGinkgoGeometry(MeshDraft wood, MeshDraft leaves, int lod)
        {
            var rng = new System.Random(1647);
            int sides = new[] { 10, 7, 3 }[lod];
            var trunk = new List<Vector3>();
            for (int i = 0; i <= 10; i++)
                trunk.Add(new Vector3(Mathf.Sin(i * .65f) * .16f, i * 1.02f, Mathf.Sin(i * .4f) * .10f));
            wood.Tube(trunk, .34f, .035f, sides);
            for (int root = 0; root < 6; root++)
            {
                float angle = root * 2.39996f;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                wood.Tube(new List<Vector3> { direction * 1.1f + Vector3.up * .02f, direction * .42f + Vector3.up * .24f, Vector3.up * .55f }, .035f, .18f, sides);
            }
            for (int branch = 0; branch < 28; branch++)
            {
                float height = 3.4f + branch * .215f;
                float angle = branch * 2.39996f + Range(rng, -.25f, .25f);
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float reach = (1.2f + 2f * Mathf.Sin((height - 2f) / 8.5f * Mathf.PI)) * Range(rng, .85f, 1.15f);
                Vector3 start = new Vector3(Mathf.Sin(height * .65f) * .14f, height, 0f);
                Vector3 mid = start + direction * reach * .52f + Vector3.up * .35f;
                Vector3 tip = start + direction * reach + Vector3.up * Range(rng, 1f, 1.8f);
                wood.Tube(new List<Vector3> { start, mid, tip }, .095f * (1f - branch / 42f), .013f, sides);
                for (int twig = 0; twig < 6; twig++)
                {
                    float t = .35f + twig * .115f;
                    Vector3 stem = Vector3.Lerp(mid, tip, Mathf.Clamp01((t - .5f) * 2f));
                    if (t < .5f) stem = Vector3.Lerp(start, mid, t * 2f);
                    Vector3 sideways = Vector3.Cross(direction, Vector3.up) * (twig % 2 == 0 ? 1f : -1f);
                    Vector3 end = stem + sideways * Range(rng, .45f, .9f) + direction * .35f + Vector3.up * Range(rng, .15f, .5f);
                    if (lod < 2) wood.Tube(new List<Vector3> { stem, end }, .025f, .006f, lod == 0 ? 5 : 3);
                    // La misma distribución se muestrea en los tres LOD; no cambia la silueta general.
                    for (int leaf = 0; leaf < 11; leaf++)
                    {
                        Vector3 offset = new Vector3(Range(rng, -.48f, .48f), Range(rng, -.18f, .42f), Range(rng, -.48f, .48f));
                        Vector3 position = Vector3.Lerp(stem, end, Range(rng, .35f, 1.2f)) + offset;
                        Quaternion rotation = Quaternion.Euler(Range(rng, -55f, 55f), Range(rng, 0, 360f), Range(rng, -40f, 40f));
                        float scale = Range(rng, .19f, .29f);
                        int stride = new[] { 1, 3, 12 }[lod];
                        if (leaf % stride == 0 && (lod < 2 || twig % 2 == 0))
                            leaves.Fan(position, rotation, scale * new[] { 1f, 1.4f, 2.4f }[lod], lod == 0 ? 6 : 4);
                    }
                }
            }
        }

        static float Range(System.Random rng, float min, float max) => Mathf.Lerp(min, max, (float)rng.NextDouble());

        [MenuItem("CHRONO WILD/Arte/Actualizar mallas del ginkgo de muestra")]
        public static void RefreshGinkgoMeshes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Sal de Play antes de actualizar las mallas.");
            for (int lod = 0; lod < 3; lod++)
            {
                var wood = new MeshDraft();
                var leaves = new MeshDraft();
                BuildGinkgoGeometry(wood, leaves, lod);
                foreach (var part in new[] { (draft: wood, name: "SM_Ginkgo_Wood_LOD" + lod), (draft: leaves, name: "SM_Ginkgo_Leaves_LOD" + lod) })
                {
                    var target = Require<Mesh>($"{MeshRoot}/{part.name}.asset");
                    var replacement = part.draft.Create(part.name);
                    EditorUtility.CopySerialized(replacement, target);
                    Object.DestroyImmediate(replacement);
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(target);
                }
            }
            SceneView.RepaintAll();
        }

        static GameObject BuildRock(Material material)
        {
            string path = $"{PrefabRoot}/PF_Rock_Review.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = new GameObject("PF_Rock_Review");
            try
            {
                var levels = new LOD[3];
                for (int lod = 0; lod < 3; lod++)
                {
                    var draft = new MeshDraft();
                    int rings = new[] { 12, 6, 3 }[lod], sides = rings * 2;
                    for (int y = 0; y <= rings; y++)
                        for (int x = 0; x <= sides; x++)
                        {
                            float theta = Mathf.PI * y / rings;
                            float phi = 2f * Mathf.PI * x / sides;
                            var direction = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                            float n = Mathf.PerlinNoise(direction.x * 2.1f + 8f, direction.z * 2.1f + direction.y * .7f + 13f);
                            float radius = .78f + n * .43f + Mathf.Sin(phi * 3f + direction.y * 4f) * Mathf.Sin(theta) * .07f;
                            var p = Vector3.Scale(direction * radius, new Vector3(1.5f, 1.05f, 1.1f));
                            p.y = Mathf.Max(p.y, -.6f) + .61f;
                            draft.Vertex(p, new Vector2(x / (float)sides * 2f, y / (float)rings));
                        }
                    for (int y = 0; y < rings; y++)
                        for (int x = 0; x < sides; x++)
                        {
                            int a = y * (sides + 1) + x, b = a + sides + 1;
                            if (y > 0) draft.Triangle(a, a + 1, b);
                            if (y < rings - 1) draft.Triangle(a + 1, b + 1, b);
                        }
                    var renderer = MeshChild(root, "LOD" + lod, SaveMesh(draft, "SM_Rock_LOD" + lod), material);
                    levels[lod] = new LOD(new[] { .14f, .045f, .008f }[lod], new Renderer[] { renderer });
                }
                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, .65f, 0f);
                collider.size = new Vector3(2.6f, 1.5f, 1.9f);
                var group = root.AddComponent<LODGroup>();
                group.SetLODs(levels);
                group.RecalculateBounds();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Mesh SaveMesh(MeshDraft draft, string name)
        {
            string path = $"{MeshRoot}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = draft.Create(name);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static MeshRenderer MeshChild(GameObject parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        static void CreateIslandReview(GameObject tree, GameObject rock)
        {
            var terrain = Object.FindObjectsByType<UnityEngine.Terrain>()
                .FirstOrDefault(t => AssetDatabase.GetAssetPath(t.terrainData) == TerrainPath);
            if (terrain == null)
            {
                EditorSceneManager.OpenScene("Assets/_Game/Scenes/World/Island_Terrain.unity", OpenSceneMode.Additive);
                terrain = Object.FindObjectsByType<UnityEngine.Terrain>()
                    .First(t => AssetDatabase.GetAssetPath(t.terrainData) == TerrainPath);
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            // Al este del claro de investigación; el eje de la ruta queda libre.
            foreach (var item in new[] { (prefab: tree, x: 343f, z: 299f), (prefab: rock, x: 346.5f, z: 294f) })
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(item.prefab, scene);
                float height = terrain.SampleHeight(new Vector3(item.x, 0, item.z)) + terrain.transform.position.y;
                instance.transform.position = new Vector3(item.x, height - (item.prefab == rock ? .12f : .03f), item.z);
            }
            EditorSceneManager.SaveScene(scene, IslandScene);
        }

        static void CreateSandbox(GameObject tree, GameObject rock, Material soil)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Suelo forestal · muestra 12 m";
            ground.transform.localScale = Vector3.one * 1.2f;
            ground.GetComponent<MeshRenderer>().sharedMaterial = soil;
            var treeInstance = (GameObject)PrefabUtility.InstantiatePrefab(tree, scene);
            treeInstance.transform.position = new Vector3(-1f, 0f, 1f);
            var rockInstance = (GameObject)PrefabUtility.InstantiatePrefab(rock, scene);
            rockInstance.transform.position = new Vector3(2f, -.12f, -1.5f);
            var light = new GameObject("Sol de revisión", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
            var camera = new GameObject("Art Review Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(12f, 5.2f, -17f);
            camera.transform.LookAt(new Vector3(0f, 4.7f, 0f));
            camera.fieldOfView = 42f;
            camera.farClipPlane = 100f;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.52f, .61f, .7f);
            RenderSettings.ambientEquatorColor = new Color(.35f, .37f, .31f);
            RenderSettings.ambientGroundColor = new Color(.14f, .12f, .09f);
            EditorSceneManager.SaveScene(scene, SandboxScene);
            EditorSceneManager.CloseScene(scene, true);
        }

        sealed class MeshDraft
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> triangles = new List<int>();

            public int Vertex(Vector3 p, Vector2 texcoord)
            {
                int index = vertices.Count;
                vertices.Add(p);
                uv.Add(texcoord);
                return index;
            }

            public void Triangle(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }

            public void Tube(List<Vector3> points, float startRadius, float endRadius, int sides)
            {
                int first = vertices.Count;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 forward = (points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(0, i - 1)]).normalized;
                    Vector3 side = Vector3.Cross(forward, Mathf.Abs(forward.y) > .9f ? Vector3.forward : Vector3.up).normalized;
                    Vector3 up = Vector3.Cross(forward, side).normalized;
                    float radius = Mathf.Lerp(startRadius, endRadius, i / (float)(points.Count - 1));
                    for (int j = 0; j <= sides; j++)
                    {
                        float angle = j / (float)sides * Mathf.PI * 2f;
                        Vertex(points[i] + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius,
                            new Vector2(j / (float)sides, i / (float)(points.Count - 1)));
                    }
                }
                for (int i = 0; i < points.Count - 1; i++)
                    for (int j = 0; j < sides; j++)
                    {
                        int a = first + i * (sides + 1) + j, b = a + sides + 1;
                        Triangle(a, a + 1, b);
                        Triangle(a + 1, b + 1, b);
                    }
            }

            public void Fan(Vector3 position, Quaternion rotation, float scale, int segments)
            {
                int center = Vertex(position, new Vector2(.5f, .05f));
                for (int i = 0; i <= segments; i++)
                {
                    float angle = Mathf.Lerp(-70f, 70f, i / (float)segments) * Mathf.Deg2Rad;
                    float notch = Mathf.Abs(angle) < .1f ? .78f : 1f;
                    Vector3 p = new Vector3(Mathf.Sin(angle), .12f * Mathf.Cos(angle * 2f), Mathf.Cos(angle) * notch) * scale;
                    Vertex(position + rotation * p, new Vector2(.5f + Mathf.Sin(angle) * .48f, .05f + Mathf.Cos(angle) * notch * .9f));
                    if (i > 0) Triangle(center, center + i, center + i + 1);
                }
            }

            public Mesh Create(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                mesh.Optimize();
                return mesh;
            }
        }
    }
}
