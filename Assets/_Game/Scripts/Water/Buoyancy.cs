using UnityEngine;

namespace ChronoWild.Water
{
    /// <summary>
    /// Floats a rigidbody on the simulated surface. Force is applied at several points, so the body
    /// also pitches and rolls with the waves instead of only bobbing.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Buoyancy : MonoBehaviour
    {
        [Tooltip("Sample points in local space. Empty derives four from the collider.")]
        public Vector3[] points = new Vector3[0];
        [Tooltip("Depth (m) at which a point is considered fully submerged.")]
        public float fullDepth = 0.4f;
        [Tooltip("Buoyant force at full submersion, as a multiple of the body's weight. 2 floats half-sunk.")]
        public float buoyancy = 2f;
        public float linearDrag = 0.9f;
        public float angularDrag = 1.2f;

        Rigidbody body;
        int[] probes;

        void OnEnable()
        {
            body = GetComponent<Rigidbody>();
            body.sleepThreshold = 0f;
            if (points == null || points.Length == 0)
                points = DerivePoints();
            probes = new int[points.Length];
            for (int i = 0; i < probes.Length; i++)
                probes[i] = -1;
        }

        void OnDisable()
        {
            var water = WaterSimulation.Instance;
            if (water == null || probes == null)
                return;
            for (int i = 0; i < probes.Length; i++)
            {
                water.ReleaseProbe(probes[i]);
                probes[i] = -1;
            }
        }

        Vector3[] DerivePoints()
        {
            var col = GetComponent<Collider>();
            Vector3 c = Vector3.zero, e = Vector3.one * 0.5f;
            if (col is CapsuleCollider capsule)
            {
                c = capsule.center;
                var axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                float half = capsule.height * 0.42f;
                var side = capsule.direction == 2 ? Vector3.right : Vector3.forward;
                return new[] { c + axis * half, c - axis * half, c + side * capsule.radius * 0.8f, c - side * capsule.radius * 0.8f };
            }
            if (col is BoxCollider box)
            {
                c = box.center;
                e = box.size * 0.45f;
            }
            return new[] { c + new Vector3(e.x, 0, e.z), c + new Vector3(-e.x, 0, e.z), c + new Vector3(e.x, 0, -e.z), c + new Vector3(-e.x, 0, -e.z) };
        }

        void FixedUpdate()
        {
            var water = WaterSimulation.Instance;
            if (water == null)
                return;
            float share = body.mass * -Physics.gravity.y * buoyancy / points.Length;
            float wetCount = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                if (probes[i] < 0)
                    probes[i] = water.AcquireProbe();
                Vector3 p = transform.TransformPoint(points[i]);
                float surface = water.SampleProbe(probes[i], p);
                if (float.IsInfinity(surface))
                    continue;
                // Points sit on the body's axis: it starts getting wet half a body above the surface.
                float depth = surface - p.y + fullDepth * 0.5f;
                if (depth <= 0f)
                    continue;
                float submerged = Mathf.Clamp01(depth / fullDepth);
                wetCount += submerged;
                body.AddForceAtPosition(Vector3.up * (share * submerged), p, ForceMode.Force);
                Vector3 pointVelocity = body.GetPointVelocity(p);
                body.AddForceAtPosition(-pointVelocity * (linearDrag * submerged * body.mass / points.Length), p, ForceMode.Force);
            }
            if (wetCount > 0f)
                body.AddTorque(-body.angularVelocity * (angularDrag * wetCount / points.Length), ForceMode.Acceleration);
        }
    }
}
