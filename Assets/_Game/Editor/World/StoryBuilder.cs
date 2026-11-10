using System.Collections.Generic;
using ChronoWild.Player;
using ChronoWild.Story;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using UnityEngine.UI;
using F = ChronoWild.Editor.World.BiomeAssetFactory;
using N = ChronoWild.Editor.World.TileableNoise;

namespace ChronoWild.Editor.World
{
    /// <summary>
    /// Builds the story layer: the time machine wreck on the beach, the scattered parts with their
    /// guiding glow and light beams, the debris trail, the meteor in the sky and the intro cinematic.
    /// </summary>
    public static class StoryBuilder
    {
        const string RootName = "30 Story";
        const string StoryDir = "Assets/_Game/Art/Story";
        static readonly Vector3 Wreck = new Vector3(322f, 0f, 109f);
        static readonly Vector3 PlayerStart = new Vector3(305f, 0f, 114f);

        struct PartSpec
        {
            public string name, entry, model;
            public Vector2 xz;
        }

        static readonly PartSpec[] Parts =
        {
            new PartSpec { name = "Caja de suministros", model = "Part_SupplyCrate", xz = new Vector2(314f, 121f),
                entry = "Caja de suministros recuperada. Filtros de agua y raciones para unas dos semanas. Dos semanas... justo el tiempo que nos queda." },
            new PartSpec { name = "Célula de energía", model = "Part_PowerCell", xz = new Vector2(291f, 251f),
                entry = "La célula de energía, enterrada entre los helechos. Todavía conserva carga. Esta selva se traga todo lo que cae en ella." },
            new PartSpec { name = "Botiquín", model = "Part_MedicalKit", xz = new Vector2(140f, 301f),
                entry = "El botiquín. La caja tiene arañazos profundos, pero sigue sellada. Algo con garras muy grandes intentó abrirla." },
            new PartSpec { name = "Módulo de navegación", model = "Part_NavigationModule", xz = new Vector2(200f, 438f),
                entry = "Módulo de navegación. Último registro: destino Jurásico... llegada hace 66 millones de años. Nos pasamos de época. Eso explica el meteorito." },
            new PartSpec { name = "Estabilizador temporal", model = "Part_TemporalStabilizer", xz = new Vector2(437f, 183f),
                entry = "El estabilizador temporal, medio hundido en el barro del pantano. Sin él, la máquina se destrozaría en el próximo salto." },
            new PartSpec { name = "Núcleo cronal", model = "Part_ChronoCore", xz = new Vector2(360f, 345f),
                entry = "El núcleo cronal. Cayó al pie del volcán y todavía está caliente. Con esto la máquina puede volver a encenderse." },
        };

