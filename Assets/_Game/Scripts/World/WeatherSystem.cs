using ChronoWild.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChronoWild.World
{
    public enum WeatherKind { Clear, PartlyCloudy, Overcast, ThickOvercast, Rain, RainWithMist }

    /// <summary>
    /// Picks the weather at random, at any hour, and for very different lengths: a shower of minutes,
    /// half a day, a whole day or several days of rain. Clouds gather first and rain only starts once
    /// the sky has closed; the ground stays wet for a while after it stops.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class WeatherSystem : MonoBehaviour
    {
        public static WeatherSystem Instance { get; private set; }

        [Header("Scene")]
        public VolumetricSky sky;
        public JungleAtmosphere atmosphere;
        public Material rainMaterial;
        public Material splashMaterial;
        public Material curtainNear, curtainFar;
        [Tooltip("Top-down canopy map: R height, G cover.")]
        public Texture2D occlusionMap;
        public IslandBiomes biomes;
        public Terrain terrain;
        public Transform follow;

        [Header("Random forecast (relative chances)")]
        public float clearChance = 30f;
        public float partlyCloudyChance = 26f;
        [Tooltip("Cloudy, but with almost normal visibility.")]
        public float overcastChance = 10f;
        [Tooltip("Low, dark cloud deck and reduced visibility.")]
        public float thickOvercastChance = 8f;
        public float rainChance = 18f;
        public float mistyRainChance = 12f;

        [Header("Rain length (relative chances, game hours)")]
        public float shortShowerChance = 42f;
        public Vector2 shortShowerHours = new Vector2(0.3f, 2f);
        public float halfDayChance = 28f;
        public Vector2 halfDayHours = new Vector2(3f, 10f);
        public float fullDayChance = 20f;
        public Vector2 fullDayHours = new Vector2(18f, 30f);
        public float manyDaysChance = 10f;
        public Vector2 manyDaysHours = new Vector2(36f, 90f);
        [Tooltip("How long dry weather lasts (game hours).")]
        public Vector2 dryHours = new Vector2(2f, 20f);

        [Header("Rain visuals")]
        public int maxDrops = 12000;
        public float dropsPerSecond = 9000f;
        public float splashesPerSecond = 2600f;
        [Tooltip("Radius around the player where drops hit the ground visibly (m).")]
        public float splashRadius = 20f;
        public bool showDebugLabel = true;

        public WeatherKind Kind { get; private set; } = WeatherKind.PartlyCloudy;
        public float RemainingHours { get; private set; }
        public float Overcast { get; private set; }
        public float Rain { get; private set; }
        public float Mist { get; private set; }
        /// <summary>0 for a high thin overcast, 1 for a low, heavy deck.</summary>
        public float Thick { get; private set; }
        public float Wetness { get; private set; }

        static readonly int RainAmountId = Shader.PropertyToID("_CW_RainAmount");
        static readonly int RainWetId = Shader.PropertyToID("_CW_RainWet");

        float intensity = 0.7f, cover, baseOpenHaze, baseCoverage, baseCoverageVariation, baseDensity, baseBottom, baseTop, baseCloudShadow, baseGustSpeed, baseAmbient;
        ParticleSystem rainParticles, splashParticles;
        Transform curtains;
        float splashCarry;
        static readonly int OcclusionMapId = Shader.PropertyToID("_CW_RainOcclusion");
        static readonly int OcclusionRectId = Shader.PropertyToID("_CW_RainOcclusionRect");
        JungleAtmosphere.FogBank[] baseBanks;
        int mistBank;
        GUIStyle labelStyle;

        void OnEnable()
        {
            Instance = this;
            baseOpenHaze = atmosphere.openHazeDensity;
            baseCoverage = sky.coverage;
            baseCoverageVariation = sky.coverageVariation;
            baseDensity = sky.density;
            baseBottom = sky.cloudBottom;
            baseTop = sky.cloudTop;
            baseCloudShadow = sky.cloudShadowStrength;
            baseAmbient = sky.ambientStrength;
            baseGustSpeed = biomes != null ? biomes.gustSpeed : 6f;
            if (occlusionMap != null)
            {
                Shader.SetGlobalTexture(OcclusionMapId, occlusionMap);
                Vector3 size = terrain.terrainData.size, origin = terrain.transform.position;
                Shader.SetGlobalVector(OcclusionRectId, new Vector4(origin.x, origin.z, 1f / size.x, 1f / size.z));
            }
            cover = 0.5f;
            baseBanks = atmosphere.extraBanks;
            mistBank = baseBanks.Length;
            var banks = new JungleAtmosphere.FogBank[Mathf.Min(baseBanks.Length + 1, 4)];
            System.Array.Copy(baseBanks, banks, Mathf.Min(baseBanks.Length, 4));
            atmosphere.extraBanks = banks;
            Set(WeatherKind.PartlyCloudy, Random.Range(dryHours.x, dryHours.y), 0.7f);
            CreateRain();
        }

        void OnDisable()
        {
            if (Instance == this)
                Instance = null;
            if (atmosphere != null)
            {
                atmosphere.extraBanks = baseBanks;
                atmosphere.openHazeDensity = baseOpenHaze;
            }
            if (sky != null)
            {
                sky.coverage = baseCoverage;
                sky.coverageVariation = baseCoverageVariation;
                sky.density = baseDensity;
                sky.cloudBottom = baseBottom;
                sky.cloudTop = baseTop;
                sky.cloudShadowStrength = baseCloudShadow;
                sky.ambientStrength = baseAmbient;
            }
            Shader.SetGlobalFloat(RainAmountId, 0f);
            Shader.SetGlobalFloat(RainWetId, 0f);
            Shader.SetGlobalVector(OcclusionRectId, Vector4.zero);
            if (biomes != null)
                biomes.gustSpeed = baseGustSpeed;
            if (rainParticles != null)
                Destroy(rainParticles.gameObject);
            if (splashParticles != null)
                Destroy(splashParticles.gameObject);
            if (curtains != null)
                Destroy(curtains.gameObject);
        }

        void CreateRain()
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            rainParticles = go.AddComponent<ParticleSystem>();
            rainParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = rainMaterial;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.034f;
            r.lengthScale = 0f;
            r.maxParticleSize = 0.0035f; // a drop right next to the camera must not fill the screen
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var main = rainParticles.main;
            main.maxParticles = GraphicsTier.IsLow ? maxDrops / 3 : maxDrops;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1.2f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.016f, 0.03f);
            main.startColor = new Color(0.82f, 0.86f, 0.92f, 0.42f);
            var shape = rainParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(32f, 1f, 32f);
            var velocity = rainParticles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 2f);
            velocity.y = new ParticleSystem.MinMaxCurve(-22f, -16f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 1f);
            var emission = rainParticles.emission;
            emission.rateOverTime = 0f;
            rainParticles.Play();
            CreateSplashes();
            CreateCurtains();
        }

        void CreateSplashes()
        {
            var go = new GameObject("Rain Splashes");
            go.transform.SetParent(transform, false);
            splashParticles = go.AddComponent<ParticleSystem>();
            splashParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = splashMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var main = splashParticles.main;
            main.maxParticles = 1200;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.09f, 0.18f);
            main.startSpeed = 0f;
            var emission = splashParticles.emission;
            emission.enabled = false;
            var size = splashParticles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.35f, 1f, 1f));
            var color = splashParticles.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            splashParticles.Play();
        }

        void CreateCurtains()
        {
            curtains = new GameObject("Rain Curtains").transform;
            curtains.SetParent(transform, false);
            void Ring(string name, float radius, float height, Material material)
            {
                const int segments = 32;
                var verts = new Vector3[(segments + 1) * 2];
                var uvs = new Vector2[verts.Length];
                var tris = new int[segments * 6];
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments, a = t * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                    verts[i * 2] = d + Vector3.down * height * 0.35f;
                    verts[i * 2 + 1] = d + Vector3.up * height * 0.65f;
                    uvs[i * 2] = new Vector2(t, 0f);
                    uvs[i * 2 + 1] = new Vector2(t, 1f);
                    if (i == segments)
                        continue;
                    int k = i * 6, b = i * 2;
                    tris[k] = b; tris[k + 1] = b + 1; tris[k + 2] = b + 2;
                    tris[k + 3] = b + 2; tris[k + 4] = b + 1; tris[k + 5] = b + 3;
                }
                var mesh = new Mesh { name = name, vertices = verts, uv = uvs, triangles = tris };
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 3f + Vector3.up * height);
                var go = new GameObject(name);
                go.transform.SetParent(curtains, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            Ring("Near Curtain", 17f, 80f, curtainNear);
            Ring("Far Curtain", 42f, 150f, curtainFar);
        }

        void EmitSplashes()
        {
            if (Rain <= 0.02f || follow == null)
                return;
            splashCarry += Rain * splashesPerSecond * (GraphicsTier.IsLow ? 0.4f : 1f) * Time.deltaTime;
            int count = Mathf.Min((int)splashCarry, 80);
            splashCarry -= (int)splashCarry;
            float originY = terrain.transform.position.y;
            var data = terrain.terrainData;
            var pos = terrain.transform.position;
            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                Vector2 around = Random.insideUnitCircle * splashRadius;
                var p = follow.position + new Vector3(around.x, 0f, around.y);
                if (occlusionMap != null && Random.value < occlusionMap.GetPixelBilinear((p.x - pos.x) / data.size.x, (p.z - pos.z) / data.size.z).g)
                    continue;
                p.y = terrain.SampleHeight(p) + originY;
                if (p.y < 0.35f || (Mathf.Abs(p.y - 1.4f) < 0.06f)) // the sea and the swamp get their rings instead
                    continue;
                emit.position = p + Vector3.up * 0.05f;
                emit.startSize = Random.Range(0.05f, 0.15f);
                emit.startColor = new Color(0.85f, 0.88f, 0.92f, 0.3f);
                splashParticles.Emit(emit, 1);
            }
        }

        void Set(WeatherKind kind, float hours, float newIntensity)
        {
            Kind = kind;
            RemainingHours = hours;
            intensity = newIntensity;
        }

        /// <summary>Forces a weather for a number of game hours; the random forecast resumes afterwards.</summary>
        public void Force(WeatherKind kind, float hours, float rainIntensity = 0.8f) => Set(kind, hours, rainIntensity);

        /// <summary>Jumps straight to the current weather with no gradual change (loading a save, tests).</summary>
        public void Snap()
        {
            bool rainy = Kind == WeatherKind.Rain || Kind == WeatherKind.RainWithMist;
            cover = Kind == WeatherKind.Clear ? 0.2f : Kind == WeatherKind.PartlyCloudy ? 0.55f : 1f;
            Overcast = TargetOvercast;
            Thick = TargetThick;
            Mist = Kind == WeatherKind.RainWithMist ? 1f : 0f;
            Rain = rainy ? intensity : 0f;
            Wetness = rainy ? Mathf.Lerp(0.55f, 1f, Rain) : 0f;
        }

        float RollRainHours()
        {
            float roll = Random.value * (shortShowerChance + halfDayChance + fullDayChance + manyDaysChance);
            Vector2 range;
            if ((roll -= shortShowerChance) < 0f) range = shortShowerHours;
            else if ((roll -= halfDayChance) < 0f) range = halfDayHours;
            else if ((roll -= fullDayChance) < 0f) range = fullDayHours;
            else range = manyDaysHours;
            return Random.Range(range.x, range.y);
        }

        void PickNext()
        {
            WeatherKind next;
            do
            {
                float roll = Random.value * (clearChance + partlyCloudyChance + overcastChance + thickOvercastChance + rainChance + mistyRainChance);
                if ((roll -= clearChance) < 0f) next = WeatherKind.Clear;
                else if ((roll -= partlyCloudyChance) < 0f) next = WeatherKind.PartlyCloudy;
                else if ((roll -= overcastChance) < 0f) next = WeatherKind.Overcast;
                else if ((roll -= thickOvercastChance) < 0f) next = WeatherKind.ThickOvercast;
                else if ((roll -= rainChance) < 0f) next = WeatherKind.Rain;
                else next = WeatherKind.RainWithMist;
            } while (next == Kind);

            bool rainy = next == WeatherKind.Rain || next == WeatherKind.RainWithMist;
            Set(next, rainy ? RollRainHours() : Random.Range(dryHours.x, dryHours.y), rainy ? Random.Range(0.35f, 1f) : 0.7f);
        }

        void Update()
        {
            var clock = DayNightCycle.Instance;
            float gameHours = clock != null && !clock.paused ? Time.deltaTime / (Mathf.Max(clock.dayLengthMinutes, 0.01f) * 60f) * 24f : 0f;
            RemainingHours -= gameHours;
            if (RemainingHours <= 0f)
                PickNext();
            DebugKeys(clock);

            bool rainy = Kind == WeatherKind.Rain || Kind == WeatherKind.RainWithMist;
            float targetCover = Kind == WeatherKind.Clear ? 0.2f : Kind == WeatherKind.PartlyCloudy ? 0.55f : 1f;
            float targetOvercast = TargetOvercast;
            float targetMist = Kind == WeatherKind.RainWithMist ? 1f : 0f;

            // Rates are per game hour, with a floor so forced changes stay visible at normal speed.
            float step = Mathf.Max(gameHours, Time.deltaTime * 0.012f);
            cover = Mathf.MoveTowards(cover, targetCover, step / 1.2f);
            Overcast = Mathf.MoveTowards(Overcast, targetOvercast, step / 1.5f);
            Mist = Mathf.MoveTowards(Mist, targetMist, step / 1.5f);
            Thick = Mathf.MoveTowards(Thick, TargetThick, step / 1.5f);
            // Rain waits for a closed sky, and stops before the clouds break.
            float allowed = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.9f, Overcast));
            Rain = Mathf.MoveTowards(Rain, rainy ? intensity * allowed : 0f, step / 0.5f);
            float dryRate = clock != null ? Mathf.Lerp(1f / 7f, 1f / 3f, clock.Daylight) : 1f / 4f;
            Wetness = Rain > 0.02f ? Mathf.MoveTowards(Wetness, Mathf.Lerp(0.55f, 1f, Rain), step / 0.35f) : Mathf.MoveTowards(Wetness, 0f, step * dryRate);

            Apply();
        }

        float TargetOvercast => Kind == WeatherKind.Clear || Kind == WeatherKind.PartlyCloudy ? 0f : 1f;
        float TargetThick => Kind == WeatherKind.ThickOvercast ? 1f : Kind == WeatherKind.RainWithMist ? 0.85f : Kind == WeatherKind.Rain ? 0.55f : 0f;

        void Apply()
        {
            // Below full overcast the layer keeps gaps; at full overcast it merges into one deck.
            sky.coverage = Mathf.Lerp(Mathf.Lerp(0.42f, 0.62f, cover), Mathf.Lerp(0.72f, 0.88f, Thick), Overcast);
            sky.coverageVariation = Mathf.Lerp(baseCoverageVariation, Mathf.Lerp(0.7f, 0.5f, Thick), Overcast);
            sky.density = Mathf.Lerp(baseDensity, baseDensity * 1.8f, Overcast);
            sky.cloudBottom = Mathf.Lerp(baseBottom, 480f, Thick * Overcast);
            sky.cloudTop = Mathf.Lerp(baseTop, 2100f, Thick * Overcast);
            // The direct sun is already gone under a closed sky; no second darkening from cloud shadows.
            sky.cloudShadowStrength = baseCloudShadow * (1f - Overcast);
            sky.ambientStrength = Mathf.Lerp(baseAmbient, baseAmbient * 1.7f, Thick * Overcast);

            atmosphere.openHazeDensity = baseOpenHaze + Rain * 0.0075f + Mist * 0.016f + Thick * Overcast * 0.0035f;
            if (biomes != null)
                biomes.gustSpeed = baseGustSpeed * (1f + Rain * 1.1f);
            if (mistBank < atmosphere.extraBanks.Length)
            {
                float y = follow != null ? follow.position.y : 0f;
                Vector3 p = follow != null ? follow.position : Vector3.zero;
                atmosphere.extraBanks[mistBank] = new JungleAtmosphere.FogBank
                {
                    name = "Weather Mist",
                    center = new Vector2(p.x, p.z),
                    radius = 2000f,
                    falloff = 500f,
                    density = Mist * 0.05f,
                    baseHeight = y - 1f,
                    heightFalloff = 0.09f
                };
            }

            Shader.SetGlobalFloat(RainAmountId, Rain);
            Shader.SetGlobalFloat(RainWetId, Wetness);

            if (follow != null)
            {
                rainParticles.transform.position = follow.position + Vector3.up * 14f;
                var cam = Camera.main;
                if (curtains != null && cam != null)
                {
                    curtains.position = cam.transform.position;
                    curtains.gameObject.SetActive(Rain > 0.01f);
                }
                var emission = rainParticles.emission;
                float scale = GraphicsTier.IsLow ? 0.34f : 1f;
                emission.rateOverTime = Rain * dropsPerSecond * scale;

                // Gusts lean the rain in changing directions.
                float t = Time.time;
                float gust = Mathf.Sin(t * 0.37f) * 0.5f + Mathf.Sin(t * 0.91f + 1.3f) * 0.35f + 0.6f;
                var velocity = rainParticles.velocityOverLifetime;
                velocity.x = new ParticleSystem.MinMaxCurve(gust * 2.2f, gust * 4.4f);
                velocity.z = new ParticleSystem.MinMaxCurve(gust * 0.8f, gust * 2.2f);
                EmitSplashes();
            }
        }

        void DebugKeys(DayNightCycle clock)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.f2Key.wasPressedThisFrame)
                showDebugLabel = !showDebugLabel;
            if (keyboard.f3Key.wasPressedThisFrame)
                Force((WeatherKind)(((int)Kind + 1) % 6), 8f);
            if (keyboard.f4Key.wasPressedThisFrame && clock != null)
                clock.SetHour(clock.hour + 1f);
        }

        void OnGUI()
        {
            if (!showDebugLabel)
                return;
            if (labelStyle == null)
                labelStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            var clock = DayNightCycle.Instance;
            string text = (clock != null ? clock.ClockText + "  ·  " : "") + KindName + $"  ·  {RemainingHours:0.0} h   [F2 hide · F3 weather · F4 +1 h]";
            GUI.Box(new Rect(Screen.width - 520, 10, 510, 28), text, labelStyle);
        }

        string KindName => Kind switch
        {
            WeatherKind.Clear => "Clear",
            WeatherKind.PartlyCloudy => "Partly cloudy",
            WeatherKind.Overcast => "Overcast",
            WeatherKind.ThickOvercast => "Thick overcast",
            WeatherKind.Rain => "Rain",
            _ => "Rain with mist"
        };
    }
}
