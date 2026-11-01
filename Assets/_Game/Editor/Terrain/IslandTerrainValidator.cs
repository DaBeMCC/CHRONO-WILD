using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ChronoWild.World;
using UnityEngine;

namespace ChronoWild.Editor.Terrain
{
    /// <summary>
    /// Static terrain validation (without entering Play): walkable surface, path slopes,
    /// travel times, POI status and percentage of the island visible from key points.
    /// Visibility ignores trees, so it is an upper bound: the real value is lower.
    /// Does not measure FPS or memory; that requires playing the scene on the target machine.
    /// </summary>
    public static class IslandTerrainValidator
    {
        public const float WalkSpeed = 4f;
        public const float RunSpeed = 7f;
        public const float WalkableSlope = 40f;
        public const float EyeHeight = 1.7f;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Validate(IslandTerrainSettings s, TerrainData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Terrain validation report");
            sb.AppendLine();
            sb.AppendLine($"- Date: {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"- Seed: {s.seed} · Terrain {F(s.terrainSize)} × {F(s.terrainSize)} m · heightmap {data.heightmapResolution}² ({F(s.terrainSize / (data.heightmapResolution - 1), 2)} m/sample) · vertical range {F(s.maxHeight)} m");
            sb.AppendLine($"- Reference speeds: walk {F(WalkSpeed)} m/s, run {F(RunSpeed)} m/s (DebugFirstPersonController)");
            sb.AppendLine();

            var heights = ReadHeights(s, data, out int res);
            AppendIslandStats(sb, s, data, heights, res);
            AppendPaths(sb, s, data);
            AppendPois(sb, s, data);
            AppendVisibility(sb, s, heights, res);
            sb.AppendLine("## Vegetation");
            sb.AppendLine();
            sb.AppendLine($"- Trees: {data.treeInstanceCount} (prototypes: {data.treePrototypes.Length})");
            for (int i = 0; i < data.treePrototypes.Length; i++)
            {
                int count = data.treeInstances.Count(t => t.prototypeIndex == i);
                sb.AppendLine($"  - {data.treePrototypes[i].prefab?.name}: {count}");
            }
            sb.AppendLine();
            sb.AppendLine("## Still to validate in Play / on the target machine");
            sb.AppendLine();
            sb.AppendLine("- FPS and memory at 720p on low quality (use the Island_Gameplay overlay; F1 hides it).");
            sb.AppendLine("- Actual CharacterController collisions at path edges and cliffs.");
            sb.AppendLine("- Actual visibility with vegetation (this report ignores trees).");
            return sb.ToString();
        }

        static float[,] ReadHeights(IslandTerrainSettings s, TerrainData data, out int res)
        {
            res = data.heightmapResolution;
            var raw = data.GetHeights(0, 0, res, res);
            var meters = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    meters[z, x] = raw[z, x] * data.size.y - s.seaFloorDepth;
            return meters;
        }

        static void AppendIslandStats(StringBuilder sb, IslandTerrainSettings s, TerrainData data, float[,] h, int res)
        {
            float cell = s.terrainSize / (res - 1);
            int land = 0, walkable = 0, steep = 0;
            float maxH = float.MinValue;
            int minX = res, maxX = 0, minZ = res, maxZ = 0;
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    if (h[z, x] <= 0.2f) continue;
                    land++;
                    maxH = Mathf.Max(maxH, h[z, x]);
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
                    float slope = data.GetSteepness(x / (float)(res - 1), z / (float)(res - 1));
                    if (slope <= WalkableSlope) walkable++;
                    if (slope > 45f) steep++;
                }
            }
            float landArea = land * cell * cell;
            float ew = (maxX - minX) * cell, ns = (maxZ - minZ) * cell;
            sb.AppendLine("## Island");
            sb.AppendLine();
            sb.AppendLine($"- Land area: {F(landArea / 10000f, 1)} ha ({F(landArea / 1e6f, 3)} km²)");
            sb.AppendLine($"- Extent: {F(ew)} m east-west × {F(ns)} m north-south");
            sb.AppendLine($"- Maximum height above sea: {F(maxH, 1)} m");
            sb.AppendLine($"- Walkable (≤ {F(WalkableSlope)}°): {Pct(walkable, land)} of land · unclimbable (> 45°): {Pct(steep, land)}");
            sb.AppendLine($"- Crossing the island side to side (straight line, longest extent): {F(Mathf.Max(ew, ns) / WalkSpeed)} s walking / {F(Mathf.Max(ew, ns) / RunSpeed)} s running");
            sb.AppendLine();
        }

