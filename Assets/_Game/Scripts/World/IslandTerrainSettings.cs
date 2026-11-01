using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChronoWild.World
{
    /// <summary>
    /// Island design parameters. All layout positions are normalized (0..1) over the terrain:
    /// x = west→east, y = south→north, so the layout survives a size change.
    /// The editor generator (ChronoWildTerrainGenerator) reads this asset; nothing is generated at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "CHRONO WILD/World/Island Terrain Settings", fileName = "IslandTerrainSettings")]
    public class IslandTerrainSettings : ScriptableObject
    {
        [Header("Seed and dimensions")]
        public int seed = 1337;
        [Tooltip("Terrain side length in meters (square).")]
        public float terrainSize = 600f;
        [Tooltip("Heightmap samples per side (2^n + 1).")]
        public int heightmapResolution = 513;
        public int alphamapResolution = 512;
        [Tooltip("Total vertical range of the TerrainData, including the seabed.")]
        public float maxHeight = 140f;
        [Tooltip("Seabed depth. The terrain sits at y = -seaFloorDepth and the water at y = 0.")]
        public float seaFloorDepth = 20f;

        [Header("Coast")]
        [Range(0.2f, 0.48f)] public float islandRadius = 0.36f;
        public float coastWarpStrength = 0.07f;
        public float coastWarpFrequency = 3f;
        [Tooltip("Meters the seabed takes to reach its maximum depth.")]
        public float offshoreSlopeWidth = 45f;

        [Tooltip("Beach direction in degrees (0 = east, 90 = north, 270 = south).")]
        public float beachDirection = 270f;
        public float beachArc = 110f;
        public float beachWidth = 40f;
        public float beachHeight = 1.8f;

        [Tooltip("Center direction of the cliff coast (degrees).")]
        public float cliffDirection = 50f;
        public float cliffArc = 130f;
        public float cliffHeight = 22f;
        [Tooltip("Horizontal meters over which the cliff rises from the water.")]
        public float cliffWidth = 7f;

        [Header("Inland relief")]
        public float inlandBaseHeight = 6f;
        public float inlandRampWidth = 50f;
        public float detailAmplitude = 5f;
        public float detailFrequency = 6f;
        [Range(1, 8)] public int detailOctaves = 4;
        [Range(0, 4)] public int smoothingPasses = 1;
        public List<ReliefFeature> reliefFeatures = DefaultReliefFeatures();

        [Header("Flat areas (camp, clearings, plateau)")]
        public List<FlatArea> flatAreas = DefaultFlatAreas();

        [Header("Paths")]
        public List<PathDefinition> paths = DefaultPaths();

        [Header("Placeholder vegetation")]
        public Vector2 forestCenter = new Vector2(0.52f, 0.50f);
        public float forestRadius = 0.30f;
        [Tooltip("Candidate grid spacing (m). Controls maximum density.")]
        public float treeSpacing = 4.5f;
        public int maxTrees = 2500;
        public float treeSlopeLimit = 30f;
        public float minTreeHeightAboveSea = 4f;
        [Tooltip("Tree-free distance on each side of path edges (m).")]
        public float pathClearance = 2.5f;
        [Range(0f, 1f)] public float scatteredTreeDensity = 0.12f;
        public float clusterFrequency = 9f;
        public Vector2 treeScaleRange = new Vector2(0.8f, 1.3f);

        [Header("Points of interest")]
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

        [ContextMenu("Reset layout to defaults")]
        public void ResetLayoutToDefaults()
        {
            reliefFeatures = DefaultReliefFeatures();
            flatAreas = DefaultFlatAreas();
            paths = DefaultPaths();
            pointsOfInterest = DefaultPointsOfInterest();
        }

        static List<ReliefFeature> DefaultReliefFeatures() => new List<ReliefFeature>
        {
            new ReliefFeature { name = "Mount Cronos (landmark)", center = new Vector2(0.66f, 0.70f), radius = 0.14f, height = 66f, ridged = 0.55f },
            new ReliefFeature { name = "West Hill", center = new Vector2(0.22f, 0.45f), radius = 0.09f, height = 20f, ridged = 0.2f },
            new ReliefFeature { name = "Valley Ridge (hides the valley)", center = new Vector2(0.41f, 0.51f), radius = 0.07f, height = 15f, ridged = 0.3f },
            new ReliefFeature { name = "East Hills", center = new Vector2(0.77f, 0.45f), radius = 0.10f, height = 24f, ridged = 0.3f },
            new ReliefFeature { name = "Portal Plateau", center = new Vector2(0.42f, 0.76f), radius = 0.09f, height = 18f, ridged = 0.1f },
            new ReliefFeature { name = "Inner Valley", center = new Vector2(0.30f, 0.63f), radius = 0.10f, height = -4f, ridged = 0f },
        };

        static List<FlatArea> DefaultFlatAreas() => new List<FlatArea>
        {
            new FlatArea { name = "Starting Beach", center = new Vector2(0.50f, 0.19f), radius = 18f, falloff = 15f, treeClearing = true },
            new FlatArea { name = "Camp Clearing", center = new Vector2(0.42f, 0.29f), radius = 14f, falloff = 12f, treeClearing = true },
            new FlatArea { name = "Ginkgo Clearing", center = new Vector2(0.56f, 0.50f), radius = 10f, falloff = 10f, treeClearing = true },
            new FlatArea { name = "Valley Floor", center = new Vector2(0.30f, 0.63f), radius = 26f, falloff = 20f, treeClearing = true },
            new FlatArea { name = "Portal Platform", center = new Vector2(0.42f, 0.76f), radius = 15f, falloff = 22f, treeClearing = true },
        };

        static List<PathDefinition> DefaultPaths() => new List<PathDefinition>
        {
            new PathDefinition
            {
                name = "Main route (beach → portal)", width = 3.5f, falloff = 4f, carveDepth = 0.25f,
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
                name = "Alternate route: west coast", width = 2.5f, falloff = 3f, carveDepth = 0.2f,
                points = new List<Vector2>
                {
                    new Vector2(0.42f, 0.29f), new Vector2(0.33f, 0.31f), new Vector2(0.24f, 0.37f),
                    new Vector2(0.19f, 0.47f), new Vector2(0.22f, 0.56f), new Vector2(0.30f, 0.63f),
                }
            },
            new PathDefinition
            {
                name = "Alternate route: mountain trail", width = 2.5f, falloff = 3f, carveDepth = 0.2f,
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
            new PointOfInterestDefinition { id = "spawn_beach", displayName = "Shipwreck Beach", type = PoiType.Spawn, position = new Vector2(0.50f, 0.19f), radius = 4f },
            new PointOfInterestDefinition { id = "res_beach_wood", displayName = "Driftwood Logs", type = PoiType.Resource, position = new Vector2(0.56f, 0.20f), radius = 6f },
            new PointOfInterestDefinition { id = "res_beach_stone", displayName = "Shoreline Rocks", type = PoiType.Resource, position = new Vector2(0.42f, 0.19f), radius = 6f },
            new PointOfInterestDefinition { id = "res_fiber", displayName = "Fiber Thicket", type = PoiType.Resource, position = new Vector2(0.48f, 0.25f), radius = 6f },
            new PointOfInterestDefinition { id = "camp", displayName = "Camp Clearing", type = PoiType.BuildZone, position = new Vector2(0.42f, 0.29f), radius = 12f },
            new PointOfInterestDefinition { id = "forest_entrance", displayName = "Forest Entrance", type = PoiType.Waypoint, position = new Vector2(0.46f, 0.36f), radius = 5f },
            new PointOfInterestDefinition { id = "research_plant", displayName = "Ancient Ginkgo", type = PoiType.Research, position = new Vector2(0.56f, 0.50f), radius = 8f },
            new PointOfInterestDefinition { id = "valley_overlook", displayName = "Valley Overlook", type = PoiType.Viewpoint, position = new Vector2(0.42f, 0.58f), radius = 5f },
            new PointOfInterestDefinition { id = "dino_valley", displayName = "Herbivore Valley", type = PoiType.CreatureZone, position = new Vector2(0.30f, 0.63f), radius = 30f },
            new PointOfInterestDefinition { id = "mount_cronos", displayName = "Mount Cronos", type = PoiType.Landmark, position = new Vector2(0.66f, 0.70f), radius = 10f },
            new PointOfInterestDefinition { id = "portal", displayName = "Time Portal", type = PoiType.Portal, position = new Vector2(0.42f, 0.76f), radius = 8f },
        };
    }

    [Serializable]
    public class ReliefFeature
    {
        public string name;
        public Vector2 center;
        [Tooltip("Normalized radius.")] public float radius = 0.1f;
        [Tooltip("Meters. Negative = basin/valley.")] public float height = 10f;
        [Range(0f, 1f)] public float ridged;
    }

    [Serializable]
    public class FlatArea
    {
        public string name;
        public Vector2 center;
        [Tooltip("Meters.")] public float radius = 10f;
        [Tooltip("Meters.")] public float falloff = 10f;
        [Tooltip("If true, uses the generated height at the center.")] public bool autoHeight = true;
        [Tooltip("Height above sea (m) if autoHeight is false; offset if true.")] public float height;
        public bool treeClearing = true;
    }

    [Serializable]
    public class PathDefinition
    {
        public string name;
        public List<Vector2> points = new List<Vector2>();
        [Tooltip("Meters.")] public float width = 3f;
        [Tooltip("Meters of blend into the terrain.")] public float falloff = 4f;
        public float carveDepth = 0.2f;
        [Tooltip("Radius (m) of longitudinal height smoothing.")] public float smoothingRadius = 25f;
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
