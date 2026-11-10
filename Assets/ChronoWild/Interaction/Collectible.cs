using UnityEngine;
using ChronoWild.Inventory;
namespace ChronoWild.Interaction
{
    public sealed class Collectible : MonoBehaviour
    {
        public ItemDefinition item;
        Renderer[] renderers;
        GameObject[] outlines;
        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            outlines = new GameObject[renderers.Length];
            var material = Resources.Load<Material>("CollectibleOutline");
            if (!material) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                var mesh = renderers[i].GetComponent<MeshFilter>();
                if (!mesh || !mesh.sharedMesh) continue;
                var go = new GameObject("ProximityOutline"); go.transform.SetParent(renderers[i].transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                outlines[i] = go; go.SetActive(false);
            }
        }
        public void SetNearby(bool nearby) { if (outlines == null) return; foreach (var go in outlines) if (go) go.SetActive(nearby); }
    }
}
