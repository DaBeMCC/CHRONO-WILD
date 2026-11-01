using UnityEngine;

namespace ChronoWild.Rendering
{
    /// <summary>
    /// Applies the scene-side half of a quality level: shader globals, terrain parallax and detail
    /// density, relief patches and fog light shafts. The render pipeline asset assigned to each
    /// Quality level handles shadows, SSAO and HDR. Levels whose name contains "Low" use the low tier.
    /// </summary>
    [ExecuteAlways]
    public class GraphicsTier : MonoBehaviour
    {
        public Terrain terrain;
        [Tooltip("Silhouette parallax patches; disabled on low.")]
        public GameObject reliefPatches;
        public JungleAtmosphere atmosphere;

        [Header("High")]
        public float highParallaxDepth = 0.085f;
        public float highDetailDistance = 70f;
        [Range(0f, 1f)] public float highDetailDensity = 1f;
        public int highFogSteps = 32;
        public float highLodBias = 1.5f;

        [Header("Low")]
        public float lowDetailDistance = 35f;
        [Range(0f, 1f)] public float lowDetailDensity = 0.5f;
        public float lowLodBias = 0.7f;

        static readonly int Quality = Shader.PropertyToID("_ChronoQuality");
        static readonly int ParallaxDepth = Shader.PropertyToID("_JF_Depth");
        static readonly int ParallaxShadow = Shader.PropertyToID("_JF_ShadowStrength");

        int appliedLevel = -1;

        public static bool IsLow => QualitySettings.names[QualitySettings.GetQualityLevel()].Contains("Low");

        void OnEnable() => Apply();

        void Update()
        {
            if (appliedLevel != QualitySettings.GetQualityLevel())
                Apply();
        }

        public void Apply()
        {
            appliedLevel = QualitySettings.GetQualityLevel();
            bool low = IsLow;
            Shader.SetGlobalFloat(Quality, low ? 0f : 1f);
            QualitySettings.lodBias = low ? lowLodBias : highLodBias;

            if (terrain != null)
            {
                var material = terrain.materialTemplate;
                if (material != null && material.HasProperty(ParallaxDepth))
                {
                    material.SetFloat(ParallaxDepth, low ? 0f : highParallaxDepth);
                    material.SetFloat(ParallaxShadow, low ? 0f : 0.85f);
                }
                terrain.detailObjectDistance = low ? lowDetailDistance : highDetailDistance;
                terrain.detailObjectDensity = low ? lowDetailDensity : highDetailDensity;
            }
            if (reliefPatches != null)
                reliefPatches.SetActive(!low);
            if (atmosphere != null)
            {
                atmosphere.marchSteps = low ? 0 : highFogSteps;
                atmosphere.Push();
            }
        }
    }
}
