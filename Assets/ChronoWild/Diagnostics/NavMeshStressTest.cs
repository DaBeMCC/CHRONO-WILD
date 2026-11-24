using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.AI;

namespace ChronoWild.Diagnostics
{
    /// <summary>
    /// NavMesh stress test: bakes a NavMesh around a point at runtime and keeps N agents walking
    /// to random destinations, so the cost of pathfinding and crowd avoidance can be profiled
    /// with many agents. Settings for the optimised configuration are exposed for comparisons.
    /// </summary>
    public sealed class NavMeshStressTest : MonoBehaviour
    {
        public GameObject agentModel;
        public float radius = 60f;
        public int agentCount = 100;
        public ObstacleAvoidanceType avoidance = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        [Tooltip("Seconds between destination changes per agent; 0 = only when it arrives.")]
        public float repathInterval = 0f;
        [Tooltip("NavMesh.pathfindingIterationsPerFrame (Unity default 100).")]
        public int pathfindingIterations = 100;
        public bool disableShadows;

        public double BakeMilliseconds { get; private set; }
        public int Triangles { get; private set; }
        public int Spawned => agents.Count;
        public int PathRequests { get; private set; }

        readonly List<NavMeshAgent> agents = new List<NavMeshAgent>();
        readonly List<float> nextRepath = new List<float>();
        NavMeshDataInstance instance;
        NavMeshData data;

        void Start()
        {
            Bake();
            NavMesh.pathfindingIterationsPerFrame = pathfindingIterations;
            for (int i = 0; i < agentCount; i++)
                Spawn(i);
        }

        void OnDestroy()
        {
            foreach (var a in agents)
                if (a != null) Destroy(a.gameObject);
            if (instance.valid)
                instance.Remove();
            NavMesh.pathfindingIterationsPerFrame = 100;
        }

        void Bake()
        {
            var bounds = new Bounds(transform.position, new Vector3(radius * 2, 60f, radius * 2));
            var sources = new List<NavMeshBuildSource>();
            var watch = Stopwatch.StartNew();
            NavMeshBuilder.CollectSources(bounds, Physics.DefaultRaycastLayers, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = .35f;
            settings.agentHeight = 1.2f;
            settings.agentSlope = 40f;
            settings.agentClimb = .4f;
            data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            instance = NavMesh.AddNavMeshData(data);
            watch.Stop();
            BakeMilliseconds = watch.Elapsed.TotalMilliseconds;
            Triangles = NavMesh.CalculateTriangulation().indices.Length / 3;
        }

        void Spawn(int index)
        {
            if (!RandomPoint(out var point))
                return;
            GameObject go;
            if (agentModel != null)
            {
                go = Instantiate(agentModel, point, Quaternion.Euler(0, Random.Range(0, 360f), 0), transform);
                foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform);
                go.transform.position = point;
            }
            if (disableShadows)
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.name = "Agente " + index;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = .35f;
            agent.height = 1.2f;
            agent.speed = Random.Range(2.5f, 4.5f);
            agent.acceleration = 10f;
            agent.angularSpeed = 360f;
            agent.obstacleAvoidanceType = avoidance;
            agent.avoidancePriority = Random.Range(30, 70);
            agents.Add(agent);
            nextRepath.Add(Time.time + Random.Range(0f, Mathf.Max(.1f, repathInterval)));
            SetDestination(agent);
        }

        bool RandomPoint(out Vector3 point)
        {
            for (int i = 0; i < 80; i++)
            {
                var flat = Random.insideUnitCircle * radius * .9f;
                if (NavMesh.SamplePosition(transform.position + new Vector3(flat.x, 0, flat.y), out var hit, 10f, NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            }
            point = default;
            return false;
        }

        void SetDestination(NavMeshAgent agent)
        {
            if (RandomPoint(out var point))
            {
                agent.SetDestination(point);
                PathRequests++;
            }
        }

        /// <summary>Top-down snapshot for the report: NavMesh triangles and agent positions (x, z).</summary>
        public string ExportMap()
        {
            var tri = NavMesh.CalculateTriangulation();
            var sb = new System.Text.StringBuilder("{\"vertices\": [");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < tri.vertices.Length; i++)
                sb.Append(i > 0 ? "," : "").Append(tri.vertices[i].x.ToString("F2", ci)).Append(',').Append(tri.vertices[i].z.ToString("F2", ci));
            sb.Append("], \"indices\": [").Append(string.Join(",", tri.indices)).Append("], \"agents\": [");
            for (int i = 0; i < agents.Count; i++)
                if (agents[i] != null)
                    sb.Append(i > 0 ? "," : "").Append(agents[i].transform.position.x.ToString("F2", ci)).Append(',').Append(agents[i].transform.position.z.ToString("F2", ci));
            sb.Append("], \"centre\": [").Append(transform.position.x.ToString("F2", ci)).Append(',').Append(transform.position.z.ToString("F2", ci)).Append("]}");
            return sb.ToString();
        }

        void Update()
        {
            float now = Time.time;
            for (int i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                if (a == null || !a.isOnNavMesh || a.pathPending)
                    continue;
                bool arrived = a.remainingDistance <= a.stoppingDistance + .3f;
                bool timed = repathInterval > 0 && now >= nextRepath[i];
                if (arrived || timed)
                {
                    SetDestination(a);
                    nextRepath[i] = now + repathInterval;
                }
            }
        }
    }
}