        [MenuItem("CHRONO WILD/World/Install Story (Parts, Wreck, Intro)")]
        public static void Install()
        {
            if (!AssetDatabase.IsValidFolder(StoryDir))
                AssetDatabase.CreateFolder("Assets/_Game/Art", "Story");
            var terrain = UnityEngine.Terrain.activeTerrain;
            var old = GameObject.Find(RootName);
            if (old != null)
                Object.DestroyImmediate(old);
            var root = new GameObject(RootName).transform;

            var glowShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_MyShaders/ShaderGraph/GuidingGlow.shadergraph");
            var glow = Mat("M_GuidingGlow", glowShader, m => { m.SetFloat("_GlowStrength", 0.9f); m.SetFloat("_RimPower", 5f); m.SetFloat("_PulseSpeed", 1.4f); });
            var glowFaint = Mat("M_GuidingGlow_Debris", glowShader, m => { m.SetFloat("_GlowStrength", 1.6f); m.SetColor("_GlowColor", new Color(1f, 0.5f, 0.15f)); });
            var beamMat = Mat("M_LightBeam", Shader.Find("CHRONO WILD/Environment/Light Beam"), m => { m.SetColor("_Color", new Color(1.5f, 0.75f, 0.22f)); m.SetFloat("_Intensity", 0.9f); });
            var meteorHeadMat = Mat("M_MeteorHead", Shader.Find("CHRONO WILD/Environment/Light Beam"), m => { m.SetColor("_Color", new Color(5f, 4f, 3f)); m.SetFloat("_BandSpeed", 0f); });
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var metal = Mat("M_MachineMetal", lit, m => { m.SetColor("_BaseColor", new Color(0.36f, 0.38f, 0.41f)); m.SetFloat("_Metallic", 0.9f); m.SetFloat("_Smoothness", 0.55f); });
            var scorched = Mat("M_ScorchedMetal", lit, m => { m.SetColor("_BaseColor", new Color(0.07f, 0.065f, 0.06f)); m.SetFloat("_Metallic", 0.6f); m.SetFloat("_Smoothness", 0.25f); });
            var copper = Mat("M_Copper", lit, m => { m.SetColor("_BaseColor", new Color(0.72f, 0.4f, 0.22f)); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 0.5f); });
            var glass = Mat("M_CockpitGlass", lit, m => { m.SetColor("_BaseColor", new Color(0.08f, 0.16f, 0.2f)); m.SetFloat("_Metallic", 0.2f); m.SetFloat("_Smoothness", 0.95f); });
            var rubber = Mat("M_Rubber", lit, m => { m.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.03f)); m.SetFloat("_Smoothness", 0.3f); });
            var redPaint = Mat("M_RedPaint", lit, m => { m.SetColor("_BaseColor", new Color(0.55f, 0.05f, 0.04f)); m.SetFloat("_Smoothness", 0.4f); });
            var machineLights = Mat("M_MachineLights", glowShader, m => { m.SetFloat("_GlowStrength", 2.5f); m.SetFloat("_PulseSpeed", 5f); });
            var stripe = Mat("M_HazardStripe", lit, m => { });
            var heat = Mat("M_HeatShield", lit, m => { });
            Textured(metal, "PaintedHull", PaintedHull);
            Textured(scorched, "ScorchedMetal", ScorchedMetal);
            Textured(stripe, "HazardStripe", HazardStripe);
            Textured(heat, "HeatShield", HeatShield);
            var remap = new Dictionary<string, Material>
            {
                { "Hull", metal }, { "HullDark", scorched }, { "Copper", copper }, { "Glass", glass }, { "Light", machineLights },
                { "Cable", rubber }, { "Rubber", rubber }, { "Red", redPaint }, { "Glow", glow }, { "Stripe", stripe }, { "Scorch", heat },
            };
            var smoke = AssetDatabase.LoadAssetAtPath<Material>(F.MaterialDir + "/M_Smoke.mat");
            var motes = AssetDatabase.LoadAssetAtPath<Material>(F.MaterialDir + "/M_Motes.mat");

            float Ground(Vector2 xz) => terrain.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + terrain.transform.position.y;

            // ---------------- Wreck
            var wreckPos = new Vector3(Wreck.x, Ground(new Vector2(Wreck.x, Wreck.z)), Wreck.z);
            var wreck = new GameObject("Time Machine Wreck").transform;
            wreck.SetParent(root, false);
            wreck.position = wreckPos;
            var model = Model("TimeMachine", wreck, remap);
            // Came down hard: tilted, one side dug into the sand.
            model.transform.localPosition = new Vector3(0f, -0.35f, 0f);
            // Applied on top of the import's own axis correction, which must be kept.
            model.transform.localRotation = Quaternion.Euler(9f, 35f, -6f) * model.transform.localRotation;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            var rng = new System.Random(4);
            for (int i = 0; i < 9; i++)
            {
                var panel = Primitive(PrimitiveType.Cube, "Hull Plate", i % 3 == 0 ? metal : scorched, wreck);
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 3.5f + (float)rng.NextDouble() * 6f;
                var p = new Vector2(wreckPos.x + Mathf.Cos(a) * r, wreckPos.z + Mathf.Sin(a) * r);
                panel.transform.position = new Vector3(p.x, Ground(p) + 0.12f, p.y);
                panel.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 50f - 25f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 40f - 20f);
                panel.transform.localScale = new Vector3(1.2f + (float)rng.NextDouble(), 0.08f, 0.7f + (float)rng.NextDouble() * 0.6f);
            }
            var fire = new GameObject("Wreck Fire").AddComponent<Light>();
            fire.transform.SetParent(wreck, false);
            fire.transform.localPosition = new Vector3(1.5f, 1.6f, -0.8f);
            fire.type = LightType.Point; fire.color = new Color(1f, 0.5f, 0.2f); fire.range = 14f; fire.intensity = 14f; fire.shadows = LightShadows.None;
            Smoke(wreck, new Vector3(1.6f, 1.2f, -0.9f), smoke);
            Sparks(wreck, new Vector3(0.4f, 1.6f, 0.2f), motes, 14f, new Color(8f, 3.2f, 0.8f, 1f));

            // ---------------- Parts
            var partsList = new List<MachinePart>();
            var beamMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            foreach (var spec in Parts)
            {
                var pos = new Vector3(spec.xz.x, Ground(spec.xz) + 0.45f, spec.xz.y);
                var go = new GameObject(spec.name);
                go.transform.SetParent(root, false);
                go.transform.position = pos;
                var partModel = Model(spec.model, go.transform, remap);
                // Thrown from the crash: lying on its side, partly sunk into the ground.
                partModel.transform.localPosition = Vector3.down * 0.52f;
                partModel.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 50f - 25f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 40f - 20f) * partModel.transform.localRotation;
                var renderers = partModel.GetComponentsInChildren<Renderer>();
                var beam = new GameObject("Light Beam");
                beam.transform.SetParent(go.transform, false);
                beam.transform.localPosition = Vector3.up * 45f;
                beam.transform.localScale = new Vector3(0.55f, 45f, 0.55f);
                beam.AddComponent<MeshFilter>().sharedMesh = beamMesh;
                var br = beam.AddComponent<MeshRenderer>();
                br.sharedMaterial = beamMat;
                br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var part = go.AddComponent<MachinePart>();
                part.displayName = spec.name;
                part.journalEntry = spec.entry;
                part.glowRenderers = renderers;
                part.beam = beam;
                partsList.Add(part);

                // Debris trail from the wreck: scorched plates, every few a faint ember of the same glow.
                int count = Mathf.Clamp(Mathf.RoundToInt(Vector2.Distance(spec.xz, new Vector2(Wreck.x, Wreck.z)) / 14f), 3, 16);
                for (int i = 1; i < count; i++)
                {
                    float t = i / (float)count;
                    var xz = Vector2.Lerp(new Vector2(Wreck.x, Wreck.z), spec.xz, t) + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 6f;
                    var plate = Primitive(PrimitiveType.Cube, "Debris", i % 3 == 0 ? glowFaint : scorched, root);
                    Object.DestroyImmediate(plate.GetComponent<Collider>());
                    plate.transform.position = new Vector3(xz.x, Ground(xz) + 0.06f, xz.y);
                    plate.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 30f - 15f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 30f - 15f);
                    plate.transform.localScale = new Vector3(0.4f + (float)rng.NextDouble() * 0.5f, 0.05f, 0.3f + (float)rng.NextDouble() * 0.3f);
                }
            }

            // ---------------- Meteor
            var player = GameObject.Find("Review Player");
            var meteor = new GameObject("Approaching Meteor");
            meteor.transform.SetParent(root, false);
            var flipbook = Mat("M_MeteorFlipbook", Shader.Find("CHRONO WILD/Environment/Flipbook Sprite"), m =>
            {
                m.SetTexture("_MainTex", BakeMeteorFrames());
                m.SetColor("_Color", new Color(4f, 3.3f, 2.6f));
                m.SetFloat("_FPS", 12f);
                // Falling at an angle, tail trailing up and to the right.
                m.SetFloat("_Rotation", -35f);
            });
            var sprite = MeshObject("Flipbook", Resources.GetBuiltinResource<Mesh>("Quad.fbx"), flipbook, meteor.transform);
            sprite.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var approaching = meteor.AddComponent<ApproachingMeteor>();
            approaching.follow = player.transform;

            // ---------------- Player start
            var controller = player.GetComponent<DebugFirstPersonController>();
            var cc = player.GetComponent<CharacterController>();
            var start = new Vector3(PlayerStart.x, Ground(new Vector2(PlayerStart.x, PlayerStart.z)) + 0.05f, PlayerStart.z);
            player.transform.SetPositionAndRotation(start, Quaternion.Euler(0f, 60f, 0f));
            EditorUtility.SetDirty(player.transform);

            var director = root.gameObject.AddComponent<MissionDirector>();
            director.player = player.transform;
            director.parts = partsList.ToArray();

            BuildIntro(root, player, controller, terrain, wreckPos, partsList[1].transform.position);

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            EditorSceneManager.SaveScene(root.gameObject.scene);
        }

        // ------------------------------------------------------------------ Intro

        static void BuildIntro(Transform root, GameObject player, DebugFirstPersonController controller, UnityEngine.Terrain terrain, Vector3 wreck, Vector3 firstTarget)
        {
            var playerCam = controller.cameraPivot.GetComponent<Camera>();
            var intro = new GameObject("Intro Cinematic");
            intro.transform.SetParent(root, false);

            var camGo = new GameObject("Cinematic Camera");
            camGo.transform.SetParent(intro.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(playerCam);
            cam.farClipPlane = 1200f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = playerCam.GetComponent<UniversalAdditionalCameraData>().antialiasing;
            var brain = camGo.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 2f);

            CinemachineCamera Shot(string name, Vector3 position, Vector3 lookAt, float fov)
            {
                var go = new GameObject(name);
                go.transform.SetParent(intro.transform, false);
                go.transform.position = position;
                go.transform.rotation = Quaternion.LookRotation(lookAt - position);
                var vcam = go.AddComponent<CinemachineCamera>();
                vcam.Lens.FieldOfView = fov;
                vcam.Lens.FarClipPlane = 1200f;
                return vcam;
            }
            float G(float x, float z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
            // Low over the water with a long lens: the distance compresses and the volcano looms over the jungle.
            var shotSea = Shot("Shot Sea", new Vector3(335f, 14f, -35f), new Vector3(395f, 52f, 420f), 24f);
            var shotDollyA = Shot("Shot Beach A", new Vector3(268f, G(268f, 100f) + 5f, 98f), wreck + Vector3.up * 1.5f, 45f);
            var shotDollyB = Shot("Shot Beach B", new Vector3(306f, G(306f, 102f) + 2.5f, 101f), wreck + Vector3.up * 1.2f, 45f);
            var shotDebris = Shot("Shot Debris", wreck + new Vector3(-2f, 1.2f, 4f), firstTarget + Vector3.up * 8f, 55f);
            Vector3 eye = player.transform.position + Vector3.up * (controller.cameraPivot.localPosition.y);
            var shotPlayer = Shot("Shot Player", eye, eye + player.transform.forward * 10f, playerCam.fieldOfView);

            // Title cards on a screen overlay, switched on and off by the timeline.
            var canvasGo = new GameObject("Intro Titles");
            canvasGo.transform.SetParent(intro.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject Card(string name, string text)
            {
                var go = new GameObject(name);
                go.transform.SetParent(canvasGo.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.1f, 0.08f); rt.anchorMax = new Vector2(0.9f, 0.3f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                var t = go.AddComponent<Text>();
                t.font = font; t.fontSize = 44; t.alignment = TextAnchor.MiddleCenter; t.color = new Color(1f, 0.94f, 0.82f);
                t.text = text;
                var shadow = go.AddComponent<Shadow>();
                shadow.effectDistance = new Vector2(2f, -2f);
                shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
                go.SetActive(false);
                return go;
            }
            // The mission and failed-jump titles are burned into the arrival video that plays first.
            var card3 = Card("Card Parts", "Las piezas de la máquina y nuestras provisiones\nquedaron esparcidas por la isla.");
            var card4 = Card("Card Meteor", "El meteorito caerá en 15 días.\n<size=30>Encuentra todas las piezas. Sobrevive.</size>");

            // Timeline: camera shots on a Cinemachine track, titles on activation tracks.
            string path = StoryDir + "/TL_Intro.playable";
            AssetDatabase.DeleteAsset(path);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, path);
            var director = intro.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.None;

            var camTrack = timeline.CreateTrack<CinemachineTrack>(null, "Cameras");
            director.SetGenericBinding(camTrack, brain);
            void Clip(CinemachineCamera vcam, double start, double end)
            {
                var clip = camTrack.CreateClip<CinemachineShot>();
                clip.start = start;
                clip.duration = end - start;
                clip.displayName = vcam.name;
                var shot = (CinemachineShot)clip.asset;
                shot.VirtualCamera.exposedName = GUID.Generate().ToString();
                director.SetReferenceValue(shot.VirtualCamera.exposedName, vcam);
            }
            // Overlapping clips blend, which turns two still cameras into a camera move.
            Clip(shotSea, 0, 7);
            Clip(shotDollyA, 6, 11);
            Clip(shotDollyB, 7.5, 14);
            Clip(shotDebris, 12.5, 19.5);
            Clip(shotPlayer, 17.5, 22);

            void Title(GameObject card, double start, double end)
            {
                var track = timeline.CreateTrack<ActivationTrack>(null, card.name);
                track.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
                var clip = track.CreateDefaultClip();
                clip.start = start;
                clip.duration = end - start;
                director.SetGenericBinding(track, card);
            }
            Title(card3, 13.2, 17.6);
            Title(card4, 18.2, 22);

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();

            camGo.SetActive(false);
            var player2 = intro.AddComponent<IntroCinematic>();
            player2.director = director;
            player2.cinematicCamera = cam;
            player2.playerCamera = playerCam;
            player2.controller = controller;
            player2.video = intro.AddComponent<UnityEngine.Video.VideoPlayer>();
            player2.video.playOnAwake = false;
            player2.soundtrack = intro.AddComponent<AudioSource>();
            player2.soundtrack.playOnAwake = false;
            player2.soundtrack.spatialBlend = 0f;
            player2.soundtrack.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(StoryDir + "/A_IntroVideo.ogg");
        }

        // ------------------------------------------------------------------ Helpers

        static Material Mat(string name, Shader shader, System.Action<Material> configure)
        {
            string path = StoryDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            configure(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ Machine textures

        delegate void Surface(float u, float v, out Color albedo, out float height, out float metallic, out float smoothness, out float ao);

        /// <summary>Bakes albedo, normal and mask maps for a surface and wires them into a URP Lit material.</summary>
        static void Textured(Material m, string name, Surface surface)
        {
            const int S = 1024;
            var albedo = new Color32[S * S];
            var mask = new Color32[S * S];
            var height = new float[S * S];
            System.Threading.Tasks.Parallel.For(0, S, y =>
            {
                for (int x = 0; x < S; x++)
                {
                    surface((x + 0.5f) / S, (y + 0.5f) / S, out var c, out var h, out var met, out var sm, out var ao);
                    int i = x + y * S;
                    c.a = 1f;
                    albedo[i] = c;
                    height[i] = h;
                    mask[i] = new Color(met, ao, 0f, sm);
                }
            });
            var normal = new Color32[S * S];
            System.Threading.Tasks.Parallel.For(0, S, y =>
            {
                for (int x = 0; x < S; x++)
                {
                    float l = height[(x + S - 1) % S + y * S], r = height[(x + 1) % S + y * S];
                    float d = height[x + (y + S - 1) % S * S], t = height[x + (y + 1) % S * S];
                    var n = new Vector3((l - r) * 6f, (d - t) * 6f, 1f).normalized;
                    normal[x + y * S] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            });
            string dir = StoryDir + "/Textures";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(StoryDir, "Textures");
            F.WritePng($"{dir}/T_{name}_Albedo.png", albedo, S, true, false);
            F.WritePng($"{dir}/T_{name}_Normal.png", normal, S, false, true);
            F.WritePng($"{dir}/T_{name}_Mask.png", mask, S, false, false);
            var maskTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/T_{name}_Mask.png");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/T_{name}_Albedo.png"));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/T_{name}_Normal.png"));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", maskTex);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_OcclusionMap", maskTex);
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(m);
        }

        static float Sat(float v) => Mathf.Clamp01(v);
        static float Band(float a, float b, float v) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, v));

        /// <summary>Distance (0 at a seam) to the nearest panel joint on a staggered plate grid.</summary>
        static float PanelSeam(float u, float v, int columns, int rows, out Vector2 inPanel)
        {
            float py = v * rows;
            float row = Mathf.Floor(py);
            float px = u * columns + (row % 2f) * 0.5f;
            inPanel = new Vector2(px - Mathf.Floor(px), py - row);
            return Mathf.Min(Mathf.Min(inPanel.x, 1f - inPanel.x) / columns, Mathf.Min(inPanel.y, 1f - inPanel.y) / rows);
        }

        /// <summary>Off-white hull paint over steel: plates and rivets, chipped edges, grime streaks and soot.</summary>
        static void PaintedHull(float u, float v, out Color c, out float h, out float met, out float sm, out float ao)
        {
            float seam = PanelSeam(u, v, 2, 2, out var p);
            float joint = (1f - Band(0f, 0.0025f, seam)) * 0.6f;
            // Rivets along the plate edges.
            float rx = p.x * 10f, ry = p.y * 8f;
            float rivetLine = Mathf.Min(Mathf.Min(p.x, 1f - p.x), Mathf.Min(p.y, 1f - p.y));
            float rivet = rivetLine < 0.06f ? Sat(1f - (new Vector2(rx - Mathf.Floor(rx) - 0.5f, ry - Mathf.Floor(ry) - 0.5f)).magnitude * 3.5f) : 0f;
            float wear = N.Fbm(u, v, 8, 5, 301) * 0.5f + 0.5f;
            float edge = 1f - Band(0f, 0.02f, seam);
            float chips = Band(0.72f, 0.78f, wear + edge * 0.25f + (N.Fbm(u, v, 40, 3, 302) * 0.5f + 0.5f) * 0.12f);
            float streak = Sat((N.PerlinXY(u * 40f, v * 3f, 40, 3, 303) * 0.5f + 0.5f) * 1.6f - 0.6f) * Band(0.2f, 0.9f, 1f - v);
            float soot = Band(0.5f, 0.95f, N.Fbm(u, v, 3, 4, 304) * 0.5f + 0.5f);
            float tint = N.Fbm(u, v, 2, 3, 305) * 0.5f + 0.5f;
            var paint = Color.Lerp(new Color(0.4f, 0.41f, 0.39f), new Color(0.47f, 0.46f, 0.43f), tint);
            var steel = new Color(0.55f, 0.55f, 0.56f);
            c = Color.Lerp(paint, steel, chips);
            c = Color.Lerp(c, new Color(0.25f, 0.21f, 0.17f), streak * 0.25f);
            c = Color.Lerp(c, new Color(0.08f, 0.075f, 0.07f), soot * 0.38f);
            c *= 1f - joint * 0.45f;
            h = 0.5f - joint * 0.5f + rivet * 0.35f - chips * 0.08f;
            met = Mathf.Lerp(0.05f, 1f, chips);
            sm = Mathf.Lerp(0.42f, 0.68f, chips) * (1f - soot * 0.5f) * (1f - streak * 0.3f);
            ao = 1f - joint * 0.6f;
        }

        /// <summary>Burnt metal: heat-tinted bands of bronze, blue and violet under soot and flaking.</summary>
        static void ScorchedMetal(float u, float v, out Color c, out float h, out float met, out float sm, out float ao)
        {
            float heatBand = N.Fbm(u, v, 3, 4, 311) * 0.5f + 0.5f;
            float soot = Band(0.35f, 0.75f, N.Fbm(u, v, 6, 5, 312) * 0.5f + 0.5f);
            float flake = Band(0.68f, 0.74f, N.Fbm(u, v, 24, 3, 313) * 0.5f + 0.5f);
            // Mostly dull grey steel; the heat tint is only a faint bronze-to-blue cast.
            var temper = Color.Lerp(new Color(0.2f, 0.17f, 0.14f), new Color(0.14f, 0.15f, 0.18f), heatBand);
            c = Color.Lerp(temper, new Color(0.05f, 0.048f, 0.045f), soot * 0.7f);
            c = Color.Lerp(c, new Color(0.32f, 0.32f, 0.33f), flake * 0.4f);
            h = 0.5f + flake * 0.2f - soot * 0.05f;
            met = Mathf.Lerp(0.85f, 0.3f, soot);
            sm = Mathf.Lerp(0.5f, 0.12f, soot);
            ao = 1f;
        }

        /// <summary>Yellow and black warning stripes, scuffed through to primer.</summary>
        static void HazardStripe(float u, float v, out Color c, out float h, out float met, out float sm, out float ao)
        {
            float d = (u + v) * 6f;
            bool yellow = d - Mathf.Floor(d) < 0.5f;
            float wear = Band(0.6f, 0.72f, (N.Fbm(u, v, 12, 4, 321) * 0.5f + 0.5f));
            c = yellow ? new Color(0.78f, 0.56f, 0.06f) : new Color(0.04f, 0.04f, 0.04f);
            c = Color.Lerp(c, new Color(0.45f, 0.45f, 0.46f), wear);
            c *= 0.85f + 0.15f * (N.Fbm(u, v, 30, 2, 322) * 0.5f + 0.5f);
            h = 0.5f - wear * 0.06f;
            met = wear;
            sm = Mathf.Lerp(0.38f, 0.6f, wear);
            ao = 1f;
        }

        /// <summary>Ablative heat shield tiles, charred by the fall through time.</summary>
        static void HeatShield(float u, float v, out Color c, out float h, out float met, out float sm, out float ao)
        {
            float seam = PanelSeam(u, v, 8, 8, out var p);
            float joint = 1f - Band(0f, 0.002f, seam);
            float burn = N.Fbm(u, v, 5, 5, 331) * 0.5f + 0.5f;
            float crack = 1f - Band(0f, 0.05f, Mathf.Abs(N.Fbm(u, v, 14, 3, 332)));
            c = Color.Lerp(new Color(0.11f, 0.085f, 0.07f), new Color(0.02f, 0.018f, 0.016f), burn);
            c *= (1f - joint * 0.6f) * (1f - crack * 0.5f);
            h = 0.5f - joint * 0.4f - crack * 0.15f + burn * 0.05f;
            met = 0f;
            sm = 0.1f + crack * 0.05f;
            ao = 1f - joint * 0.5f;
        }

        /// <summary>Places an imported Blender model and swaps its placeholder materials for the project's.</summary>
        static GameObject Model(string file, Transform parent, Dictionary<string, Material> remap)
        {
            string modelPath = StoryDir + "/Models/" + file + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            if (!importer.bakeAxisConversion)
            {
                // Blender is Z-up: bake the conversion into the mesh so the model stands upright.
                importer.bakeAxisConversion = true;
                importer.SaveAndReimport();
            }
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && remap.TryGetValue(mats[i].name, out var m)) mats[i] = m;
                r.sharedMaterials = mats;
            }
            return go;
        }

        /// <summary>
        /// Draws 16 frames of a burning meteor into one sheet: a white-hot head with a flickering tail
        /// of fire and smoke. Played back by the flipbook shader, like an animated GIF.
        /// </summary>
        static Texture2D BakeMeteorFrames()
        {
            const int cell = 128, grid = 4, size = cell * grid;
            var px = new Color32[size * size];
            for (int f = 0; f < grid * grid; f++)
            {
                int cx = f % grid, cy = grid - 1 - f / grid;
                for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    // Quad space: head at the bottom, tail stretching to the top.
                    float u = (x + 0.5f) / cell - 0.5f, v = (y + 0.5f) / cell;
                    float headD = Mathf.Sqrt(u * u + (v - 0.14f) * (v - 0.14f));
                    float head = Mathf.Exp(-headD * headD * 900f) + Mathf.Exp(-headD * headD * 90f) * 0.45f;
                    float along = Mathf.Clamp01((v - 0.12f) / 0.86f);
                    float width = 0.025f + along * 0.17f;
                    float flicker = 0.65f + 0.35f * Mathf.PerlinNoise(along * 9f - f * 0.9f, u * 14f + f * 0.37f);
                    float tail = Mathf.Exp(-u * u / (width * width)) * Mathf.Pow(1f - along, 1.4f) * flicker * (v > 0.1f ? 1f : 0f);
                    float hot = head * 1.4f + tail * 0.85f;
                    var c = Color.Lerp(new Color(0.9f, 0.3f, 0.08f), new Color(1f, 0.92f, 0.75f), Mathf.Clamp01(head * 1.6f + (1f - along) * tail * 0.6f)) * Mathf.Clamp01(hot);
                    c.a = 1f;
                    px[(cx * cell + x) + (cy * cell + y) * size] = c;
                }
            }
            string path = StoryDir + "/T_MeteorFlipbook.png";
            F.WritePng(path, px, size, true, false);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static GameObject Primitive(PrimitiveType type, string name, Material material, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static GameObject MeshObject(string name, Mesh mesh, Material material, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static void Smoke(Transform parent, Vector3 local, Material material)
        {
            var go = new GameObject("Wreck Smoke");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.prewarm = true;
            var emission = ps.emission;
            emission.rateOverTime = 7f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.8f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.2f, 0.19f, 0.18f), 0f), new GradientColorKey(new Color(0.45f, 0.44f, 0.43f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.1f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            ps.Play();
        }

        static ParticleSystem Sparks(Transform parent, Vector3 local, Material material, float rate, Color color)
        {
            var go = new GameObject("Sparks");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = color;
            main.gravityModifier = -0.15f;
            main.maxParticles = 60;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;
            ps.Play();
            return ps;
        }

        static Mesh TorusMesh(string name, float radius, float thickness, int segments, int sides)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var center = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                var outward = center.normalized;
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2f;
                    var n = outward * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    verts.Add(center + n * thickness);
                    norms.Add(n);
                    uvs.Add(new Vector2(i / (float)segments, j / (float)sides));
                }
            }
            for (int i = 0; i < segments; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = i * (sides + 1) + j, c = a + sides + 1;
                tris.AddRange(new[] { a, a + 1, c, c, a + 1, c + 1 });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
