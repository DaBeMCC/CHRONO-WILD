using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChronoWild.Editor.Terrain
{
    /// <summary>
    /// Crea los assets provisionales de la primera prueba: Terrain Layers de color con algo de
    /// ruido, árboles de primitivas con LOD Group y materiales URP simples. Se reemplazarán por
    /// arte final cuando la escala y los recorridos estén aprobados.
    /// </summary>
    public static class PlaceholderAssetFactory
    {
        public const string Root = "Assets/_Game/Art/Terrain/Generated";
        public const string TexturesFolder = Root + "/Textures";
        public const string LayersFolder = Root + "/Layers";
        public const string MaterialsFolder = "Assets/_Game/Art/Materials/Placeholder";
        public const string MeshesFolder = "Assets/_Game/Art/Meshes/Placeholder";
        public const string PrefabsFolder = "Assets/_Game/Prefabs/Placeholder";

        public enum Layer
        {
            SandDry,
            SandWet,
            ForestSoil,
            LeafLitter,
            Mud,
            Rock,
            CliffRock,
            PathDirt,
        }

        public static readonly int LayerCount = System.Enum.GetValues(typeof(Layer)).Length;

        static readonly Color[] LayerColors =
        {
            new Color(0.86f, 0.78f, 0.58f),
            new Color(0.60f, 0.53f, 0.39f),
            new Color(0.33f, 0.27f, 0.18f),
            new Color(0.34f, 0.40f, 0.17f),
            new Color(0.27f, 0.22f, 0.16f),
            new Color(0.49f, 0.48f, 0.45f),
            new Color(0.37f, 0.35f, 0.33f),
            new Color(0.56f, 0.43f, 0.29f),
        };

        public static TerrainLayer[] EnsureTerrainLayers()
        {
            EnsureFolder(TexturesFolder);
            EnsureFolder(LayersFolder);
            var layers = new TerrainLayer[LayerCount];
            for (int i = 0; i < LayerCount; i++)
            {
                string name = ((Layer)i).ToString();
                string layerPath = $"{LayersFolder}/TL_{name}.terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
                if (layer == null)
                {
                    var texture = CreateNoiseTexture($"{TexturesFolder}/T_{name}_Placeholder.png", LayerColors[i], i);
                    layer = new TerrainLayer { diffuseTexture = texture, tileSize = new Vector2(6f, 6f), smoothness = 0.05f };
                    AssetDatabase.CreateAsset(layer, layerPath);
                }
                layers[i] = layer;
            }
            return layers;
        }

        static Texture2D CreateNoiseTexture(string path, Color baseColor, int salt)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Ruido periódico (dominio toroidal) para que la textura sea tileable.
                    float u = x / (float)size * Mathf.PI * 2f, v = y / (float)size * Mathf.PI * 2f;
                    float n = Mathf.PerlinNoise(Mathf.Cos(u) * 2f + salt * 10f, Mathf.Sin(u) * 2f + Mathf.Cos(v) * 2f)
                            * 0.6f + Mathf.PerlinNoise(Mathf.Sin(v) * 6f + salt, Mathf.Cos(u) * 6f) * 0.4f;
                    pixels[y * size + x] = baseColor * Mathf.Lerp(0.85f, 1.12f, n);
                }
            }
            tex.SetPixels(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 128;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Material EnsureLitMaterial(string name, Color color, float smoothness = 0.1f)
        {
            EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Agua provisional: URP Lit transparente. El shader económico definitivo llega en la fase de agua.</summary>
        public static Material EnsureWaterMaterial()
        {
            EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/M_Water_Placeholder.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.10f, 0.38f, 0.48f, 0.72f));
            mat.SetFloat("_Smoothness", 0.9f);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        public static GameObject[] EnsureTreePrefabs()
        {
            EnsureFolder(PrefabsFolder);
            return new[]
            {
                EnsureTree("PF_Conifer_Placeholder", BuildConifer),
                EnsureTree("PF_Ginkgo_Placeholder", BuildGinkgo),
                EnsureTree("PF_Cycad_Placeholder", BuildCycad),
            };
        }

        static GameObject EnsureTree(string name, System.Action<GameObject> build)
        {
            string path = $"{PrefabsFolder}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            var root = new GameObject(name);
            build(root);

            // Solo el tronco colisiona; las copas no necesitan collider.
            foreach (var c in root.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(c);
            var renderers = root.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
                r.shadowCastingMode = ShadowCastingMode.On;

            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.015f, renderers) });
            lod.RecalculateBounds();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void BuildConifer(GameObject root)
        {
            var bark = EnsureLitMaterial("Bark_Placeholder", new Color(0.30f, 0.21f, 0.14f));
            var needles = EnsureLitMaterial("Conifer_Placeholder", new Color(0.13f, 0.27f, 0.15f));
            AddPart(root, PrimitiveType.Cylinder, bark, new Vector3(0f, 4f, 0f), new Vector3(0.6f, 4f, 0.6f));
            AddMeshPart(root, EnsureConeMesh(), needles, new Vector3(0f, 3f, 0f), new Vector3(5f, 7f, 5f));
            AddMeshPart(root, EnsureConeMesh(), needles, new Vector3(0f, 7.5f, 0f), new Vector3(3.6f, 6f, 3.6f));
            root.AddComponent<CapsuleCollider>().Configure(0.35f, 8f, 4f);
        }

        static void BuildGinkgo(GameObject root)
        {
            var bark = EnsureLitMaterial("Bark_Placeholder", new Color(0.30f, 0.21f, 0.14f));
            var leaves = EnsureLitMaterial("Ginkgo_Placeholder", new Color(0.36f, 0.50f, 0.18f));
            AddPart(root, PrimitiveType.Cylinder, bark, new Vector3(0f, 3f, 0f), new Vector3(0.7f, 3f, 0.7f));
            AddPart(root, PrimitiveType.Sphere, leaves, new Vector3(0f, 7.5f, 0f), new Vector3(6f, 5.5f, 6f));
            root.AddComponent<CapsuleCollider>().Configure(0.4f, 6f, 3f);
        }

        static void BuildCycad(GameObject root)
        {
            var bark = EnsureLitMaterial("CycadTrunk_Placeholder", new Color(0.36f, 0.29f, 0.18f));
            var fronds = EnsureLitMaterial("Cycad_Placeholder", new Color(0.22f, 0.40f, 0.16f));
            AddPart(root, PrimitiveType.Cylinder, bark, new Vector3(0f, 0.75f, 0f), new Vector3(0.9f, 0.75f, 0.9f));
            for (int i = 0; i < 6; i++)
            {
                var frond = AddPart(root, PrimitiveType.Cube, fronds, new Vector3(0f, 1.6f, 0f), new Vector3(0.35f, 0.08f, 2.6f));
                frond.transform.localRotation = Quaternion.Euler(-25f, i * 60f, 0f);
                frond.transform.localPosition += frond.transform.forward * 1.1f;
            }
            root.AddComponent<CapsuleCollider>().Configure(0.45f, 1.5f, 0.75f);
        }

        static void Configure(this CapsuleCollider c, float radius, float height, float centerY)
        {
            c.radius = radius;
            c.height = height;
            c.center = new Vector3(0f, centerY, 0f);
        }

        static GameObject AddPart(GameObject root, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static void AddMeshPart(GameObject root, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>Cono unitario (radio 0.5, altura 1, base en y = 0) con 10 lados: barato y suficiente a distancia.</summary>
        static Mesh EnsureConeMesh()
        {
            EnsureFolder(MeshesFolder);
            string path = $"{MeshesFolder}/SM_Cone_Placeholder.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
                return mesh;

            const int sides = 10;
            var verts = new Vector3[sides * 2 + 2];
            var tris = new int[sides * 6];
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var rim = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
                verts[i] = rim;
                verts[sides + i] = rim;
            }
            int apex = sides * 2, bottom = sides * 2 + 1;
            verts[apex] = new Vector3(0f, 1f, 0f);
            verts[bottom] = Vector3.zero;
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                tris[i * 6 + 0] = i; tris[i * 6 + 1] = apex; tris[i * 6 + 2] = next;
                tris[i * 6 + 3] = sides + next; tris[i * 6 + 4] = bottom; tris[i * 6 + 5] = sides + i;
            }
            mesh = new Mesh { name = "SM_Cone_Placeholder", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
