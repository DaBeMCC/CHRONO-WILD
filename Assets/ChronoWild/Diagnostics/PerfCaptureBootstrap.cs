using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

namespace ChronoWild.Diagnostics
{
    /// <summary>
    /// Automated performance capture for player builds. Does nothing unless the game is started
    /// with <c>-perf-label NAME</c>. Example:
    ///   CHRONO_WILD.x86_64 -perf-label base_playa -perf-pos 305,114,295 -perf-out /tmp/perf
    ///   ... -perf-navmesh 200 -perf-avoid low|high|none -perf-iterations 100
    /// It skips the intro, places the player, fixes the time of day, records the
    /// <see cref="PerformanceProbe"/> window, optionally runs the NavMesh stress test, then quits.
    /// </summary>
    public sealed class PerfCaptureBootstrap : MonoBehaviour
    {
        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeScene()
        {
            if (Arg("-perf-label") != null)
                Story.IntroCinematic.SkipNext = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterScene()
        {
            if (Arg("-perf-label") == null || FindAnyObjectByType<PerfCaptureBootstrap>() != null)
                return;
            var go = new GameObject("Perf Capture");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfCaptureBootstrap>();
        }

        IEnumerator Start()
        {
            string label = Arg("-perf-label");
            string output = Arg("-perf-out") ?? Path.Combine(Application.persistentDataPath, "perf");
            int frames = int.TryParse(Arg("-perf-frames"), out var f) ? f : 900;
            Application.targetFrameRate = -1;
            // Keep running when the window loses focus, or the capture stalls.
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;

            // Let streaming and the first frames settle.
            yield return new WaitForSecondsRealtime(6f);

            var player = FindAnyObjectByType<Player.DebugFirstPersonController>();
            var pos = Arg("-perf-pos");
            if (player != null && pos != null)
            {
                var p = pos.Split(',');
                float x = float.Parse(p[0], CultureInfo.InvariantCulture), z = float.Parse(p[1], CultureInfo.InvariantCulture);
                float yaw = p.Length > 2 ? float.Parse(p[2], CultureInfo.InvariantCulture) : 0f;
                var terrain = Terrain.activeTerrain;
                var target = new Vector3(x, terrain ? terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y + .1f : 2f, z);
                var body = player.GetComponent<CharacterController>();
                if (body) body.enabled = false;
                player.transform.SetPositionAndRotation(target, Quaternion.Euler(0, yaw, 0));
                if (body) body.enabled = true;
                if (player.cameraPivot) player.cameraPivot.localRotation = Quaternion.Euler(5, 0, 0);
            }
            var clock = World.DayNightCycle.Instance;
            if (clock != null)
            {
                clock.SetHour(10f);
                clock.paused = true;
            }

            NavMeshStressTest stress = null;
            if (int.TryParse(Arg("-perf-navmesh"), out var agents) && agents > 0 && player != null)
            {
                var host = new GameObject("NavMesh Stress");
                host.transform.position = player.transform.position + player.transform.forward * 25f;
                host.SetActive(false);
                stress = host.AddComponent<NavMeshStressTest>();
                stress.agentCount = agents;
                stress.avoidance = (Arg("-perf-avoid") ?? "high") switch
                {
                    "none" => ObstacleAvoidanceType.NoObstacleAvoidance,
                    "low" => ObstacleAvoidanceType.LowQualityObstacleAvoidance,
                    "medium" => ObstacleAvoidanceType.MedQualityObstacleAvoidance,
                    _ => ObstacleAvoidanceType.HighQualityObstacleAvoidance,
                };
                if (int.TryParse(Arg("-perf-iterations"), out var iterations)) stress.pathfindingIterations = iterations;
                if (float.TryParse(Arg("-perf-repath"), NumberStyles.Float, CultureInfo.InvariantCulture, out var repath)) stress.repathInterval = repath;
                stress.disableShadows = Arg("-perf-agent-shadows") == "off";
                var herd = FindAnyObjectByType<Survival.DryosaurHerd>();
                stress.agentModel = herd != null ? herd.model : null;
                host.SetActive(true);
            }

            yield return new WaitForSecondsRealtime(4f);
            var probe = gameObject.AddComponent<PerformanceProbe>();
            probe.label = label;
            probe.frames = frames;
            probe.warmupFrames = 60;
            probe.outputPath = Path.Combine(output, label + ".probe.json");
            while (!probe.Done)
                yield return null;

            if (stress != null && Arg("-perf-navmap") != null)
                File.WriteAllText(Path.Combine(output, label + ".navmap.json"), stress.ExportMap());
            if (stress != null)
                File.WriteAllText(Path.Combine(output, label + ".navmesh.json"), string.Format(CultureInfo.InvariantCulture,
                    "{{\"agents\": {0}, \"spawned\": {1}, \"bakeMs\": {2:F1}, \"navTriangles\": {3}, \"pathRequests\": {4}, \"avoidance\": \"{5}\", \"iterations\": {6} }}\n",
                    stress.agentCount, stress.Spawned, stress.BakeMilliseconds, stress.Triangles, stress.PathRequests, stress.avoidance, NavMesh.pathfindingIterationsPerFrame));
            yield return new WaitForSecondsRealtime(.5f);
            Application.Quit();
        }
    }
}
