#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ChronoWild.Core;
using ChronoWild.Health;
using ChronoWild.Inventory;
using ChronoWild.Interaction;
using ChronoWild.UI;
using ChronoWild.FirstPerson;
using ChronoWild.Prototype;

namespace ChronoWild.EditorTools
{
    public static class ChronoWildInstaller
    {
        const string Root = "Assets/ChronoWild/";
        static readonly string[] ids = { "radio", "lata", "cable", "bateria", "pila_aa", "pila_c", "cinta", "linterna", "usb", "disquete", "tarjeta", "engranaje", "manivela", "valvula", "encendedor" };
        static readonly string[] names = { "Radio", "Lata", "Cable", "Batería", "Pila AA", "Pila C", "Cinta aislante", "Linterna", "Memoria USB", "Disquete", "Tarjeta de seguridad", "Engranaje", "Manivela", "Válvula", "Encendedor" };
        static readonly string[] hints = { "Una voz antigua podría guiarte al portal.", "La supervivencia también necesita alimento.", "Lleva corriente hasta la máquina.", "Una reserva de energía para el portal.", "Una pequeña carga puede despertar un circuito.", "La consola necesita energía de respaldo.", "Aísla una conexión rota.", "La oscuridad esconde piezas importantes.", "Tal vez contiene datos de calibración.", "Un registro antiguo del experimento.", "Abre el acceso restringido.", "La transmisión perdió una pieza.", "Puede activar un mecanismo manual.", "Controla el flujo de energía.", "Útil cuando todo queda a oscuras." };
        static readonly string[] paths = {
            "Assets/Survival Tools/Prefabs/walkie.prefab", "Assets/Survival Tools/Prefabs/cannedfood.prefab", "Assets/HorrorPuzzleItems/Cable/Prefabs/Cable_Black.prefab", "Assets/Survival Tools/Prefabs/battery.prefab", "Assets/HorrorPuzzleItems/Battery/Prefabs/BatteryAA.prefab", "Assets/HorrorPuzzleItems/Battery/Prefabs/BatteryC.prefab", "Assets/Survival Tools/Prefabs/tape.prefab", "Assets/Survival Tools/Prefabs/flashlight.prefab", "Assets/HorrorPuzzleItems/USBKey/Prefabs/USBKey_Metal.prefab", "Assets/HorrorPuzzleItems/Floppy/Prefabs/Floppy_Blue.prefab", "Assets/HorrorPuzzleItems/Card/Prefabs/Card_Security.prefab", "Assets/HorrorPuzzleItems/Gear/Prefabs/Gear.prefab", "Assets/HorrorPuzzleItems/Crank/Prefabs/Crank.prefab", "Assets/HorrorPuzzleItems/Valve/Prefabs/Valve.prefab", "Assets/HorrorPuzzleItems/Lighter/Prefabs/Lighter_Metal.prefab"
        };
        static readonly string[] iconPaths = {
            "", "", "Assets/HorrorPuzzleItems/Cable/Icons/Cable_Black.png", "", "Assets/HorrorPuzzleItems/Battery/Icons/Icon_Battery_AA.png", "Assets/HorrorPuzzleItems/Battery/Icons/Icon_Battery_C.png", "", "", "Assets/HorrorPuzzleItems/USBKey/Icons/Icon_USBKey_Metal.png", "Assets/HorrorPuzzleItems/Floppy/Icons/Icon_Floopy_Blue.png", "Assets/HorrorPuzzleItems/Card/Icons/Icon_Card_Security.png", "Assets/HorrorPuzzleItems/Gear/Icons/Icon_Gear.png", "Assets/HorrorPuzzleItems/Crank/Icons/Icon_Crank.png", "Assets/HorrorPuzzleItems/Valve/Icons/Icon_Valve.png", "Assets/HorrorPuzzleItems/Lighter/Icons/Icon_Lighter_Chrome.png"
        };
        [MenuItem("Chrono Wild/Install Planned Prototype")]
        public static void Install()
        {
            PrepareTextures();
            var definitions = CreateCatalog();
            CreateMaterials();
            var scene = EditorSceneManager.OpenScene("Assets/Prototype/DinoAI/DinoPrototype.unity");
            var player = UnityEngine.Object.FindFirstObjectByType<PrototypePlayer>();
            if (!player) throw new Exception("PrototypePlayer missing");
            var state = player.gameObject.GetComponent<GameStateController>() ? player.gameObject.GetComponent<GameStateController>() : player.gameObject.AddComponent<GameStateController>();
            var health = player.gameObject.GetComponent<PlayerHealth>() ? player.gameObject.GetComponent<PlayerHealth>() : player.gameObject.AddComponent<PlayerHealth>();
            var inventory = player.gameObject.GetComponent<ChronoWild.Inventory.Inventory>() ? player.gameObject.GetComponent<ChronoWild.Inventory.Inventory>() : player.gameObject.AddComponent<ChronoWild.Inventory.Inventory>();
            inventory.SetCatalog(definitions);
            var camera = player.GetComponentInChildren<Camera>();
            var interactor = player.gameObject.GetComponent<ItemInteractor>() ? player.gameObject.GetComponent<ItemInteractor>() : player.gameObject.AddComponent<ItemInteractor>();
            interactor.viewCamera = camera; interactor.inventory = inventory;
            player.hitFlash = null;
            if (camera && camera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData)) cameraData.renderPostProcessing = true;
            if (camera) { var shake = camera.GetComponent<CameraShake>() ? camera.GetComponent<CameraShake>() : camera.gameObject.AddComponent<CameraShake>(); shake.health = health; }
            InstallCollectibles(definitions);
            InstallUI(state, health, inventory, camera);
            InstallHands(player, camera);
            EnsureEventSystem();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            CreateMenu();
            var scenes = new[] { "Assets/ChronoWild/MainMenu.unity", "Assets/Prototype/DinoAI/DinoPrototype.unity" };
            EditorBuildSettings.scenes = Array.ConvertAll(scenes, p => new EditorBuildSettingsScene(p, true));
            EditorSceneManager.OpenScene("Assets/Prototype/DinoAI/DinoPrototype.unity");
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("Chrono Wild planned prototype installed.");
        }
        static void PrepareTextures()
        {
            foreach (var file in new[] { "vistaMochila", "has_muerto", "garra1", "garra2" })
            {
                var p = Root + "Art/" + file + ".jpeg"; AssetDatabase.ImportAsset(p);
                var importer = AssetImporter.GetAtPath(p) as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Sprite; importer.alphaIsTransparency = true; importer.SaveAndReimport();
            }
        }
        static ItemDefinition[] CreateCatalog()
        {
            var result = new ItemDefinition[15];
            for (int i = 0; i < result.Length; i++)
            {
                var p = Root + "Inventory/" + ids[i] + ".asset";
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(p);
                if (!item) { item = ScriptableObject.CreateInstance<ItemDefinition>(); AssetDatabase.CreateAsset(item, p); }
                item.id = ids[i]; item.displayName = names[i]; item.hint = hints[i];
                item.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                item.icon = !string.IsNullOrEmpty(iconPaths[i]) ? AssetDatabase.LoadAssetAtPath<Sprite>(iconPaths[i]) : null;
                if (!item.icon) item.icon = CreatePreviewIcon(item.prefab, ids[i]);
                EditorUtility.SetDirty(item); result[i] = item;
            }
            return result;
        }
        static Sprite CreatePreviewIcon(GameObject prefab, string id)
        {
            if (!prefab) return null;
            var path = Root + "Art/" + id + "_preview.png";
            var preview = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);
            if (preview)
            {
                var readable = new Texture2D(preview.width, preview.height, TextureFormat.RGBA32, false);
                RenderTexture temp = RenderTexture.GetTemporary(preview.width, preview.height);
                Graphics.Blit(preview, temp); var previous = RenderTexture.active; RenderTexture.active = temp;
                readable.ReadPixels(new Rect(0,0,preview.width,preview.height),0,0); readable.Apply();
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temp);
                File.WriteAllBytes(path, readable.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(readable);
                AssetDatabase.ImportAsset(path);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                importer.textureType = TextureImporterType.Sprite; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        static void CreateMaterials()
        {
            var outline = new Material(Shader.Find("ChronoWild/CollectibleOutline")); outline.SetColor("_Color", Color.white);
            var dir = Root + "Resources"; if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Resources");
            var path = dir + "/CollectibleOutline.mat"; var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!old) AssetDatabase.CreateAsset(outline, path); else UnityEngine.Object.DestroyImmediate(outline);
            foreach (var pair in new[] { ("Claw", new Color(0,1,0), 0.32f), ("Death", new Color(0.08f,0.28f,0.07f), 0.17f) })
            {
                var p = Root + "UI/Chroma" + pair.Item1 + ".mat"; var mat = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (!mat) { mat = new Material(Shader.Find("ChronoWild/UIChromaKey")); AssetDatabase.CreateAsset(mat, p); }
                mat.SetColor("_KeyColor", pair.Item2); mat.SetFloat("_Tolerance", pair.Item3); mat.SetFloat("_Softness", 0.12f); mat.SetFloat("_Halo", 0.6f); if (pair.Item1 == "Death") mat.SetVector("_KeyRegion", new Vector4(0.2f,0.15f,0.8f,0.9f)); EditorUtility.SetDirty(mat);
            }
        }
        static void InstallCollectibles(ItemDefinition[] items)
        {
            var root = GameObject.Find("Collectibles") ?? new GameObject("Collectibles");
            for (int i = 0; i < items.Length; i++)
            {
                var existing = root.transform.Find(ids[i]); if (existing) continue;
                if (!items[i].prefab) { Debug.LogWarning("Missing prefab: " + ids[i]); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(items[i].prefab);
                go.name = ids[i]; go.transform.SetParent(root.transform);
                go.transform.position = new Vector3(-12f + (i % 5) * 6f, 1.8f, -7f + (i / 5) * 7f);
                var bounds = new Bounds(go.transform.position, Vector3.one * 0.5f); bool found = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds); }
                float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                if (maxDimension > 2f) go.transform.localScale *= 1.2f / maxDimension;
                var collider = go.GetComponent<Collider>();
                if (!collider) { var box = go.AddComponent<BoxCollider>(); box.center = go.transform.InverseTransformPoint(bounds.center); box.size = bounds.size / Mathf.Max(0.001f,go.transform.lossyScale.x); }
                foreach (var mesh in go.GetComponentsInChildren<MeshCollider>()) mesh.convex = true;
                var rb = go.GetComponent<Rigidbody>() ? go.GetComponent<Rigidbody>() : go.AddComponent<Rigidbody>(); rb.mass = 1;
                var collectible = go.GetComponent<Collectible>() ? go.GetComponent<Collectible>() : go.AddComponent<Collectible>(); collectible.item = items[i];
            }
        }
        static GameObject UI(string name, Transform parent) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go; }
        static RectTransform Rect(GameObject go, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax) { var r = (RectTransform)go.transform; r.anchorMin=min; r.anchorMax=max; r.offsetMin=offsetMin; r.offsetMax=offsetMax; return r; }
        static UnityEngine.UI.Image Image(string name, Transform parent, Color color, Sprite sprite = null)
        { var go=UI(name,parent); var image=go.AddComponent<UnityEngine.UI.Image>(); image.color=color; image.sprite=sprite; image.raycastTarget=false; return image; }
        static UnityEngine.UI.Text Text(string name, Transform parent, string value, int size, TextAnchor alignment = TextAnchor.MiddleCenter)
        { var go=UI(name,parent); var t=go.AddComponent<UnityEngine.UI.Text>(); t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.text=value; t.fontSize=size; t.alignment=alignment; t.color=Color.white; t.raycastTarget=false; return t; }
        static UnityEngine.UI.Button Button(string label, Transform parent, Vector2 anchor, Vector2 size, UnityEngine.Events.UnityAction action)
        { var image=Image(label,parent,new Color(0.09f,0.13f,0.14f,0.9f)); image.raycastTarget=true; var r=(RectTransform)image.transform; r.anchorMin=r.anchorMax=anchor; r.sizeDelta=size; r.anchoredPosition=Vector2.zero; var b=image.gameObject.AddComponent<UnityEngine.UI.Button>(); AddListener(b.onClick,action); var text=Text("Label",image.transform,label,24); Rect(text.gameObject,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); return b; }
        static CanvasGroup Panel(string name, Transform parent, Color shade)
        { var image=Image(name,parent,shade); Rect(image.gameObject,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); image.raycastTarget=true; return image.gameObject.AddComponent<CanvasGroup>(); }
        static Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Root+"Art/"+name+".jpeg");
        static Sprite CreateHeartSprite()
        {
            var path=Root+"Art/heart.png";
            if (!File.Exists(path))
            {
                var tex=new Texture2D(64,64,TextureFormat.RGBA32,false);
                for(int y=0;y<64;y++) for(int x=0;x<64;x++)
                { float nx=(x-32f)/32f, ny=(y-32f)/32f; float v=Mathf.Pow(nx*nx+ny*ny-0.35f,3)-nx*nx*ny*ny*ny; tex.SetPixel(x,y,v<0 ? Color.white : Color.clear); }
                tex.Apply(); File.WriteAllBytes(path,tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
            }
            AssetDatabase.ImportAsset(path); var importer=AssetImporter.GetAtPath(path) as TextureImporter; importer.textureType=TextureImporterType.Sprite; importer.alphaIsTransparency=true; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        public static void InstallUI(GameStateController state, PlayerHealth health, ChronoWild.Inventory.Inventory inventory, Camera camera)
        {
            var old=GameObject.Find("Chrono Wild UI"); if (old) UnityEngine.Object.DestroyImmediate(old);
            var go=new GameObject("Chrono Wild UI",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=go.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=50;
            var scaler=go.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1920,1080);
            var hud=go.AddComponent<GameHUD>(); hud.states=state; hud.health=health; hud.inventory=inventory;
            var heart=Image("Heart",go.transform,new Color(1,0.3f,0.3f),CreateHeartSprite()); var hr=(RectTransform)heart.transform; hr.anchorMin=hr.anchorMax=new Vector2(0,1); hr.pivot=new Vector2(0,1); hr.anchoredPosition=new Vector2(40,-40); hr.sizeDelta=new Vector2(55,55); hud.heart=heart;
            var hp=Text("Health",go.transform,"100 HP",25,TextAnchor.MiddleLeft); var hpRect=(RectTransform)hp.transform; hpRect.anchorMin=hpRect.anchorMax=new Vector2(0,1); hpRect.pivot=new Vector2(0,1); hpRect.anchoredPosition=new Vector2(105,-42); hpRect.sizeDelta=new Vector2(130,50); hud.healthText=hp;
            var damage=Panel("Damage",go.transform,new Color(0.5f,0,0,0.23f)); damage.blocksRaycasts=false; damage.GetComponent<UnityEngine.UI.Image>().raycastTarget=false; hud.damage=damage;
            var clawMat=AssetDatabase.LoadAssetAtPath<Material>(Root+"UI/ChromaClaw.mat");
            foreach (var name in new[]{"garra1","garra2"}) { var img=Image(name,damage.transform,Color.white,Art(name)); img.material=clawMat; Rect(img.gameObject,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); if(name=="garra1") hud.claw1=img; else hud.claw2=img; }
            var backpack=Panel("Backpack",go.transform,new Color(0,0,0,0.7f)); hud.backpack=backpack;
            var bag=Image("Backpack Image",backpack.transform,Color.white,Art("vistaMochila")); Rect(bag.gameObject,new Vector2(0.12f,0.06f),new Vector2(0.88f,0.93f),Vector2.zero,Vector2.zero); hud.backpackBackground=bag;
            hud.slotIcons=new UnityEngine.UI.Image[15]; hud.slotLabels=new UnityEngine.UI.Text[15]; hud.slotButtons=new UnityEngine.UI.Button[15];
            for(int i=0;i<15;i++) { var slot=Image("Slot "+(i+1),bag.transform,new Color(0,0,0,0.16f)); var r=(RectTransform)slot.transform; r.anchorMin=r.anchorMax=new Vector2(0.15f+(i%5)*0.175f,0.72f-(i/5)*0.27f); r.sizeDelta=new Vector2(105,105); slot.raycastTarget=true; var b=slot.gameObject.AddComponent<UnityEngine.UI.Button>(); hud.slotButtons[i]=b; hud.slotIcons[i]=slot; var hover=slot.gameObject.AddComponent<SlotHover>(); hover.hud=hud; hover.index=i; var label=Text("Name",slot.transform,"?",17); Rect(label.gameObject,new Vector2(0,-0.2f),new Vector2(1,0.15f),Vector2.zero,Vector2.zero); hud.slotLabels[i]=label; }
            var tooltip=Text("Hint",backpack.transform,"",28); Rect(tooltip.gameObject,new Vector2(0.1f,0.02f),new Vector2(0.9f,0.11f),Vector2.zero,Vector2.zero); hud.tooltip=tooltip;
            var pause=Panel("Pause",go.transform,new Color(0,0,0,0.62f)); hud.pause=pause;
            var title=Text("Pause Title",pause.transform,"PAUSA",70); Rect(title.gameObject,new Vector2(0.3f,0.77f),new Vector2(0.7f,0.91f),Vector2.zero,Vector2.zero);
            var count=Text("Count",pause.transform,"0/15",32); Rect(count.gameObject,new Vector2(0.35f,0.68f),new Vector2(0.65f,0.75f),Vector2.zero,Vector2.zero); hud.countText=count;
            Button("Reanudar",pause.transform,new Vector2(0.5f,0.57f),new Vector2(280,60),state.Resume);
            Button("Opciones",pause.transform,new Vector2(0.5f,0.48f),new Vector2(280,60),hud.ToggleOptions);
            Button("Salir",pause.transform,new Vector2(0.5f,0.39f),new Vector2(280,60),state.MainMenu);
            var tip=Text("Tip",pause.transform,"",23); Rect(tip.gameObject,new Vector2(0.15f,0.14f),new Vector2(0.85f,0.22f),Vector2.zero,Vector2.zero); hud.tipText=tip;
            var options=Panel("Options",pause.transform,new Color(0.02f,0.04f,0.05f,0.98f)); Rect(options.gameObject,new Vector2(0.28f,0.25f),new Vector2(0.72f,0.75f),Vector2.zero,Vector2.zero); hud.options=options;
            MakeSlider(options.transform,"Volumen",0.68f,0,1,AudioListener.volume,state.SetVolume);
            MakeSlider(options.transform,"Sensibilidad",0.43f,0.02f,0.4f,0.12f,state.SetSensitivity);
            Button("Volver",options.transform,new Vector2(0.5f,0.16f),new Vector2(220,52),hud.ToggleOptions);
            var death=Panel("Death",go.transform,Color.black); hud.death=death;
            var deathImage=Image("Death Image",death.transform,Color.white,Art("has_muerto")); Rect(deathImage.gameObject,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); deathImage.material=AssetDatabase.LoadAssetAtPath<Material>(Root+"UI/ChromaDeath.mat"); hud.deathBackground=deathImage;
            var deathText=Text("Return",death.transform,"Presiona ENTER para regresar al menú principal",35); Rect(deathText.gameObject,new Vector2(0.1f,0.06f),new Vector2(0.9f,0.16f),Vector2.zero,Vector2.zero); hud.deathText=deathText;
            var source=go.AddComponent<AudioSource>(); source.playOnAwake=false; hud.damageAudio=source;
            var volumeGo=new GameObject("Pause Desaturation",typeof(Volume)); var volume=volumeGo.GetComponent<Volume>(); volume.isGlobal=true; volume.priority=100; volume.weight=1; volume.enabled=false;
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root+"UI/PauseVolume.asset"); if (!profile) { profile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,Root+"UI/PauseVolume.asset"); } if (!profile.TryGet(out ColorAdjustments color)) color=profile.Add<ColorAdjustments>(true); color.saturation.Override(-100); volume.sharedProfile=profile; hud.pauseVolume=volume;
        }
        static void MakeSlider(Transform parent,string label,float y,float min,float max,float value,UnityEngine.Events.UnityAction<float> action)
        { var text=Text(label,parent,label,24); Rect(text.gameObject,new Vector2(0.15f,y+0.06f),new Vector2(0.85f,y+0.17f),Vector2.zero,Vector2.zero); var bg=Image(label+" Slider",parent,new Color(0.25f,0.3f,0.3f)); Rect(bg.gameObject,new Vector2(0.18f,y),new Vector2(0.82f,y+0.06f),Vector2.zero,Vector2.zero); var fill=Image("Fill",bg.transform,new Color(0.7f,0.1f,0.1f)); Rect(fill.gameObject,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero); var handle=Image("Handle",bg.transform,Color.white); var h=(RectTransform)handle.transform; h.sizeDelta=new Vector2(18,38); var slider=bg.gameObject.AddComponent<Slider>(); slider.fillRect=(RectTransform)fill.transform; slider.handleRect=h; slider.targetGraphic=handle; slider.minValue=min; slider.maxValue=max; slider.value=value; AddListener(slider.onValueChanged,action); }
        // Listeners added in the editor only survive in the scene when they are persistent.
        static void AddListener(UnityEngine.Events.UnityEvent e, UnityEngine.Events.UnityAction action)
        { if (action.Target is UnityEngine.Object) UnityEditor.Events.UnityEventTools.AddPersistentListener(e, action); else e.AddListener(action); }
        static void AddListener(UnityEngine.Events.UnityEvent<float> e, UnityEngine.Events.UnityAction<float> action)
        { if (action.Target is UnityEngine.Object) UnityEditor.Events.UnityEventTools.AddPersistentListener(e, action); else e.AddListener(action); }
        public static void EnsureEventSystem() { if (UnityEngine.Object.FindFirstObjectByType<EventSystem>()) return; var go=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule)); }
        static void InstallHands(PrototypePlayer player,Camera camera)
        { if (!camera || camera.transform.Find("Hand Rig")) return; var rig=new GameObject("Hand Rig"); rig.transform.SetParent(camera.transform,false); var motion=rig.AddComponent<HandMotion>(); motion.player=player;
          var right=new GameObject("Right Pivot"); right.transform.SetParent(rig.transform,false); right.transform.localPosition=new Vector3(0.36f,-0.48f,0.8f); motion.rightPivot=right.transform;
          var left=new GameObject("Left Pivot"); left.transform.SetParent(rig.transform,false); left.transform.localPosition=new Vector3(-0.36f,-0.48f,0.8f); motion.leftPivot=left.transform;
          var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PolyOne/Free VR Hands/Prefabs/Free Pack - VR Hands ( Rigged ).prefab");
          if(prefab) { var visual=(GameObject)PrefabUtility.InstantiatePrefab(prefab); visual.transform.SetParent(rig.transform,false); visual.transform.localPosition=new Vector3(0,-0.48f,0.8f); visual.transform.localScale=Vector3.one*0.35f; motion.animator=visual.GetComponentInChildren<Animator>(); motion.runVisual=visual; }
          var rightVisual=GameObject.CreatePrimitive(PrimitiveType.Capsule); rightVisual.name="Right Hand Fallback"; rightVisual.transform.SetParent(right.transform,false); rightVisual.transform.localScale=new Vector3(0.15f,0.25f,0.12f); UnityEngine.Object.DestroyImmediate(rightVisual.GetComponent<Collider>()); motion.rightFallback=rightVisual;
          var leftVisual=GameObject.CreatePrimitive(PrimitiveType.Capsule); leftVisual.name="Left Hand Fallback"; leftVisual.transform.SetParent(left.transform,false); leftVisual.transform.localScale=new Vector3(0.15f,0.25f,0.12f); UnityEngine.Object.DestroyImmediate(leftVisual.GetComponent<Collider>()); motion.leftVisual=leftVisual;
        }
        static void CreateMenu()
        { var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single); var go=new GameObject("Main Menu",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)); go.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay; go.GetComponent<CanvasScaler>().uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
          var bg=Image("Background",go.transform,new Color(0.015f,0.04f,0.04f)); Rect(bg.gameObject,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
          var title=Text("Title",go.transform,"CHRONO WILD",86); Rect(title.gameObject,new Vector2(0.1f,0.57f),new Vector2(0.9f,0.8f),Vector2.zero,Vector2.zero);
          Button("Jugar",go.transform,new Vector2(0.5f,0.43f),new Vector2(300,70),()=>SceneManager.LoadScene("DinoPrototype"));
          Button("Salir",go.transform,new Vector2(0.5f,0.31f),new Vector2(300,70),Application.Quit); EnsureEventSystem(); EditorSceneManager.SaveScene(scene,Root+"MainMenu.unity"); }
    }
}
#endif
