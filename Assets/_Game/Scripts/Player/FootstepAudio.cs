using ChronoWild.World;
using UnityEngine;

namespace ChronoWild.Player
{
    public enum GroundSurface { Dirt, Leaves, Grass, Moss, Sand, Rock, Mud, Water }

    /// <summary>
    /// Plays footsteps that match what is underfoot: reads the terrain painting and the water level
    /// at the player's feet, and adds a wet squelch while the ground is soaked by rain.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FootstepAudio : MonoBehaviour
    {
        [System.Serializable]
        public class SurfaceClips
        {
            public GroundSurface surface;
            public AudioClip[] clips;
            [Range(0f, 1f)] public float volume = 0.6f;
        }

        public Terrain terrain;
        public SurfaceClips[] surfaces = new SurfaceClips[0];
        [Tooltip("Squelch layered on top when the ground is wet.")]
        public AudioClip[] wetLayer = new AudioClip[0];
        public float walkStride = 1.65f;
        public float runStride = 2.4f;
        public float swampLevel = 1.4f;
        public Vector2 swampCenter = new Vector2(462f, 215f);
        public Vector2 swampRadii = new Vector2(54f, 74f);

        /// <summary>Steps played so far and the last surface, for diagnostics.</summary>
        public int StepCount { get; private set; }
        public GroundSurface LastSurface { get; private set; }

        CharacterController controller;
        AudioSource source;
        GroundSurface[] layerSurface;
        Vector3 lastPosition;
        float distance, airTime;
        int lastClip = -1;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            var layers = terrain.terrainData.terrainLayers;
            layerSurface = new GroundSurface[layers.Length];
            for (int i = 0; i < layers.Length; i++)
                layerSurface[i] = Classify(layers[i] != null ? layers[i].name : "");
            lastPosition = transform.position;
        }

        static GroundSurface Classify(string name)
        {
            if (name.Contains("Sand") || name.Contains("Shell")) return GroundSurface.Sand;
            if (name.Contains("Rock")) return GroundSurface.Rock;
            if (name.Contains("Mud") || name.Contains("Peat") || name.Contains("Muck")) return GroundSurface.Mud;
            if (name.Contains("Moor")) return GroundSurface.Grass;
            if (name.Contains("Litter")) return GroundSurface.Leaves;
            if (name.Contains("Moss")) return GroundSurface.Moss;
            return GroundSurface.Dirt;
        }

        /// <summary>Water depth at a point, from the sea and the swamp level.</summary>
        float WaterDepth(Vector3 p, float ground)
        {
            if (ground < 0.02f)
                return -ground + 0.05f;
            float dx = (p.x - swampCenter.x) / swampRadii.x, dz = (p.z - swampCenter.y) / swampRadii.y;
            return dx * dx + dz * dz < 0.8f && ground < swampLevel ? swampLevel - ground : 0f;
        }

        /// <summary>What a step at this point lands on.</summary>
        public GroundSurface SurfaceAt(Vector3 p)
        {
            float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
            if (WaterDepth(p, ground) > 0.04f)
                return GroundSurface.Water;
            var data = terrain.terrainData;
            Vector3 origin = terrain.transform.position;
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x - origin.x) / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((p.z - origin.z) / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            var weights = data.GetAlphamaps(x, z, 1, 1);
            int best = 0;
            for (int l = 1; l < layerSurface.Length; l++)
                if (weights[0, 0, l] > weights[0, 0, best]) best = l;
            return layerSurface[best];
        }

        void Update()
        {
            Vector3 position = transform.position;
            Vector3 moved = position - lastPosition;
            lastPosition = position;
            moved.y = 0f;

            if (!controller.isGrounded)
            {
                airTime += Time.deltaTime;
                return;
            }
            if (airTime > 0.25f)
                PlayStep(1.25f);
            airTime = 0f;

            distance += moved.magnitude;
            float speed = moved.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            bool running = speed > 5.2f;
            if (distance >= (running ? runStride : walkStride))
            {
                distance = 0f;
                PlayStep(running ? 1f : 0.72f);
            }
        }

        void PlayStep(float loudness)
        {
            LastSurface = SurfaceAt(transform.position);
            StepCount++;
            SurfaceClips set = null;
            foreach (var s in surfaces)
                if (s.surface == LastSurface) { set = s; break; }
            if (set == null || set.clips == null || set.clips.Length == 0)
                return;
            // Never the same recording twice in a row: repetition is what makes footsteps feel fake.
            int index = Random.Range(0, set.clips.Length);
            if (index == lastClip && set.clips.Length > 1)
                index = (index + 1) % set.clips.Length;
            lastClip = index;
            source.pitch = Random.Range(0.92f, 1.08f);
            source.PlayOneShot(set.clips[index], set.volume * loudness);

            var weather = WeatherSystem.Instance;
            float wet = weather != null ? weather.Wetness : 0f;
            if (wet > 0.05f && LastSurface != GroundSurface.Water && wetLayer.Length > 0)
                source.PlayOneShot(wetLayer[Random.Range(0, wetLayer.Length)], wet * loudness * (LastSurface == GroundSurface.Rock ? 0.25f : 0.55f));
        }
    }
}
