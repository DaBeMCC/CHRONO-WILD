using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChronoWild.Editor.Art
{
    public static class TerrainPbrReview
    {
        const string Layers = "Assets/_Game/Art/Terrain/Generated/Layers";
        const string Textures = "Assets/_Game/Art/Textures/Environment/Terrain";
        static readonly string[] Names = { "SandDry", "SandWet", "LeafLitter", "Mud", "Rock", "CliffRock", "PathDirt" };
        static readonly string[] Zones = { "PlayaSur", "CaminoPrincipal", "AcantiladoNE" };
        static readonly Vector3[] Positions = { new Vector3(300, 3.2f, 97), new Vector3(274, 12, 205), new Vector3(508, 36, 450) };
        static readonly Vector3[] Targets = { new Vector3(315, .4f, 87), new Vector3(284, 9, 237), new Vector3(450, 26, 420) };
        static string TexturePath(string name, string suffix)
        {
            if (name == "CliffRock") name = "Rock";
            if (name == "SandWet" && suffix == "Normal.png") name = "SandDry";
            return $"{Textures}/T_{name}_{suffix}";
        }

        [MenuItem("CHRONO WILD/Arte/Aplicar siete capas PBR")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Sal de Play antes de aplicar texturas.");
            // Comprobar todo antes de modificar ninguna capa.
            foreach (string name in Names)
            {
                if (AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{Layers}/TL_{name}.terrainlayer") == null)
                    throw new FileNotFoundException("Falta la capa existente " + name);
                foreach (string suffix in new[] { "Albedo.jpg", "Normal.png", "Mask.png" })
                    if (!File.Exists(TexturePath(name, suffix))) throw new FileNotFoundException(name + " " + suffix);
            }
            AssetDatabase.Refresh();
            foreach (string file in Directory.GetFiles(Textures).Where(p => p.EndsWith(".jpg") || p.EndsWith(".png")))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(file);
                bool normal = file.Contains("_Normal"), mask = file.Contains("_Mask");
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !normal && !mask;
                importer.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                importer.isReadable = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.maxTextureSize = 1024;
                importer.anisoLevel = 4;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                foreach (string platform in new[] { "Standalone", "WebGL" })
                    importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                    {
                        name = platform, overridden = true, maxTextureSize = 1024, compressionQuality = 100,
                        format = normal && platform == "Standalone" ? TextureImporterFormat.BC5 :
                            normal || mask ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1
                    });
                importer.SaveAndReimport();
            }
            foreach (string name in Names)
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{Layers}/TL_{name}.terrainlayer");
                Undo.RecordObject(layer, "Aplicar PBR " + name);
                layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(name, "Albedo.jpg"));
                layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(name, "Normal.png"));
                layer.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath(name, "Mask.png"));
                float size = name.Contains("Rock") ? 4f : name == "LeafLitter" ? 2.5f : 3f;
                layer.tileSize = new Vector2(size, size);
                layer.normalScale = name == "SandWet" ? .3f : name == "SandDry" ? .45f : .65f;
                layer.metallic = 0f;
                layer.smoothness = 1f;
                EditorUtility.SetDirty(layer);
                AssetDatabase.SaveAssetIfDirty(layer);
            }
            // Retirar únicamente duplicados de esta fase, una vez reasignadas sus referencias.
            foreach (string duplicate in new[] { "SandWet_Normal.png", "CliffRock_Albedo.jpg", "CliffRock_Normal.png", "CliffRock_Mask.png" })
                if (File.Exists($"{Textures}/T_{duplicate}")) AssetDatabase.DeleteAsset($"{Textures}/T_{duplicate}");
        }

        public static void Capture(string suffix)
        {
            Directory.CreateDirectory("Exports/TerrainPbr");
            var go = new GameObject("Terrain PBR Capture") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            var rt = new RenderTexture(1280, 720, 24);
            var previous = RenderTexture.active;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt;
                camera.fieldOfView = 58f;
                camera.farClipPlane = 650f;
                for (int i = 0; i < Zones.Length; i++)
                {
                    camera.transform.position = Positions[i];
                    camera.transform.LookAt(Targets[i]);
                    camera.Render();
                    RenderTexture.active = rt;
                    image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    image.Apply();
                    File.WriteAllBytes($"Exports/TerrainPbr/{Zones[i]}_{suffix}.png", image.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        public static string Measure()
        {
            var original = Object.FindObjectsByType<UnityEngine.Terrain>().First(t => t.name == "Island_Terrain");
            var go = new GameObject("Terrain PBR Bench") { hideFlags = HideFlags.HideAndDontSave, layer = 31 };
            var terrain = go.AddComponent<UnityEngine.Terrain>();
            terrain.terrainData = original.terrainData;
            go.transform.position = original.transform.position;
            terrain.materialTemplate = original.materialTemplate;
            terrain.drawInstanced = original.drawInstanced;
            terrain.heightmapPixelError = original.heightmapPixelError;
            terrain.drawTreesAndFoliage = false;
            var cameraObject = new GameObject("Terrain PBR Bench Camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.cullingMask = 1 << 31;
            camera.fieldOfView = 58f;
            camera.farClipPlane = 650;
            var rt = new RenderTexture(1280, 720, 24);
            camera.targetTexture = rt;
            var previous = RenderTexture.active;
            var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var report = new List<string>
            {
                "# Fase 2 · Medición del terreno",
                "GPU: " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType,
                "CPU: " + SystemInfo.processorType,
                "Unity " + Application.unityVersion + " · Calidad actual: " + QualitySettings.names[QualitySettings.GetQualityLevel()],
                "Salida 1280 × 720; renderScale del perfil existente sin modificar. Terreno aislado, sin vegetación, agua ni objetos.",
                "Tiempo de pared de Camera.Render + ReadPixels síncrono de un píxel + Apply. Incluye CPU, GPU, driver y readback; NO es tiempo GPU aislado ni FPS de gameplay.",
                "Por zona y variante: 3 rondas alternando el orden, 8 calentamientos y 24 muestras por ronda (72 muestras). No se guarda ninguna variante de terreno o calidad.",
                "| Zona | Variante | Mediana ms | P95 ms | Mín ms | Máx ms |",
                "|---|---|---:|---:|---:|---:|"
            };
            try
            {
                for (int zone = 0; zone < Zones.Length; zone++)
                {
                    camera.transform.position = Positions[zone];
                    camera.transform.LookAt(Targets[zone]);
                    var samples = new[] { new List<double>(), new List<double>() };
                    for (int round = 0; round < 3; round++)
                        for (int step = 0; step < 2; step++)
                        {
                            int variant = (round + step) % 2;
                            terrain.basemapDistance = variant == 0 ? 10000f : 0f;
                            for (int frame = -8; frame < 24; frame++)
                            {
                                var watch = Stopwatch.StartNew();
                                camera.Render();
                                RenderTexture.active = rt;
                                pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                                pixel.Apply(false);
                                watch.Stop();
                                if (frame >= 0) samples[variant].Add(watch.Elapsed.TotalMilliseconds);
                            }
                        }
                    for (int variant = 0; variant < 2; variant++)
                    {
                        var sorted = samples[variant].OrderBy(v => v).ToArray();
                        report.Add($"| {Zones[zone]} | {(variant == 0 ? "8 capas, detalle" : "Solo basemap")} | {sorted[36]:F3} | {sorted[68]:F3} | {sorted[0]:F3} | {sorted[71]:F3} |");
                    }
                }
                report.Add("\n## Texturas de las ocho capas");
                var textures = terrain.terrainData.terrainLayers.SelectMany(l => new[] { l.diffuseTexture, l.normalMapTexture, l.maskMapTexture }).Where(t => t != null).Distinct().ToArray();
                long bytes = textures.Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));
                report.Add($"{textures.Length} Texture2D; {bytes} bytes ({bytes / 1048576.0:F2} MiB) según Profiler en editor. No equivale a VRAM aislada ni a memoria total del proceso.");
                foreach (var t in textures) report.Add($"- {t.name}: {t.width} × {t.height}, {t.format}.");
                Directory.CreateDirectory("Docs/Art");
                File.WriteAllText("Docs/Art/Fase2_Mediciones.md", string.Join("\n", report));
                return string.Join("\n", report.Take(14));
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(pixel);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(go);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
