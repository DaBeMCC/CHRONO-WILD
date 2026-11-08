using UnityEngine;

namespace ChronoWild.Rendering
{
    /// <summary>
    /// Drives the "CHRONO WILD/Sky/Volumetric Sky" skybox: atmospheric scattering and ray-marched
    /// volumetric clouds. The sun direction and colour follow RenderSettings.sun (or the light set
    /// here), and the march cost follows the active quality tier (see GraphicsTier.IsLow).
    /// </summary>
    [ExecuteAlways]
    public class VolumetricSky : MonoBehaviour
    {
        [Tooltip("Directional light that acts as the sun. Empty uses RenderSettings.sun.")]
        public Light sun;

        // Filled in by DayNightCycle. With it on, the light may be the moon, so the sky needs the real sun.
        [HideInInspector] public bool externalLighting;
        [HideInInspector] public Vector3 skySunDirection, skyMoonDirection;
        [HideInInspector] public float nightFactor, overcastFactor, thickFactor;
        [HideInInspector] public Color overcastColor;

        [Header("Atmosphere")]
        [Range(0f, 4f)] public float rayleigh = 1.6f;
        [Tooltip("Haze in the air: higher values whiten the horizon and widen the sun glow.")]
        [Range(1f, 20f)] public float turbidity = 4.5f;
        [Range(0f, 0.1f)] public float mieCoefficient = 0.005f;
        [Range(0f, 0.999f)] public float mieAnisotropy = 0.8f;
        [Tooltip("Overall sky brightness, matched to the scene's sun intensity.")]
        [Range(0f, 4f)] public float exposure = 0.3f;
        public float sunIlluminance = 1000f;
        [Tooltip("Colour multiplied into the sky below the horizon.")]
        public Color groundColor = new Color(0.36f, 0.42f, 0.46f);
        [Range(0f, 1f)] public float groundDarkening = 1f;

        [Header("Sun disc")]
        [Range(0.1f, 3f)] public float sunAngularDiameter = 0.9f;
        [Range(0f, 200f)] public float sunDiscIntensity = 40f;

        [Header("Cloud layer")]
        public bool clouds = true;
        [Tooltip("Height (m) of the cloud base.")]
        public float cloudBottom = 1400f;
        [Tooltip("Height (m) of the highest cloud tops.")]
        public float cloudTop = 3600f;
        [Range(0f, 1f)] public float coverage = 0.6f;
        [Tooltip("How much the large-scale weather breaks the layer into separate cloud fields.")]
        [Range(0f, 2f)] public float coverageVariation = 0.9f;
        [Tooltip("Extinction per meter inside a cloud.")]
        [Range(0.001f, 0.1f)] public float density = 0.04f;

        [Header("Cloud shape")]
        [Tooltip("Size (m) of the main cloud shapes.")]
        public float shapeSize = 9000f;
        [Tooltip("Size (m) of the edge erosion detail.")]
        public float detailSize = 1300f;
        [Tooltip("Size (m) of the weather pattern that groups clouds into fields.")]
        public float weatherSize = 40000f;
        [Range(0f, 1f)] public float detailErosion = 0.45f;
        public Vector3 wind = new Vector3(12f, 0f, 5f);

        [Header("Cloud lighting")]
        [Range(0f, 0.99f)] public float forwardScattering = 0.8f;
        [Range(-0.99f, 0f)] public float backScattering = -0.25f;
        [Range(0f, 3f)] public float ambientStrength = 0.8f;
        [Range(0f, 1f)] public float powderEffect = 0.5f;
        [Tooltip("Multiplier applied to the sun light colour inside the clouds.")]
        [Range(0f, 4f)] public float cloudSunScale = 0.7f;

        [Header("Cloud shadows")]
        [Tooltip("How much sunlight a cloud removes from the ground below it (0 disables).")]
        [Range(0f, 1f)] public float cloudShadowStrength = 0.55f;
        [Tooltip("3D shape noise; empty uses the one on the skybox material.")]
        public Texture3D cloudShape;

        [Header("Ray march (High / Low quality)")]
        [Range(8, 128)] public int highSteps = 56;
        [Range(1, 12)] public int highLightSteps = 6;
        [Range(4, 64)] public int lowSteps = 22;
        [Range(1, 8)] public int lowLightSteps = 3;
        [Tooltip("Clouds farther than this (m) are not marched; they are already lost in the haze.")]
        public float maxDistance = 45000f;
        [Tooltip("Distance (m) over which clouds fade into the horizon haze.")]
        public float fadeDistance = 22000f;

