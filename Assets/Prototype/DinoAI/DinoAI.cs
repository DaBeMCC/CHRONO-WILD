using UnityEngine;

namespace ChronoWild.Prototype
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class DinoAI : MonoBehaviour
    {
        public enum DinoState { Patrol, Rest, Alert, Chase, HitRecovery }

        [Header("Target")]
        public PrototypePlayer player;
        public DinoState state = DinoState.Patrol;
        public Animator animator;

        [Header("Movement")]
        [Min(0f)] public float patrolSpeed = 1.8f;
        [Min(0f)] public float chaseSpeed = 6.2f;
        public Vector2 patrolTravelSeconds = new Vector2(3f, 6f);
        public Vector2 patrolRestSeconds = new Vector2(3f, 6f);
        public Vector2 turnAngleDegrees = new Vector2(20f, 180f);

        [Header("Detection (Unity metres)")]
        [Min(0f)] public float mediumRange = 12f;
        [Min(0f)] public float closeRange = 4f;
        [Min(0f)] public float escapeRange = 20f;
        [Min(0f)] public float alertSeconds = 3f;
        [Min(0f)] public float escapeSeconds = 10f;
        [Min(0f)] public float hitRange = 1.6f;
        [Min(0f)] public float hitRecoverySeconds = 2f;

        CharacterController controller;
        Vector3 patrolDirection;
        float stateElapsed;
        float stateDuration;
        float escapeElapsed;
        float verticalVelocity;
        string currentAnimation;

        public float StateElapsed => stateElapsed;
        public float EscapeElapsed => escapeElapsed;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            patrolDirection = transform.forward;
            stateDuration = Random.Range(patrolTravelSeconds.x, patrolTravelSeconds.y);
        }

        void Update()
        {
            if (player == null) return;
            float dt = Time.deltaTime;
            stateElapsed += dt;
            Vector3 toPlayer = player.transform.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;

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
                        player.ReceiveHit();
                        Enter(DinoState.HitRecovery);
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

            string desiredAnimation = state == DinoState.Patrol ? "Walk" :
                state == DinoState.Chase ? "Run" : "Idle";
            if (state == DinoState.HitRecovery && stateElapsed < 0.75f)
                desiredAnimation = "Attack";
            PlayAnimation(desiredAnimation);

            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), 360f * dt);
            if (controller.isGrounded && verticalVelocity < 0f) verticalVelocity = -2f;
            verticalVelocity += -20f * dt;
            CollisionFlags flags = controller.Move((direction * speed + Vector3.up * verticalVelocity) * dt);
            if (state == DinoState.Patrol && (flags & CollisionFlags.Sides) != 0)
                Enter(DinoState.Rest);
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
            animator.CrossFadeInFixedTime(name, restart ? 0.03f : 0.15f, 0, 0f);
            currentAnimation = name;
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
