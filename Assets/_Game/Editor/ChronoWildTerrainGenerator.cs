using System.IO;
using ChronoWild.Editor.Terrain;
using ChronoWild.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChronoWild.Editor
{
    /// <summary>
    /// Herramienta de editor para generar la isla. El terreno se genera aquí y se guarda como
    /// asset; el juego nunca lo regenera en tiempo de ejecución.
    ///
    /// Conservación de cambios manuales: cada generación guarda la altura "base" generada en
    /// Island_HeightBaseline.bytes. Al regenerar con "Conservar retoques manuales", la diferencia
    /// entre el terreno actual y esa base (lo esculpido a mano) se vuelve a sumar al nuevo relieve.
    /// </summary>
    public class ChronoWildTerrainGenerator : EditorWindow
    {
        public const string SettingsPath = "Assets/_Game/Settings/World/IslandTerrainSettings.asset";
        public const string TerrainDataPath = PlaceholderAssetFactory.Root + "/Island_TerrainData.asset";
        public const string BaselinePath = PlaceholderAssetFactory.Root + "/Island_HeightBaseline.bytes";
        public const string ReportPath = "Docs/Terrain/ValidationReport.md";
        public const string MaskExportFolder = "Exports/TerrainMasks";

        IslandTerrainSettings settings;
        UnityEditor.Editor settingsEditor;
        Texture2D preview;
        Vector2 scroll;
        string lastReport;
        bool preserveManualEdits = true;
        bool paintSurfaces = true;
        bool placeTrees = true;

        [MenuItem("CHRONO WILD/Generador de terreno")]
        public static void Open() => GetWindow<ChronoWildTerrainGenerator>("Generador de isla");

        void OnEnable()
        {
            settings = AssetDatabase.LoadAssetAtPath<IslandTerrainSettings>(SettingsPath);
        }

        void OnDisable()
        {
            if (settingsEditor != null) DestroyImmediate(settingsEditor);
            if (preview != null) DestroyImmediate(preview);
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            settings = (IslandTerrainSettings)EditorGUILayout.ObjectField("Configuración", settings, typeof(IslandTerrainSettings), false);
            if (settings == null)
            {
                if (GUILayout.Button("Crear configuración por defecto"))
                    settings = LoadOrCreateSettings();
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generación", EditorStyles.boldLabel);
            preserveManualEdits = EditorGUILayout.ToggleLeft("Conservar retoques manuales de altura", preserveManualEdits);
            paintSurfaces = EditorGUILayout.ToggleLeft("Repintar superficies (sobrescribe el pintado manual)", paintSurfaces);
            placeTrees = EditorGUILayout.ToggleLeft("Recolocar vegetación provisional", placeTrees);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Previsualizar")) RefreshPreview();
                if (GUILayout.Button("Generar / actualizar TerrainData"))
                {
                    GenerateTerrainData(settings, preserveManualEdits, paintSurfaces, placeTrees);
                    RefreshPreview();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Construir / actualizar escenas") && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    BuildScenes(settings);
                if (GUILayout.Button("Validar")) lastReport = Validate(settings);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("POIs: escena ← configuración")) SyncPois(settings);
                if (GUILayout.Button("POIs: configuración ← escena")) PullPois(settings);
                if (GUILayout.Button("Exportar máscaras PNG")) ExportMasks();
            }

            if (preview != null)
            {
                float side = Mathf.Min(position.width - 20f, 384f);
                GUILayout.Label(preview, GUILayout.Width(side), GUILayout.Height(side));
                EditorGUILayout.HelpBox("Azul: mar · arena · verde: tierra (más oscuro = bosque) · gris: roca alta · marrón: caminos · puntos: POIs", MessageType.None);
            }

            if (!string.IsNullOrEmpty(lastReport))
                EditorGUILayout.TextArea(lastReport, GUILayout.MinHeight(160));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parámetros", EditorStyles.boldLabel);
            UnityEditor.Editor.CreateCachedEditor(settings, null, ref settingsEditor);
            settingsEditor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }

        public static IslandTerrainSettings LoadOrCreateSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<IslandTerrainSettings>(SettingsPath);
            if (s != null)
                return s;
            PlaceholderAssetFactory.EnsureFolder(Path.GetDirectoryName(SettingsPath).Replace('\\', '/'));
            s = CreateInstance<IslandTerrainSettings>();
            AssetDatabase.CreateAsset(s, SettingsPath);
            AssetDatabase.SaveAssets();
            return s;
        }

        public static TerrainData GenerateTerrainData(IslandTerrainSettings s, bool preserveManualEdits, bool paintSurfaces, bool placeTrees)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int res = s.heightmapResolution;
            var gen = IslandHeightGenerator.Generate(s, res);

            PlaceholderAssetFactory.EnsureFolder(PlaceholderAssetFactory.Root);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data != null)
                IslandSceneBuilder.BackupAsset(TerrainDataPath);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, TerrainDataPath);
            }
            Undo.RegisterCompleteObjectUndo(data, "Generar terreno de la isla");

            float[,] manualDelta = null;
            if (preserveManualEdits && data.heightmapResolution == res && File.Exists(BaselinePath))
                manualDelta = ComputeManualDelta(data, res);

            // El orden importa: cambiar la resolución reinicia el tamaño.
            data.heightmapResolution = res;
            data.size = new Vector3(s.terrainSize, s.maxHeight, s.terrainSize);
            data.alphamapResolution = s.alphamapResolution;
            data.baseMapResolution = 512;

            var baseline = new float[res, res];
            var final = new float[res, res];
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float n = (gen.heights[z, x] + s.seaFloorDepth) / s.maxHeight;
                    baseline[z, x] = n;
                    final[z, x] = Mathf.Clamp01(n + (manualDelta != null ? manualDelta[z, x] : 0f));
                }
            }
            data.SetHeights(0, 0, final);
            WriteBaseline(baseline, res);

            if (paintSurfaces)
            {
                data.terrainLayers = PlaceholderAssetFactory.EnsureTerrainLayers();
                data.SetAlphamaps(0, 0, IslandSurfacePainter.BuildAlphamaps(s, data, gen));
            }

            if (placeTrees)
            {
                var prefabs = PlaceholderAssetFactory.EnsureTreePrefabs();
                var prototypes = new TreePrototype[prefabs.Length];
                for (int i = 0; i < prefabs.Length; i++)
                    prototypes[i] = new TreePrototype { prefab = prefabs[i] };
                data.treePrototypes = prototypes;
                data.SetTreeInstances(IslandSurfacePainter.PlaceTrees(s, data, gen, prototypes.Length), true);
            }

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CHRONO WILD] Terreno generado (semilla {s.seed}, {res}², {data.treeInstanceCount} árboles, retoques manuales {(manualDelta != null ? "conservados" : "no aplicados")}) en {timer.ElapsedMilliseconds} ms");
            return data;
        }

        static float[,] ComputeManualDelta(TerrainData data, int res)
        {
            var bytes = File.ReadAllBytes(BaselinePath);
            if (bytes.Length != res * res * 4)
                return null;
            var current = data.GetHeights(0, 0, res, res);
            var delta = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    delta[z, x] = current[z, x] - System.BitConverter.ToSingle(bytes, (z * res + x) * 4);
            return delta;
        }

        static void WriteBaseline(float[,] baseline, int res)
        {
            var bytes = new byte[res * res * 4];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    System.BitConverter.GetBytes(baseline[z, x]).CopyTo(bytes, (z * res + x) * 4);
            File.WriteAllBytes(BaselinePath, bytes);
            AssetDatabase.ImportAsset(BaselinePath);
        }

        public static void BuildScenes(IslandTerrainSettings s)
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath) ?? GenerateTerrainData(s, false, true, true);
            IslandSceneBuilder.BuildScenes(s, data);
        }

        public static string Validate(IslandTerrainSettings s)
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null)
                return "No hay TerrainData generado todavía.";
            string report = IslandTerrainValidator.Validate(s, data);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report);
            Debug.Log($"[CHRONO WILD] Informe de validación escrito en {ReportPath}");
            return report;
        }

        static void SyncPois(IslandTerrainSettings s)
        {
            var scene = SceneManager.GetSceneByPath(IslandSceneBuilder.GameplayScenePath);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (!scene.isLoaded || data == null)
            {
                EditorUtility.DisplayDialog("POIs", "Abre Island_Gameplay y genera el terreno primero.", "OK");
                return;
            }
            IslandSceneBuilder.SyncPointsOfInterest(scene, s, data);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static void PullPois(IslandTerrainSettings s)
        {
            var scene = SceneManager.GetSceneByPath(IslandSceneBuilder.GameplayScenePath);
            if (!scene.isLoaded)
            {
                EditorUtility.DisplayDialog("POIs", "Abre Island_Gameplay primero.", "OK");
                return;
            }
            int count = IslandSceneBuilder.PullPointsOfInterest(scene, s);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CHRONO WILD] {count} POIs copiados de la escena a la configuración.");
        }

        static void ExportMasks()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null) return;
            Directory.CreateDirectory(MaskExportFolder);
            var textures = data.alphamapTextures;
            for (int i = 0; i < textures.Length; i++)
            {
                // Cada splatmap RGBA guarda 4 capas en el orden de data.terrainLayers.
                var readable = new Texture2D(textures[i].width, textures[i].height, TextureFormat.RGBA32, false);
                var rt = RenderTexture.GetTemporary(textures[i].width, textures[i].height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(textures[i], rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes($"{MaskExportFolder}/Island_Splat{i}.png", readable.EncodeToPNG());
                DestroyImmediate(readable);
            }
            Debug.Log($"[CHRONO WILD] {textures.Length} splatmaps exportados a {MaskExportFolder}");
        }

        void RefreshPreview()
        {
            if (preview != null) DestroyImmediate(preview);
            preview = BuildPreview(settings, 257);
        }

        public static Texture2D BuildPreview(IslandTerrainSettings s, int res)
        {
            var gen = IslandHeightGenerator.Generate(s, res);
            var noise = new IslandNoise(s.seed);
            var tex = new Texture2D(res, res, TextureFormat.RGB24, false) { filterMode = FilterMode.Point };
            var pixels = new Color[res * res];
            float peak = s.HeightAboveSeaMax;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float h = gen.heights[z, x];
                    var p = new Vector2(x / (float)(res - 1), z / (float)(res - 1));
                    Color c;
                    if (h <= 0f)
                        c = Color.Lerp(new Color(0.35f, 0.65f, 0.75f), new Color(0.05f, 0.18f, 0.35f), -h / s.seaFloorDepth);
                    else if (h < s.beachHeight + 0.8f)
                        c = new Color(0.86f, 0.80f, 0.60f);
                    else
                    {
                        float t = Mathf.Clamp01(h / peak);
                        c = Color.Lerp(new Color(0.42f, 0.58f, 0.28f), new Color(0.55f, 0.52f, 0.48f), Mathf.InverseLerp(0.25f, 0.7f, t));
                        float forest = IslandSurfacePainter.ForestDensity(s, noise, p);
                        c = Color.Lerp(c, new Color(0.15f, 0.32f, 0.15f), forest * (1f - t) * 0.8f);
                        c *= Mathf.Lerp(0.85f, 1.15f, t);
                    }
                    c = Color.Lerp(c, new Color(0.55f, 0.35f, 0.2f), gen.pathMask[z, x]);
                    pixels[z * res + x] = c;
                }
            }
            foreach (var poi in s.pointsOfInterest)
            {
                int px = Mathf.RoundToInt(poi.position.x * (res - 1)), pz = Mathf.RoundToInt(poi.position.y * (res - 1));
                var color = PointOfInterest.ColorFor(poi.type);
                for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int ix = px + dx, iz = pz + dz;
                        if (ix >= 0 && iz >= 0 && ix < res && iz < res) pixels[iz * res + ix] = color;
                    }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        [MenuItem("CHRONO WILD/Terreno/Generar todo (terreno + escenas + validación)")]
        public static void GenerateAll()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var s = LoadOrCreateSettings();
            GenerateTerrainData(s, true, true, true);
            BuildScenes(s);
            Validate(s);
        }

        /// <summary>Guarda la previsualización como PNG (útil para revisar el layout fuera de Unity).</summary>
        public static void SavePreviewPng(string path, int res = 513)
        {
            var tex = BuildPreview(LoadOrCreateSettings(), res);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            DestroyImmediate(tex);
        }
    }
}