        static readonly int LightDirId = Shader.PropertyToID("_VSkyLightDir");
        static readonly int NightId = Shader.PropertyToID("_VSkyNight");
        static readonly int MoonId = Shader.PropertyToID("_VSkyMoon");
        static readonly int GreyId = Shader.PropertyToID("_VSkyGrey");
        static readonly int SunId = Shader.PropertyToID("_VSkySun");
        static readonly int SunColorId = Shader.PropertyToID("_VSkySunColor");
        static readonly int AtmosphereId = Shader.PropertyToID("_VSkyAtmosphere");
        static readonly int ToneId = Shader.PropertyToID("_VSkyTone");
        static readonly int GroundId = Shader.PropertyToID("_VSkyGroundColor");
        static readonly int LayerId = Shader.PropertyToID("_VCloudLayer");
        static readonly int NoiseId = Shader.PropertyToID("_VCloudNoise");
        static readonly int WindId = Shader.PropertyToID("_VCloudWind");
        static readonly int MarchId = Shader.PropertyToID("_VCloudMarch");
        static readonly int LightId = Shader.PropertyToID("_VCloudLight");
        static readonly int QualityId = Shader.PropertyToID("_VCloudQuality");
        static readonly int ShapeTexId = Shader.PropertyToID("_VCloudShapeTex");
        static readonly int ShadowId = Shader.PropertyToID("_VCloudShadow");

        void OnEnable() => Push();
        void OnValidate() => Push();
        void Update() => Push();

        public void Push()
        {
            var light = sun != null ? sun : RenderSettings.sun;
            Vector3 toSun = light != null ? -light.transform.forward : new Vector3(0.3f, 0.6f, 0.4f).normalized;
            Color sunColor = Color.white;
            if (light != null)
            {
                sunColor = light.color.linear * light.intensity;
                if (light.useColorTemperature)
                    sunColor *= Mathf.CorrelatedColorTemperatureToRGB(light.colorTemperature).linear;
            }

            bool low = GraphicsTier.IsLow;
            Vector3 skySun = externalLighting ? skySunDirection : toSun;
            Shader.SetGlobalVector(LightDirId, toSun);
            Shader.SetGlobalVector(MoonId, externalLighting ? skyMoonDirection : Vector3.down);
            Shader.SetGlobalVector(NightId, new Vector4(externalLighting ? nightFactor : 0f, externalLighting ? overcastFactor : 0f, externalLighting ? thickFactor : 0f, 0f));
            Shader.SetGlobalColor(GreyId, overcastColor);
            Shader.SetGlobalVector(SunId, new Vector4(skySun.x, skySun.y, skySun.z, sunAngularDiameter * 0.5f * Mathf.Deg2Rad));
            Shader.SetGlobalVector(AtmosphereId, new Vector4(rayleigh, turbidity, mieCoefficient, mieAnisotropy));
            Shader.SetGlobalVector(ToneId, new Vector4(exposure, sunIlluminance, groundDarkening, 8f));
            Shader.SetGlobalColor(GroundId, groundColor.linear);
            float top = Mathf.Max(cloudTop, cloudBottom + 100f);
            Shader.SetGlobalVector(LayerId, new Vector4(cloudBottom, top, coverage, density));
            Shader.SetGlobalVector(NoiseId, new Vector4(1f / Mathf.Max(shapeSize, 1f), 1f / Mathf.Max(detailSize, 1f), 1f / Mathf.Max(weatherSize, 1f), detailErosion));
            Shader.SetGlobalVector(WindId, new Vector4(wind.x, wind.y, wind.z, coverageVariation));
            Shader.SetGlobalVector(MarchId, new Vector4(low ? lowSteps : highSteps, low ? lowLightSteps : highLightSteps, maxDistance, Mathf.Max(fadeDistance, 1f)));
            Shader.SetGlobalVector(LightId, new Vector4(forwardScattering, backScattering, ambientStrength, powderEffect));
            var shape = cloudShape;
            if (shape == null && RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_CloudShape"))
                shape = RenderSettings.skybox.GetTexture("_CloudShape") as Texture3D;
            if (shape != null)
                Shader.SetGlobalTexture(ShapeTexId, shape);
            Shader.SetGlobalFloat(ShadowId, clouds && shape != null ? cloudShadowStrength : 0f);
            Shader.SetGlobalVector(QualityId, new Vector4(clouds ? 1f : 0f, low ? 0f : 1f, 0f, 0f));
            // The cloud sun colour is scaled separately; the disc intensity compensates so the disc stays put.
            Shader.SetGlobalVector(SunColorId, new Vector4(sunColor.r * cloudSunScale, sunColor.g * cloudSunScale, sunColor.b * cloudSunScale, sunDiscIntensity / Mathf.Max(cloudSunScale, 0.01f)));
        }
    }
}
