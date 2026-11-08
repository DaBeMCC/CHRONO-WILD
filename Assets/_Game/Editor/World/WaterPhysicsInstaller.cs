using ChronoWild.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChronoWild.Editor.World
{
    /// <summary>Adds the interactive water systems and a set of floating logs to the open scene.</summary>
    public static class WaterPhysicsInstaller
    {
        const string RootName = "25 Water Physics";
        const string Biomes = "Assets/_Game/Art/Biomes";
        const string MaterialPath = Biomes + "/Materials/M_PbfDroplets.mat";

        [MenuItem("CHRONO WILD/World/Install Water Physics")]
        public static void Install()
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            var old = GameObject.Find(RootName);
            if (old != null)
                Object.DestroyImmediate(old);
            var root = new GameObject(RootName);

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Hidden/CHRONO WILD/Water/PBF Droplets")) { name = "M_PbfDroplets" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            var player = GameObject.Find("Review Player");
            var sim = root.AddComponent<WaterSimulation>();
            sim.compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_MyShaders/Water/WaterSim.compute");
            sim.oceanMaterial = GameObject.Find("Ocean Coast").GetComponent<MeshRenderer>().sharedMaterial;
            sim.biomeMap = AssetDatabase.LoadAssetAtPath<Texture2D>(Biomes + "/T_IslandBiomeMap.png");
            sim.terrain = terrain;
            sim.focus = player.transform;
            sim.swampLevel = IslandBiomeBuilder.SwampWaterLevel;

            var pbf = root.AddComponent<PbfSplashes>();
            pbf.compute = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_MyShaders/Water/PbfSplash.compute");
            pbf.dropletMaterial = material;

            var interactor = player.GetComponent<WaterInteractor>();
            if (interactor == null)
                interactor = player.AddComponent<WaterInteractor>();
            var controller = player.GetComponent<CharacterController>();
            interactor.bottomOffset = controller.center.y - controller.height * 0.5f;
            interactor.height = controller.height;
            interactor.radius = 0.35f;
            interactor.waterDrag = 0f;

            var tester = player.GetComponent<WaterPhysicsTester>();
            if (tester == null)
                tester = player.AddComponent<WaterPhysicsTester>();
            tester.stonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Prefabs/Geology/PF_Rock1.prefab");

            PlaceLogs(terrain, root.transform);
            EditorUtility.SetDirty(player);
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
        }

        static void PlaceLogs(UnityEngine.Terrain terrain, Transform parent)
        {
            var rng = new System.Random(77);
            var biome = AssetDatabase.LoadAssetAtPath<Texture2D>(Biomes + "/T_IslandBiomeMap.png");
            var path = AssetDatabase.GetAssetPath(biome);
            var bytes = System.IO.File.ReadAllBytes(path);
            var readable = new Texture2D(2, 2);
            readable.LoadImage(bytes);

            int swamp = 0, ocean = 0;
            for (int attempt = 0; attempt < 40000 && (swamp < 14 || ocean < 12); attempt++)
            {
                float x = (float)rng.NextDouble() * 600f, z = (float)rng.NextDouble() * 600f;
                float ground = terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;
                bool inSwamp = readable.GetPixelBilinear(x / 600f, z / 600f).b > 0.5f;
                string id = "ABC".Substring(rng.Next(3), 1);
                if (inSwamp && swamp < 14 && ground < 0.9f)
                {
                    Spawn($"{Biomes}/Prefabs/PF_SwampLog_{id}.prefab", new Vector3(x, 1.7f, z), rng, parent, 1.1f + (float)rng.NextDouble() * 0.9f);
                    swamp++;
                }
                else if (!inSwamp && ocean < 12 && ground < -3.5f && ground > -9f && z < 130f)
                {
                    Spawn($"{Biomes}/Prefabs/PF_BeachDrift_{id}.prefab", new Vector3(x, 0.6f, z), rng, parent, 1.1f + (float)rng.NextDouble() * 0.9f);
                    ocean++;
                }
            }
            Object.DestroyImmediate(readable);
            Debug.Log($"CHRONO WILD: placed {swamp} swamp logs and {ocean} ocean logs.");
        }

        static void Spawn(string prefabPath, Vector3 position, System.Random rng, Transform parent, float scale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            go.transform.localScale = Vector3.one * scale;

            var bounds = go.GetComponent<MeshFilter>().sharedMesh.bounds;
            var capsule = go.AddComponent<CapsuleCollider>();
            Vector3 s = bounds.size;
            int axis = s.x >= s.y && s.x >= s.z ? 0 : s.z >= s.y ? 2 : 1;
            capsule.direction = axis;
            capsule.center = bounds.center;
            capsule.height = s[axis];
            capsule.radius = Mathf.Max(0.04f, Mathf.Min(s[(axis + 1) % 3], s[(axis + 2) % 3]) * 0.5f);

            var body = go.AddComponent<Rigidbody>();
            float worldLength = capsule.height * scale, worldRadius = capsule.radius * scale;
            body.mass = Mathf.Max(5f, Mathf.PI * worldRadius * worldRadius * worldLength * 550f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var buoyancy = go.AddComponent<Buoyancy>();
            buoyancy.fullDepth = Mathf.Max(worldRadius * 2f, 0.12f);
            var interactor = go.AddComponent<WaterInteractor>();
            interactor.radius = Mathf.Clamp(worldLength * 0.25f, 0.25f, 0.8f);
            interactor.height = worldRadius * 3f;
            interactor.bottomOffset = -worldRadius;
            interactor.mass = 0.5f;
            interactor.strideLength = 2.5f;
            interactor.waterDrag = 0f;
        }
    }
}
