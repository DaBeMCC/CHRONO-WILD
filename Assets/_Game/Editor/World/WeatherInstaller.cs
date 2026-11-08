using ChronoWild.Rendering;
using ChronoWild.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using F = ChronoWild.Editor.World.BiomeAssetFactory;

namespace ChronoWild.Editor.World
{
    /// <summary>Adds the day-night cycle and the random weather to the open scene.</summary>
    public static class WeatherInstaller
    {
        const string RootName = "29 Time And Weather";

        /// <summary>
        /// Top-down map of what shelters the ground from rain, drawn from every tree's footprint.
        /// R is the canopy height: a drop below it is under a tree. G is a blurred "how covered" for surfaces.
        /// </summary>
        static Texture2D BakeOcclusion(UnityEngine.Terrain terrain)
        {
            const int res = 1024;
            var size = terrain.terrainData.size;
            var height = new float[res * res];
            var cover = new float[res * res];
            for (int i = 0; i < height.Length; i++) height[i] = -60f;
            foreach (var group in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
            {
                string n = group.name;
                if (n.Contains("Snag") || n.Contains("Rock") || n.Contains("Cliff") || n.Contains("Moss") || n.Contains("Debris"))
                    continue;
                var lods = group.GetLODs();
                if (lods.Length == 0 || lods[0].renderers.Length == 0 || lods[0].renderers[0] == null)
                    continue;
                Bounds b = lods[0].renderers[0].bounds;
                foreach (var r in lods[0].renderers)
                    if (r != null) b.Encapsulate(r.bounds);
                if (b.size.y < 3f || b.size.x < 1.5f)
                    continue;
                float rx = b.size.x * 0.46f, rz = b.size.z * 0.46f;
                int x0 = Mathf.FloorToInt((b.center.x - rx) / size.x * res), x1 = Mathf.CeilToInt((b.center.x + rx) / size.x * res);
                int z0 = Mathf.FloorToInt((b.center.z - rz) / size.z * res), z1 = Mathf.CeilToInt((b.center.z + rz) / size.z * res);
                for (int z = Mathf.Max(z0, 0); z <= Mathf.Min(z1, res - 1); z++)
                for (int x = Mathf.Max(x0, 0); x <= Mathf.Min(x1, res - 1); x++)
                {
                    float dx = ((x + 0.5f) / res * size.x - b.center.x) / rx, dz = ((z + 0.5f) / res * size.z - b.center.z) / rz;
                    if (dx * dx + dz * dz > 1f)
                        continue;
                    int i = x + z * res;
                    height[i] = Mathf.Max(height[i], b.max.y);
                    cover[i] = 1f;
                }
            }
            var blurred = new float[res * res];
            const int radius = 2;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float sum = 0f; int count = 0;
                for (int oz = -radius; oz <= radius; oz++)
                for (int ox = -radius; ox <= radius; ox++)
                {
                    int px = x + ox, pz = z + oz;
                    if (px < 0 || pz < 0 || px >= res || pz >= res) continue;
                    sum += cover[px + pz * res]; count++;
                }
                blurred[x + z * res] = sum / count;
            }
            var pixels = new Color[res * res];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(height[i], blurred[i], 0f, 1f);
            var tex = new Texture2D(res, res, TextureFormat.RGFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "T_RainOcclusion" };
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            string path = F.Root + "/T_RainOcclusion.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        [MenuItem("CHRONO WILD/World/Install Day-Night And Weather")]
        public static void Install()
        {
            var old = GameObject.Find(RootName);
            if (old != null)
                Object.DestroyImmediate(old);
            var root = new GameObject(RootName);
            var sky = Object.FindFirstObjectByType<VolumetricSky>();
            var atmosphere = Object.FindFirstObjectByType<JungleAtmosphere>();
            var player = GameObject.Find("Review Player").transform;

            var weather = root.AddComponent<WeatherSystem>();
            weather.sky = sky;
            weather.atmosphere = atmosphere;
            weather.follow = player;
            weather.rainMaterial = F.MaterialVariant(new Material(Shader.Find("CHRONO WILD/Environment/Soft Particle")), "M_Rain", m =>
            {
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(F.TextureDir + "/T_SoftPuff.png"));
                m.SetFloat("_SunAmount", 0.45f);
                m.SetFloat("_SoftDistance", 0.05f);
                m.SetFloat("_NearFade", 2.5f);
                m.SetFloat("_RainDrop", 1f);
                m.SetTexture("_BaseMap", F.BakeRainDrop());
            });
            weather.splashMaterial = F.MaterialVariant(new Material(Shader.Find("CHRONO WILD/Environment/Soft Particle")), "M_RainSplash", m =>
            {
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(F.TextureDir + "/T_SoftPuff.png"));
                m.SetFloat("_SunAmount", 0.5f);
                m.SetFloat("_SoftDistance", 0.01f);
                m.SetFloat("_NightDim", 0.8f);
            });
            var streaks = F.BakeRainStreaks();
            var curtainShader = Shader.Find("CHRONO WILD/Environment/Rain Curtain");
            Material Curtain(string name, Vector2 tiling, float speed, float opacity, float soft) => F.MaterialVariant(new Material(curtainShader), name, m =>
            {
                m.SetTexture("_StreakTex", streaks);
                m.SetVector("_Tiling", tiling);
                m.SetFloat("_Speed", speed);
                m.SetFloat("_Opacity", opacity);
                m.SetFloat("_SoftDistance", soft);
            });
            weather.curtainNear = Curtain("M_RainCurtainNear", new Vector2(11f, 1.5f), 1.35f, 0.1f, 3f);
            weather.curtainFar = Curtain("M_RainCurtainFar", new Vector2(21f, 1.9f), 0.95f, 0.13f, 10f);
            weather.occlusionMap = BakeOcclusion(UnityEngine.Terrain.activeTerrain);
            weather.terrain = UnityEngine.Terrain.activeTerrain;
            weather.biomes = Object.FindFirstObjectByType<IslandBiomes>();

            var clock = root.AddComponent<DayNightCycle>();
            clock.mainLight = RenderSettings.sun;
            clock.sky = sky;
            clock.atmosphere = atmosphere;
            var surf = GameObject.Find("Surf Breakers");
            if (surf != null)
                clock.unlitParticles = System.Array.FindAll(surf.GetComponentsInChildren<ParticleSystemRenderer>(true), r => r.sharedMaterial != null && r.sharedMaterial.shader.name.Contains("Particles/Unlit"));

            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
        }
    }
}
