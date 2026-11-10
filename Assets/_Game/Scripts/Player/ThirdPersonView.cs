using UnityEngine;
using UnityEngine.InputSystem;

namespace ChronoWild.Player
{
    /// <summary>
    /// Optional over-the-shoulder view. The game always starts in first person; V switches.
    /// The same camera is used in both views: its rotation still comes from the first-person
    /// controller's mouse look, only its position moves back behind the shoulder, pulled in when
    /// trees or rocks get between the camera and the player.
    /// Runs after other camera scripts (head bob, hit shake) and offsets whatever they set.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class ThirdPersonView : MonoBehaviour
    {
        public DebugFirstPersonController controller;
        public CharacterAvatar avatar;
        public float distance = 3.2f;
        public Vector2 shoulder = new Vector2(0.45f, 0.15f);
        public float collisionRadius = 0.25f;
        public float minDistance = 0.5f;
        public Key toggleKey = Key.V;

        public bool ThirdPerson { get; private set; }

        Transform cam;
        Vector3 eyeLocal;
        Component shake;
        float currentDistance;
        readonly RaycastHit[] hits = new RaycastHit[16];

        void Awake()
        {
            if (controller == null) controller = GetComponent<DebugFirstPersonController>();
            if (avatar == null) avatar = GetComponent<CharacterAvatar>();
            cam = controller != null ? controller.cameraPivot : null;
            if (cam == null) { enabled = false; return; }
            eyeLocal = cam.localPosition;
            shake = cam.GetComponent("CameraShake");
        }

        void Start() => SetThirdPerson(false);   // first person is always the default

        public void SetThirdPerson(bool on)
        {
            ThirdPerson = on;
            if (avatar != null) avatar.FirstPerson = !on;
            currentDistance = minDistance;
            if (!on && cam != null) cam.localPosition = eyeLocal;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame && controller != null && controller.enabled)
                SetThirdPerson(!ThirdPerson);
        }

        void LateUpdate()
        {
            if (!ThirdPerson || cam == null)
                return;
            // Without a shake script nothing resets the camera each frame, so start from the eye.
            Vector3 baseLocal = shake != null && ((Behaviour)shake).enabled ? cam.localPosition : eyeLocal;
            Vector3 pivot = cam.parent.TransformPoint(baseLocal);
            Vector3 offset = cam.rotation * new Vector3(shoulder.x, shoulder.y, -distance);
            float wanted = offset.magnitude;
            Vector3 dir = offset / wanted;

            float allowed = wanted;
            int n = Physics.SphereCastNonAlloc(pivot, collisionRadius, dir, hits, wanted, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform)) continue;   // our own capsule
                if (hits[i].distance > 0f) allowed = Mathf.Min(allowed, hits[i].distance);
            }
            allowed = Mathf.Max(allowed, minDistance);
            // Pull in at once when blocked, ease back out when clear.
            currentDistance = allowed < currentDistance ? allowed : Mathf.MoveTowards(currentDistance, allowed, 6f * Time.deltaTime);
            cam.position = pivot + dir * currentDistance;
        }
    }
}
