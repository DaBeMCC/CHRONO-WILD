using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChronoWild.World
{
    /// <summary>
    /// Parámetros de diseño de la isla. Todas las posiciones de layout están normalizadas (0..1)
    /// sobre el terreno: x = oeste→este, y = sur→norte. Así el layout sobrevive a un cambio de tamaño.
    /// El generador de editor (ChronoWildTerrainGenerator) lee este asset; nada se genera en runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "CHRONO WILD/World/Island Terrain Settings", fileName = "IslandTerrainSettings")]
    public class IslandTerrainSettings : ScriptableObject
    {
        [Header("Semilla y dimensiones")]
        public int seed = 1337;
        [Tooltip("Lado del terreno en metros (cuadrado).")]
        public float terrainSize = 600f;
        [Tooltip("Muestras del heightmap por lado (2^n + 1).")]
        public int heightmapResolution = 513;
        public int alphamapResolution = 512;
        [Tooltip("Rango vertical total del TerrainData, incluido el lecho marino.")]
        public float maxHeight = 140f;
        [Tooltip("Profundidad del lecho marino. El terreno se coloca en y = -seaFloorDepth y el agua en y = 0.")]
        public float seaFloorDepth = 20f;

        [Header("Costa")]
        [Range(0.2f, 0.48f)] public float islandRadius = 0.36f;
        public float coastWarpStrength = 0.07f;
        public float coastWarpFrequency = 3f;
        [Tooltip("Metros que tarda el fondo marino en alcanzar su profundidad máxima.")]
        public float offshoreSlopeWidth = 45f;

        [Tooltip("Dirección de la playa en grados (0 = este, 90 = norte, 270 = sur).")]
        public float beachDirection = 270f;
        public float beachArc = 110f;
        public float beachWidth = 40f;
        public float beachHeight = 1.8f;

        [Tooltip("Dirección central de la costa acantilada (grados).")]
        public float cliffDirection = 50f;
        public float cliffArc = 130f;
        public float cliffHeight = 22f;
        [Tooltip("Metros horizontales en los que el acantilado sube desde el agua.")]
        public float cliffWidth = 7f;

        [Header("Relieve interior")]
        public float inlandBaseHeight = 6f;
        public float inlandRampWidth = 50f;
        public float detailAmplitude = 5f;
        public float detailFrequency = 6f;
        [Range(1, 8)] public int detailOctaves = 4;
        [Range(0, 4)] public int smoothingPasses = 1;
        public List<ReliefFeature> reliefFeatures = DefaultReliefFeatures();

        [Header("Áreas planas (campamento, claros, meseta)")]
        public List<FlatArea> flatAreas = DefaultFlatAreas();

        [Header("Caminos")]
        public List<PathDefinition> paths = DefaultPaths();

        [Header("Vegetación provisional")]
        public Vector2 forestCenter = new Vector2(0.52f, 0.50f);
        public float forestRadius = 0.30f;
        [Tooltip("Separación de la rejilla de candidatos (m). Controla la densidad máxima.")]
        public float treeSpacing = 4.5f;
        public int maxTrees = 2500;
        public float treeSlopeLimit = 30f;
        public float minTreeHeightAboveSea = 4f;
        [Tooltip("Distancia libre de árboles a cada lado del borde de los caminos (m).")]
        public float pathClearance = 2.5f;
        [Range(0f, 1f)] public float scatteredTreeDensity = 0.12f;
        public float clusterFrequency = 9f;
        public Vector2 treeScaleRange = new Vector2(0.8f, 1.3f);

        [Header("Puntos de interés")]
        public List<PointOfInterestDefinition> pointsOfInterest = DefaultPointsOfInterest();

        public float HeightAboveSeaMax => maxHeight - seaFloorDepth;

        public Vector3 NormalizedToWorldXZ(Vector2 normalized)
        {
            return new Vector3(normalized.x * terrainSize, 0f, normalized.y * terrainSize);
        }

        public Vector2 WorldToNormalized(Vector3 world)
        {
            return new Vector2(world.x / terrainSize, world.z / terrainSize);
        }

        [ContextMenu("Restaurar layout por defecto")]
        public void ResetLayoutToDefaults()
        {
            reliefFeatures = DefaultReliefFeatures();
            flatAreas = DefaultFlatAreas();
            paths = DefaultPaths();
            pointsOfInterest = DefaultPointsOfInterest();
        }

        static List<ReliefFeature> DefaultReliefFeatures() => new List<ReliefFeature>
        {
            new ReliefFeature { name = "Monte Cronos (hito)", center = new Vector2(0.66f, 0.70f), radius = 0.14f, height = 66f, ridged = 0.55f },
            new ReliefFeature { name = "Colina Oeste", center = new Vector2(0.22f, 0.45f), radius = 0.09f, height = 20f, ridged = 0.2f },
            new ReliefFeature { name = "Cresta del Valle (oculta el valle)", center = new Vector2(0.41f, 0.51f), radius = 0.07f, height = 15f, ridged = 0.3f },
            new ReliefFeature { name = "Colinas Este", center = new Vector2(0.77f, 0.45f), radius = 0.10f, height = 24f, ridged = 0.3f },
            new ReliefFeature { name = "Meseta del Portal", center = new Vector2(0.42f, 0.76f), radius = 0.09f, height = 18f, ridged = 0.1f },
            new ReliefFeature { name = "Valle Interior", center = new Vector2(0.30f, 0.63f), radius = 0.10f, height = -4f, ridged = 0f },
        };

        static List<FlatArea> DefaultFlatAreas() => new List<FlatArea>
        {
            new FlatArea { name = "Playa inicial", center = new Vector2(0.50f, 0.19f), radius = 18f, falloff = 15f, treeClearing = true },
            new FlatArea { name = "Claro del campamento", center = new Vector2(0.42f, 0.29f), radius = 14f, falloff = 12f, treeClearing = true },
            new FlatArea { name = "Claro del Ginkgo", center = new Vector2(0.56f, 0.50f), radius = 10f, falloff = 10f, treeClearing = true },
            new FlatArea { name = "Fondo del valle", center = new Vector2(0.30f, 0.63f), radius = 26f, falloff = 20f, treeClearing = true },
            new FlatArea { name = "Plataforma del Portal", center = new Vector2(0.42f, 0.76f), radius = 15f, falloff = 22f, treeClearing = true },
        };

        static List<PathDefinition> DefaultPaths() => new List<PathDefinition>
        {
            new PathDefinition
            {
                name = "Ruta principal (playa → portal)", width = 3.5f, falloff = 4f, carveDepth = 0.25f,
                points = new List<Vector2>
                {
                    new Vector2(0.50f, 0.19f), new Vector2(0.45f, 0.24f), new Vector2(0.42f, 0.29f),
                    new Vector2(0.46f, 0.36f), new Vector2(0.50f, 0.43f), new Vector2(0.56f, 0.50f),
                    new Vector2(0.51f, 0.56f), new Vector2(0.42f, 0.58f), new Vector2(0.34f, 0.60f),
                    new Vector2(0.28f, 0.67f), new Vector2(0.27f, 0.72f), new Vector2(0.34f, 0.74f),
                    new Vector2(0.42f, 0.76f),
                }
            },
            new PathDefinition
            {
                name = "Ruta alternativa: costa oeste", width = 2.5f, falloff = 3f, carveDepth = 0.2f,
                points = new List<Vector2>
                {
                    new Vector2(0.42f, 0.29f), new Vector2(0.33f, 0.31f), new Vector2(0.24f, 0.37f),
                    new Vector2(0.19f, 0.47f), new Vector2(0.22f, 0.56f), new Vector2(0.30f, 0.63f),
                }
            },
            new PathDefinition
            {
                name = "Ruta alternativa: sendero del monte", width = 2.5f, falloff = 3f, carveDepth = 0.2f,
                points = new List<Vector2>
                {
                    new Vector2(0.56f, 0.50f), new Vector2(0.62f, 0.53f), new Vector2(0.63f, 0.58f),
                    new Vector2(0.57f, 0.63f), new Vector2(0.51f, 0.69f), new Vector2(0.46f, 0.74f),
                    new Vector2(0.42f, 0.76f),
                }
            },
        };

        static List<PointOfInterestDefinition> DefaultPointsOfInterest() => new List<PointOfInterestDefinition>
        {
            new PointOfInterestDefinition { id = "spawn_beach", displayName = "Playa del naufragio", type = PoiType.Spawn, position = new Vector2(0.50f, 0.19f), radius = 4f },
            new PointOfInterestDefinition { id = "res_beach_wood", displayName = "Troncos a la deriva", type = PoiType.Resource, position = new Vector2(0.56f, 0.20f), radius = 6f },
            new PointOfInterestDefinition { id = "res_beach_stone", displayName = "Rocas de la orilla", type = PoiType.Resource, position = new Vector2(0.42f, 0.19f), radius = 6f },
            new PointOfInterestDefinition { id = "res_fiber", displayName = "Matorral de fibras", type = PoiType.Resource, position = new Vector2(0.48f, 0.25f), radius = 6f },
            new PointOfInterestDefinition { id = "camp", displayName = "Claro del campamento", type = PoiType.BuildZone, position = new Vector2(0.42f, 0.29f), radius = 12f },
            new PointOfInterestDefinition { id = "forest_entrance", displayName = "Entrada al bosque", type = PoiType.Waypoint, position = new Vector2(0.46f, 0.36f), radius = 5f },
            new PointOfInterestDefinition { id = "research_plant", displayName = "Ginkgo ancestral", type = PoiType.Research, position = new Vector2(0.56f, 0.50f), radius = 8f },
            new PointOfInterestDefinition { id = "valley_overlook", displayName = "Mirador del valle", type = PoiType.Viewpoint, position = new Vector2(0.42f, 0.58f), radius = 5f },
            new PointOfInterestDefinition { id = "dino_valley", displayName = "Valle de los herbívoros", type = PoiType.CreatureZone, position = new Vector2(0.30f, 0.63f), radius = 30f },
            new PointOfInterestDefinition { id = "mount_cronos", displayName = "Monte Cronos", type = PoiType.Landmark, position = new Vector2(0.66f, 0.70f), radius = 10f },
            new PointOfInterestDefinition { id = "portal", displayName = "Portal temporal", type = PoiType.Portal, position = new Vector2(0.42f, 0.76f), radius = 8f },
        };
    }

    [Serializable]
    public class ReliefFeature
    {
        public string name;
        public Vector2 center;
        [Tooltip("Radio normalizado.")] public float radius = 0.1f;
        [Tooltip("Metros. Negativo = cuenca/valle.")] public float height = 10f;
        [Range(0f, 1f)] public float ridged;
    }

    [Serializable]
    public class FlatArea
    {
        public string name;
        public Vector2 center;
        [Tooltip("Metros.")] public float radius = 10f;
        [Tooltip("Metros.")] public float falloff = 10f;
        [Tooltip("Si es verdadero, usa la altura generada en el centro.")] public bool autoHeight = true;
        [Tooltip("Altura sobre el mar (m) si autoHeight es falso; offset si es verdadero.")] public float height;
        public bool treeClearing = true;
    }

    [Serializable]
    public class PathDefinition
    {
        public string name;
        public List<Vector2> points = new List<Vector2>();
        [Tooltip("Metros.")] public float width = 3f;
        [Tooltip("Metros de transición con el terreno.")] public float falloff = 4f;
        public float carveDepth = 0.2f;
        [Tooltip("Radio (m) del suavizado longitudinal de alturas.")] public float smoothingRadius = 25f;
    }

    public enum PoiType
    {
        Spawn,
        Resource,
        BuildZone,
        Research,
        CreatureZone,
        Landmark,
        Viewpoint,
        Portal,
        Waypoint,
    }

    [Serializable]
    public class PointOfInterestDefinition
    {
        public string id;
        public string displayName;
        public PoiType type;
        public Vector2 position;
        public float radius = 5f;
    }
}
