using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ChronoWild.World;
using UnityEngine;

namespace ChronoWild.Editor.Terrain
{
    /// <summary>
    /// Validación estática del terreno (sin entrar en Play): superficie transitable, pendientes de
    /// los caminos, tiempos de recorrido, estado de los POIs y porcentaje de isla visible desde
    /// puntos clave. La visibilidad ignora árboles, así que es una cota superior: la real es menor.
    /// No mide FPS ni memoria; eso requiere jugar la escena en el equipo objetivo.
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
            sb.AppendLine("# Informe de validación del terreno");
            sb.AppendLine();
            sb.AppendLine($"- Fecha: {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"- Semilla: {s.seed} · Terreno {F(s.terrainSize)} × {F(s.terrainSize)} m · heightmap {data.heightmapResolution}² ({F(s.terrainSize / (data.heightmapResolution - 1), 2)} m/muestra) · rango vertical {F(s.maxHeight)} m");
            sb.AppendLine($"- Velocidades de referencia: caminar {F(WalkSpeed)} m/s, correr {F(RunSpeed)} m/s (DebugFirstPersonController)");
            sb.AppendLine();

            var heights = ReadHeights(s, data, out int res);
            AppendIslandStats(sb, s, data, heights, res);
            AppendPaths(sb, s, data);
            AppendPois(sb, s, data);
            AppendVisibility(sb, s, heights, res);
            sb.AppendLine("## Vegetación");
            sb.AppendLine();
            sb.AppendLine($"- Árboles: {data.treeInstanceCount} (prototipos: {data.treePrototypes.Length})");
            for (int i = 0; i < data.treePrototypes.Length; i++)
            {
                int count = data.treeInstances.Count(t => t.prototypeIndex == i);
                sb.AppendLine($"  - {data.treePrototypes[i].prefab?.name}: {count}");
            }
            sb.AppendLine();
            sb.AppendLine("## Pendiente de validar en Play / equipo objetivo");
            sb.AppendLine();
            sb.AppendLine("- FPS y memoria a 720p en calidad baja (usar el overlay de Island_Gameplay; F1 lo oculta).");
            sb.AppendLine("- Colisiones reales del CharacterController en bordes de caminos y acantilados.");
            sb.AppendLine("- Visibilidad real con vegetación (este informe ignora los árboles).");
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
            sb.AppendLine("## Isla");
            sb.AppendLine();
            sb.AppendLine($"- Superficie emergida: {F(landArea / 10000f, 1)} ha ({F(landArea / 1e6f, 3)} km²)");
            sb.AppendLine($"- Extensión: {F(ew)} m este-oeste × {F(ns)} m norte-sur");
            sb.AppendLine($"- Altura máxima sobre el mar: {F(maxH, 1)} m");
            sb.AppendLine($"- Transitable (≤ {F(WalkableSlope)}°): {Pct(walkable, land)} de la tierra · no escalable (> 45°): {Pct(steep, land)}");
            sb.AppendLine($"- Cruzar la isla de lado a lado (línea recta, la mayor extensión): {F(Mathf.Max(ew, ns) / WalkSpeed)} s caminando / {F(Mathf.Max(ew, ns) / RunSpeed)} s corriendo");
            sb.AppendLine();
        }

        static void AppendPaths(StringBuilder sb, IslandTerrainSettings s, TerrainData data)
        {
            sb.AppendLine("## Caminos");
            sb.AppendLine();
            sb.AppendLine("| Camino | Longitud | Desnivel + / − | Pendiente máx. | Tramo > 15° | Tramo > 25° | Caminando | Corriendo |");
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
            sb.AppendLine("Pendiente medida en ventanas de 4 m sobre el eje del camino. El CharacterController de prueba sube hasta 45°.");
            sb.AppendLine();
        }

        static void AppendPois(StringBuilder sb, IslandTerrainSettings s, TerrainData data)
        {
            sb.AppendLine("## Puntos de interés");
            sb.AppendLine();
            sb.AppendLine("| Id | Nombre | Tipo | Posición (m) | Altura | Pendiente | Distancia al spawn | Aviso |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            var spawn = s.pointsOfInterest.FirstOrDefault(p => p.type == PoiType.Spawn);
            foreach (var poi in s.pointsOfInterest)
            {
                var pos = IslandSceneBuilder.GroundPoint(s, data, poi.position);
                float slope = data.GetSteepness(poi.position.x, poi.position.y);
                float dist = spawn != null ? Vector2.Distance(poi.position, spawn.position) * s.terrainSize : 0f;
                var warnings = new List<string>();
                if (pos.y < 0.3f) warnings.Add("bajo el agua");
                if (slope > 25f && poi.type != PoiType.Landmark) warnings.Add("pendiente alta");
                if (poi.type == PoiType.BuildZone && slope > 12f) warnings.Add("poco plano para construir");
                sb.AppendLine($"| {poi.id} | {poi.displayName} | {poi.type} | ({F(pos.x)}, {F(pos.z)}) | {F(pos.y, 1)} m | {F(slope, 1)}° | {F(dist)} m | {string.Join(", ", warnings)} |");
            }
            sb.AppendLine();
        }

        static void AppendVisibility(StringBuilder sb, IslandTerrainSettings s, float[,] h, int res)
        {
            sb.AppendLine("## Revelación progresiva: % de tierra visible");
            sb.AppendLine();
            sb.AppendLine("Línea de visión desde la altura de los ojos contra el relieve (sin árboles ni niebla). Cuanto menor, más se revela al explorar.");
            sb.AppendLine();
            sb.AppendLine("| Desde | % de tierra visible | Portal visible | Monte Cronos visible |");
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
        static string YesNo(bool b) => b ? "sí" : "no";
        static string Time(float seconds) => seconds < 60f ? $"{F(seconds)} s" : $"{(int)(seconds / 60)} min {F(seconds % 60f)} s";
    }
}
