using System.Collections.Generic;
using ChronoWild.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChronoWild.Editor.World
{
    /// <summary>
    /// Puts the time traveler (Art/Character/TimeTraveler.fbx, built in Blender by
    /// Tools/Blender/build_character.py) on the player: import settings and looping clips, URP
    /// materials, an animator (idle/walk/run blend by speed, jump while airborne) and the
    /// third-person view (V), with first person as the default.
    /// </summary>
    public static class PlayerCharacterInstaller
    {
        const string Dir = "Assets/_Game/Art/Character";
        const string ModelPath = Dir + "/TimeTraveler.fbx";
        const string ControllerPath = Dir + "/AC_TimeTraveler.controller";

        static readonly Dictionary<string, (Color color, float metallic, float smoothness, float emission)> Looks = new()
        {
            { "Suit", (new Color(0.36f, 0.34f, 0.24f), 0f, 0.28f, 0f) },
            { "Pants", (new Color(0.22f, 0.22f, 0.16f), 0f, 0.25f, 0f) },
            { "SuitDark", (new Color(0.11f, 0.12f, 0.11f), 0f, 0.3f, 0f) },
            { "Armor", (new Color(0.47f, 0.45f, 0.38f), 0.15f, 0.45f, 0f) },
            { "Accent", (new Color(0.85f, 0.36f, 0.08f), 0f, 0.45f, 0f) },
            { "Leather", (new Color(0.2f, 0.13f, 0.08f), 0f, 0.35f, 0f) },
            { "Visor", (new Color(0.55f, 0.3f, 0.06f), 0.35f, 0.93f, 0f) },
            { "Glow", (new Color(1f, 0.55f, 0.15f), 0f, 0.6f, 1.4f) },
            { "Metal", (new Color(0.5f, 0.5f, 0.52f), 0.85f, 0.62f, 0f) },
            { "Rubber", (new Color(0.04f, 0.04f, 0.04f), 0f, 0.15f, 0f) },
        };

        [MenuItem("CHRONO WILD/World/Install Player Character")]
        public static void Install()
        {
            var materials = ConfigureImport();
            var controller = BuildAnimator();
            var player = Object.FindFirstObjectByType<DebugFirstPersonController>(FindObjectsInactive.Include);
            if (player == null)
            {
                Debug.LogError("Install Player Character: no DebugFirstPersonController in the open scene.");
                return;
            }
            var old = player.transform.Find("Time Traveler");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, player.transform);
            model.name = "Time Traveler";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var animator = GetOrAdd<Animator>(model);
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // its shadow shows in first person

            var avatar = GetOrAdd<CharacterAvatar>(player.gameObject);
            avatar.animator = animator;
            avatar.model = model.transform;
            avatar.body = player.GetComponent<CharacterController>();
            var view = GetOrAdd<ThirdPersonView>(player.gameObject);
            view.controller = player;
            view.avatar = avatar;

            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            EditorSceneManager.SaveScene(player.gameObject.scene);
            Debug.Log($"Install Player Character: time traveler on '{player.name}' ({materials} materials). V toggles third person; first person is the default.");
        }

        // Unity's fake-null components make "??" unreliable here.
        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static int ConfigureImport()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var clip in importer.defaultClipAnimations)
            {
                string name = clip.name.Contains("|") ? clip.name.Substring(clip.name.LastIndexOf('|') + 1) : clip.name;
                clip.name = name;
                clip.loopTime = true;
                clip.loopPose = name != "Jump";
                clips.Add(clip);
            }
            importer.clipAnimations = clips.ToArray();

            string matDir = Dir + "/Materials";
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(Dir, "Materials");
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            int count = 0;
            foreach (var (name, look) in Looks)
            {
                string path = $"{matDir}/M_Traveler_{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
                m.shader = lit;
                m.SetColor("_BaseColor", look.color);
                m.SetFloat("_Metallic", look.metallic);
                m.SetFloat("_Smoothness", look.smoothness);
                // Woven fabric on the suit, grain on the leather: surfaces read as cloth and hide, not plastic.
                var detail = name == "Leather" ? LeatherNormal() : (name == "Suit" || name == "Pants" || name == "SuitDark") ? FabricNormal() : null;
                if (detail != null)
                {
                    m.SetTexture("_BumpMap", detail);
                    m.SetFloat("_BumpScale", name == "Leather" ? 0.6f : 0.8f);
                    m.SetTextureScale("_BaseMap", Vector2.one * (name == "Leather" ? 6f : 14f));
                    m.EnableKeyword("_NORMALMAP");
                }
                if (look.emission > 0f)
                {
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    m.SetColor("_EmissionColor", look.color * look.emission);
                }
                EditorUtility.SetDirty(m);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), m);
                count++;
            }
            importer.SaveAndReimport();
            return count;
        }

        static Texture2D fabric, leather;

        static Texture2D FabricNormal() => fabric != null ? fabric : fabric = NormalMap("T_Traveler_Fabric_Normal", (u, v) =>
        {
            // Plain weave: warp and weft threads alternately over and under, with fibre noise.
            const int threads = 24;
            float fu = u * threads, fv = v * threads;
            int cu = Mathf.FloorToInt(fu), cv = Mathf.FloorToInt(fv);
            float warp = Mathf.Sin((fu - cu) * Mathf.PI), weft = Mathf.Sin((fv - cv) * Mathf.PI);
            bool warpOver = ((cu + cv) & 1) == 0;
            float h = warpOver ? 0.6f * warp + 0.4f * weft * warp : 0.6f * weft + 0.4f * warp * weft;
            return h + 0.15f * Noise(u * 64f, v * 64f, 64) + 0.08f * Noise(u * 16f, v * 16f, 16);
        }, 3f);

        static Texture2D LeatherNormal() => leather != null ? leather : leather = NormalMap("T_Traveler_Leather_Normal", (u, v) =>
            0.55f * Noise(u * 24f, v * 24f, 24) + 0.3f * Noise(u * 56f, v * 56f, 56) + 0.15f * Noise(u * 8f, v * 8f, 8), 2.2f);

        /// <summary>Tileable value noise (period in cells) in 0..1.</summary>
        static float Noise(float x, float y, int period)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float H(int a, int b)
            {
                a = ((a % period) + period) % period; b = ((b % period) + period) % period;
                uint n = (uint)(a * 374761393 + b * 668265263);
                n = (n ^ (n >> 13)) * 1274126177u;
                return (n ^ (n >> 16)) / 4294967295f;
            }
            float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(H(xi, yi), H(xi + 1, yi), sx), Mathf.Lerp(H(xi, yi + 1), H(xi + 1, yi + 1), sx), sy);
        }

        static Texture2D NormalMap(string name, System.Func<float, float, float> height, float strength)
        {
            string path = $"{Dir}/Textures/{name}.png";
            if (!AssetDatabase.IsValidFolder(Dir + "/Textures")) AssetDatabase.CreateFolder(Dir, "Textures");
            const int size = 512;
            var h = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    h[y * size + x] = height(x / (float)size, y / (float)size);
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false, true);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = h[y * size + (x + 1) % size] - h[y * size + (x + size - 1) % size];
                    float dy = h[((y + 1) % size) * size + x] - h[((y + size - 1) % size) * size + x];
                    var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    tex.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f));
                }
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 512;
            importer.streamingMipmaps = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static AnimatorController BuildAnimator()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ac.parameters[1].defaultBool = true;
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    clips[clip.name] = clip;

            var sm = ac.layers[0].stateMachine;
            var locomotion = ac.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(clips["Idle"], 0f);
            tree.AddChild(clips["Walk"], 3.5f);
            tree.AddChild(clips["Run"], 7f);
            var jump = sm.AddState("Jump");
            jump.motion = clips["Jump"];
            sm.defaultState = locomotion;

            var toJump = locomotion.AddTransition(jump);
            toJump.hasExitTime = false;
            toJump.duration = 0.12f;
            toJump.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            var land = jump.AddTransition(locomotion);
            land.hasExitTime = false;
            land.duration = 0.15f;
            land.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            AssetDatabase.SaveAssets();
            return ac;
        }
    }
}
