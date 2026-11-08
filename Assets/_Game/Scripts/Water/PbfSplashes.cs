using System.Collections.Generic;
using System.Runtime.InteropServices;
using ChronoWild.Rendering;
using UnityEngine;

namespace ChronoWild.Water
{
    /// <summary>
    /// GPU Position Based Fluids for splashes and spray. The solver only runs while droplets are
    /// alive, so calm water costs nothing. Droplets that fall back disturb the WaterSimulation grid.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class PbfSplashes : MonoBehaviour
    {
        public static PbfSplashes Instance { get; private set; }

        [StructLayout(LayoutKind.Sequential)]
        struct Particle
        {
            public Vector3 x;
            public float life;
            public Vector3 v;
            public float age;
        }

        // The prefix sum over the buckets is serial, so this count is directly GPU time per step.
        const int CellCount = 1 << 14;
        const float StepTime = 1f / 75f;
        const int MaxEmitPerFrame = 4096;

        public ComputeShader compute;
        public Material dropletMaterial;
        public int highMaxParticles = 24000;
        public int lowMaxParticles = 5000;
        public int highIterations = 3;
        public int lowIterations = 2;
        [Tooltip("Smoothing radius of the fluid (m).")]
        public float smoothingRadius = 0.16f;
        [Tooltip("Rest spacing between droplets (m).")]
        public float restSpacing = 0.06f;
        public float dropletRadius = 0.024f;
        public float dropletLife = 3.5f;

        ComputeBuffer particles, emit, predicted, deltas, gridCounts, gridIndices;
        readonly List<Particle> pending = new List<Particle>();
        int maxParticles, iterations, head;
        int kEmit, kPredict, kClear, kCount, kPrefix, kScatter, kLambda, kDelta, kApply, kFinalize;
        float accumulator, busyUntil;
        MaterialPropertyBlock block;
        System.Random rng = new System.Random(1234);

        void OnEnable()
        {
            Instance = this;
            bool low = GraphicsTier.IsLow;
            maxParticles = low ? lowMaxParticles : highMaxParticles;
            iterations = low ? lowIterations : highIterations;
            int stride = Marshal.SizeOf<Particle>();
            particles = new ComputeBuffer(maxParticles, stride);
            particles.SetData(new Particle[maxParticles]);
            emit = new ComputeBuffer(MaxEmitPerFrame, stride);
            predicted = new ComputeBuffer(maxParticles, sizeof(float) * 4);
            deltas = new ComputeBuffer(maxParticles, sizeof(float) * 4);
            gridCounts = new ComputeBuffer(CellCount, sizeof(uint));
            gridIndices = new ComputeBuffer(maxParticles, sizeof(uint));

            kEmit = compute.FindKernel("Emit");
            kPredict = compute.FindKernel("Predict");
            kClear = compute.FindKernel("ClearGrid");
            kCount = compute.FindKernel("CountGrid");
            kPrefix = compute.FindKernel("PrefixGrid");
            kScatter = compute.FindKernel("ScatterGrid");
            kLambda = compute.FindKernel("Lambda");
            kDelta = compute.FindKernel("Delta");
            kApply = compute.FindKernel("Apply");
            kFinalize = compute.FindKernel("Finalize");

            compute.SetBuffer(kEmit, "_Particles", particles);
            compute.SetBuffer(kEmit, "_EmitBuffer", emit);
            compute.SetBuffer(kPredict, "_Particles", particles);
            compute.SetBuffer(kPredict, "_Predicted", predicted);
            compute.SetBuffer(kPredict, "_Deltas", deltas);
            compute.SetBuffer(kClear, "_GridCounts", gridCounts);
            compute.SetBuffer(kPrefix, "_GridCounts", gridCounts);
            foreach (int k in new[] { kCount, kScatter })
            {
                compute.SetBuffer(k, "_Particles", particles);
                compute.SetBuffer(k, "_Predicted", predicted);
                compute.SetBuffer(k, "_GridCounts", gridCounts);
            }
            compute.SetBuffer(kScatter, "_GridIndices", gridIndices);
            foreach (int k in new[] { kLambda, kDelta })
            {
                compute.SetBuffer(k, "_Particles", particles);
                compute.SetBuffer(k, "_Predicted", predicted);
                compute.SetBuffer(k, "_GridCounts", gridCounts);
                compute.SetBuffer(k, "_GridIndices", gridIndices);
            }
            compute.SetBuffer(kDelta, "_Deltas", deltas);
            compute.SetBuffer(kApply, "_Particles", particles);
            compute.SetBuffer(kApply, "_Predicted", predicted);
            compute.SetBuffer(kApply, "_Deltas", deltas);
            compute.SetBuffer(kFinalize, "_Particles", particles);
            compute.SetBuffer(kFinalize, "_Predicted", predicted);

            block = new MaterialPropertyBlock();
            block.SetBuffer("_Particles", particles);
            block.SetFloat("_DropletRadius", dropletRadius);
        }

        void OnDisable()
        {
            if (Instance == this)
                Instance = null;
            particles?.Release();
            emit?.Release();
            predicted?.Release();
            deltas?.Release();
            gridCounts?.Release();
            gridIndices?.Release();
        }

        double Rand() => rng.NextDouble();

        /// <summary>Crown of droplets thrown up and outward from a point on the water.</summary>
        public void EmitSplash(Vector3 position, Vector3 inheritedVelocity, int count, float radius, float speed)
        {
            // Droplets born closer than the rest spacing would be blasted apart by the solver,
            // so the crown widens with the droplet count.
            radius = Mathf.Max(radius, Mathf.Sqrt(count) * restSpacing * 0.45f);
            for (int i = 0; i < count && pending.Count < MaxEmitPerFrame; i++)
            {
                float angle = (float)Rand() * Mathf.PI * 2f;
                float ring = radius * Mathf.Sqrt((float)Rand());
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // Most droplets barely leave the surface and a few fly high; the centre forms a
                // tall column while the rim is thrown outward and low, which reads as a crown.
                float centre = 1f - ring / Mathf.Max(radius, 0.01f);
                float s = speed * (0.12f + 1.25f * Mathf.Pow((float)Rand(), 2.2f)) * Mathf.Lerp(0.55f, 1.25f, centre);
                pending.Add(new Particle
                {
                    x = position + dir * ring + Vector3.up * ((float)Rand() * 0.25f + 0.03f),
                    v = dir * (speed * (0.08f + 0.5f * (1f - centre)) * (0.5f + (float)Rand())) + Vector3.up * s + inheritedVelocity * 0.25f,
                    life = dropletLife * (0.6f + 0.4f * (float)Rand()),
                    age = 0f
                });
            }
        }

        void Update()
        {
            var water = WaterSimulation.Instance;
            if (water == null)
                return;
            if (pending.Count > 0)
                FlushEmits();
            if (Time.time > busyUntil)
            {
                accumulator = 0f;
                return;
            }
            SetCommonParameters(water);
            accumulator = Mathf.Min(accumulator + Time.deltaTime, StepTime * 2f);
            while (accumulator >= StepTime)
            {
                accumulator -= StepTime;
                Step();
            }
            Draw();
        }

        /// <summary>Queues the droplets for the cameras rendered next.</summary>
        public void Draw()
        {
            var bounds = new Bounds(transform.position, Vector3.one * 2000f);
            var rp = new RenderParams(dropletMaterial) { worldBounds = bounds, matProps = block, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off };
            Graphics.RenderPrimitives(rp, MeshTopology.Triangles, 6, maxParticles);
        }

        void FlushEmits()
        {
            emit.SetData(pending);
            compute.SetInt("_EmitCount", pending.Count);
            compute.SetInt("_EmitHead", head);
            compute.SetInt("_MaxParticles", maxParticles);
            compute.Dispatch(kEmit, Mathf.CeilToInt(pending.Count / 64f), 1, 1);
            head = (head + pending.Count) % maxParticles;
            pending.Clear();
            busyUntil = Time.time + dropletLife + 0.5f;
        }

        void SetCommonParameters(WaterSimulation water)
        {
            compute.SetInt("_MaxParticles", maxParticles);
            compute.SetInt("_CellCount", CellCount);
            compute.SetInt("_Resolution", water.Resolution);
            compute.SetFloat("_Dt", StepTime);
            compute.SetFloat("_H", smoothingRadius);
            compute.SetFloat("_RestDensity", 1f / (restSpacing * restSpacing * restSpacing));
            compute.SetFloat("_Epsilon", 5f);
            compute.SetFloat("_TensileK", 0.02f);
            compute.SetVector("_RippleOrigin", water.OriginData);
            water.BindSurfaceParameters(compute, kFinalize, true);
            compute.SetTexture(kFinalize, "_Impacts", water.ImpactTexture);
            compute.SetTexture(kApply, "_TerrainHeight", water.TerrainHeights);
            compute.SetVector("_TerrainRect", water.TerrainRect);
        }

        void Step()
        {
            int groups = Mathf.CeilToInt(maxParticles / 256f);
            compute.Dispatch(kPredict, groups, 1, 1);
            compute.Dispatch(kClear, Mathf.CeilToInt(CellCount / 1024f), 1, 1);
            compute.Dispatch(kCount, groups, 1, 1);
            compute.Dispatch(kPrefix, 1, 1, 1);
            compute.Dispatch(kScatter, groups, 1, 1);
            for (int i = 0; i < iterations; i++)
            {
                compute.Dispatch(kLambda, groups, 1, 1);
                compute.Dispatch(kDelta, groups, 1, 1);
                compute.Dispatch(kApply, groups, 1, 1);
            }
            compute.Dispatch(kFinalize, groups, 1, 1);
        }

        /// <summary>Debug: counts live droplets (reads the buffer back, so it is slow).</summary>
        public int CountAlive(out float maxSpeed, out bool hasNaN)
        {
            var data = new Particle[maxParticles];
            particles.GetData(data);
            int alive = 0;
            maxSpeed = 0f;
            hasNaN = false;
            foreach (var p in data)
            {
                if (p.life <= 0f)
                    continue;
                alive++;
                maxSpeed = Mathf.Max(maxSpeed, p.v.magnitude);
                hasNaN |= float.IsNaN(p.x.x) || float.IsNaN(p.v.y);
            }
            return alive;
        }
    }
}
