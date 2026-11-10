using UnityEngine;

namespace ChronoWild.Player
{
    /// <summary>
    /// The time traveler's body: drives its animator from the character controller (speed and
    /// whether it is airborne) and turns it toward where it walks. In first person the body only
    /// casts its shadow, so the player sees their own shadow without the model blocking the view.
    /// </summary>
    public class CharacterAvatar : MonoBehaviour
    {
        public Animator animator;
        public Transform model;
        public CharacterController body;
        [Tooltip("Seconds in the air before the jump pose plays (ignores small bumps).")]
        public float airborneDelay = 0.18f;
        public float turnSpeed = 720f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");
        Renderer[] renderers = new Renderer[0];
        bool firstPerson = true;
        float airTime;
        Vector3 lastPosition;

        public bool FirstPerson
        {
            get => firstPerson;
            set
            {
                firstPerson = value;
                var mode = value ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
                foreach (var r in renderers)
                    if (r) r.shadowCastingMode = mode;
            }
        }

        void Awake()
        {
            if (body == null) body = GetComponent<CharacterController>();
            if (model != null) renderers = model.GetComponentsInChildren<Renderer>(true);
            FirstPerson = true;
            lastPosition = transform.position;
        }

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 velocity = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;
            velocity.y = 0f;
            float speed = velocity.magnitude;
            if (speed > 20f) speed = 0f;   // a teleport, not a run

            bool grounded = body == null || body.isGrounded ||
                            Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, 0.45f, ~0, QueryTriggerInteraction.Ignore);
            airTime = grounded ? 0f : airTime + dt;

            if (animator != null)
            {
                animator.SetFloat(SpeedId, speed, 0.12f, dt);
                animator.SetBool(GroundedId, airTime < airborneDelay);
            }

            // Seen from behind, the body faces the way it moves; at rest (and in first person) it faces the view.
            if (model != null)
            {
                Quaternion target = !firstPerson && speed > 0.6f
                    ? Quaternion.LookRotation(velocity.normalized, Vector3.up)
                    : transform.rotation;
                model.rotation = Quaternion.RotateTowards(model.rotation, target, turnSpeed * dt);
            }
        }
    }
}
