using System.Collections;
using UnityEngine;

namespace ChronoWild.Survival
{
    /// <summary>
    /// Small Jurassic herbivore: grazes, wanders near its home, bolts away from the player and
    /// drops raw meat when killed. The model's parts (Body, Head, Tail, LegL, LegR) are animated
    /// procedurally, so it needs no rig.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Dryosaur : MonoBehaviour
    {
        enum Mode { Graze, Walk, Flee, Dead }

        public float walkSpeed = 1.3f;
        public float fleeSpeed = 6.4f;
        [Tooltip("Starts fleeing when the player comes this close (more if the player runs).")]
        public float fleeRadius = 11f;
        public float calmRadius = 26f;
        public float homeRadius = 32f;
        public int health = 3;
        public int meatPortions = 2;
        public float waterLevel = 0.6f;
        [HideInInspector] public Transform player;

        public bool IsDead => mode == Mode.Dead;
        public event System.Action<Dryosaur> Died;

        Transform body, head, tail, legL, legR;
        Quaternion headRest, tailRest, legLRest, legRRest;
        Mode mode = Mode.Graze;
        Vector3 heading, home;
        float timer, stride, speed, grazeBlend;
        Terrain terrain;

        void Awake()
        {
            home = transform.position;
            heading = transform.forward;
            terrain = Terrain.activeTerrain;
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                if (t.name.StartsWith("Body")) body = t;
                else if (t.name.StartsWith("Head")) head = t;
                else if (t.name.StartsWith("Tail")) tail = t;
                else if (t.name.StartsWith("LegL")) legL = t;
                else if (t.name.StartsWith("LegR")) legR = t;
            }
            if (head) headRest = head.localRotation;
            if (tail) tailRest = tail.localRotation;
            if (legL) legLRest = legL.localRotation;
            if (legR) legRRest = legR.localRotation;
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.center = new Vector3(0, .62f, .05f);
            capsule.radius = .26f;
            capsule.height = 1.5f;
            timer = Random.Range(2f, 6f);
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void Update()
        {
            if (mode == Mode.Dead)
                return;
            float dt = Time.deltaTime;
            float distance = player ? Flat(transform.position - player.position).magnitude : float.MaxValue;
            bool playerRunning = player && player.TryGetComponent<Player.DebugFirstPersonController>(out var c) && c.IsRunning;
            float alarm = fleeRadius * (playerRunning ? 1.6f : 1f);

            if (mode != Mode.Flee && distance < alarm)
                StartFleeing(player.position);
            else if (mode == Mode.Flee && distance > calmRadius)
            {
                mode = Mode.Walk;
                timer = Random.Range(3f, 6f);
            }

            timer -= dt;
            switch (mode)
            {
                case Mode.Graze:
                    speed = Mathf.MoveTowards(speed, 0, dt * 4);
                    if (timer <= 0) { mode = Mode.Walk; timer = Random.Range(3f, 7f); heading = Wander(); }
                    break;
                case Mode.Walk:
                    speed = Mathf.MoveTowards(speed, walkSpeed, dt * 3);
                    if (timer <= 0) { mode = Mode.Graze; timer = Random.Range(3f, 8f); }
                    break;
                case Mode.Flee:
                    speed = Mathf.MoveTowards(speed, fleeSpeed, dt * 14);
                    if (player && timer <= 0)
                    {
                        // Zig-zag a little so it is not trivial to run down.
                        heading = Quaternion.Euler(0, Random.Range(-35f, 35f), 0) * Flat(transform.position - player.position).normalized;
                        timer = Random.Range(.6f, 1.4f);
                    }
                    break;
            }
            Move(dt);
            Animate(dt);
        }

        void StartFleeing(Vector3 from)
        {
            mode = Mode.Flee;
            heading = Flat(transform.position - from).normalized;
            timer = .8f;
        }

        Vector3 Wander()
        {
            Vector3 toHome = Flat(home - transform.position);
            if (toHome.magnitude > homeRadius)
                return toHome.normalized;
            return Quaternion.Euler(0, Random.Range(-140f, 140f), 0) * transform.forward;
        }

        void Move(float dt)
        {
            if (speed < .01f)
            {
                Ground();
                return;
            }
            Vector3 next = transform.position + heading * speed * dt;
            bool blocked = Physics.Raycast(transform.position + Vector3.up * .45f, heading, 1.1f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool wet = terrain && terrain.SampleHeight(next + heading * 1.5f) + terrain.transform.position.y < waterLevel + .15f;
            if (blocked || wet)
            {
                heading = Quaternion.Euler(0, Random.value < .5f ? 110f : -110f, 0) * heading;
                return;
            }
            transform.position = next;
            Ground();
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(heading),
                (mode == Mode.Flee ? 420f : 140f) * dt);
        }

        void Ground()
        {
            if (!terrain)
                return;
            var p = transform.position;
            p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
            transform.position = p;
        }

        void Animate(float dt)
        {
            float pace = speed / Mathf.Max(.01f, fleeSpeed);
            stride += dt * (2.2f + speed * 2.1f);
            float swing = Mathf.Lerp(14f, 42f, pace) * Mathf.Clamp01(speed / .3f);
            if (legL) legL.localRotation = legLRest * Quaternion.Euler(Mathf.Sin(stride) * swing, 0, 0);
            if (legR) legR.localRotation = legRRest * Quaternion.Euler(-Mathf.Sin(stride) * swing, 0, 0);
            grazeBlend = Mathf.MoveTowards(grazeBlend, mode == Mode.Graze ? 1 : 0, dt * 1.5f);
            if (head)
            {
                float peck = mode == Mode.Graze ? Mathf.Max(0, Mathf.Sin(Time.time * 2.3f + home.x)) * 12f : 0;
                head.localRotation = headRest * Quaternion.Euler(grazeBlend * 48f + peck + Mathf.Sin(stride * 2) * 4f * pace, 0, 0);
            }
            if (tail)
                tail.localRotation = tailRest * Quaternion.Euler(-6f * pace, Mathf.Sin(Time.time * 1.7f + stride * .5f) * (10f - 6f * pace), 0);
            if (body)
                body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(stride)) * .035f * pace, 0);
        }

        /// <summary>A weapon hit: hurts it and sends it running away from the attacker.</summary>
        public void TakeHit(int damage, Vector3 from)
        {
            if (mode == Mode.Dead)
                return;
            health -= damage;
            if (health <= 0)
            {
                StartCoroutine(Die());
                return;
            }
            StartFleeing(from);
            speed = fleeSpeed;
        }

        IEnumerator Die()
        {
            mode = Mode.Dead;
            GetComponent<Collider>().enabled = false;
            Quaternion start = transform.rotation;
            Quaternion end = start * Quaternion.Euler(0, 0, 84f);
            Vector3 low = transform.position;
            for (float t = 0; t < 1; t += Time.deltaTime / .55f)
            {
                transform.rotation = Quaternion.Slerp(start, end, t * t);
                transform.position = low + Vector3.up * (.18f * Mathf.Sin(t * Mathf.PI) * (1 - t));
                yield return null;
            }
            transform.rotation = end;
            Carcass.Drop(transform.position + Vector3.up * .1f, meatPortions);
            Died?.Invoke(this);
            yield return new WaitForSeconds(25f);
            Destroy(gameObject);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0;
            return v;
        }
    }
}
