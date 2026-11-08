using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChronoWild.Water
{
    /// <summary>
    /// Runs the interactive water surface: a wave-equation grid that follows the player, and GPU
    /// height probes so physics objects ride the same waves that are drawn.
    /// Other systems call Splash() to disturb the water and SampleHeight() to read it.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class WaterSimulation : MonoBehaviour
    {
        public static WaterSimulation Instance { get; private set; }

        const int MaxProbes = 256;
        const int MaxSplatsPerFrame = 32;
        const float StepTime = 1f / 60f;

        [Header("References")]
        public ComputeShader compute;
        public Material oceanMaterial;
        public Texture2D biomeMap;
        public Terrain terrain;
        [Tooltip("The grid follows this transform. Empty uses the main camera.")]
        public Transform focus;
        public float swampLevel = 1.4f;

        [Header("Grid")]
        public float areaSize = 64f;
        public int highResolution = 512;
        public int lowResolution = 256;
        [Tooltip("Top wave speed (m/s). Must stay below 0.7 * texel size * 60 to remain stable.")]
        public float maxWaveSpeed = 4.5f;
        [Range(0.9f, 1f)] public float damping = 0.992f;

        struct Splat { public Vector2 xz; public float radius; public float kick; }

        RenderTexture[] ripple = new RenderTexture[2];
        RenderTexture impacts;
        Texture2D terrainHeights;
        ComputeBuffer probeIn, probeOut;
        int current, resolution, stepKernel, splatKernel, probeKernel;
        float texel, accumulator;
        Vector2Int originTexels;
        int pendingScrollX, pendingScrollY;
        readonly List<Splat> splats = new List<Splat>();
        readonly Vector2[] probeXZ = new Vector2[MaxProbes];
        readonly float[] probeHeights = new float[MaxProbes];
        readonly bool[] probeUsed = new bool[MaxProbes];
        int probeHighWater;
        bool readbackPending;

        static readonly int RippleTexId = Shader.PropertyToID("_CW_RippleTex");
        static readonly int RippleRectId = Shader.PropertyToID("_CW_RippleRect");

        public RenderTexture ImpactTexture => impacts;
        public Vector4 OriginData => new Vector4(originTexels.x * texel, originTexels.y * texel, texel, 0f);
        public int Resolution => resolution;
        public Texture2D TerrainHeights => terrainHeights;
        public Vector4 TerrainRect { get; private set; }

        void OnEnable()
        {
            Instance = this;
            resolution = ChronoWild.Rendering.GraphicsTier.IsLow ? lowResolution : highResolution;
            texel = areaSize / resolution;
            stepKernel = compute.FindKernel("RippleStep");
            splatKernel = compute.FindKernel("RippleSplat");
            probeKernel = compute.FindKernel("ProbeHeights");
            for (int i = 0; i < 2; i++)
            {
                ripple[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    enableRandomWrite = true,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "WaterRipple" + i
                };
                ripple[i].Create();
                ClearTexture(ripple[i]);
            }
            impacts = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RInt)
            {
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                name = "WaterImpacts"
            };
            impacts.Create();
            ClearTexture(impacts);
            probeIn = new ComputeBuffer(MaxProbes, sizeof(float) * 2);
            probeOut = new ComputeBuffer(MaxProbes, sizeof(float));
            BakeTerrainHeights();
            originTexels = SnappedOrigin();
        }

        void OnDisable()
        {
            if (Instance == this)
                Instance = null;
            Shader.SetGlobalVector(RippleRectId, Vector4.zero);
            foreach (var rt in ripple)
                if (rt != null) rt.Release();
            if (impacts != null) impacts.Release();
            probeIn?.Release();
            probeOut?.Release();
            if (terrainHeights != null) Destroy(terrainHeights);
        }

        static void ClearTexture(RenderTexture rt)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        void BakeTerrainHeights()
        {
            var data = terrain.terrainData;
            int n = data.heightmapResolution;
            var heights = data.GetHeights(0, 0, n, n);
            terrainHeights = new Texture2D(n, n, TextureFormat.RFloat, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new NativeArray<float>(n * n, Allocator.Temp);
            float scale = data.size.y, offset = terrain.transform.position.y;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                pixels[x + z * n] = heights[z, x] * scale + offset;
            terrainHeights.SetPixelData(pixels, 0);
            terrainHeights.Apply(false, false);
            pixels.Dispose();
            Vector3 p = terrain.transform.position;
            // Heightmap samples sit on texel corners; the half-texel inset keeps bilinear lookups aligned.
            float dx = data.size.x / (n - 1), dz = data.size.z / (n - 1);
            TerrainRect = new Vector4(p.x - dx * 0.5f, p.z - dz * 0.5f, 1f / (data.size.x + dx), 1f / (data.size.z + dz));
        }

        Vector2Int SnappedOrigin()
        {
            Vector3 c = focus != null ? focus.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            return new Vector2Int(Mathf.RoundToInt(c.x / texel) - resolution / 2, Mathf.RoundToInt(c.z / texel) - resolution / 2);
        }

        /// <summary>Pushes the water surface at a point. Negative strength pushes down (m/s of surface velocity).</summary>
        public void Splash(Vector3 position, float radius, float strength)
        {
            if (splats.Count < MaxSplatsPerFrame)
                splats.Add(new Splat { xz = new Vector2(position.x, position.z), radius = radius, kick = strength });
        }

        // ------------------------------------------------------------------ Height probes

        public int AcquireProbe()
        {
            for (int i = 0; i < MaxProbes; i++)
            {
                if (probeUsed[i])
                    continue;
                probeUsed[i] = true;
                probeHighWater = Mathf.Max(probeHighWater, i + 1);
                return i;
            }
            return -1;
        }

        public void ReleaseProbe(int index)
        {
            if (index >= 0 && index < MaxProbes)
                probeUsed[index] = false;
        }

        /// <summary>Moves a probe and returns the last known water height there (a few frames old).</summary>
        public float SampleProbe(int index, Vector3 position)
        {
            if (index < 0)
                return float.NegativeInfinity;
            probeXZ[index] = new Vector2(position.x, position.z);
            return probeHeights[index];
        }

        /// <summary>Terrain height from the baked map, without touching the physics scene.</summary>
        public float GroundHeight(Vector3 position) => terrain.SampleHeight(position) + terrain.transform.position.y;

        // ------------------------------------------------------------------ Update

        public void BindSurfaceParameters(ComputeShader target, int kernel, bool ocean)
        {
            target.SetTexture(kernel, "_BiomeTex", biomeMap);
            target.SetTexture(kernel, "_TerrainHeight", terrainHeights);
            target.SetVector("_TerrainRect", TerrainRect);
            target.SetFloat("_SwampLevel", swampLevel);
            if (!ocean)
                return;
            var m = oceanMaterial;
            target.SetTexture(kernel, "_SeabedTex", Shader.GetGlobalTexture("_ChronoSeabedTex"));
            target.SetTexture(kernel, "_FFTDisp", m.GetTexture("_FFTDisplacement"));
            target.SetVector("_SeabedRect", Shader.GetGlobalVector("_ChronoSeabedRect"));
            target.SetFloat("_SeabedOpenDepth", Shader.GetGlobalFloat("_ChronoSeabedOpenDepth"));
            target.SetFloat("_SimTime", Time.timeSinceLevelLoad);
            foreach (var name in OceanFloats)
                target.SetFloat(name, m.GetFloat(name));
        }

        static readonly string[] OceanFloats =
        {
            "_WaveHeight", "_WaveLength", "_WaveDirection", "_WaveSpeed", "_SurfHeight", "_SurfPeriod", "_BreakerIndex",
            "_RunUp", "_SurfStartDepth", "_FFTPatch", "_FFTFrames", "_FFTPeriod", "_FFTStrength", "_FFTDetail"
        };

        void Update()
        {
            var origin = SnappedOrigin();
            pendingScrollX += origin.x - originTexels.x;
            pendingScrollY += origin.y - originTexels.y;
            originTexels = origin;

            compute.SetInt("_Resolution", resolution);
            compute.SetVector("_RippleOrigin", OriginData);
            DispatchSplats();

            accumulator = Mathf.Min(accumulator + Time.deltaTime, StepTime * 3f);
            while (accumulator >= StepTime)
            {
                accumulator -= StepTime;
                StepRipples();
            }

            Shader.SetGlobalTexture(RippleTexId, ripple[current]);
            Shader.SetGlobalVector(RippleRectId, new Vector4(originTexels.x * texel, originTexels.y * texel, 1f / (resolution * texel), texel));
            UpdateProbes();
        }

        void DispatchSplats()
        {
            compute.SetTexture(splatKernel, "_Impacts", impacts);
            foreach (var s in splats)
            {
                float radius = Mathf.Max(s.radius / texel, 1f);
                var center = new Vector2(s.xz.x / texel - originTexels.x, s.xz.y / texel - originTexels.y);
                int r = Mathf.CeilToInt(radius * 2f);
                int size = r * 2 + 1;
                compute.SetInts("_SplatMin", Mathf.FloorToInt(center.x) - r, Mathf.FloorToInt(center.y) - r);
                compute.SetInts("_SplatSize", size, size);
                compute.SetFloats("_SplatCenter", center.x, center.y);
                compute.SetFloat("_SplatRadius", radius);
                compute.SetInt("_SplatKick", Mathf.RoundToInt(s.kick * 1000f));
                int groups = Mathf.CeilToInt(size / 16f);
                compute.Dispatch(splatKernel, groups, groups, 1);
            }
            splats.Clear();
        }

        void StepRipples()
        {
            int next = 1 - current;
            compute.SetInt("_ScrollX", pendingScrollX);
            compute.SetInt("_ScrollY", pendingScrollY);
            pendingScrollX = pendingScrollY = 0;
            compute.SetFloat("_Dt", StepTime);
            compute.SetFloat("_MaxWaveSpeed", Mathf.Min(maxWaveSpeed, texel * 60f * 0.7f));
            compute.SetFloat("_Damping", damping);
            compute.SetTexture(stepKernel, "_RippleSrc", ripple[current]);
            compute.SetTexture(stepKernel, "_RippleDst", ripple[next]);
            compute.SetTexture(stepKernel, "_Impacts", impacts);
            BindSurfaceParameters(compute, stepKernel, false);
            int groups = Mathf.CeilToInt(resolution / 32f);
            compute.Dispatch(stepKernel, groups, groups, 1);
            current = next;
        }

        void UpdateProbes()
        {
            if (probeHighWater == 0 || readbackPending)
                return;
            probeIn.SetData(probeXZ, 0, 0, probeHighWater);
            compute.SetInt("_ProbeCount", probeHighWater);
            compute.SetBuffer(probeKernel, "_ProbeIn", probeIn);
            compute.SetBuffer(probeKernel, "_ProbeOut", probeOut);
            compute.SetTexture(probeKernel, "_RippleSrc", ripple[current]);
            BindSurfaceParameters(compute, probeKernel, true);
            compute.Dispatch(probeKernel, Mathf.CeilToInt(probeHighWater / 32f), 1, 1);
            readbackPending = true;
            AsyncGPUReadback.Request(probeOut, r =>
            {
                readbackPending = false;
                if (r.hasError || this == null || !isActiveAndEnabled)
                    return;
                r.GetData<float>().CopyTo(probeHeights);
            });
        }
    }
}
