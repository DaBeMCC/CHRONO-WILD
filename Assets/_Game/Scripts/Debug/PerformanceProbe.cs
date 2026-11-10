using System.Collections;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace ChronoWild.DebugTools
{
    /// <summary>
    /// Repeatable performance benchmark: puts the camera at fixed viewpoints around the island,
    /// lets each settle, then averages frame time, GPU time, draw calls, triangles and memory.
    /// The report is written to a text file so runs before and after a change can be compared.
    /// </summary>
    public class PerformanceProbe : MonoBehaviour
    {
        public struct Viewpoint
        {
            public string name;
            public Vector3 position;
            public float yaw, pitch;
            public Viewpoint(string name, Vector3 position, float yaw, float pitch) { this.name = name; this.position = position; this.yaw = yaw; this.pitch = pitch; }
        }

        public static readonly Viewpoint[] Island =
        {
            new Viewpoint("Beach start", new Vector3(305f, 0f, 114f), 60f, 0f),
            new Viewpoint("Beach to jungle", new Vector3(300f, 0f, 150f), 0f, 0f),
            new Viewpoint("Deep jungle", new Vector3(290f, 0f, 255f), 330f, 0f),
            new Viewpoint("Swamp", new Vector3(430f, 0f, 190f), 200f, 0f),
            new Viewpoint("Volcano slope view", new Vector3(370f, 0f, 360f), 220f, 8f),
            new Viewpoint("Island overview", new Vector3(300f, 60f, 60f), 20f, 12f),
        };

        /// <summary>Viewpoints the next run visits (defaults to the whole island tour).</summary>
        public static Viewpoint[] Points = Island;
        public static string LastReport { get; private set; }
        public static bool Running { get; private set; }

        Transform eye;
        Camera cam;
        string outputPath;
        float settleSeconds, sampleSeconds;

        public static void Run(string outputPath, float settleSeconds = 2.5f, float sampleSeconds = 4f)
        {
            var go = new GameObject("Performance Probe");
            var probe = go.AddComponent<PerformanceProbe>();
            probe.outputPath = outputPath;
            probe.settleSeconds = settleSeconds;
            probe.sampleSeconds = sampleSeconds;
        }

        IEnumerator Start()
        {
            Running = true;
            cam = Camera.main;
            if (cam == null) { Running = false; yield break; }
            // A free camera of our own, so player controllers and scripts can't move it.
            var player = cam.transform.root;
            foreach (var mb in player.GetComponentsInChildren<MonoBehaviour>())
                if (mb != null && mb.GetType().Name.Contains("Controller")) mb.enabled = false;
            eye = cam.transform;
            var terrain = Terrain.activeTerrain;
            var report = new StringBuilder();
            report.AppendLine($"Performance probe  {System.DateTime.Now:yyyy-MM-dd HH:mm}  {Screen.width}x{Screen.height}  quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            report.AppendLine("viewpoint            | fps   | cpu ms | gpu ms | batches | draws | setpass | tris(k) | tex MB | mesh MB");
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            using var texMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Texture Memory");
            using var meshMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Mesh Memory");
            var timings = new FrameTiming[1];
            foreach (var vp in Points)
            {
                var pos = vp.position;
                float ground = terrain != null ? terrain.SampleHeight(pos) + terrain.transform.position.y : 0f;
                pos.y = Mathf.Max(ground, 0f) + 1.7f + vp.position.y;
                eye.SetPositionAndRotation(pos, Quaternion.Euler(vp.pitch, vp.yaw, 0f));
                yield return new WaitForSecondsRealtime(settleSeconds);
                int frames = 0; double cpu = 0, gpu = 0, b = 0, d = 0, s = 0, t = 0; int gpuFrames = 0;
                float start = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - start < sampleSeconds)
                {
                    yield return null;
                    eye.SetPositionAndRotation(pos, Quaternion.Euler(vp.pitch, vp.yaw, 0f));
                    frames++;
                    cpu += Time.unscaledDeltaTime * 1000.0;
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) { gpu += timings[0].gpuFrameTime; gpuFrames++; }
                    b += batches.LastValue; d += draws.LastValue; s += setpass.LastValue; t += tris.LastValue;
                }
                double fps = frames / (Time.realtimeSinceStartup - start);
                report.AppendLine($"{vp.name,-20} | {fps,5:F0} | {cpu / frames,6:F2} | {(gpuFrames > 0 ? (gpu / gpuFrames).ToString("F2") : "  n/a"),6} | {b / frames,7:F0} | {d / frames,5:F0} | {s / frames,7:F0} | {t / frames / 1000.0,7:F0} | {texMem.LastValue / 1048576.0,6:F0} | {meshMem.LastValue / 1048576.0,7:F0}");
            }
            LastReport = report.ToString();
            if (!string.IsNullOrEmpty(outputPath))
                System.IO.File.WriteAllText(outputPath, LastReport);
            Debug.Log(LastReport);
            Running = false;
        }
    }
}
