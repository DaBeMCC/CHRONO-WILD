using UnityEngine;

namespace ChronoWild.Rendering
{
    /// <summary>
    /// Scene settings for the humid jungle atmosphere: height fog that pools near the ground and
    /// sunlight scattered through it, shadowed by the canopy (volumetric light shafts).
    /// JungleFogFeature only runs while one of these is enabled, so other scenes pay nothing.
    /// </summary>
    [ExecuteAlways]
    public class JungleAtmosphere : MonoBehaviour
    {
        public static JungleAtmosphere Active { get; private set; }

        [Header("Height fog")]
        [Tooltip("Extinction per meter at the reference height.")]
        [Range(0f, 0.2f)] public float density = 0.025f;
        [Tooltip("World height (m) where the density equals the value above.")]
        public float baseHeight = 8f;
        [Tooltip("How fast the fog thins out with height (1/m).")]
        [Range(0.001f, 1f)] public float heightFalloff = 0.12f;
        [Tooltip("Fog color lit only by the sky and forest bounce.")]
        public Color ambientColor = new Color(0.34f, 0.40f, 0.36f);
        [Tooltip("Distance used for the sky so it fades into the haze instead of staying sharp.")]
        public float skyDistance = 300f;

        [Header("Drifting humidity")]
        [Range(0f, 1f)] public float noiseAmount = 0.45f;
        public float noiseScale = 0.06f;
        public Vector3 windVelocity = new Vector3(0.35f, 0.02f, 0.2f);

        [Header("Sun scattering (light shafts)")]
        [Range(0f, 8f)] public float sunScattering = 1.6f;
        [Tooltip("Forward scattering: higher values concentrate shafts around the sun.")]
        [Range(0f, 0.95f)] public float anisotropy = 0.6f;
        [Tooltip("Meters of the view ray that are marched against the shadow map.")]
        public float marchDistance = 60f;
        [Range(8, 64)] public int marchSteps = 28;

        [Header("Distance haze")]
        [Tooltip("Extinction per meter beyond the start distance; fades far objects into the sky color.")]
        [Range(0f, 0.05f)] public float hazeDensity = 0.012f;
        public float hazeStart = 18f;
        [Tooltip("How fast the haze thins with height (1/m). Low values keep hills hazy.")]
        [Range(0f, 0.2f)] public float hazeHeightFalloff = 0.012f;
        public Color hazeColor = new Color(0.55f, 0.62f, 0.62f);
        [Range(0f, 1f)] public float hazeSunTint = 0.12f;

        static readonly int FogParams = Shader.PropertyToID("_JungleFogParams");
        static readonly int FogAmbient = Shader.PropertyToID("_JungleFogAmbient");
        static readonly int FogNoise = Shader.PropertyToID("_JungleFogNoise");
        static readonly int FogWind = Shader.PropertyToID("_JungleFogWind");
        static readonly int FogMarch = Shader.PropertyToID("_JungleFogMarch");
        static readonly int Haze = Shader.PropertyToID("_JungleHaze");
        static readonly int HazeColor = Shader.PropertyToID("_JungleHazeColor");

        void OnEnable() => Active = this;

        void OnDisable()
        {
            if (Active == this)
                Active = null;
        }

        void Update()
        {
            if (Active != this && Active == null)
                Active = this;
            Push();
        }

        void OnValidate() => Push();

        public void Push()
        {
            Shader.SetGlobalVector(FogParams, new Vector4(density, baseHeight, heightFalloff, skyDistance));
            Shader.SetGlobalColor(FogAmbient, ambientColor.linear);
            Shader.SetGlobalVector(FogNoise, new Vector4(noiseAmount, noiseScale, 0f, 0f));
            Shader.SetGlobalVector(FogWind, windVelocity);
            Shader.SetGlobalVector(FogMarch, new Vector4(sunScattering, anisotropy, marchDistance, marchSteps));
            Shader.SetGlobalVector(Haze, new Vector4(hazeDensity, hazeStart, hazeHeightFalloff, 0f));
            var haze = hazeColor.linear;
            Shader.SetGlobalVector(HazeColor, new Vector4(haze.r, haze.g, haze.b, hazeSunTint));
        }
    }
}
