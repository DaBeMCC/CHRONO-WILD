using UnityEngine;
using UnityEngine.InputSystem;

namespace ChronoWild.Water
{
    /// <summary>Development helper: the right mouse button throws a stone to try out the water.</summary>
    public class WaterPhysicsTester : MonoBehaviour
    {
        public GameObject stonePrefab;
        public float throwSpeed = 16f;
        public float stoneScale = 0.28f;
        public float stoneLifetime = 12f;

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                Throw();
        }

        public GameObject Throw()
        {
            var cam = Camera.main;
            if (cam == null || stonePrefab == null)
                return null;
            return Launch(cam.transform.position + cam.transform.forward * 1.2f, cam.transform.forward * throwSpeed);
        }

        public GameObject Launch(Vector3 position, Vector3 velocity)
        {
            var stone = Instantiate(stonePrefab, position, Random.rotation);
            stone.transform.localScale = Vector3.one * stoneScale;
            var collider = stone.AddComponent<SphereCollider>();
            collider.radius = 0.5f;
            var body = stone.AddComponent<Rigidbody>();
            body.mass = 8f;
            body.linearVelocity = velocity;
            var interactor = stone.AddComponent<WaterInteractor>();
            interactor.radius = 0.3f;
            interactor.height = 0.5f;
            interactor.mass = 1.4f;
            Destroy(stone, stoneLifetime);
            return stone;
        }
    }
}
