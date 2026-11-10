using System.Text;
using UnityEditor;
using UnityEngine;

namespace ChronoWild.Editor.Optimization
{
    /// <summary>
    /// Geometry budget, from measurements in the jungle (the heaviest view): the LOD bias drives
    /// how far detailed meshes are kept, and small ground clutter can disappear sooner because
    /// beyond ~40 m it is hidden between trunks anyway. The intro raises the LOD bias again for
    /// its wide shots (IntroCinematic.cinematicLodBias).
    /// </summary>
    public static class GeometryBudgets
    {
        const float GameplayLodBias = 1.0f;

        // Prefab -> screen height (fraction) below which it is culled.
        static readonly (string path, float cull)[] Clutter =
        {
            ("Assets/_Game/Art/Prefabs/Vegetation/JurassicRealism/PF_JR_GroundFern_A.prefab", 0.05f),
            ("Assets/_Game/Art/Prefabs/Vegetation/JurassicRealism/PF_JR_GroundFern_B.prefab", 0.05f),
            ("Assets/_Game/Art/Prefabs/Vegetation/JurassicRealism/PF_JR_GroundFern_C.prefab", 0.05f),
            ("Assets/_Game/Art/Prefabs/Vegetation/JurassicRealism/PF_JR_ForestDebris_A.prefab", 0.04f),
            ("Assets/_Game/Art/Prefabs/Vegetation/JurassicRealism/PF_JR_ForestDebris_B.prefab", 0.04f),
            ("Assets/_Game/Art/Prefabs/Vegetation/DepthStudy/PF_MossPatch.prefab", 0.07f),
        };

        [MenuItem("CHRONO WILD/Optimization/Apply Geometry Budgets")]
        public static string Apply()
        {
            var report = new StringBuilder();
            // LOD bias of the High quality level (Low keeps its own lower value).
            int current = QualitySettings.GetQualityLevel();
            int high = System.Array.IndexOf(QualitySettings.names, "High");
            if (high >= 0)
            {
                QualitySettings.SetQualityLevel(high, false);
                report.AppendLine($"High LOD bias {QualitySettings.lodBias} -> {GameplayLodBias}");
                QualitySettings.lodBias = GameplayLodBias;
                QualitySettings.SetQualityLevel(current, false);
            }
            foreach (var (path, cull) in Clutter)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var group = root.GetComponentInChildren<LODGroup>();
                    if (group == null) { report.AppendLine("no LODGroup: " + path); continue; }
                    var lods = group.GetLODs();
                    var last = lods.Length - 1;
                    float before = lods[last].screenRelativeTransitionHeight;
                    lods[last].screenRelativeTransitionHeight = Mathf.Max(before, cull);
                    // Keep the transitions strictly decreasing.
                    for (int i = last - 1; i >= 0; i--)
                        lods[i].screenRelativeTransitionHeight = Mathf.Max(lods[i].screenRelativeTransitionHeight, lods[i + 1].screenRelativeTransitionHeight + 0.01f);
                    group.SetLODs(lods);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    report.AppendLine($"{System.IO.Path.GetFileNameWithoutExtension(path)} cull {before:F3} -> {lods[last].screenRelativeTransitionHeight:F3}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Geometry budgets applied:\n" + report);
            return report.ToString();
        }
    }
}
