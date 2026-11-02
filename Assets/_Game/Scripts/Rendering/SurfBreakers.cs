using System.Collections.Generic;
using UnityEngine;

namespace ChronoWild.Rendering
{
    /// <summary>
    /// Breaking-wave extras along the beach: a lip strip mesh that the BreakerLip shader curls when
    /// each wave breaks, and spray/mist particle bursts thrown from the crest at that moment.
    /// The break line is traced on the baked seabed (OceanSeabed) at the average breaking depth,
    /// and wave timing mirrors the CoastalOcean surf model so everything lines up.
    /// </summary>
    [ExecuteAlways]
    public class SurfBreakers : MonoBehaviour
    {
        public OceanSeabed seabed;
        [Tooltip("Coastal ocean material: surf height, period, breaker index and swell direction are read from it.")]
        public Material oceanMaterial;
        public Material lipMaterial;
        public ParticleSystem spray;
        public ParticleSystem mist;

        [Header("Break line")]
        public Vector2 islandCenter = new Vector2(300f, 300f);
        [Tooltip("Angular range (degrees, 0 = east, 270 = south) of coast that gets breakers.")]
        public Vector2 arcDegrees = new Vector2(205f, 335f);
        [Range(0.1f, 2f)] public float angleStep = 0.35f;
        [Range(4, 24)] public int lipSegments = 12;

        [Header("Spray")]
        public float sprayDistance = 70f;
        [Range(0, 60)] public int sprayPerBurst = 18;
        [Range(0, 20)] public int mistPerBurst = 4;
        [Tooltip("Meters between spray emitters along the break line.")]
        public float emitterSpacing = 3f;

        struct BreakPoint
        {
            public Vector3 position;
            public Vector2 shoreDir;
            public float depth;
            public float slope;
            public float along;
            public float lastPhase;
        }

        readonly List<BreakPoint> points = new List<BreakPoint>();
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;

        void OnEnable()
        {
            Rebuild();
        }

        float Seabed(Vector2 xz)
        {
            var rect = seabed.worldRect;
            float u = (xz.x - rect.x) / rect.z, v = (xz.y - rect.y) / rect.w;
            if (u < 0 || v < 0 || u > 1 || v > 1)
                return -seabed.openSeaDepth;
            return seabed.seabed.GetPixelBilinear(u, v).r;
        }

        float AverageBreakDepth()
        {
            float h = oceanMaterial.GetFloat("_SurfHeight") * 0.81f;
            return h / oceanMaterial.GetFloat("_BreakerIndex");
        }

        [ContextMenu("Rebuild Break Line")]
        public void Rebuild()
        {
            points.Clear();
            if (seabed == null || seabed.seabed == null || oceanMaterial == null)
                return;
            float target = -AverageBreakDepth();
            float swell = oceanMaterial.GetFloat("_WaveDirection") * Mathf.Deg2Rad;
            var swellDir = new Vector2(Mathf.Cos(swell), Mathf.Sin(swell));
            for (float a = arcDegrees.x; a <= arcDegrees.y; a += angleStep)
            {
                var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                // March inward from open water until the seabed rises to the breaking depth.
                float prev = Seabed(islandCenter + dir * 330f);
                for (float r = 329f; r > 20f; r -= 0.5f)
                {
                    var p = islandCenter + dir * r;
                    float h = Seabed(p);
                    if (h >= target && prev < target)
                    {
                        const float e = 6f;
                        var grad = new Vector2(Seabed(p + Vector2.right * e) - Seabed(p - Vector2.right * e), Seabed(p + Vector2.up * e) - Seabed(p - Vector2.up * e));
                        if (grad.sqrMagnitude < 1e-6f)
                            break;
                        points.Add(new BreakPoint
                        {
                            position = new Vector3(p.x, 0f, p.y),
                            shoreDir = grad.normalized,
                            depth = -h,
                            slope = Mathf.Clamp(grad.magnitude / (2f * e), 0.03f, 0.45f),
                            along = Vector2.Dot(p, new Vector2(-swellDir.y, swellDir.x)),
                            lastPhase = 0f,
                        });
                        break;
                    }
                    prev = h;
                }
            }
            BuildLipMesh();
        }

