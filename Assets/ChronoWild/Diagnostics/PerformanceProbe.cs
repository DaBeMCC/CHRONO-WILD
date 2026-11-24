using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace ChronoWild.Diagnostics
{
    /// <summary>
    /// Records frame time (CPU main/render thread, GPU), render counters and memory/GC counters
    /// over a fixed number of frames and writes avg/p50/p95/max to a JSON file.
    /// Used for the QA performance report (before/after comparisons).
    /// </summary>
    public sealed class PerformanceProbe : MonoBehaviour
    {
        public string label = "capture";
        public string outputPath;
        public int warmupFrames = 90;
        public int frames = 600;
        public bool Done { get; private set; }

        // Markers recorded per frame (ms and calls). Scripts use the names the Profiler shows.
        static readonly (string key, string marker)[] Markers =
        {
            ("Scripts: BehaviourUpdate", "BehaviourUpdate"),
            ("Physics: FixedUpdate", "FixedUpdate.PhysicsFixedUpdate"),
            ("Render: URP total", "Inl_UniversalRenderTotal"),
            ("Render: main light shadows", "MainLightShadow"),
            ("Render: SRP Batcher flush", "SRPBatcher.Flush"),
            ("Render: terrain culling", "TerrainManager.CullAllTerrains"),
            ("Render: terrain details", "Terrain.Details.Render"),
            ("UI: IMGUI repaint", "GUI.Repaint"),
            ("GC: collect", "GC.Collect"),
            ("Script: PerformanceOverlay.OnGUI", "ChronoWild.Runtime.dll!ChronoWild.Diagnostics::PerformanceOverlay.OnGUI() [Invoke]"),
            ("Script: MissionDirector.OnGUI", "ChronoWild.Runtime.dll!ChronoWild.Story::MissionDirector.OnGUI() [Invoke]"),
            ("Script: SurvivalRun.Update", "Assembly-CSharp.dll!ChronoWild.Survival::SurvivalRun.Update() [Invoke]"),
            ("Script: SurvivalHUD.Update", "Assembly-CSharp.dll!ChronoWild.Survival::SurvivalHUD.Update() [Invoke]"),
            ("Script: Dryosaur.Update", "Assembly-CSharp.dll!ChronoWild.Survival::Dryosaur.Update() [Invoke]"),
            ("Script: DinoAI.Update", "Assembly-CSharp.dll!ChronoWild.Prototype::DinoAI.Update() [Invoke]"),
            ("Script: NavMeshStressTest.Update", "Assembly-CSharp.dll!ChronoWild.Diagnostics::NavMeshStressTest.Update() [Invoke]"),
            ("Script: WaterSimulation.Update", "ChronoWild.Runtime.dll!ChronoWild.Water::WaterSimulation.Update() [Invoke]"),
            ("Script: AmbientFlock.Update", "ChronoWild.Runtime.dll!ChronoWild.World::AmbientFlock.Update() [Invoke]"),
            ("Nav: AI update total", "PreUpdate.AIUpdate"),
            ("Nav: NavMeshManager", "NavMeshManager"),
            ("Nav: crowd avoidance", "Crowd.Avoidance"),
            ("Nav: crowd pathfinding", "Crowd.Pathfinding"),
            ("Nav: crowd collision", "Crowd.Collision"),
        };
        readonly List<(string key, ProfilerRecorder recorder)> markerRecorders = new List<(string, ProfilerRecorder)>();

        static readonly (string name, ProfilerCategory category)[] Counters =
        {
            ("Batches Count", ProfilerCategory.Render),
            ("Draw Calls Count", ProfilerCategory.Render),
            ("SetPass Calls Count", ProfilerCategory.Render),
            ("Triangles Count", ProfilerCategory.Render),
            ("Shadow Casters Count", ProfilerCategory.Render),
            ("GC Allocated In Frame", ProfilerCategory.Memory),
            ("GC Allocation In Frame Count", ProfilerCategory.Memory),
            ("GC Used Memory", ProfilerCategory.Memory),
            ("GC Reserved Memory", ProfilerCategory.Memory),
            ("Total Used Memory", ProfilerCategory.Memory),
            ("Gfx Used Memory", ProfilerCategory.Memory),
        };

        readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
        readonly Dictionary<string, List<double>> samples = new Dictionary<string, List<double>>();
        readonly FrameTiming[] timing = new FrameTiming[1];
        int frame;

        void OnEnable()
        {
            foreach (var (name, category) in Counters)
                recorders.Add(ProfilerRecorder.StartNew(category, name));
        }

        void OnDisable()
        {
            foreach (var r in recorders)
                r.Dispose();
            recorders.Clear();
            foreach (var m in markerRecorders)
                m.recorder.Dispose();
            markerRecorders.Clear();
        }

        void StartMarkers()
        {
            // Script markers only exist once the method has run, so look them up after warm-up.
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var byName = new Dictionary<string, ProfilerRecorderHandle>();
            foreach (var h in handles)
                byName[ProfilerRecorderHandle.GetDescription(h).Name] = h;
            foreach (var (key, marker) in Markers)
                if (byName.TryGetValue(marker, out var handle))
                    markerRecorders.Add((key, new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.StartImmediately)));
        }

        void Add(string key, double value)
        {
            if (!samples.TryGetValue(key, out var list))
                samples[key] = list = new List<double>(frames);
            list.Add(value);
        }

        void Update()
        {
            if (Done)
                return;
            FrameTimingManager.CaptureFrameTimings();
            frame++;
            if (frame == warmupFrames)
                StartMarkers();
            if (frame <= warmupFrames)
                return;
            foreach (var (key, recorder) in markerRecorders)
            {
                if (!recorder.Valid || recorder.Count == 0)
                {
                    Add(key + " (ms)", 0);
                    Add(key + " (calls)", 0);
                    continue;
                }
                var sample = recorder.GetSample(0);
                Add(key + " (ms)", sample.Value / 1e6);
                Add(key + " (calls)", sample.Count);
            }

            Add("Frame Time (ms)", Time.unscaledDeltaTime * 1000.0);
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
            {
                Add("CPU Main Thread (ms)", timing[0].cpuMainThreadFrameTime);
                Add("CPU Render Thread (ms)", timing[0].cpuRenderThreadFrameTime);
                Add("GPU (ms)", timing[0].gpuFrameTime);
            }
            for (int i = 0; i < recorders.Count; i++)
            {
                var r = recorders[i];
                if (!r.Valid)
                    continue;
                double value = r.LastValue;
                string key = Counters[i].name;
                if (key == "Main Thread") { key = "Main Thread marker (ms)"; value /= 1e6; }
                else if (key.Contains("Memory")) { key += " (MB)"; value /= 1048576.0; }
                else if (key == "GC Allocated In Frame") { key += " (KB)"; value /= 1024.0; }
                Add(key, value);
            }

            if (frame >= warmupFrames + frames)
                Finish();
        }

        void Finish()
        {
            Done = true;
            var sb = new StringBuilder();
            sb.Append("{\n  \"label\": \"").Append(label).Append("\",\n  \"frames\": ").Append(frames).Append(",\n  \"metrics\": {\n");
            var keys = samples.Keys.ToList();
            for (int k = 0; k < keys.Count; k++)
            {
                var values = samples[keys[k]].OrderBy(v => v).ToList();
                double avg = values.Average();
                double P(double q) => values[Mathf.Clamp((int)(q * (values.Count - 1)), 0, values.Count - 1)];
                double max = values[values.Count - 1];
                sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "    \"{0}\": {{\"avg\": {1:F3}, \"p50\": {2:F3}, \"p95\": {3:F3}, \"p99\": {4:F3}, \"max\": {5:F3} ", keys[k], avg, P(.5), P(.95), P(.99), max)).Append('}');
                sb.Append(k < keys.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  },\n  \"series\": {\n");
            string[] series = { "Frame Time (ms)", "GC Allocated In Frame (KB)", "GC Used Memory (MB)" };
            for (int s = 0; s < series.Length; s++)
            {
                samples.TryGetValue(series[s], out var list);
                sb.Append($"    \"{series[s]}\": [").Append(list == null ? "" : string.Join(",", list.Select(v => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))).Append("]");
                sb.Append(s < series.Length - 1 ? ",\n" : "\n");
            }
            sb.Append("  }\n}\n");
            if (!string.IsNullOrEmpty(outputPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllText(outputPath, sb.ToString());
            }
        }
    }
}
