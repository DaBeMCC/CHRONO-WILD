#if UNITY_EDITOR
using System.Linq;
using ChronoWild.Core;
using ChronoWild.Health;
using ChronoWild.Player;
using ChronoWild.Prototype;
using ChronoWild.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChronoWild.EditorTools
{
    /// <summary>
    /// Brings the survival layer (health, pause, game over and velociraptors) from the DinoAI
    /// prototype into the island scene that is open, wired to the island's own player.
    /// </summary>
    public static class IslandSurvivalInstaller
    {
        const string PrototypeScene = "Assets/Prototype/DinoAI/DinoPrototype.unity";
        const string RaptorPrefab = "Assets/ChronoWild/Prefabs/Raptor.prefab";
        const float SeaLevel = 0.6f;

        // Raptor dens in the jungle, away from the beach where the player starts (x, z, territory radius).
        static readonly Vector3[] Dens =
        {
            new Vector3(165f, 315f, 32f),   // near the medical kit with the claw marks
            new Vector3(225f, 420f, 30f),   // on the way to the navigation module
            new Vector3(390f, 330f, 30f),   // foot of the volcano, by the chrono core
        };

        static readonly string[] Tips =
        {
            "Los velocirraptores cazan en la selva: si uno te ve, aléjate rápido.",
            "Un raptor deja de perseguirte si te alejas de su territorio.",
            "Busca las piezas cerca de donde se apaga el haz de luz.",
            "El meteorito baja un poco cada día. No pierdas tiempo.",
        };

        [MenuItem("CHRONO WILD/World/Install Survival (Health, Raptors)")]
        public static void Install()
        {
            var controller = Object.FindFirstObjectByType<DebugFirstPersonController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogError("Install Survival: no DebugFirstPersonController in the open scene.");
                return;
            }
            var scene = controller.gameObject.scene;
            var raptorPrefab = EnsureRaptorPrefab();
            if (raptorPrefab == null)
                return;
            EditorSceneManager.SetActiveScene(scene);

            var player = controller.gameObject;
            var health = GetOrAdd<PlayerHealth>(player);
            var states = GetOrAdd<GameStateController>(player);
            states.backpackEnabled = false;
            var bridge = GetOrAdd<GameplayBridge>(player);
            bridge.states = states;
            bridge.controller = controller;
            var camera = controller.cameraPivot != null ? controller.cameraPivot.GetComponent<Camera>() : player.GetComponentInChildren<Camera>();
            if (camera != null)
                GetOrAdd<CameraShake>(camera.gameObject).health = health;

            ChronoWildInstaller.InstallUI(states, health, null, camera);
            AdaptUI(bridge);
            ChronoWildInstaller.EnsureEventSystem();
            PlaceRaptors(raptorPrefab, player.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Install Survival: health, pause and game over on '{player.name}', {Dens.Length} raptors placed.");
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        /// <summary>Saves the configured raptor from the prototype scene as a prefab (once).</summary>
        static GameObject EnsureRaptorPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RaptorPrefab);
            if (existing != null)
                return existing;
            var proto = EditorSceneManager.OpenScene(PrototypeScene, OpenSceneMode.Additive);
            try
            {
                var dino = proto.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DinoAI>(true)).FirstOrDefault();
                if (dino == null)
                {
                    Debug.LogError("Install Survival: no DinoAI found in " + PrototypeScene);
                    return null;
                }
                var copy = Object.Instantiate(dino.gameObject);
                copy.name = "Raptor";
                var ai = copy.GetComponent<DinoAI>();
                ai.player = null;
                ai.target = null;
                System.IO.Directory.CreateDirectory("Assets/ChronoWild/Prefabs");
                var prefab = PrefabUtility.SaveAsPrefabAsset(copy, RaptorPrefab);
                Object.DestroyImmediate(copy);
                return prefab;
            }
            finally
            {
                EditorSceneManager.CloseScene(proto, true);
            }
        }

        // Overlays with a real transparent background (the green-screen originals plus the chroma
        // key shader left a green box in a linear-colour project).
        static Sprite Sprite(string name)
        {
            string path = "Assets/ChronoWild/Art/" + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && (importer.textureType != TextureImporterType.Sprite || !importer.alphaIsTransparency))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void AdaptUI(GameplayBridge bridge)
        {
            var ui = GameObject.Find("Chrono Wild UI");
            if (ui == null)
                return;
            foreach (var (path, sprite) in new[] { ("Damage/garra1", "garra1"), ("Damage/garra2", "garra2"), ("Death/Death Image", "has_muerto") })
            {
                var img = ui.transform.Find(path)?.GetComponent<UnityEngine.UI.Image>();
                if (img == null) continue;
                img.sprite = Sprite(sprite);
                img.material = null;
                img.preserveAspect = false;
            }
            // The frozen island shows through the death frame's window.
            var deathPanel = ui.transform.Find("Death")?.GetComponent<UnityEngine.UI.Image>();
            if (deathPanel != null) deathPanel.color = new Color(0f, 0f, 0f, 0.45f);
            var hideList = new System.Collections.Generic.List<GameObject>();
            foreach (var name in new[] { "Heart", "Health" })
                if (ui.transform.Find(name) is { } t) hideList.Add(t.gameObject);
            bridge.hideDuringIntro = hideList.ToArray();
            EditorUtility.SetDirty(bridge);
            var hud = ui.GetComponent<GameHUD>();
            // Health goes to the bottom-left corner, clear of the mission HUD and the debug overlay.
            foreach (var name in new[] { "Heart", "Health" })
            {
                var t = ui.transform.Find(name) as RectTransform;
                if (t == null) continue;
                t.anchorMin = t.anchorMax = Vector2.zero;
                t.pivot = Vector2.zero;
                t.anchoredPosition = new Vector2(name == "Heart" ? 40f : 105f, 36f);
            }
            // No inventory on the island yet: no backpack, no item counter.
            var backpack = ui.transform.Find("Backpack");
            if (backpack != null) Object.DestroyImmediate(backpack.gameObject);
            var count = ui.transform.Find("Pause/Count");
            if (count != null) Object.DestroyImmediate(count.gameObject);
            if (hud != null)
            {
                hud.backpack = null;
                hud.backpackBackground = null;
                hud.slotIcons = new UnityEngine.UI.Image[0];
                hud.slotLabels = new UnityEngine.UI.Text[0];
                hud.slotButtons = new UnityEngine.UI.Button[0];
                hud.tooltip = null;
                hud.countText = null;
                hud.tips = Tips;
                if (hud.deathText != null)
                    hud.deathText.text = "Presiona ENTER para volver al menú principal";
                EditorUtility.SetDirty(hud);
            }
            var exit = ui.transform.Find("Pause/Salir/Label");
            if (exit != null && exit.GetComponent<UnityEngine.UI.Text>() is { } label)
                label.text = "Salir al menú";
        }

        const string MenuScene = "Assets/ChronoWild/MainMenu.unity";

        /// <summary>Makes the main menu work (its buttons had no saved actions) and puts it first in the build.</summary>
        [MenuItem("CHRONO WILD/World/Install Main Menu")]
        public static void InstallMainMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var game = EditorSceneManager.GetActiveScene();
            var menu = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Additive);
            var root = menu.GetRootGameObjects().FirstOrDefault(g => g.name == "Main Menu");
            if (root == null)
            {
                Debug.LogError("Install Main Menu: no 'Main Menu' canvas in " + MenuScene);
                return;
            }
            var controller = GetOrAdd<MainMenuController>(root);
            controller.gameScene = game.name;
            void Wire(string name, UnityEngine.Events.UnityAction action)
            {
                var button = root.transform.Find(name)?.GetComponent<UnityEngine.UI.Button>();
                if (button == null) return;
                for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                    UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, i);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, action);
                EditorUtility.SetDirty(button);
            }
            Wire("Jugar", controller.Play);
            Wire("Salir", controller.Quit);
            controller.quitButton = root.transform.Find("Salir")?.gameObject;
            // A camera so the screen behind the menu is cleared to black.
            if (!menu.GetRootGameObjects().Any(g => g.GetComponent<Camera>()))
            {
                var camGo = new GameObject("Menu Camera", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, menu);
                var cam = camGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.cullingMask = 0;
            }
            EditorUtility.SetDirty(controller);
            EditorSceneManager.SaveScene(menu);
            EditorSceneManager.CloseScene(menu, true);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScene, true),
                new EditorBuildSettingsScene(game.path, true),
            };
            Debug.Log("Install Main Menu: menu wired to '" + game.name + "' and first in the build.");
        }

        static void PlaceRaptors(GameObject prefab, Transform player)
        {
            var old = GameObject.Find("40 Raptors");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("40 Raptors");
            var terrain = Terrain.activeTerrain;
            var rng = new System.Random(40);
            for (int i = 0; i < Dens.Length; i++)
            {
                var den = Dens[i];
                var pos = new Vector3(den.x, 0f, den.y);
                pos.y = terrain != null ? terrain.SampleHeight(pos) + terrain.transform.position.y + 0.1f : 0f;
                var raptor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                raptor.name = "Raptor " + (i + 1);
                raptor.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                var ai = raptor.GetComponent<DinoAI>();
                ai.target = player;
                ai.territoryRadius = den.z;
                ai.waterLevel = SeaLevel;
                EditorUtility.SetDirty(ai);
            }
        }
    }
}
#endif
