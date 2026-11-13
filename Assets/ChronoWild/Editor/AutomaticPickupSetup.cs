#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ChronoWild.Interaction;
using ChronoWild.UI;
namespace ChronoWild.EditorTools
{
    public static class AutomaticPickupSetup
    {
        static readonly string[] ids={"radio","lata","cable","bateria","pila_aa","pila_c","cinta","linterna","usb","disquete","tarjeta","engranaje","manivela","valvula","encendedor"};
        static readonly string[] labels={"la radio","la lata de comida","el cable","la batería","la pila AA","la pila C","la cinta aislante","la linterna","la memoria USB","el disquete","la tarjeta de seguridad","el engranaje","la manivela","la válvula","el encendedor"};
        static readonly string[] uses={
            "buscar una señal de rescate|escuchar avisos de peligro antes de acercarme al portal",
            "recuperar fuerzas durante la exploración|tener una reserva de alimento mientras busco una salida",
            "reconectar la alimentación del portal|reparar el cableado de la consola",
            "alimentar la consola del portal|mantener encendido el equipo de emergencia",
            "alimentar un dispositivo portátil|devolver energía a los sensores de la ruta",
            "dar energía al equipo de respaldo|mantener activa la señal de emergencia",
            "aislar cables dañados|asegurar una reparación provisional del portal",
            "explorar sin perder el camino en la oscuridad|localizar piezas entre los restos del experimento",
            "recuperar los datos de calibración del portal|consultar las instrucciones del experimento",
            "recuperar el registro antiguo del portal|buscar las coordenadas de regreso",
            "entrar en las zonas restringidas|acceder al control del portal",
            "reparar la transmisión del mecanismo|hacer que vuelva a moverse el sistema del portal",
            "accionar el mecanismo cuando falle la energía|abrir manualmente el acceso de emergencia",
            "regular el flujo del sistema de refrigeración|controlar la presión del mecanismo del portal",
            "encender una fogata para mantenerme a salvo|conseguir luz de emergencia si se agotan las pilas"
        };
        [MenuItem("Chrono Wild/Set Up Automatic Pickup")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new System.Exception("Exit Play Mode first");
            foreach(var c in Object.FindObjectsByType<Collectible>())
            {
                if(!c.FloatingPlacementApplied) { var pos=c.transform.position; pos.y*=0.5f; c.transform.position=pos; c.MarkFloatingPlacement(); }
                foreach(var collider in c.GetComponentsInChildren<Collider>()) collider.enabled=false;
                var trigger=c.GetComponent<SphereCollider>(); if(!trigger) trigger=c.gameObject.AddComponent<SphereCollider>();
                trigger.enabled=true; trigger.isTrigger=true; trigger.center=Vector3.zero;
                trigger.radius=0.65f/Mathf.Max(c.transform.lossyScale.x,c.transform.lossyScale.y,c.transform.lossyScale.z);
                var rb=c.GetComponent<Rigidbody>(); if(!rb) rb=c.gameObject.AddComponent<Rigidbody>();
                rb.useGravity=false; rb.isKinematic=true;
                EditorUtility.SetDirty(c);
            }
            for(int i=0;i<ids.Length;i++)
            {
                var item=AssetDatabase.LoadAssetAtPath<ChronoWild.Inventory.ItemDefinition>("Assets/ChronoWild/Inventory/"+ids[i]+".asset");
                item.pickupName=labels[i]; item.survivalUses=uses[i].Split('|'); EditorUtility.SetDirty(item);
            }
            var hud=Object.FindAnyObjectByType<GameHUD>();
            var message=hud.GetComponentInChildren<PickupMessageUI>();
            if(!message)
            {
                var go=new GameObject("Pickup Message",typeof(RectTransform),typeof(CanvasGroup),typeof(UnityEngine.UI.Image),typeof(PickupMessageUI)); go.transform.SetParent(hud.transform,false);
                var r=(RectTransform)go.transform; r.anchorMin=new Vector2(0.12f,0.035f); r.anchorMax=new Vector2(0.88f,0.14f); r.offsetMin=r.offsetMax=Vector2.zero;
                var background=go.GetComponent<UnityEngine.UI.Image>(); background.color=new Color(0.015f,0.025f,0.02f,0.87f); background.raycastTarget=false;
                var text=new GameObject("Message Text",typeof(RectTransform),typeof(UnityEngine.UI.Text)); text.transform.SetParent(go.transform,false);
                var tr=(RectTransform)text.transform; tr.anchorMin=Vector2.zero; tr.anchorMax=Vector2.one; tr.offsetMin=new Vector2(24,8); tr.offsetMax=new Vector2(-24,-8);
                var label=text.GetComponent<UnityEngine.UI.Text>(); label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize=28; label.alignment=TextAnchor.MiddleCenter; label.color=Color.white; label.raycastTarget=false;
                message=go.GetComponent<PickupMessageUI>(); message.label=label; message.group=go.GetComponent<CanvasGroup>(); message.group.alpha=0; message.group.blocksRaycasts=false; message.group.interactable=false;
            }
            message.inventory=hud.inventory; message.states=hud.states;
            EditorUtility.SetDirty(message);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene); EditorSceneManager.SaveScene(hud.gameObject.scene); AssetDatabase.SaveAssets();
        }
    }
}
#endif
