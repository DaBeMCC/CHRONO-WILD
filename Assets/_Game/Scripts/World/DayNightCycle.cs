using ChronoWild.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChronoWild.World
{
    /// <summary>
    /// World clock and everything that follows it: sun and moon, ambient light, fog colours, sky and
    /// sea reflections. Weather (from WeatherSystem) is layered on top of the time-of-day colours.
    /// One directional light is used as sun by day and moon by night, swapping while it is dark at
    /// the horizon, so shadows keep working with no second shadow map.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class DayNightCycle : MonoBehaviour
    {
        public static DayNightCycle Instance { get; private set; }

        [Header("Clock")]
        [Tooltip("Real minutes for a full 24 h game day.")]
        public float dayLengthMinutes = 24f;
        [Range(0f, 24f)] public float hour = 9f;
        public int day = 1;
        public bool paused;

        [Header("Scene")]
        public Light mainLight;
        public VolumetricSky sky;
        public JungleAtmosphere atmosphere;
        [Tooltip("Sun tilt away from straight overhead (degrees).")]
        public float solarTilt = 19f;
        [Tooltip("Unlit particles (surf spray) that must dim with the light.")]
        public Renderer[] unlitParticles = new Renderer[0];
        [Tooltip("Compass direction of sunrise (degrees).")]
        public float sunriseAzimuth = 70f;

        struct Key
        {
            public float hour;
            public Color light; public float intensity;
            public Color sky, ground, haze, zenith, horizon, grey;
            public Key(float h, Color l, float i, Color s, Color g, Color hz, Color z, Color ho, Color gr)
            { hour = h; light = l; intensity = i; sky = s; ground = g; haze = hz; zenith = z; horizon = ho; grey = gr; }
        }

        static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

        // Values are linear. Sky/ground: ambient light. Zenith/horizon: what the sea reflects. Grey: overcast sky.
        static readonly Key[] Keys =
        {
            new Key(0f,    C(0.55f,0.66f,0.95f), 0.34f, C(0.085f,0.11f,0.2f), C(0.035f,0.045f,0.08f), C(0.03f,0.045f,0.09f), C(0.015f,0.025f,0.06f), C(0.04f,0.055f,0.11f), C(0.03f,0.035f,0.055f)),
            new Key(4.8f,  C(0.55f,0.66f,0.95f), 0.34f, C(0.09f,0.115f,0.21f), C(0.038f,0.048f,0.085f),C(0.04f,0.055f,0.1f),  C(0.02f,0.032f,0.07f), C(0.06f,0.07f,0.13f),  C(0.034f,0.04f,0.06f)),
            new Key(5.7f,  C(1f,0.5f,0.26f),     0.25f, C(0.2f,0.2f,0.3f),     C(0.08f,0.06f,0.06f),   C(0.56f,0.34f,0.3f),    C(0.1f,0.15f,0.3f),     C(0.8f,0.42f,0.28f),  C(0.13f,0.12f,0.13f)),
            new Key(6.6f,  C(1f,0.58f,0.3f),     1.25f, C(0.42f,0.4f,0.44f),   C(0.16f,0.12f,0.1f),    C(0.92f,0.62f,0.42f),   C(0.24f,0.34f,0.58f),   C(1f,0.66f,0.42f),    C(0.3f,0.28f,0.28f)),
            new Key(8.5f,  C(1f,0.86f,0.68f),    2.3f,  C(0.5f,0.58f,0.7f),    C(0.22f,0.19f,0.16f),   C(0.66f,0.74f,0.84f),   C(0.29f,0.46f,0.76f),   C(0.5f,0.64f,0.8f),   C(0.42f,0.43f,0.45f)),
            new Key(13f,   C(1f,0.95f,0.86f),    2.8f,  C(0.52f,0.62f,0.78f),  C(0.25f,0.22f,0.18f),   C(0.62f,0.74f,0.86f),   C(0.3f,0.48f,0.78f),    C(0.46f,0.62f,0.8f),  C(0.46f,0.47f,0.49f)),
            new Key(16.2f, C(1f,0.87f,0.68f),    2.4f,  C(0.52f,0.58f,0.68f),  C(0.24f,0.2f,0.16f),    C(0.7f,0.74f,0.78f),    C(0.28f,0.44f,0.72f),   C(0.58f,0.64f,0.72f), C(0.42f,0.42f,0.43f)),
            new Key(17.5f, C(1f,0.56f,0.26f),    1.5f,  C(0.46f,0.4f,0.42f),   C(0.18f,0.13f,0.1f),    C(1f,0.6f,0.34f),       C(0.26f,0.32f,0.56f),   C(1f,0.56f,0.32f),    C(0.3f,0.26f,0.25f)),
            new Key(18.3f, C(1f,0.34f,0.18f),    0.32f, C(0.22f,0.17f,0.26f),  C(0.08f,0.055f,0.06f),  C(0.7f,0.28f,0.26f),    C(0.1f,0.13f,0.3f),     C(0.86f,0.3f,0.24f),  C(0.13f,0.1f,0.11f)),
            new Key(19.4f, C(0.55f,0.66f,0.95f), 0.26f, C(0.1f,0.11f,0.2f),  C(0.04f,0.045f,0.08f), C(0.07f,0.07f,0.14f),   C(0.026f,0.036f,0.09f), C(0.08f,0.08f,0.16f), C(0.04f,0.042f,0.06f)),
            new Key(24f,   C(0.55f,0.66f,0.95f), 0.34f, C(0.085f,0.11f,0.2f), C(0.035f,0.045f,0.08f), C(0.03f,0.045f,0.09f), C(0.015f,0.025f,0.06f), C(0.04f,0.055f,0.11f), C(0.03f,0.035f,0.055f)),
        };

        public float Daylight { get; private set; }
        public float Night => 1f - Daylight;
        /// <summary>Time of day as hh:mm.</summary>
        public string TimeText => $"{Mathf.FloorToInt(hour):00}:{Mathf.FloorToInt(hour % 1f * 60f):00}";

        /// <summary>Clock text for the debug label.</summary>
        public string ClockText => $"Day {day}  {Mathf.FloorToInt(hour):00}:{Mathf.FloorToInt(hour % 1f * 60f):00}";

        static readonly int SkyZenithId = Shader.PropertyToID("_CW_SkyZ");
        static readonly int SkyHorizonId = Shader.PropertyToID("_CW_SkyH");
        static readonly int EnvDimId = Shader.PropertyToID("_CW_EnvDim");

        Color baseForestFog, baseForestHaze;
        Volume probeVolumeOverride;
        ProbeVolumesOptions probeOptions;
        VolumeProfile runtimeProfile;
        float nextAmbientUpdate;
        bool originalTemperature;
        float originalIntensity;
        Color originalColor;
        Quaternion originalRotation;
        UnityEngine.Rendering.SphericalHarmonicsL2 originalProbe;
        float originalShadowStrength, originalExposure;
        Material[] unlitInstances;
        Color[] unlitColors;

        void OnEnable()
        {
            Instance = this;
            baseForestFog = atmosphere.ambientColor;
            baseForestHaze = atmosphere.hazeColor;
            originalTemperature = mainLight.useColorTemperature;
            originalIntensity = mainLight.intensity;
            originalColor = mainLight.color;
            originalRotation = mainLight.transform.rotation;
            originalProbe = RenderSettings.ambientProbe;
            originalShadowStrength = mainLight.shadowStrength;
            mainLight.useColorTemperature = false;
            originalExposure = sky.exposure;
            unlitInstances = new Material[unlitParticles.Length];
            unlitColors = new Color[unlitParticles.Length];
            for (int i = 0; i < unlitParticles.Length; i++)
            {
                unlitInstances[i] = unlitParticles[i].material; // a runtime copy, the asset stays untouched
                unlitColors[i] = unlitInstances[i].GetColor("_BaseColor");
            }

            // The jungle's baked light probes hold daylight; this blends them out in the dark.
            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            probeOptions = runtimeProfile.Add<ProbeVolumesOptions>(true);
            var go = new GameObject("Probe Light Scale");
            go.transform.SetParent(transform, false);
            probeVolumeOverride = go.AddComponent<Volume>();
            probeVolumeOverride.isGlobal = true;
            probeVolumeOverride.priority = 50f;
            probeVolumeOverride.sharedProfile = runtimeProfile;
            sky.externalLighting = true;
        }

        void OnDisable()
        {
            if (Instance == this)
                Instance = null;
            if (sky != null)
                { sky.externalLighting = false; sky.exposure = originalExposure; }
            if (mainLight != null)
            {
                mainLight.useColorTemperature = originalTemperature;
                mainLight.intensity = originalIntensity;
                mainLight.color = originalColor;
                mainLight.transform.rotation = originalRotation;
                mainLight.shadowStrength = originalShadowStrength;
            }
            RenderSettings.ambientProbe = originalProbe;
            Shader.SetGlobalVector(SkyZenithId, Vector4.zero);
            Shader.SetGlobalVector(SkyHorizonId, Vector4.zero);
            Shader.SetGlobalFloat(EnvDimId, 0f);
            if (probeVolumeOverride != null)
                Destroy(probeVolumeOverride.gameObject);
            if (runtimeProfile != null)
                Destroy(runtimeProfile);
        }

        public void SetHour(float value)
        {
            hour = Mathf.Repeat(value, 24f);
        }

        static Key Sample(float h)
        {
            for (int i = 0; i < Keys.Length - 1; i++)
            {
                if (h > Keys[i + 1].hour)
                    continue;
                Key a = Keys[i], b = Keys[i + 1];
                float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.hour, b.hour, h));
                return new Key(h, Color.Lerp(a.light, b.light, t), Mathf.Lerp(a.intensity, b.intensity, t), Color.Lerp(a.sky, b.sky, t), Color.Lerp(a.ground, b.ground, t),
                    Color.Lerp(a.haze, b.haze, t), Color.Lerp(a.zenith, b.zenith, t), Color.Lerp(a.horizon, b.horizon, t), Color.Lerp(a.grey, b.grey, t));
            }
            return Keys[Keys.Length - 1];
        }

        static Color Greyed(Color c, float amount, float scale)
        {
            float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            return Color.Lerp(c, new Color(lum, lum, lum * 1.04f, 1f), amount) * scale;
        }

        void Update()
        {
            if (!paused)
            {
                hour += Time.deltaTime / (Mathf.Max(dayLengthMinutes, 0.01f) * 60f) * 24f;
                if (hour >= 24f)
                {
                    hour -= 24f;
                    day++;
                }
            }

            var q = Quaternion.AngleAxis(sunriseAzimuth, Vector3.up);
            Vector3 east = q * Vector3.right, south = q * Vector3.back;
            float angle = (hour - 6f) / 12f * Mathf.PI;
            Vector3 overhead = (Vector3.up * Mathf.Cos(solarTilt * Mathf.Deg2Rad) + south * Mathf.Sin(solarTilt * Mathf.Deg2Rad)).normalized;
            Vector3 toSun = east * Mathf.Cos(angle) + overhead * Mathf.Sin(angle);
            // The moon rides opposite the sun, offset so it is not exactly mirrored.
            Vector3 toMoon = (-toSun + south * 0.22f).normalized;

            float elevation = toSun.y;
            Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0.16f, elevation));

            var weather = WeatherSystem.Instance;
            float overcast = weather != null ? weather.Overcast : 0f;
            float rain = weather != null ? weather.Rain : 0f;
            float thick = weather != null ? weather.Thick * overcast : 0f;
            var key = Sample(hour);

            // The light is moved while it is at its dimmest, right at the horizon.
            bool sunUp = elevation >= 0f;
            float swapFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.04f, Mathf.Abs(elevation)));
            mainLight.transform.rotation = Quaternion.LookRotation(sunUp ? -toSun : -toMoon, Vector3.up);
            mainLight.color = Greyed(key.light, overcast * 0.7f, 1f);
            mainLight.intensity = key.intensity * swapFade * Mathf.Lerp(1f, 0.3f, overcast) * Mathf.Lerp(1f, 0.42f, thick) * Mathf.Lerp(1f, 0.8f, rain);
            mainLight.shadowStrength = Mathf.Lerp(1f, 0.25f, overcast) * Mathf.Lerp(1f, 0.3f, thick);

            float ambientScale = Mathf.Lerp(1f, 0.92f, overcast) * Mathf.Lerp(1f, 0.72f, thick) * Mathf.Lerp(1f, 0.9f, rain);
            Color skyLight = Greyed(key.sky, overcast * 0.85f, ambientScale);
            Color groundLight = Greyed(key.ground, overcast * 0.6f, ambientScale);
            if (Time.time >= nextAmbientUpdate)
            {
                nextAmbientUpdate = Time.time + 0.2f;
                var sh = new SphericalHarmonicsL2();
                sh.AddAmbientLight((skyLight + groundLight) * 0.5f);
                sh.AddDirectionalLight(Vector3.up, (skyLight - groundLight) * 0.6f, 1f);
                RenderSettings.ambientProbe = sh;
            }
            probeOptions.intensityMultiplier.Override(Mathf.Clamp01(Daylight * Mathf.Lerp(1f, 0.7f, overcast)));

            Color haze = Greyed(key.haze, overcast * 0.8f, Mathf.Lerp(1f, 0.72f, overcast));
            float forestScale = Mathf.Lerp(0.05f, 1f, Daylight) * Mathf.Lerp(1f, 0.7f, overcast);
            atmosphere.ambientColor = baseForestFog * forestScale + haze * 0.08f;
            atmosphere.hazeColor = Color.Lerp(baseForestHaze * forestScale, haze, 0.35f);
            atmosphere.openHazeColor = haze;

            sky.skySunDirection = toSun;
            sky.skyMoonDirection = toMoon;
            // Stars only once the sun is really gone; the low-sun sky is lifted so dawn and dusk glow.
            sky.nightFactor = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.2f, -0.03f, elevation));
            sky.exposure = originalExposure * Mathf.Lerp(3.4f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.42f, Mathf.Abs(elevation))));
            sky.overcastFactor = overcast;
            sky.overcastColor = Greyed(key.grey, 0.25f, Mathf.Lerp(1f, 0.76f, thick) * Mathf.Lerp(1f, 0.82f, rain));
            sky.thickFactor = thick;

            float brightness = Mathf.Clamp01(Mathf.Max(haze.r, Mathf.Max(haze.g, haze.b)) / 0.8f) * Mathf.Lerp(1f, 0.55f, overcast);
            for (int i = 0; i < unlitInstances.Length; i++)
                unlitInstances[i].SetColor("_BaseColor", new Color(unlitColors[i].r * brightness, unlitColors[i].g * brightness, unlitColors[i].b * brightness, unlitColors[i].a));

            float reflect = Mathf.Lerp(1f, 0.55f, overcast);
            Color zenith = Color.Lerp(key.zenith, key.grey, overcast * 0.9f);
            Color horizon = Color.Lerp(key.horizon, key.grey * 1.1f, overcast * 0.9f);
            Shader.SetGlobalVector(SkyZenithId, new Vector4(zenith.r, zenith.g, zenith.b, 1f));
            Shader.SetGlobalVector(SkyHorizonId, new Vector4(horizon.r, horizon.g, horizon.b, 1f));
            Shader.SetGlobalFloat(EnvDimId, 1f - Mathf.Clamp01(Mathf.Lerp(0.04f, 1f, Daylight) * reflect));
        }
    }
}
