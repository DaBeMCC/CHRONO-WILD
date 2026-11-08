using UnityEngine;

namespace ChronoWild.Water
{
    /// <summary>
    /// Makes an object disturb the water: entry splashes, a wake while moving and droplets thrown
    /// up by each stride. Works with anything that moves (player controllers, rigidbodies).
    /// </summary>
    public class WaterInteractor : MonoBehaviour
    {
        [Tooltip("Radius of the disturbance (m).")]
        public float radius = 0.35f;
        [Tooltip("Height of the lowest point relative to the transform.")]
        public float bottomOffset = 0f;
        [Tooltip("Height of the object; it stops disturbing the surface once fully under water.")]
        public float height = 1.8f;
        [Tooltip("Multiplies the splash size.")]
        public float mass = 1f;
        [Tooltip("Metres moved in the water between droplet bursts.")]
        public float strideLength = 0.75f;
        [Tooltip("Linear drag applied to a rigidbody under water (1/s).")]
        public float waterDrag = 3f;

        int probe = -1;
        Vector3 lastPosition;
        float strideDistance;
        bool wasWet;
        Rigidbody body;

        void OnEnable()
        {
            lastPosition = transform.position;
            body = GetComponent<Rigidbody>();
        }

        void OnDisable()
        {
            WaterSimulation.Instance?.ReleaseProbe(probe);
            probe = -1;
        }

        void Update()
        {
            var water = WaterSimulation.Instance;
            if (water == null || Time.deltaTime <= 0f)
                return;
            if (probe < 0)
            {
                probe = water.AcquireProbe();
                if (probe < 0)
                    return;
            }

            Vector3 position = transform.position;
            // Physics moves a rigidbody less often than frames are drawn, so a per-frame position
            // difference would read zero on most frames.
            Vector3 velocity = body != null ? body.linearVelocity : (position - lastPosition) / Time.deltaTime;
            lastPosition = position;

            float surface = water.SampleProbe(probe, position);
            float bottom = position.y + bottomOffset;
            bool wet = surface > bottom && surface > water.GroundHeight(position);
            bool crossing = wet && bottom + height > surface;

            if (wet && !wasWet)
                OnEntry(water, position, surface, velocity);
            wasWet = wet;

            if (crossing)
            {
                Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
                float speed = horizontal.magnitude;
                if (speed > 0.15f)
                {
                    // Push down behind the body and up ahead of it: the pair is what forms a wake.
                    Vector3 forward = horizontal / speed;
                    float strength = Mathf.Min(speed, 8f) * 0.55f * mass;
                    water.Splash(position + forward * radius, radius, strength);
                    water.Splash(position - forward * radius, radius, -strength);
                    strideDistance += speed * Time.deltaTime;
                    if (strideDistance >= strideLength)
                    {
                        strideDistance = 0f;
                        PbfSplashes.Instance?.EmitSplash(new Vector3(position.x, surface, position.z), velocity, Mathf.RoundToInt(20f + speed * 16f * mass), radius, 1.2f + speed * 0.45f);
                    }
                }
            }
        }

        void FixedUpdate()
        {
            if (body != null && wasWet && waterDrag > 0f)
                body.AddForce(-body.linearVelocity * waterDrag, ForceMode.Acceleration);
        }

        void OnEntry(WaterSimulation water, Vector3 position, float surface, Vector3 velocity)
        {
            float impact = Mathf.Max(-velocity.y, 0f) + Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude * 0.25f;
            if (impact < 0.8f)
                return;
            float energy = Mathf.Min(impact, 16f) * mass;
            water.Splash(position, radius * 1.6f, -energy * 1.3f);
            PbfSplashes.Instance?.EmitSplash(new Vector3(position.x, surface, position.z), velocity, Mathf.Min(Mathf.RoundToInt(50f * energy), 900), radius * 1.2f, 1.2f + Mathf.Sqrt(energy) * 1.5f);
        }
    }
}
