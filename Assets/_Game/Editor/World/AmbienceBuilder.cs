using System;
using System.IO;
using ChronoWild.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using F = ChronoWild.Editor.World.BiomeAssetFactory;

namespace ChronoWild.Editor.World
{
    /// <summary>
    /// Adds what makes the island feel inhabited: the volcano's plume and lava, steam vents, mist,
    /// ash, pollen and insects, flying creatures, and zone-mixed ambient sound synthesized here.
    /// </summary>
    public static class AmbienceBuilder
    {
        const string RootName = "26 Ambience";
        const string AudioDir = F.Root + "/Audio";
        static readonly Vector3 VolcanoTop = new Vector3(390f, 108f, 420f);

        [MenuItem("CHRONO WILD/World/Install Ambience (Life, Smoke, Sound)")]
        public static void Install()
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            var old = GameObject.Find(RootName);
            if (old != null)
                UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject(RootName).transform;

            var puff = F.BakeSmokeTexture();
            Material Particle(string name, float sun, float soft, float nightDim = 0f) => F.MaterialVariant(new Material(Shader.Find("CHRONO WILD/Environment/Soft Particle")), name, m =>
            {
                m.SetFloat("_NightDim", nightDim);
                m.SetTexture("_BaseMap", puff);
                m.SetFloat("_SunAmount", sun);
                m.SetFloat("_SoftDistance", soft);
            });
            var smoke = Particle("M_Smoke", 0.42f, 8f, 0.82f);
            var mist = Particle("M_Mist", 0.5f, 3f, 0.82f);
            var motes = Particle("M_Motes", 0.8f, 0.2f);
            var insect = Particle("M_Insects", 0.05f, 0.05f);

            BuildVolcano(terrain, root, smoke, motes);
            BuildMist(root, mist);
            BuildMotes(root, motes, insect);
            BuildFlocks(root);
            BuildSound(root, terrain);

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            EditorSceneManager.SaveScene(root.gameObject.scene);
        }

        // ------------------------------------------------------------------ Particles

        static ParticleSystem System(Transform parent, string name, Vector3 position, Material material, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 8f;
            var main = ps.main;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            main.loop = true;
            main.duration = 20f;
            main.playOnAwake = true;
            return ps;
        }

