using UnityEngine;

namespace ChronoWild.Story
{
    /// <summary>
    /// A piece of the time machine (or a supply) lost in the crash. It glows through the GuidingGlow
    /// shader graph; the mission director decides whether its light beam is shown.
    /// </summary>
    public class MachinePart : MonoBehaviour
    {
        public string displayName = "Power Cell";
        [TextArea] public string journalEntry = "";
        [Tooltip("The protagonist reading the journal entry aloud (optional).")]
        public AudioClip voiceLine;
        public Renderer[] glowRenderers = new Renderer[0];
        public Light glowLight;
        public GameObject beam;
        public ParticleSystem sparks;
        public float pickupRadius = 2.6f;
        [Tooltip("Within this distance the beam is gone, so the last stretch has to be searched (m).")]
        public float beamHideDistance = 20f;
        [Tooltip("Beyond this distance the beam is at full strength (m).")]
        public float beamFullDistance = 45f;
        [HideInInspector] public Transform player;

        public bool Collected { get; private set; }

        static readonly int ActivationId = Shader.PropertyToID("_Activation");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        MaterialPropertyBlock beamBlock;
        Renderer beamRenderer;
        bool guided;
        MaterialPropertyBlock block;
        float fade = 1f;
        float baseLight;
        Vector3 restPosition;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            beamBlock = new MaterialPropertyBlock();
            beamRenderer = beam != null ? beam.GetComponent<Renderer>() : null;
            baseLight = glowLight != null ? glowLight.intensity : 0f;
            restPosition = transform.position;
        }

        public void SetGuided(bool guided)
        {
            this.guided = guided && !Collected;
            UpdateBeam();
        }

        void UpdateBeam()
        {
            if (beam == null)
                return;
            float fade = 1f;
            if (player != null)
                fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(beamHideDistance, beamFullDistance, Vector3.Distance(player.position, transform.position)));
            bool show = guided && fade > 0.01f;
            if (beam.activeSelf != show)
                beam.SetActive(show);
            if (show && beamRenderer != null)
            {
                beamRenderer.GetPropertyBlock(beamBlock);
                beamBlock.SetFloat(IntensityId, 0.9f * fade);
                beamRenderer.SetPropertyBlock(beamBlock);
            }
        }

        public void Collect()
        {
            if (Collected)
                return;
            Collected = true;
            if (beam != null)
                beam.SetActive(false);
            if (sparks != null)
                sparks.Stop();
        }

        void Update()
        {
            // Once recovered its last light dies out and it is stowed.
            UpdateBeam();
            fade = Mathf.MoveTowards(fade, Collected ? 0f : 1f, Time.deltaTime * 1.4f);
            foreach (var r in glowRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(ActivationId, fade);
                r.SetPropertyBlock(block);
            }
            if (glowLight != null)
                glowLight.intensity = baseLight * fade * (0.8f + 0.2f * Mathf.Sin(Time.time * 2.2f));
            if (Collected && fade <= 0f)
                gameObject.SetActive(false);
        }
    }
}
