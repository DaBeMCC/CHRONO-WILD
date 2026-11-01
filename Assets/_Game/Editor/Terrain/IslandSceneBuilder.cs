using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChronoWild.Diagnostics;
using ChronoWild.Player;
using ChronoWild.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ChronoWild.Editor.Terrain
{
    /// <summary>
    /// Monta las dos escenas de la isla:
    /// - Island_Terrain: terreno, océano, sol y niebla (entorno; lo edita David).
    /// - Island_Gameplay: POIs, jugador de prueba y cargador del entorno (lo edita Gerardo).
    /// Solo crea lo que falta; nunca borra objetos añadidos a mano. Antes de tocar una escena
    /// existente deja una copia en Backups/ (fuera de Assets).
    /// </summary>
    public static class IslandSceneBuilder
    {
        public const string ScenesFolder = "Assets/_Game/Scenes/World";
        public const string TerrainScenePath = ScenesFolder + "/Island_Terrain.unity";
        public const string GameplayScenePath = ScenesFolder + "/Island_Gameplay.unity";

        const string TerrainName = "Island_Terrain";
        const string OceanName = "Ocean";
        const string SunName = "Sun";
        const string PoiRootName = "PointsOfInterest";
        const string PlayerName = "Player";
        const string LoaderName = "WorldSceneLoader";

        public static void BuildScenes(IslandTerrainSettings s, TerrainData data)
        {
            PlaceholderAssetFactory.EnsureFolder(ScenesFolder);
            BackupAsset(TerrainScenePath);
            BackupAsset(GameplayScenePath);

            var terrainScene = OpenOrCreate(TerrainScenePath, NewSceneMode.Single, OpenSceneMode.Single);
            SceneManager.SetActiveScene(terrainScene);
            EnsureTerrain(terrainScene, s, data);
            EnsureOcean(terrainScene, s);
            EnsureSun(terrainScene);
            ConfigureAtmosphere();
            EditorSceneManager.SaveScene(terrainScene);

            var gameplayScene = OpenOrCreate(GameplayScenePath, NewSceneMode.Additive, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(gameplayScene);
            EnsureLoader(gameplayScene);
            SyncPointsOfInterest(gameplayScene, s, data);
            EnsurePlayer(gameplayScene, s, data);
            EditorSceneManager.SaveScene(gameplayScene);

            SceneManager.SetActiveScene(terrainScene);
            RegisterInBuildSettings();
        }

        static Scene OpenOrCreate(string path, NewSceneMode newMode, OpenSceneMode openMode)
        {
            var loaded = SceneManager.GetSceneByPath(path);
            if (loaded.isLoaded)
                return loaded;
            if (File.Exists(path))
                return EditorSceneManager.OpenScene(path, openMode);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, newMode);
            EditorSceneManager.SaveScene(scene, path);
            return scene;
        }

        static GameObject FindRoot(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);

        static void EnsureTerrain(Scene scene, IslandTerrainSettings s, TerrainData data)
        {
            var go = FindRoot(scene, TerrainName);
            if (go == null)
            {
                go = UnityEngine.Terrain.CreateTerrainGameObject(data);
                go.name = TerrainName;
                SceneManager.MoveGameObjectToScene(go, scene);
            }
            go.isStatic = true;
            go.transform.position = new Vector3(0f, -s.seaFloorDepth, 0f);

            var terrain = go.GetComponent<UnityEngine.Terrain>();
            terrain.terrainData = data;
            go.GetComponent<TerrainCollider>().terrainData = data;
            if (terrain.materialTemplate == null && GraphicsSettings.defaultRenderPipeline != null)
                terrain.materialTemplate = GraphicsSettings.defaultRenderPipeline.defaultTerrainMaterial;

            // Valores medios; el perfil de calidad baja los ajustará en tiempo de ejecución.
            terrain.heightmapPixelError = 5f;
            terrain.basemapDistance = 200f;
            terrain.treeDistance = 400f;
            terrain.treeBillboardDistance = 120f;
            terrain.detailObjectDistance = 60f;
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = ShadowCastingMode.On;
        }

        static void EnsureOcean(Scene scene, IslandTerrainSettings s)
        {
            var go = FindRoot(scene, OceanName);
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Plane);
                go.name = OceanName;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                SceneManager.MoveGameObjectToScene(go, scene);
            }
            go.isStatic = true;
            float extent = s.terrainSize * 5f;
            go.transform.position = new Vector3(s.terrainSize * 0.5f, 0f, s.terrainSize * 0.5f);
            go.transform.localScale = new Vector3(extent / 10f, 1f, extent / 10f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = PlaceholderAssetFactory.EnsureWaterMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void EnsureSun(Scene scene)
        {
            var go = FindRoot(scene, SunName);
            if (go != null)
                return;
            go = new GameObject(SunName);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = new Color(1f, 0.95f, 0.86f);
            light.shadows = LightShadows.Soft;
        }

        /// <summary>
        /// Niebla lineal: oculta el borde del mundo y crea perspectiva aérea a coste casi nulo.
        /// </summary>
        static void ConfigureAtmosphere()
        {
            if (RenderSettings.skybox == null)
                RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.68f, 0.77f, 0.80f);
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 520f;
        }

        static void EnsureLoader(Scene scene)
        {
            var go = FindRoot(scene, LoaderName);
            if (go != null)
                return;
            go = new GameObject(LoaderName);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<WorldSceneLoader>();
        }

        public static void SyncPointsOfInterest(Scene scene, IslandTerrainSettings s, TerrainData data)
        {
            var root = FindRoot(scene, PoiRootName);
            if (root == null)
            {
                root = new GameObject(PoiRootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }

            var existing = root.GetComponentsInChildren<PointOfInterest>(true)
                .Where(p => !string.IsNullOrEmpty(p.id))
                .GroupBy(p => p.id)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var def in s.pointsOfInterest)
            {
                if (!existing.TryGetValue(def.id, out var poi))
                {
                    var go = new GameObject("POI_" + def.id);
                    go.transform.SetParent(root.transform, false);
                    poi = go.AddComponent<PointOfInterest>();
                    poi.id = def.id;
                    AddPlaceholderVisual(poi.transform, def.type);
                }
                poi.displayName = def.displayName;
                poi.type = def.type;
                poi.radius = def.radius;
                poi.transform.position = GroundPoint(s, data, def.position);
                EditorUtility.SetDirty(poi);
            }
        }

        /// <summary>Copia al asset de configuración las posiciones de POIs movidos a mano en la escena.</summary>
        public static int PullPointsOfInterest(Scene scene, IslandTerrainSettings s)
        {
            var root = FindRoot(scene, PoiRootName);
            if (root == null)
                return 0;
            Undo.RecordObject(s, "Leer POIs de la escena");
            int count = 0;
            foreach (var poi in root.GetComponentsInChildren<PointOfInterest>(true))
            {
                var def = s.pointsOfInterest.FirstOrDefault(d => d.id == poi.id);
                if (def == null)
                {
                    def = new PointOfInterestDefinition { id = poi.id };
                    s.pointsOfInterest.Add(def);
                }
                def.displayName = poi.displayName;
                def.type = poi.type;
                def.radius = poi.radius;
                def.position = s.WorldToNormalized(poi.transform.position);
                count++;
            }
            EditorUtility.SetDirty(s);
            return count;
        }

        static void EnsurePlayer(Scene scene, IslandTerrainSettings s, TerrainData data)
        {
            var spawn = s.pointsOfInterest.FirstOrDefault(p => p.type == PoiType.Spawn);
            // Margen sobre el suelo: si el terreno se regenera más alto, la cápsula no debe quedar enterrada.
            Vector3 spawnPos = GroundPoint(s, data, spawn != null ? spawn.position : new Vector2(0.5f, 0.2f)) + Vector3.up * 0.5f;

            var go = FindRoot(scene, PlayerName);
            if (go != null)
            {
                go.transform.position = spawnPos;
                return;
            }

            go = new GameObject(PlayerName);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = spawnPos;
            // Mira tierra adentro (norte) al aparecer.
            go.transform.rotation = Quaternion.identity;

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.4f;

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(go.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 700f;
            cam.fieldOfView = 70f;
            camGo.AddComponent<AudioListener>();

            var controller = go.AddComponent<DebugFirstPersonController>();
            controller.cameraPivot = camGo.transform;

            var overlay = go.AddComponent<PerformanceOverlay>();
            var so = new SerializedObject(overlay);
            so.FindProperty("player").objectReferenceValue = controller;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static Vector3 GroundPoint(IslandTerrainSettings s, TerrainData data, Vector2 normalized)
        {
            float h = data.GetInterpolatedHeight(normalized.x, normalized.y) - s.seaFloorDepth;
            var p = s.NormalizedToWorldXZ(normalized);
            p.y = h;
            return p;
        }

        static void AddPlaceholderVisual(Transform parent, PoiType type)
        {
            switch (type)
            {
                case PoiType.Portal:
                {
                    var stone = PlaceholderAssetFactory.EnsureLitMaterial("PortalStone_Placeholder", new Color(0.42f, 0.40f, 0.46f));
                    var glow = PlaceholderAssetFactory.EnsureLitMaterial("PortalGlow_Placeholder", new Color(0.45f, 0.35f, 1f), 0.9f);
                    Part(parent, PrimitiveType.Cube, stone, new Vector3(-2.5f, 3f, 0f), new Vector3(1f, 6f, 1f));
                    Part(parent, PrimitiveType.Cube, stone, new Vector3(2.5f, 3f, 0f), new Vector3(1f, 6f, 1f));
                    Part(parent, PrimitiveType.Cube, stone, new Vector3(0f, 6.4f, 0f), new Vector3(6.4f, 0.9f, 1.1f));
                    var core = Part(parent, PrimitiveType.Cube, glow, new Vector3(0f, 3f, 0f), new Vector3(4f, 5.6f, 0.15f));
                    Object.DestroyImmediate(core.GetComponent<Collider>());
                    break;
                }
                case PoiType.Research:
                {
                    // Ginkgo monumental: hito visual del bosque y objetivo de investigación.
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceholderAssetFactory.PrefabsFolder + "/PF_Ginkgo_Placeholder.prefab");
                    if (prefab == null) break;
                    var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                    tree.transform.localPosition = Vector3.zero;
                    tree.transform.localScale = Vector3.one * 2.6f;
                    break;
                }
                case PoiType.BuildZone:
                {
                    var mat = PlaceholderAssetFactory.EnsureLitMaterial("BuildZone_Placeholder", new Color(0.3f, 0.6f, 0.9f));
                    var marker = Part(parent, PrimitiveType.Cylinder, mat, new Vector3(0f, 0.05f, 0f), new Vector3(3f, 0.05f, 3f));
                    Object.DestroyImmediate(marker.GetComponent<Collider>());
                    break;
                }
            }
        }

        static GameObject Part(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static void RegisterInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(GameplayScenePath, true),
                new EditorBuildSettingsScene(TerrainScenePath, true),
            };
            foreach (var existing in EditorBuildSettings.scenes)
            {
                if (existing.path != GameplayScenePath && existing.path != TerrainScenePath)
                    scenes.Add(new EditorBuildSettingsScene(existing.path, false));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Copia con fecha en &lt;proyecto&gt;/Backups (no importada por Unity, ignorada por git).</summary>
        public static void BackupAsset(string assetPath)
        {
            if (!File.Exists(assetPath))
                return;
            string dir = Path.Combine("Backups", Path.GetDirectoryName(assetPath).Replace("Assets/", ""));
            Directory.CreateDirectory(dir);
            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(assetPath)}_{stamp}{Path.GetExtension(assetPath)}");
            File.Copy(assetPath, target, true);
        }
    }
}