        static void AppendPaths(StringBuilder sb, IslandTerrainSettings s, TerrainData data)
        {
            sb.AppendLine("## Paths");
            sb.AppendLine();
            sb.AppendLine("| Path | Length | Elevation + / − | Max slope | Stretch > 15° | Stretch > 25° | Walking | Running |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var path in s.paths)
            {
                var pts = IslandHeightGenerator.SamplePathMeters(path, s.terrainSize, 1f);
                if (pts.Count < 2) continue;
                var y = pts.Select(p => data.GetInterpolatedHeight(p.x / s.terrainSize, p.y / s.terrainSize)).ToArray();
                float length = 0f, up = 0f, down = 0f, maxSlope = 0f, over15 = 0f, over25 = 0f;
                const int window = 4;
                for (int i = 1; i < pts.Count; i++)
                {
                    float horiz = Vector2.Distance(pts[i - 1], pts[i]);
                    float dy = y[i] - y[i - 1];
                    length += Mathf.Sqrt(horiz * horiz + dy * dy);
                    if (dy > 0) up += dy; else down -= dy;
                    int j = Mathf.Max(0, i - window);
                    float span = 0f;
                    for (int k = j + 1; k <= i; k++) span += Vector2.Distance(pts[k - 1], pts[k]);
                    float slope = Mathf.Atan2(Mathf.Abs(y[i] - y[j]), Mathf.Max(0.01f, span)) * Mathf.Rad2Deg;
                    maxSlope = Mathf.Max(maxSlope, slope);
                    if (slope > 15f) over15 += horiz;
                    if (slope > 25f) over25 += horiz;
                }
                sb.AppendLine($"| {path.name} | {F(length)} m | {F(up)} / {F(down)} m | {F(maxSlope, 1)}° | {F(over15)} m | {F(over25)} m | {Time(length / WalkSpeed)} | {Time(length / RunSpeed)} |");
            }
            sb.AppendLine();
            sb.AppendLine("Slope measured over 4 m windows along the path axis. The test CharacterController climbs up to 45°.");
            sb.AppendLine();
        }

        static void AppendPois(StringBuilder sb, IslandTerrainSettings s, TerrainData data)
        {
            sb.AppendLine("## Points of interest");
            sb.AppendLine();
            sb.AppendLine("| Id | Name | Type | Position (m) | Height | Slope | Distance to spawn | Warning |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            var spawn = s.pointsOfInterest.FirstOrDefault(p => p.type == PoiType.Spawn);
            foreach (var poi in s.pointsOfInterest)
            {
                var pos = IslandSceneBuilder.GroundPoint(s, data, poi.position);
                float slope = data.GetSteepness(poi.position.x, poi.position.y);
                float dist = spawn != null ? Vector2.Distance(poi.position, spawn.position) * s.terrainSize : 0f;
                var warnings = new List<string>();
                if (pos.y < 0.3f) warnings.Add("underwater");
                if (slope > 25f && poi.type != PoiType.Landmark) warnings.Add("steep slope");
                if (poi.type == PoiType.BuildZone && slope > 12f) warnings.Add("too uneven to build on");
                sb.AppendLine($"| {poi.id} | {poi.displayName} | {poi.type} | ({F(pos.x)}, {F(pos.z)}) | {F(pos.y, 1)} m | {F(slope, 1)}° | {F(dist)} m | {string.Join(", ", warnings)} |");
            }
            sb.AppendLine();
        }

        static void AppendVisibility(StringBuilder sb, IslandTerrainSettings s, float[,] h, int res)
        {
            sb.AppendLine("## Progressive reveal: % of land visible");
            sb.AppendLine();
            sb.AppendLine("Line of sight from eye height against the relief (no trees or fog). The lower it is, the more exploring reveals.");
            sb.AppendLine();
            sb.AppendLine("| From | % of land visible | Portal visible | Mount Cronos visible |");
            sb.AppendLine("|---|---|---|---|");

            const int grid = 96;
            var targets = new List<Vector3>();
            for (int gz = 0; gz < grid; gz++)
            {
                for (int gx = 0; gx < grid; gx++)
                {
                    float xm = (gx + 0.5f) / grid * s.terrainSize, zm = (gz + 0.5f) / grid * s.terrainSize;
                    float y = IslandHeightGenerator.Bilinear(h, res, s.terrainSize, xm, zm);
                    if (y > 0.2f) targets.Add(new Vector3(xm, y + 0.5f, zm));
                }
            }

            var portal = s.pointsOfInterest.FirstOrDefault(p => p.type == PoiType.Portal);
            var mountain = s.pointsOfInterest.FirstOrDefault(p => p.id == "mount_cronos");
            string[] viewpoints = { "spawn_beach", "camp", "forest_entrance", "research_plant", "valley_overlook", "dino_valley", "portal" };
            foreach (var id in viewpoints)
            {
                var poi = s.pointsOfInterest.FirstOrDefault(p => p.id == id);
                if (poi == null) continue;
                Vector3 eye = Ground(s, h, res, poi.position) + Vector3.up * EyeHeight;
                int visible = targets.Count(t => LineOfSight(s, h, res, eye, t));
                string portalSeen = portal != null && portal != poi ? YesNo(LineOfSight(s, h, res, eye, Ground(s, h, res, portal.position) + Vector3.up * 6f)) : "—";
                string mountSeen = mountain != null ? YesNo(LineOfSight(s, h, res, eye, Ground(s, h, res, mountain.position) + Vector3.up * 8f)) : "—";
                sb.AppendLine($"| {poi.displayName} | {Pct(visible, targets.Count)} | {portalSeen} | {mountSeen} |");
            }
            sb.AppendLine();
        }

        static Vector3 Ground(IslandTerrainSettings s, float[,] h, int res, Vector2 n)
        {
            float xm = n.x * s.terrainSize, zm = n.y * s.terrainSize;
            return new Vector3(xm, IslandHeightGenerator.Bilinear(h, res, s.terrainSize, xm, zm), zm);
        }

        static bool LineOfSight(IslandTerrainSettings s, float[,] h, int res, Vector3 from, Vector3 to)
        {
            float dist = Vector3.Distance(from, to);
            int steps = Mathf.CeilToInt(dist / 2f);
            for (int i = 1; i < steps; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)steps);
                if (IslandHeightGenerator.Bilinear(h, res, s.terrainSize, p.x, p.z) > p.y)
                    return false;
            }
            return true;
        }

        static string F(float v, int decimals = 0) => v.ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), Inv);
        static string Pct(int a, int b) => b == 0 ? "—" : (100f * a / b).ToString("0.0", Inv) + " %";
        static string YesNo(bool b) => b ? "yes" : "no";
        static string Time(float seconds) => seconds < 60f ? $"{F(seconds)} s" : $"{(int)(seconds / 60)} min {F(seconds % 60f)} s";
    }
}
