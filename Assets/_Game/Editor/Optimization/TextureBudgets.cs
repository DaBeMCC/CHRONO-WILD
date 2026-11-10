using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ChronoWild.Editor.Optimization
{
    /// <summary>
    /// Texture budget by role: how large a texture may be depends on how big and how close the
    /// thing wearing it gets on screen. Small props get 512, foliage 1024, characters, hero trees,
    /// rocks and terrain layers 2048; nothing is ever enlarged. Mipmap streaming is enabled on
    /// every 3D texture except terrain layers (the terrain shader reports no UV density, so
    /// streaming would load them blurry) and UI.
    /// </summary>
    public static class TextureBudgets
    {
        struct Rule
        {
            public string match;   // path fragment
            public int maxSize;
            public int maxSizeMasks; // for masks/height/AO maps (often lower)
            public bool stream;
            public string role;
            public Rule(string match, int maxSize, int maxSizeMasks, bool stream, string role) { this.match = match; this.maxSize = maxSize; this.maxSizeMasks = maxSizeMasks; this.stream = stream; this.role = role; }
        }

        // First match wins.
        static readonly Rule[] Rules =
        {
            new Rule("/Textures/Environment/Sky", 4096, 4096, false, "sky"),
            new Rule("/Textures/Terrain/", 2048, 1024, false, "terrain layer"),
            new Rule("/Biomes/TerrainLayers", 2048, 1024, false, "terrain layer"),
            new Rule("/Terrain/Generated", 2048, 1024, false, "terrain layer"),
            new Rule("Assets/Survival Tools/", 512, 512, true, "small prop"),
            new Rule("Assets/HorrorPuzzleItems/", 512, 512, true, "small prop"),
            new Rule("Assets/ModularFirstPersonController/", 512, 512, true, "demo prop"),
            new Rule("Assets/PolyOne/", 1024, 512, true, "first-person hands"),
            new Rule("Assets/FerociousIndustries/", 2048, 1024, true, "character"),
            new Rule("/Art/Story/", 1024, 1024, true, "time machine and parts"),
            new Rule("/Vegetation/HeroForest", 2048, 1024, true, "hero tree"),
            new Rule("/Vegetation/Impostors", 1024, 1024, true, "far impostor"),
            new Rule("/Vegetation/", 1024, 512, true, "foliage"),
            new Rule("/Textures/Geology", 2048, 1024, true, "rock and cliff"),
            new Rule("/Biomes/Textures", 2048, 1024, true, "rock and cliff"),
            new Rule("/Textures/Environment/", 1024, 512, true, "ground patch"),
            new Rule("Assets/", 2048, 1024, true, "default"),
        };

        static bool IsMask(string path)
        {
            string n = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            return n.Contains("mask") || n.Contains("height") || n.Contains("_ao") || n.Contains("rough") || n.Contains("metal") || n.Contains("smooth");
        }

        [MenuItem("CHRONO WILD/Optimization/Apply Texture Budgets")]
        public static void Apply() => Run(true);

        [MenuItem("CHRONO WILD/Optimization/Report Texture Budgets (dry run)")]
        public static void Report() => Run(false);

        public static string Run(bool apply)
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });
            var report = new StringBuilder();
            int changed = 0, streamed = 0;
            var perRole = new Dictionary<string, int>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                        continue;
                    bool ui = importer.textureType == TextureImporterType.Sprite || importer.textureType == TextureImporterType.GUI ||
                              path.Contains("/Icons/") || path.StartsWith("Assets/ChronoWild/Art/");
                    var rule = Rules.First(r => path.Contains(r.match));
                    int cap = ui ? 2048 : (IsMask(path) ? rule.maxSizeMasks : rule.maxSize);
                    // Atlases pack several plants into one texture and are seen up close: they keep 2048.
                    if (!ui && path.Contains("Atlas")) cap = Mathf.Max(cap, 2048);
                    bool stream = !ui && rule.stream && importer.mipmapEnabled;
                    bool dirty = false;
                    var notes = new List<string>();

                    if (importer.maxTextureSize > cap) { notes.Add($"{importer.maxTextureSize}->{cap}"); if (apply) importer.maxTextureSize = cap; dirty = true; }
                    foreach (var platform in new[] { "Standalone", "WebGL" })
                    {
                        var s = importer.GetPlatformTextureSettings(platform);
                        if (s.overridden && s.maxTextureSize > cap)
                        {
                            notes.Add($"{platform} {s.maxTextureSize}->{cap}");
                            if (apply) { s.maxTextureSize = cap; importer.SetPlatformTextureSettings(s); }
                            dirty = true;
                        }
                    }
                    if (ui && importer.mipmapEnabled) { notes.Add("UI: no mipmaps"); if (apply) importer.mipmapEnabled = false; dirty = true; }
                    if (importer.streamingMipmaps != stream) { notes.Add(stream ? "stream" : "no stream"); if (apply) importer.streamingMipmaps = stream; dirty = true; }
                    if (stream) streamed++;
                    if (!dirty)
                        continue;
                    changed++;
                    string role = ui ? "UI" : rule.role;
                    perRole[role] = perRole.TryGetValue(role, out var c) ? c + 1 : 1;
                    report.AppendLine($"{role,-22} {string.Join(", ", notes),-28} {path}");
                    if (apply) importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            string summary = $"Texture budgets {(apply ? "applied" : "(dry run)")}: {changed} of {guids.Length} textures changed, {streamed} stream their mipmaps. By role: " +
                             string.Join(", ", perRole.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}"));
            Debug.Log(summary + "\n" + report);
            return summary + "\n" + report;
        }
    }
}
