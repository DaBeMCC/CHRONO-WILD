#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using ChronoWild.Interaction;
namespace ChronoWild.EditorTools
{
    public static class BackpackVerification
    {
        static int step;
        static double next;
        static Collectible target;
        static CharacterController player;
        static ChronoWild.Inventory.Inventory inventory;
        static Vector3 initial;
        static float lightIntensity;
        static Collectible[] all;
        static int index;
        static double deadline;
        static string report;
        public static void Start()
        {
            foreach(var d in Object.FindObjectsByType<ChronoWild.Prototype.DinoAI>()) d.enabled=false;
            var p=Object.FindAnyObjectByType<ChronoWild.Prototype.PrototypePlayer>(); p.enabled=false;
            player=p.GetComponent<CharacterController>(); inventory=p.GetComponent<ChronoWild.Inventory.Inventory>();
            all=Object.FindObjectsByType<Collectible>(); index=0; report=""; target=all[0]; initial=target.transform.position; lightIntensity=target.GetComponentInChildren<Light>().intensity;
            deadline=EditorApplication.timeSinceStartup+30;
            Object.FindAnyObjectByType<ChronoWild.UI.PickupMessageUI>().duration=12f;
            step=0; next=EditorApplication.timeSinceStartup+0.8; EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<next) return;
            try
            {
                if(EditorApplication.timeSinceStartup>deadline) throw new System.Exception("Contact test timeout");
                if(step==0)
                {
                    if(Mathf.Approximately(initial.y,target.transform.position.y)) throw new System.Exception("No bob motion");
                    if(Mathf.Approximately(lightIntensity,target.GetComponentInChildren<Light>().intensity)) throw new System.Exception("No aura pulse");
                    if(inventory.Count!=index) throw new System.Exception("Collected before contact");
                    player.enabled=false; player.transform.position=target.transform.position-Vector3.up*0.9f-Vector3.forward*1.3f; player.enabled=true;
                    step=1; next=EditorApplication.timeSinceStartup+0.1;
                }
                else if(step==1)
                {
                    player.Move(Vector3.forward*0.1f);
                    if(!target || !target.gameObject.activeInHierarchy) { step=2; next=EditorApplication.timeSinceStartup+0.1; }
                }
                else
                {
                    if(inventory.Count!=index+1) throw new System.Exception("Automatic pickup missing");
                    var message=Object.FindAnyObjectByType<ChronoWild.UI.PickupMessageUI>();
                    if(string.IsNullOrEmpty(message.label.text)) throw new System.Exception("Missing notification");
                    report+="PASS automatic contact item "+index+"; inventory="+inventory.Count+"\n";
                    var hud=Object.FindAnyObjectByType<ChronoWild.UI.GameHUD>();
                    for(int i=0;i<hud.inventory.Catalog.Count;i++) if(hud.inventory.Has(hud.inventory.Catalog[i].id) && hud.slotIcons[i].sprite!=hud.inventory.Catalog[i].icon) throw new System.Exception("Slot not updated");
                    index++;
                    if(index<all.Length)
                    {
                        target=all[index]; initial=target.transform.position; lightIntensity=target.GetComponentInChildren<Light>().intensity;
                        step=0; next=EditorApplication.timeSinceStartup+0.3; return;
                    }
                    System.IO.File.WriteAllText("Temp/automatic-pickup-verification.txt",report+"PASS bob motion and aura pulse for all 15\nPASS fixed slot icons updated\nPASS message: "+message.label.text+"\nCOMPLETE");
                    EditorApplication.update-=Tick;
                }
            }
            catch(System.Exception e) { System.IO.File.WriteAllText("Temp/automatic-pickup-verification.txt","FAIL "+e); EditorApplication.update-=Tick; }
        }
    }
}
#endif
