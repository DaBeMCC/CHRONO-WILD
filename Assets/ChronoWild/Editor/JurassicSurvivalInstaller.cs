using System;
using System.Linq;
using ChronoWild.Player;
using ChronoWild.Survival;
using ChronoWild.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ChronoWild.EditorTools
{
    public static class JurassicSurvivalInstaller
    {
        const string Sources = "Supervivencia · recursos";
        const string Pickups = "Assets/_Game/Art/Prefabs/Environment/Pickups/";
        const string Ferns = "Assets/_Game/Art/Prefabs/Vegetation/JurassicRealism/";
        const string Supplies = "Assets/Survival Tools/Prefabs/";

        [MenuItem("CHRONO WILD/World/Install Jurassic Survival Loop")]
        public static void Install()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var player = Object.FindAnyObjectByType<DebugFirstPersonController>();
            var terrain = Terrain.activeTerrain;
            if (scene.name != "Jungle_JurassicWetland" || player == null || terrain == null ||
                player.gameObject.scene != scene || terrain.gameObject.scene != scene)
                throw new InvalidOperationException("Open Jurassic Wetland before installing survival.");

            var wood = Prefab(Pickups + "PF_Pickup_Driftwood_A.prefab");
            var stone = Prefab(Pickups + "PF_Pickup_Stone_A.prefab");
            var fiber = Prefab(Ferns + "PF_JR_GroundFern_A.prefab");
            var shoots = Prefab(Ferns + "PF_JR_GroundFern_C.prefab");
            var food = Prefab(Supplies + "cannedfood.prefab");
            var water = Prefab(Supplies + "waterbottle.prefab");

            var previous = GameObject.Find(Sources);
            if (previous != null)
                Undo.DestroyObjectImmediate(previous);
            var root = new GameObject(Sources);
            Undo.RegisterCreatedObjectUndo(root, "Place survival resources");

            // First caches are near the wreck. The remaining clusters cover the forest edge,
            // swamp approach and western plateau so the loop travels with the story.
            Place(wood, "wood", "Madera", 3, 360, .7f, (299, 123), (313, 135), (322, 147), (284, 174),
                (246, 270), (376, 286), (418, 203), (176, 367), (238, 438), (363, 342));
            Place(stone, "stone", "Piedra", 3, 360, .65f, (298, 128), (321, 138), (307, 149), (278, 173),
                (244, 275), (381, 289), (420, 207), (174, 373), (233, 440), (368, 347));
            Place(fiber, "fiber", "Fibra", 3, 280, .72f, (310, 137), (292, 149), (330, 158), (282, 177),
                (248, 278), (385, 292), (427, 211), (181, 375), (239, 444), (359, 344));
            Place(shoots, "shoots", "Brotes", 2, 210, .68f, (305, 142), (323, 156), (287, 176), (250, 281),
                (378, 294), (429, 214), (183, 371), (236, 448), (361, 352), (291, 240));
            Place(food, "ration", "Raciones", 2, 0, 2.2f, (299, 118), (323, 151), (253, 282), (424, 202), (241, 441));
            Place(water, "water", "Agua", 3, 0, 2.2f, (301, 118), (324, 151), (252, 282), (425, 202), (241, 442),
                (176, 372), (366, 350), (296, 242));

            var rationMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ChronoWild/Materials/CannedFood_URP.mat");
            foreach (var ration in root.GetComponentsInChildren<ResourcePickup>().Where(p => p.itemId == "ration"))
            foreach (var renderer in ration.GetComponentsInChildren<MeshRenderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && materials[i].name == "CannedFood")
                        materials[i] = rationMaterial;
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }

            // The survival-tools bottle ships with Standard materials, which render magenta in URP.
            foreach (var bottle in root.GetComponentsInChildren<ResourcePickup>().Where(p => p.itemId == "water"))
            foreach (var renderer in bottle.GetComponentsInChildren<MeshRenderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && materials[i].shader != null && materials[i].shader.name.StartsWith("Standard"))
                        materials[i] = UrpCopy(materials[i]);
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }

            MarkTrees(scene);
            var survival = player.GetComponent<SurvivalRun>();
            if (survival == null)
                survival = Undo.AddComponent<SurvivalRun>(player.gameObject);
            var settings = new SerializedObject(survival);
            SetMaterial("woodMaterial", "Assets/_Game/Art/Materials/Environment/M_JR_Driftwood.mat");
            SetMaterial("stoneMaterial", "Assets/_Game/Art/Materials/Geology/M_Rock_Dry_Fallback.mat");
            SetMaterial("fiberMaterial", "Assets/_Game/Art/Materials/Vegetation/MossLighting/M_DampForestLitter.mat");
            SetMaterial("waterMaterial", "Assets/Survival Tools/Materials/WaterBottle.mat");
            settings.FindProperty("previewShader").objectReferenceValue = Shader.Find("Universal Render Pipeline/Unlit");
            SetMaterial("flameTemplate", "Assets/_Game/Art/Biomes/Materials/M_Mist.mat");
            settings.FindProperty("freshWoodMaterial").objectReferenceValue = CampMaterial("M_Camp_CutWood", new Color(.67f, .43f, .24f));
            var weave = CampMaterial("M_Camp_Weave", new Color(.55f, .61f, .36f));
            weave.SetFloat("_Cull", 0);
            var litter = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/Vegetation/MossLighting/M_DampForestLitter.mat");
            weave.SetTexture("_BaseMap", litter.GetTexture("_BaseMap"));
            EditorUtility.SetDirty(weave);
            settings.FindProperty("wovenRoofMaterial").objectReferenceValue = weave;
            AssetDatabase.SaveAssetIfDirty(weave);
            SetupSurvivalArt(settings);
            PlaceHerd(scene, terrain);
            var pickups = Object.FindObjectsByType<ResourcePickup>(FindObjectsInactive.Include)
                .Where(p => p.gameObject.scene == scene).ToArray();
            var pickupProperty = settings.FindProperty("pickups");
            pickupProperty.arraySize = pickups.Length;
            for (int i = 0; i < pickups.Length; i++)
                pickupProperty.GetArrayElementAtIndex(i).objectReferenceValue = pickups[i];
            settings.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);

            void SetMaterial(string property, string path)
            {
                settings.FindProperty(property).objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(path);
            }

            void Place(GameObject prefab, string id, string label, int amount, float respawn, float scale, params (float x, float z)[] sites)
            {
                var random = new System.Random(id.Length * 137);
                foreach (var (x, z) in sites)
                {
                    var point = new Vector3(x, 0, z);
                    point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
                    if (point.y <= .9f)
                        continue;
                    if (Physics.CheckSphere(point + Vector3.up * .45f, .24f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        continue;
                    var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    item.transform.localScale *= scale;
                    item.transform.SetPositionAndRotation(point, Quaternion.Euler(0, (float)random.NextDouble() * 360, 0));
                    var pickup = item.GetComponent<ResourcePickup>();
                    if (pickup == null)
                        pickup = item.AddComponent<ResourcePickup>();
                    pickup.itemId = id;
                    pickup.displayName = label;
                    pickup.amount = amount;
                    pickup.respawnSeconds = respawn;
                    pickup.interactDistance = 3.15f;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(pickup);
                }
            }
        }

        const string Art = "Assets/ChronoWild/Art/Survival/";
        const string Herd = "Supervivencia · dryosaurios";

        // Blender exports (Tools/Blender/build_survival_assets.py) name their material slots;
        // each slot is remapped to a URP material built from the converted Poly Haven textures.
        static void SetupSurvivalArt(SerializedObject settings)
        {
            if (!AssetDatabase.IsValidFolder(Art + "Materials"))
                AssetDatabase.CreateFolder(Art.TrimEnd('/'), "Materials");
            var materials = new System.Collections.Generic.Dictionary<string, Material>
            {
                ["Bark"] = SurvivalMaterial("Bark", "Log", .45f),
                ["Stump"] = SurvivalMaterial("Stump", "Stump", .45f),
                ["FirePit"] = SurvivalMaterial("FirePit", "FirePit", .5f),
                ["Bucket"] = SurvivalMaterial("Bucket", "Bucket", .6f),
                ["Rock"] = SurvivalMaterial("Rock", "Rock", .5f),
                ["Branch"] = SurvivalMaterial("Branch", "Branch", .4f),
                ["Fiber"] = SurvivalMaterial("Fiber", "Fiber", .15f),
                ["Char"] = SurvivalMaterial("Char", null, .1f, new Color(.05f, .04f, .035f)),
                ["DryoSkin"] = SurvivalMaterial("DryoSkin", "DryoSkin", .38f),
                ["DryoBelly"] = SurvivalMaterial("DryoBelly", "DryoBelly", .3f),
                ["DryoEye"] = SurvivalMaterial("DryoEye", null, .85f, new Color(.02f, .015f, .01f)),
                ["MeatRaw"] = SurvivalMaterial("MeatRaw", "MeatRaw", .62f),
                ["MeatCooked"] = SurvivalMaterial("MeatCooked", "MeatCooked", .45f),
                ["Bone"] = SurvivalMaterial("Bone", null, .35f, new Color(.86f, .81f, .7f)),
            };
            string[] models = { "SM_Log", "SM_Stick", "SM_Stump", "SM_FirePit", "SM_Bucket", "SM_MeatRaw", "SM_MeatCooked",
                "SM_StoneAxe", "SM_Spear", "SM_Club", "SM_Torch", "SM_Stone_A", "SM_Stone_B", "SM_Stone_C", "SK_Dryosaurus" };
            foreach (var model in models)
            {
                var importer = AssetImporter.GetAtPath(Art + "Models/" + model + ".fbx") as ModelImporter;
                if (importer == null)
                    throw new InvalidOperationException($"Missing {model}.fbx · run Tools/Blender/build_survival_assets.py");
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.importAnimation = false;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                foreach (var pair in materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
                importer.SaveAndReimport();
            }

            void SetModel(string property, string model) =>
                settings.FindProperty(property).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Models/" + model + ".fbx");
            SetModel("logPrefab", "SM_Log");
            SetModel("stickPrefab", "SM_Stick");
            SetModel("stumpPrefab", "SM_Stump");
            SetModel("firePitPrefab", "SM_FirePit");
            SetModel("bucketPrefab", "SM_Bucket");
            SetModel("meatRawPrefab", "SM_MeatRaw");
            SetModel("stoneAxePrefab", "SM_StoneAxe");
            SetModel("spearPrefab", "SM_Spear");
            SetModel("clubPrefab", "SM_Club");
            SetModel("torchPrefab", "SM_Torch");
            settings.FindProperty("ghostMaterial").objectReferenceValue = GhostMaterial();

            var icon = AssetImporter.GetAtPath("Assets/ChronoWild/Resources/SurvivalGuide/energy.png") as TextureImporter;
            if (icon != null && !icon.alphaIsTransparency)
            {
                icon.alphaIsTransparency = true;
                icon.SaveAndReimport();
            }
        }

        static Material SurvivalMaterial(string name, string textures, float smoothness, Color? tint = null)
        {
            string path = Art + "Materials/M_S_" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_S_" + name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", tint ?? Color.white);
            material.SetFloat("_Smoothness", smoothness);
            if (textures != null)
            {
                material.SetTexture("_BaseMap", Texture(textures, "Albedo", TextureImporterType.Default, true));
                var normal = Texture(textures, "Normal", TextureImporterType.NormalMap, false);
                material.SetTexture("_BumpMap", normal);
                if (normal != null) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
                var mask = Texture(textures, "MetallicSmooth", TextureImporterType.Default, false);
                material.SetTexture("_MetallicGlossMap", mask);
                if (mask != null) material.EnableKeyword("_METALLICSPECGLOSSMAP"); else material.DisableKeyword("_METALLICSPECGLOSSMAP");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D Texture(string set, string kind, TextureImporterType type, bool srgb)
        {
            string path = $"{Art}Textures/T_{set}_{kind}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return null;
            if (importer.textureType != type || importer.sRGBTexture != srgb)
            {
                importer.textureType = type;
                importer.sRGBTexture = srgb;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material GhostMaterial()
        {
            string path = Art + "Materials/M_S_BuildGhost.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "M_S_BuildGhost" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", 0);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(.55f, .86f, 1f, .38f));
            EditorUtility.SetDirty(material);
            return material;
        }

        static void PlaceHerd(UnityEngine.SceneManagement.Scene scene, Terrain terrain)
        {
            var previous = GameObject.Find(Herd);
            if (previous != null)
                Undo.DestroyObjectImmediate(previous);
            var root = new GameObject(Herd);
            Undo.RegisterCreatedObjectUndo(root, "Place dryosaurs");
            var herd = root.AddComponent<DryosaurHerd>();
            herd.model = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Models/SK_Dryosaurus.fbx");
            // Clearings at the forest edge and plateau, a short walk from the camp resources.
            var sites = new System.Collections.Generic.List<Vector3>();
            foreach (var (x, z) in new[] { (318f, 128f), (274f, 186f), (240f, 290f), (392f, 300f), (436f, 218f),
                         (172f, 384f), (230f, 452f), (352f, 356f), (300f, 250f), (268f, 228f) })
            {
                var point = new Vector3(x, 0, z);
                point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
                if (point.y > 1.4f)
                    sites.Add(point);
            }
            herd.spawnPoints = sites.ToArray();
            herd.population = 8;
        }

        static Material UrpCopy(Material source)
        {
            string path = $"Assets/ChronoWild/Materials/{source.name}_URP.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = source.name + "_URP" };
            if (source.HasProperty("_MainTex")) material.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            if (source.HasProperty("_Color")) material.SetColor("_BaseColor", source.GetColor("_Color"));
            if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null)
            {
                material.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                material.EnableKeyword("_NORMALMAP");
            }
            material.SetFloat("_Smoothness", source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : .5f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Material CampMaterial(string name, Color color)
        {
            string path = $"Assets/ChronoWild/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            material.SetFloat("_Smoothness", .18f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void MarkTrees(UnityEngine.SceneManagement.Scene scene)
        {
            var groups = Object.FindObjectsByType<LODGroup>();
            var candidates = groups.Where(g => g.gameObject.scene == scene &&
                (g.name.StartsWith("PF_ConiferHigh") || g.name.StartsWith("Background Conifer")) &&
                g.GetComponent<Collider>() != null).ToArray();
            var selected = new System.Collections.Generic.HashSet<LODGroup>();
            foreach (var (x, z) in new[] { (309,166), (289,188), (328,194), (285,224), (321,231), (349,220), (362,286), (237,285) })
            {
                foreach (var tree in candidates.Where(g => !selected.Contains(g))
                    .OrderBy(g => (new Vector2(g.transform.position.x, g.transform.position.z) - new Vector2(x,z)).sqrMagnitude)
                    .Take(3))
                {
                    selected.Add(tree);
                    if (!tree.TryGetComponent<FellableTree>(out _))
                        Undo.AddComponent<FellableTree>(tree.gameObject);
                    foreach (var child in tree.GetComponentsInChildren<Transform>(true))
                    {
                        var go = child.gameObject;
                        var flags = GameObjectUtility.GetStaticEditorFlags(go);
                        var baked = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
                        if ((flags & baked) != 0)
                            GameObjectUtility.SetStaticEditorFlags(go, flags & ~baked);
                    }
                }
            }
        }

        static GameObject Prefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path) ??
            throw new InvalidOperationException($"Missing prefab at {path}");
    }
}