        void BuildLipMesh()
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = lipMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            int n = lipSegments + 1;
            var verts = new List<Vector3>(points.Count * n);
            var uv0 = new List<Vector2>(points.Count * n);
            var uv1 = new List<Vector2>(points.Count * n);
            var uv2 = new List<Vector4>(points.Count * n);
            var tris = new List<int>();
            float u = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                var bp = points[i];
                if (i > 0)
                    u += Vector3.Distance(points[i - 1].position, bp.position);
                for (int k = 0; k < n; k++)
                {
                    verts.Add(bp.position);
                    uv0.Add(new Vector2(u, k / (float)lipSegments));
                    uv1.Add(bp.shoreDir);
                    uv2.Add(new Vector4(bp.depth, bp.slope, bp.along, 0f));
                }
                // Break the strip where neighbouring points are far apart (headlands, gaps).
                if (i > 0 && Vector3.Distance(points[i - 1].position, bp.position) < 4f)
                {
                    int a = (i - 1) * n, b = i * n;
                    for (int k = 0; k < lipSegments; k++)
                    {
                        tris.Add(a + k); tris.Add(b + k); tris.Add(a + k + 1);
                        tris.Add(a + k + 1); tris.Add(b + k); tris.Add(b + k + 1);
                    }
                }
            }
            var mesh = meshFilter.sharedMesh;
            if (mesh == null || mesh.name != "SM_BreakerLip")
                mesh = new Mesh { name = "SM_BreakerLip", hideFlags = HideFlags.DontSave };
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetTriangles(tris, 0);
            var bounds = new Bounds();
            foreach (var p in points) bounds.Encapsulate(p.position);
            bounds.Expand(new Vector3(6f, 6f, 6f));
            mesh.bounds = bounds;
            meshFilter.sharedMesh = mesh;
        }

        void Update()
        {
            if (!Application.isPlaying || spray == null || points.Count == 0 || oceanMaterial == null)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            float period = oceanMaterial.GetFloat("_SurfPeriod");
            float surfHeight = oceanMaterial.GetFloat("_SurfHeight");
            float t = Time.time;
            float stepAlong = 0f;
            Vector3 last = points[0].position;
            for (int i = 0; i < points.Count; i++)
            {
                var bp = points[i];
                stepAlong += Vector3.Distance(last, bp.position);
                last = bp.position;
                if (stepAlong < emitterSpacing)
                    continue;
                stepAlong = 0f;
                if ((bp.position - cam.transform.position).sqrMagnitude > sprayDistance * sprayDistance)
                    continue;
                float travel = 2f * Mathf.Sqrt(bp.depth) / (bp.slope * 3.132f);
                float phase = Mathf.Repeat((t + travel) / period + bp.along * 0.0025f, 1f);
                // The crest is at the break line when phase passes 0.12: that's when the lip throws.
                if (bp.lastPhase < 0.12f && phase >= 0.12f)
                    Burst(bp, surfHeight * (0.62f + 0.38f * Mathf.Sin(2f * Mathf.PI * t / (period * 7f) + bp.along * 0.012f)));
                bp.lastPhase = phase;
                points[i] = bp;
            }
        }

        void Burst(BreakPoint bp, float waveH)
        {
            var shore = new Vector3(bp.shoreDir.x, 0f, bp.shoreDir.y);
            var side = new Vector3(-shore.z, 0f, shore.x);
            var top = bp.position + Vector3.up * waveH * 0.95f;
            var emit = new ParticleSystem.EmitParams();
            for (int k = 0; k < sprayPerBurst; k++)
            {
                emit.position = top + side * Random.Range(-1f, 1f) * emitterSpacing + shore * Random.Range(-0.2f, 0.5f) + Vector3.up * Random.Range(-0.3f, 0.1f);
                emit.velocity = shore * Random.Range(0.8f, 3f) + Vector3.up * Random.Range(0.8f, 2.6f) + side * Random.Range(-0.3f, 0.3f);
                emit.startSize = Random.Range(0.3f, 0.9f) * Mathf.Lerp(0.7f, 1.3f, waveH / 1.5f);
                emit.startLifetime = Random.Range(0.6f, 1.1f);
                emit.rotation = Random.Range(0f, 360f);
                spray.Emit(emit, 1);
            }
            if (mist == null)
                return;
            for (int k = 0; k < mistPerBurst; k++)
            {
                emit.position = top + side * Random.Range(-2f, 2f);
                emit.velocity = shore * Random.Range(0.8f, 2f) + Vector3.up * Random.Range(0.3f, 1.2f);
                emit.startSize = Random.Range(1.2f, 2.4f);
                emit.startLifetime = Random.Range(1.8f, 3f);
                emit.rotation = Random.Range(0f, 360f);
                mist.Emit(emit, 1);
            }
        }

        public int PointCount => points.Count;
    }
}
