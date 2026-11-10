using UnityEngine;
using ChronoWild.Health;

namespace ChronoWild.Prototype
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class DinoAI : MonoBehaviour
    {
        public enum DinoState { Patrol, Rest, Alert, Chase, HitRecovery }

        [Header("Target")]
        [Tooltip("Target used by detection and pursuit. Assign the scene player's Transform.")]
        public Transform target;
        [Tooltip("Optional: the prototype player (only in the DinoAI test scene).")]
        public PrototypePlayer player;
        public DinoState state = DinoState.Patrol;
        public Animator animator;

        [Header("Raptor presentation")]
        [SerializeField, Range(-180f, 180f)] float modelYawOffset = 180f;
        [SerializeField, Min(0f)] float rotationDegreesPerSecond = 360f;
        [SerializeField, Min(0.1f)] float chaseAnimationSpeed = 1.4f;
        [SerializeField, Min(0.1f)] float normalAnimationSpeed = 1f;
        [SerializeField, Min(0f)] float animationBlendSeconds = 0.15f;

        [Header("Movement")]
        [Min(0f)] public float patrolSpeed = 1.8f;
        [Min(0f)] public float chaseSpeed = 6.2f;
        public Vector2 patrolTravelSeconds = new Vector2(3f, 6f);
        public Vector2 patrolRestSeconds = new Vector2(3f, 6f);
        public Vector2 turnAngleDegrees = new Vector2(20f, 180f);

        [Header("Detection (Unity metres)")]
        [Min(0f)] public float mediumRange = 24f;
        [Min(0f)] public float closeRange = 12f;
        [Min(0f)] public float escapeRange = 10f;
        [Min(0f)] public float alertSeconds = 3f;
        [Min(0f)] public float escapeSeconds = 5f;
        [Min(0f)] public float hitRange = 1.4f;
        [Min(0f)] public float hitRecoverySeconds = 2f;

        [Header("Territory")]
        [Tooltip("The raptor patrols within this radius of where it starts and gives up a chase far beyond it (0 = anywhere).")]
        [Min(0f)] public float territoryRadius = 0f;
        [Tooltip("Ground lower than this world height is treated as water and avoided.")]
        public float waterLevel = float.NegativeInfinity;

        CharacterController controller;
        Vector3 patrolDirection;
        float stateElapsed;
        float stateDuration;
        float escapeElapsed;
        float verticalVelocity;
        string currentAnimation;
        PlayerHealth targetHealth;
        Vector3 home;

        public float StateElapsed => stateElapsed;
        public float EscapeElapsed => escapeElapsed;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            ApplyModelOrientation();
            if (target == null && player != null) target = player.transform;
            if (target != null) targetHealth = target.GetComponentInParent<PlayerHealth>();
            home = transform.position;
            patrolDirection = transform.forward;
            stateDuration = Random.Range(patrolTravelSeconds.x, patrolTravelSeconds.y);
        }

        void Update()
        {
            if (target == null) return;
            if (targetHealth == null) targetHealth = target.GetComponentInParent<PlayerHealth>();
            float dt = Time.deltaTime;
            stateElapsed += dt;
            Vector3 toPlayer = target.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

            // A dead player, or one far outside the territory, is left alone.
            bool targetGone = (targetHealth != null && targetHealth.IsDead) ||
                (territoryRadius > 0f && Flat(target.position - home).magnitude > territoryRadius * 1.6f);
            if (targetGone)
            {
                if (state == DinoState.Chase || state == DinoState.Alert || state == DinoState.HitRecovery)
                    Enter(DinoState.Rest);
                distance = float.MaxValue;
            }

            if (state == DinoState.Patrol || state == DinoState.Rest || state == DinoState.Alert)
            {
                if (distance <= closeRange)
                    Enter(DinoState.Chase);
                else if (distance <= mediumRange)
                {
                    if (state != DinoState.Alert) Enter(DinoState.Alert);
                    else if (stateElapsed >= alertSeconds) Enter(DinoState.Chase);
                }
                else if (state == DinoState.Alert)
                    Enter(DinoState.Rest);
            }

            if (state == DinoState.Chase || state == DinoState.HitRecovery)
            {
                escapeElapsed = distance > escapeRange ? escapeElapsed + dt : 0f;
                if (escapeElapsed >= escapeSeconds) Enter(DinoState.Rest);
            }

            Vector3 direction = Vector3.zero;
            float speed = 0f;
            switch (state)
            {
                case DinoState.Patrol:
                    // Wandering out of its territory or toward the water: head back home.
                    if ((territoryRadius > 0f && Flat(transform.position - home).magnitude > territoryRadius) ||
                        IsWater(transform.position + patrolDirection * 3f))
                        patrolDirection = Flat(home - transform.position).normalized;
                    direction = patrolDirection;
                    speed = patrolSpeed;
                    if (stateElapsed >= stateDuration) Enter(DinoState.Rest);
                    break;
                case DinoState.Rest:
                    if (stateElapsed >= stateDuration)
                    {
                        float turn = Random.Range(turnAngleDegrees.x, turnAngleDegrees.y);
                        if (Random.value < 0.5f) turn = -turn;
                        patrolDirection = Quaternion.Euler(0f, turn, 0f) * patrolDirection;
                        Enter(DinoState.Patrol);
                    }
                    break;
                case DinoState.Alert:
                    direction = toPlayer.normalized;
                    break;
                case DinoState.Chase:
                    if (distance <= hitRange)
                    {
                        if (!Physics.Linecast(transform.position + Vector3.up * 1.2f, target.position + Vector3.up * 1.2f, out var obstacle, ~0, QueryTriggerInteraction.Ignore) || obstacle.transform.IsChildOf(target) || obstacle.collider.transform == transform)
                        {
                            if (player != null) player.ReceiveHit();
                            else if (targetHealth != null) targetHealth.TakeDamage(PlayerHealth.AttackDamage);
                            Enter(DinoState.HitRecovery);
                        }
                    }
                    direction = toPlayer.normalized;
                    speed = chaseSpeed;
                    break;
                case DinoState.HitRecovery:
                    direction = toPlayer.normalized;
                    speed = patrolSpeed;
                    if (stateElapsed >= hitRecoverySeconds) Enter(DinoState.Chase);
                    break;
            }

            if (state == DinoState.Rest || state == DinoState.Alert)
            {
                direction = Vector3.zero;
                speed = 0f;
            }
            if (state == DinoState.HitRecovery) speed = patrolSpeed;
            // Never run into the sea, not even after the player.
            if (speed > 0f && IsWater(transform.position + direction * 2f))
                speed = 0f;

            string desiredAnimation = state == DinoState.Patrol ? "Walk" :
                state == DinoState.Chase ? "Run" : "Idle";
            if (state == DinoState.HitRecovery && stateElapsed < 0.75f)
                desiredAnimation = "Attack";
            PlayAnimation(desiredAnimation);
            if (animator != null)
                animator.speed = state == DinoState.Chase ? chaseAnimationSpeed : normalAnimationSpeed;

            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), rotationDegreesPerSecond * dt);
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += -20f * dt;
            CollisionFlags flags = controller.Move((direction * speed + Vector3.up * verticalVelocity) * dt);
            if (state == DinoState.Patrol && (flags & CollisionFlags.Sides) != 0)
                Enter(DinoState.Rest);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        bool IsWater(Vector3 position)
        {
            if (float.IsNegativeInfinity(waterLevel)) return false;
            var terrain = Terrain.activeTerrain;
            if (terrain == null) return false;
            return terrain.SampleHeight(position) + terrain.transform.position.y < waterLevel;
        }

        void Enter(DinoState next)
        {
            if (state == next) return;
            state = next;
            stateElapsed = 0f;
            if (next == DinoState.Patrol)
                stateDuration = Random.Range(patrolTravelSeconds.x, patrolTravelSeconds.y);
            else if (next == DinoState.Rest)
                stateDuration = Random.Range(patrolRestSeconds.x, patrolRestSeconds.y);
            if (next == DinoState.Chase && escapeElapsed >= escapeSeconds)
                escapeElapsed = 0f;
            if (next == DinoState.Rest) escapeElapsed = 0f;
            if (next == DinoState.HitRecovery) PlayAnimation("Attack", true);
        }

        void PlayAnimation(string name, bool restart = false)
        {
            if (animator == null || (!restart && currentAnimation == name)) return;
            animator.CrossFadeInFixedTime(name, restart ? 0.03f : animationBlendSeconds, 0, 0f);
            currentAnimation = name;
        }

        void ApplyModelOrientation()
        {
            // DinoAI's root +Z remains the movement direction; only the FBX visual is reversed.
            if (animator != null && animator.transform != transform)
                animator.transform.localRotation = Quaternion.Euler(0f, modelYawOffset, 0f);
        }

        void LateUpdate()
        {
            // Apply after Animator has evaluated the pose so imported root keys cannot undo it.
            ApplyModelOrientation();
        }

        void OnValidate()
        {
            if (animator != null) ApplyModelOrientation();
        }

        void OnDrawGizmos()
        {
            Vector3 origin = transform.position + Vector3.up * 2.2f;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(origin, origin + transform.right * 1.5f);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(origin, origin + transform.up * 1.5f);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(origin, origin + transform.forward * 1.5f);
            Gizmos.color = new Color(1f, 0.7f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, mediumRange);
            Gizmos.color = new Color(1f, 0f, 0f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, closeRange);
        }
    }
}
