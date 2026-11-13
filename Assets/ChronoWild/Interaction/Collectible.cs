using UnityEngine;
using ChronoWild.Inventory;
namespace ChronoWild.Interaction
{
    public sealed class Collectible : MonoBehaviour
    {
        public ItemDefinition item;
        [SerializeField, HideInInspector] bool visibilityUpgradeApplied;
        [SerializeField, HideInInspector] bool floatingPlacementApplied;
        public float bobAmplitude = 0.08f;
        public float bobFrequency = 0.35f;
        public float rotationSpeed = 18f;
        public float auraPeakIntensity = 2.5f;
        public float auraPeriod = 3f;
        public bool VisibilityUpgradeApplied => visibilityUpgradeApplied;
        public bool FloatingPlacementApplied => floatingPlacementApplied;
#if UNITY_EDITOR
        public void MarkVisibilityUpgrade() => visibilityUpgradeApplied = true;
        public void MarkFloatingPlacement() => floatingPlacementApplied = true;
#endif
        GameObject[] outlines;
        Vector3 restPosition;
        Quaternion restRotation;
        Light aura;
        float phase;
        bool collected;
        void Awake()
        {
            restPosition = transform.position; restRotation = transform.rotation;
            phase = Random.Range(0f, Mathf.PI * 2f);
            var body = GetComponent<Rigidbody>();
            if (body) { body.useGravity = false; body.isKinematic = true; }
            aura = GetComponentInChildren<Light>();
            var renderers = GetComponentsInChildren<Renderer>();
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
        void Update()
        {
            float time = Time.time;
            transform.position = restPosition + Vector3.up * (Mathf.Sin(time * bobFrequency * Mathf.PI * 2f + phase) * bobAmplitude);
            transform.rotation = restRotation * Quaternion.Euler(0, time * rotationSpeed, 0);
            if (aura) aura.intensity = auraPeakIntensity * (0.5f + 0.5f * Mathf.Sin(time * Mathf.PI * 2f / Mathf.Max(0.1f, auraPeriod) + phase));
        }
        void OnTriggerEnter(Collider other) => Contact(other);
        void OnTriggerStay(Collider other) => Contact(other);
        void OnCollisionEnter(Collision collision) => Contact(collision.collider);
        void Contact(Collider other)
        {
            if (collected) return;
            var receiver = other.GetComponentInParent<ItemInteractor>();
            if (receiver) receiver.TryCollect(this);
        }
        public void Consume()
        {
            if (collected) return;
            collected = true; SetNearby(false); gameObject.SetActive(false); Destroy(gameObject);
        }
        public void SetNearby(bool nearby) { if (outlines == null) return; foreach (var go in outlines) if (go) go.SetActive(nearby); }
    }
}