        static void Fade(ParticleSystem ps, Color tint, float inTime, float outTime)
        {
            var c = ps.colorOverLifetime;
            c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, inTime), new GradientAlphaKey(1f, outTime), new GradientAlphaKey(0f, 1f) });
            c.color = g;
        }

        static void Box(ParticleSystem ps, Vector3 size)
        {
            var s = ps.shape;
            s.enabled = true;
            s.shapeType = ParticleSystemShapeType.Box;
            s.scale = size;
        }

        static void Drift(ParticleSystem ps, float strength, float frequency)
        {
            var n = ps.noise;
            n.enabled = true;
            n.strength = strength;
            n.frequency = frequency;
            n.scrollSpeed = 0.15f;
            n.quality = ParticleSystemNoiseQuality.Low;
        }

        static void BuildVolcano(UnityEngine.Terrain terrain, Transform root, Material smoke, Material motes)
        {
            // A crater floor sample decides where the molten surface sits.
            float floor = float.MaxValue;
            for (float a = 0f; a < 360f; a += 30f)
            for (float r = 0f; r <= 6f; r += 2f)
            {
                var p = new Vector3(VolcanoTop.x + Mathf.Cos(a * Mathf.Deg2Rad) * r, 0f, VolcanoTop.z + Mathf.Sin(a * Mathf.Deg2Rad) * r);
                floor = Mathf.Min(floor, terrain.SampleHeight(p) + terrain.transform.position.y);
            }
            float lavaLevel = floor + 3f;
            var lavaMaterial = F.MaterialVariant(new Material(Shader.Find("CHRONO WILD/Environment/Lava")), "M_Lava", m => { });
            var lavaMesh = F.SaveAsset(F.GridMesh("SM_LavaDisc", new Vector2(44f, 44f), 8), F.MeshDir + "/SM_LavaDisc.asset");
            var lava = new GameObject("Crater Lava");
            lava.transform.SetParent(root, false);
            lava.transform.position = new Vector3(VolcanoTop.x, lavaLevel, VolcanoTop.z);
            lava.AddComponent<MeshFilter>().sharedMesh = lavaMesh;
            var lr = lava.AddComponent<MeshRenderer>();
            lr.sharedMaterial = lavaMaterial;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var glow = new GameObject("Crater Glow").AddComponent<Light>();
            glow.transform.SetParent(root, false);
            glow.transform.position = new Vector3(VolcanoTop.x, lavaLevel + 8f, VolcanoTop.z);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.42f, 0.14f);
            glow.range = 55f;
            glow.intensity = 60f;
            glow.shadows = LightShadows.None;

            // The plume: broad, slow, leaning with the wind and widening as it rises.
            var plume = System(root, "Volcano Plume", new Vector3(VolcanoTop.x, lavaLevel + 4f, VolcanoTop.z), smoke, 220);
            var pm = plume.main;
            pm.startLifetime = new ParticleSystem.MinMaxCurve(26f, 40f);
            pm.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
            pm.startSize = new ParticleSystem.MinMaxCurve(16f, 28f);
            pm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var pe = plume.emission;
            pe.rateOverTime = 6f;
            var ps = plume.shape;
            ps.enabled = true;
            ps.shapeType = ParticleSystemShapeType.Cone;
            ps.angle = 9f;
            ps.radius = 7f;
            ps.rotation = new Vector3(-90f, 0f, 0f);
            var pv = plume.velocityOverLifetime;
            pv.enabled = true;
            pv.space = ParticleSystemSimulationSpace.World;
            pv.x = new ParticleSystem.MinMaxCurve(2.6f, 5f);
            pv.y = new ParticleSystem.MinMaxCurve(0f, 1f);
            pv.z = new ParticleSystem.MinMaxCurve(1f, 2.6f);
            var size = plume.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 3.4f));
            Fade(plume, new Color(0.42f, 0.42f, 0.44f, 0.42f), 0.08f, 0.5f);
            Drift(plume, 2.5f, 0.05f);
            plume.GetComponent<ParticleSystemRenderer>().maxParticleSize = 40f;

            // Embers carried up out of the crater.
            var embers = System(root, "Crater Embers", new Vector3(VolcanoTop.x, lavaLevel + 1f, VolcanoTop.z), motes, 150);
            var em = embers.main;
            em.startLifetime = new ParticleSystem.MinMaxCurve(3f, 7f);
            em.startSpeed = new ParticleSystem.MinMaxCurve(6f, 15f);
            em.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.55f);
            em.startColor = new Color(9f, 2.8f, 0.5f, 1f);
            var ee = embers.emission;
            ee.rateOverTime = 26f;
            var es = embers.shape;
            es.enabled = true;
            es.shapeType = ParticleSystemShapeType.Circle;
            es.radius = 12f;
            es.rotation = new Vector3(90f, 0f, 0f);
            Drift(embers, 2.2f, 0.3f);

            // Steam vents scattered over the upper flanks.
            var rng = new System.Random(5);
            int vents = 0;
            for (int attempt = 0; attempt < 600 && vents < 14; attempt++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 24f + (float)rng.NextDouble() * 50f;
                var p = new Vector3(VolcanoTop.x + Mathf.Cos(a) * r, 0f, VolcanoTop.z + Mathf.Sin(a) * r);
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                if (p.y < 35f)
                    continue;
                vents++;
                var vent = System(root, "Steam Vent " + vents, p, smoke, 26);
                var m = vent.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.4f);
                m.startSize = new ParticleSystem.MinMaxCurve(1.6f, 4f);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var e = vent.emission;
                e.rateOverTime = 3.5f;
                var s = vent.shape;
                s.enabled = true;
                s.shapeType = ParticleSystemShapeType.Cone;
                s.angle = 12f;
                s.radius = 0.6f;
                s.rotation = new Vector3(-90f, 0f, 0f);
                var grow = vent.sizeOverLifetime;
                grow.enabled = true;
                grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.2f));
                Fade(vent, new Color(0.86f, 0.86f, 0.84f, 0.22f), 0.15f, 0.45f);
                Drift(vent, 0.6f, 0.3f);
            }

            // Ash flakes falling around the mountain.
            var ash = System(root, "Falling Ash", new Vector3(VolcanoTop.x, 85f, VolcanoTop.z), motes, 900);
            Box(ash, new Vector3(150f, 8f, 150f));
            var am = ash.main;
            am.startLifetime = new ParticleSystem.MinMaxCurve(10f, 18f);
            am.startSpeed = 0f;
            am.gravityModifier = 0.012f;
            am.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            am.startColor = new Color(0.32f, 0.31f, 0.31f, 0.8f);
            var ae = ash.emission;
            ae.rateOverTime = 62f;
            Drift(ash, 1.2f, 0.12f);
        }

        static void BuildMist(Transform root, Material mist)
        {
            void Bank(string name, Vector3 center, Vector3 box, int max, float rate, Vector2 scale, Color tint)
            {
                var ps = System(root, name, center, mist, max);
                Box(ps, box);
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(18f, 30f);
                m.startSpeed = 0f;
                m.startSize = new ParticleSystem.MinMaxCurve(scale.x, scale.y);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var e = ps.emission;
                e.rateOverTime = rate;
                var v = ps.velocityOverLifetime;
                v.enabled = true;
                v.space = ParticleSystemSimulationSpace.World;
                v.x = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
                v.y = new ParticleSystem.MinMaxCurve(0f, 0f);
                v.z = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
                Fade(ps, tint, 0.25f, 0.65f);
                ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 20f;
            }
            Bank("Swamp Mist Sheets", new Vector3(462f, 2.5f, 215f), new Vector3(100f, 1.2f, 140f), 200, 7f, new Vector2(7f, 15f), new Color(0.72f, 0.76f, 0.68f, 0.04f));
            Bank("Moor Mist Sheets", new Vector3(195f, 36f, 440f), new Vector3(200f, 10f, 140f), 90, 3.5f, new Vector2(14f, 26f), new Color(0.76f, 0.78f, 0.76f, 0.014f));
        }

        static void BuildMotes(Transform root, Material motes, Material insect)
        {
            void Cloud(string name, Vector3 center, Vector3 box, Material m, int max, float rate, Vector2 size, Color tint, float drift, float freq, Vector2 life)
            {
                var ps = System(root, name, center, m, max);
                Box(ps, box);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
                var e = ps.emission;
                e.rateOverTime = rate;
                Fade(ps, tint, 0.2f, 0.8f);
                Drift(ps, drift, freq);
            }
            var gate = root.gameObject.AddComponent<WeatherGatedParticles>();
            ParticleSystem Find(string n) => root.Find(n).GetComponent<ParticleSystem>();
            Cloud("Fireflies", new Vector3(462f, 2.6f, 215f), new Vector3(110f, 3.4f, 150f), motes, 260, 26f, new Vector2(0.09f, 0.16f), new Color(3.2f, 4f, 0.8f, 1f), 1.1f, 0.5f, new Vector2(4f, 9f));
            Cloud("Swamp Midges", new Vector3(462f, 3.6f, 215f), new Vector3(100f, 3f, 140f), insect, 1800, 180f, new Vector2(0.018f, 0.04f), new Color(0.05f, 0.05f, 0.04f, 1f), 1.5f, 1.3f, new Vector2(6f, 11f));
            Cloud("Swamp Spores", new Vector3(462f, 6f, 215f), new Vector3(100f, 9f, 140f), motes, 500, 34f, new Vector2(0.03f, 0.08f), new Color(0.95f, 0.95f, 0.72f, 0.75f), 0.8f, 0.2f, new Vector2(10f, 18f));
            Cloud("Moor Seeds", new Vector3(195f, 38f, 440f), new Vector3(210f, 12f, 150f), motes, 900, 60f, new Vector2(0.03f, 0.075f), new Color(0.95f, 0.92f, 0.78f, 0.7f), 1.4f, 0.14f, new Vector2(10f, 18f));
            Cloud("Woodland Pollen", new Vector3(130f, 16f, 250f), new Vector3(110f, 18f, 220f), motes, 700, 44f, new Vector2(0.025f, 0.07f), new Color(1f, 0.95f, 0.72f, 0.7f), 0.9f, 0.16f, new Vector2(10f, 18f));
            Cloud("East Meadow Pollen", new Vector3(470f, 18f, 350f), new Vector3(100f, 16f, 90f), motes, 400, 26f, new Vector2(0.025f, 0.07f), new Color(1f, 0.95f, 0.72f, 0.7f), 0.9f, 0.16f, new Vector2(10f, 18f));
            gate.dryDay = new[] { Find("Swamp Midges"), Find("Swamp Spores"), Find("Moor Seeds"), Find("Woodland Pollen"), Find("East Meadow Pollen") };
            gate.dryNight = new[] { Find("Fireflies") };
        }

        static void BuildFlocks(Transform root)
        {
            var mesh = F.SaveAsset(F.FlyerMesh("SM_Flyer"), F.MeshDir + "/SM_Flyer.asset");
            var shader = Shader.Find("CHRONO WILD/Environment/Flying Creature");
            var ptero = F.MaterialVariant(new Material(shader), "M_Pterosaur", m => { m.SetFloat("_FlapRate", 1.1f); m.SetFloat("_FlapHeight", 0.8f); });
            var small = F.MaterialVariant(new Material(shader), "M_SmallFlyer", m =>
            {
                m.SetColor("_BaseColor", new Color(0.05f, 0.09f, 0.1f));
                m.SetColor("_WingColor", new Color(0.32f, 0.44f, 0.42f));
                m.SetFloat("_FlapRate", 26f);
                m.SetFloat("_FlapHeight", 0.45f);
            });
            void Flock(string name, Vector3 center, float radius, int count, Vector2 height, Vector2 speed, Vector2 scale, Material m, int seed)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                var f = go.AddComponent<AmbientFlock>();
                f.mesh = mesh; f.material = m; f.center = center; f.radius = radius; f.count = count;
                f.height = height; f.speed = speed; f.scale = scale; f.seed = seed;
            }
            Flock("Pterosaurs Over Moor", new Vector3(195f, 32f, 440f), 95f, 9, new Vector2(18f, 62f), new Vector2(9f, 14f), new Vector2(1.2f, 2.2f), ptero, 11);
            Flock("Pterosaurs Over Volcano", new Vector3(390f, 75f, 420f), 110f, 7, new Vector2(30f, 85f), new Vector2(10f, 15f), new Vector2(1.6f, 2.6f), ptero, 12);
            Flock("Pterosaurs Over Coast", new Vector3(300f, 8f, 95f), 130f, 7, new Vector2(10f, 32f), new Vector2(8f, 12f), new Vector2(1f, 1.6f), ptero, 13);
            Flock("Swamp Dragonflies", new Vector3(462f, 1.9f, 215f), 46f, 36, new Vector2(0.4f, 3.2f), new Vector2(2.5f, 6f), new Vector2(0.04f, 0.07f), small, 14);
        }

        // ------------------------------------------------------------------ Sound

        const int Rate = 22050;

        static AudioClip SaveWav(string name, float[] samples)
        {
            if (!AssetDatabase.IsValidFolder(AudioDir))
                AssetDatabase.CreateFolder(F.Root, "Audio");
            string path = AudioDir + "/" + name + ".wav";
            float peak = 1e-4f;
            foreach (var s in samples) peak = Mathf.Max(peak, Mathf.Abs(s));
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (var s in samples) w.Write((short)(s / peak * 0.85f * short.MaxValue));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        /// <summary>Fades the tail into the head so the clip repeats with no click.</summary>
        static float[] MakeLoop(float[] s, int cross)
        {
            var o = new float[s.Length - cross];
            Array.Copy(s, cross, o, 0, o.Length);
            for (int i = 0; i < cross; i++)
            {
                float t = i / (float)cross;
                o[o.Length - cross + i] = o[o.Length - cross + i] * Mathf.Sqrt(1f - t) + s[i] * Mathf.Sqrt(t);
            }
            return o;
        }

        delegate float Sampler(float t, int i);

        static float[] Render(float seconds, Sampler sampler)
        {
            int n = (int)(seconds * Rate);
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = sampler(i / (float)Rate, i);
            return MakeLoop(s, Rate * 2);
        }

        static void BuildSound(Transform root, UnityEngine.Terrain terrain)
        {
            var rng = new System.Random(9);
            float White() => (float)rng.NextDouble() * 2f - 1f;
            float l1 = 0f, l2 = 0f;

            // Surf: deep noise that swells and hisses with each wave.
            var surf = SaveWav("A_Surf", Render(36f, (t, i) =>
            {
                float wave = Mathf.Pow(Mathf.Sin(t * 0.7f) * 0.5f + 0.5f, 2.5f) * 0.75f + Mathf.Pow(Mathf.Sin(t * 0.43f + 1f) * 0.5f + 0.5f, 3f) * 0.5f;
                l1 += (White() - l1) * (0.05f + wave * 0.3f);
                l2 += (White() - l2) * 0.012f;
                return l1 * (0.15f + wave) + l2 * 2.2f;
            }));

            var go = new GameObject("Ambient Sound");
            go.transform.SetParent(root, false);
            var zones = go.AddComponent<AmbientSoundZones>();
            var player = GameObject.Find("Review Player").transform;
            zones.listener = player;
            zones.terrain = terrain;
            zones.zones = new[]
            {
                new AmbientSoundZones.Zone { name = "Surf", bed = surf, volume = 0.8f, coastal = true },
            };
            // Footsteps are removed for now; the FootstepAudio script is kept for when new sounds are added.
            var steps = player.GetComponent<ChronoWild.Player.FootstepAudio>();
            if (steps != null)
                UnityEngine.Object.DestroyImmediate(steps);
            var cam = Camera.main != null ? Camera.main : GameObject.Find("PlayerCamera").GetComponent<Camera>();
            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null)
                cam.gameObject.AddComponent<AudioListener>();
        }
    }
}
