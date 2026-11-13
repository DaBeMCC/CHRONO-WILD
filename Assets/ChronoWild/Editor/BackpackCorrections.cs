#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ChronoWild.Interaction;
using ChronoWild.UI;

namespace ChronoWild.EditorTools
{
    public static class BackpackCorrections
    {
        [MenuItem("Chrono Wild/Apply Backpack Corrections")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var hud = Object.FindAnyObjectByType<GameHUD>();
            if (!hud || !hud.backpack) throw new System.InvalidOperationException("Open DinoPrototype first.");
            foreach (var c in Object.FindObjectsByType<Collectible>(FindObjectsSortMode.None))
            {
                if (!c.VisibilityUpgradeApplied) { c.transform.localScale *= 2f; c.MarkVisibilityUpgrade(); }
                var lightTransform = c.transform.Find("Collectible Glow");
                var light = lightTransform ? lightTransform.GetComponent<Light>() : null;
                if (!light) { var go = new GameObject("Collectible Glow", typeof(Light)); go.transform.SetParent(c.transform, false); light = go.GetComponent<Light>(); }
                light.type = LightType.Point; light.color = new Color(0.55f, 0.9f, 1f); light.range = 3f; light.intensity = 2.5f; light.shadows = LightShadows.None;
                // Imported Survival Tools uses Standard shaders, which render magenta in URP.
                // Use owned copies so imported originals and the user's material edits remain intact.
                foreach (var renderer in c.GetComponentsInChildren<Renderer>())
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var old = materials[i]; if (!old || !old.shader || !(old.shader.name == "Standard" || old.shader.name == "Autodesk Interactive" || old.shader.name == "Standard (Specular setup)")) continue;
                        Directory.CreateDirectory("Assets/ChronoWild/Materials");
                        var path = "Assets/ChronoWild/Materials/" + old.name + "_URP.mat";
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (!mat)
                        {
                            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                            mat.SetTexture("_BaseMap", old.GetTexture("_MainTex")); mat.SetColor("_BaseColor", old.GetColor("_Color"));
                            if (old.HasProperty("_BumpMap")) { mat.SetTexture("_BumpMap",old.GetTexture("_BumpMap")); if (old.GetTexture("_BumpMap")) mat.EnableKeyword("_NORMALMAP"); }
                            AssetDatabase.CreateAsset(mat,path);
                        }
                        materials[i] = mat;
                    }
                    renderer.sharedMaterials = materials;
                }
                EditorUtility.SetDirty(c);
            }
            if (!hud.backpackContent)
            {
                var go = new GameObject("Backpack Content", typeof(RectTransform), typeof(CanvasGroup));
                go.transform.SetParent(hud.backpack.transform,false);
                Stretch((RectTransform)go.transform);
                hud.backpackContent = go.GetComponent<CanvasGroup>();
                hud.backpackBackground.transform.SetParent(go.transform,false);
                if (hud.tooltip) hud.tooltip.transform.SetParent(go.transform,false);
            }
            hud.backpack.GetComponent<UnityEngine.UI.Image>().color = new Color(0,0,0,0.88f);
            hud.backpack.alpha = 0; hud.backpack.blocksRaycasts = false; hud.backpack.interactable = false;
            hud.backpackContent.alpha = 0;
            foreach (var icon in hud.slotIcons)
            {
                if (!icon || icon.transform.Find("Slot Border")) continue;
                var frame = new GameObject("Slot Border",typeof(RectTransform)); frame.transform.SetParent(icon.transform,false); Stretch((RectTransform)frame.transform);
                Edge(frame.transform,"Top",new Vector2(0,1),Vector2.one,new Vector2(0,-3),Vector2.zero);
                Edge(frame.transform,"Bottom",Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,3));
                Edge(frame.transform,"Left",Vector2.zero,new Vector2(0,1),Vector2.zero,new Vector2(3,0));
                Edge(frame.transform,"Right",new Vector2(1,0),Vector2.one,new Vector2(-3,0),Vector2.zero);
            }
            EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }
        public static void RebuildPreviewIcons()
        {
            foreach (var id in new[] { "radio", "lata", "bateria", "cinta", "linterna" })
            {
                var item = AssetDatabase.LoadAssetAtPath<ChronoWild.Inventory.ItemDefinition>("Assets/ChronoWild/Inventory/"+id+".asset");
                var preview = new PreviewRenderUtility();
                var previewMaterials = new System.Collections.Generic.List<Material>();
                try
                {
                    preview.BeginStaticPreview(new Rect(0,0,256,256));
                    var clone = UnityEngine.Object.Instantiate(item.prefab);
                    clone.transform.position=Vector3.zero;
                    foreach(var collider in clone.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                    foreach(var r in clone.GetComponentsInChildren<Renderer>())
                    {
                        var mats=r.sharedMaterials;
                        for(int i=0;i<mats.Length;i++)
                        {
                            if (!mats[i]) continue;
                            var source=mats[i]; var mat=new Material(Shader.Find("Standard"));
                            if(source.HasProperty("_MainTex")) mat.mainTexture=source.GetTexture("_MainTex");
                            if(source.HasProperty("_Color")) mat.color=source.GetColor("_Color");
                            mat.SetFloat("_Glossiness",0.2f); previewMaterials.Add(mat); mats[i]=mat;
                        }
                        r.sharedMaterials=mats;
                    }
                    preview.AddSingleGO(clone);
                    var renderers=clone.GetComponentsInChildren<Renderer>(); var b=renderers[0].bounds;
                    foreach(var r in renderers) b.Encapsulate(r.bounds);
                    preview.camera.fieldOfView=30;
                    float distance=b.extents.magnitude/Mathf.Sin(15*Mathf.Deg2Rad)*1.15f;
                    preview.camera.transform.position=b.center+new Vector3(1,0.65f,-1).normalized*distance;
                    preview.camera.transform.LookAt(b.center); preview.camera.nearClipPlane=distance*0.01f; preview.camera.farClipPlane=distance*10;
                    preview.camera.clearFlags=CameraClearFlags.SolidColor; preview.camera.backgroundColor=Color.clear;
                    preview.lights[0].intensity=1.4f; preview.lights[0].transform.rotation=Quaternion.Euler(40,40,0);
                    preview.lights[1].intensity=1;
                    preview.Render(false);
                    var texture=preview.EndStaticPreview();
                    var path="Assets/ChronoWild/Art/"+id+"_preview.png";
                    File.WriteAllBytes(path,texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
                    AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
                    var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                    importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
                    importer.alphaSource=TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency=true;
                    importer.SaveAndReimport();
                    item.icon=AssetDatabase.LoadAssetAtPath<Sprite>(path); EditorUtility.SetDirty(item);
                }
                finally { preview.Cleanup(); foreach(var mat in previewMaterials) UnityEngine.Object.DestroyImmediate(mat); }
            }
            AssetDatabase.SaveAssets();
        }
        static void Stretch(RectTransform r) { r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.offsetMin=r.offsetMax=Vector2.zero; }
        static void Edge(Transform parent,string name,Vector2 min,Vector2 max,Vector2 offsetMin,Vector2 offsetMax)
        {
            var go = new GameObject(name,typeof(RectTransform),typeof(UnityEngine.UI.Image)); go.transform.SetParent(parent,false);
            var r=(RectTransform)go.transform; r.anchorMin=min; r.anchorMax=max; r.offsetMin=offsetMin; r.offsetMax=offsetMax;
            var image=go.GetComponent<UnityEngine.UI.Image>(); image.color=new Color(0.9f,0.95f,0.85f,0.9f); image.raycastTarget=false;
        }
    }
}
#endif
