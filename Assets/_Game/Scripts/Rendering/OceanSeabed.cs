using UnityEngine;

namespace ChronoWild.Rendering
{
    /// <summary>
    /// Feeds the coastal ocean shader a baked seabed height map (meters, relative to sea level)
    /// so waves can shoal, turn toward the beach and break where the water gets shallow.
    /// </summary>
    [ExecuteAlways]
    public class OceanSeabed : MonoBehaviour
    {
        [Tooltip("Single-channel texture with seabed height in meters (negative = under water).")]
        public Texture2D seabed;
        [Tooltip("World XZ rectangle covered by the texture: x, z, width, depth.")]
        public Vector4 worldRect = new Vector4(0f, 0f, 600f, 600f);
        [Tooltip("Depth assumed outside the texture (open sea).")]
        public float openSeaDepth = 25f;

        static readonly int SeabedTex = Shader.PropertyToID("_ChronoSeabedTex");
        static readonly int SeabedRect = Shader.PropertyToID("_ChronoSeabedRect");
        static readonly int OpenDepth = Shader.PropertyToID("_ChronoSeabedOpenDepth");

        void OnEnable() => Push();
        void OnValidate() => Push();

        void Update()
        {
            // Cheap: keeps the globals alive after domain reloads and scene loads.
            Push();
        }

        public void Push()
        {
            if (seabed == null)
                return;
            Shader.SetGlobalTexture(SeabedTex, seabed);
            Shader.SetGlobalVector(SeabedRect, worldRect);
            Shader.SetGlobalFloat(OpenDepth, openSeaDepth);
        }
    }
}
