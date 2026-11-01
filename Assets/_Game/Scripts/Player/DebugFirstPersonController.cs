using UnityEngine;
using UnityEngine.InputSystem;

namespace ChronoWild.Player
{
    /// <summary>
    /// Minimal first-person controller for validating terrain scale and travel routes.
    /// Not the final game controller: no stamina, no interaction, no swimming.
    /// Keys: WASD move, Shift run, Space jump, T resets the route timer,
    /// Esc releases the cursor.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class DebugFirstPersonController : MonoBehaviour
    {
        public float walkSpeed = 4f;
        public float runSpeed = 7f;
        public float jumpHeight = 1.1f;
        public float gravity = -20f;
        public float lookSensitivity = 0.12f;
        public Transform cameraPivot;

        public float DistanceTravelled { get; private set; }
        public float TimerSeconds { get; private set; }
        public bool IsRunning { get; private set; }
        public float GroundSlope { get; private set; }

        CharacterController controller;
        float pitch;
        float verticalVelocity;
        Vector3 lastPosition;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (cameraPivot == null && Camera.main != null)
                cameraPivot = Camera.main.transform;
        }

        void OnEnable()
        {
            lastPosition = transform.position;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 look = mouse.delta.ReadValue() * lookSensitivity;
                transform.Rotate(0f, look.x, 0f);
                pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
                if (cameraPivot != null)
                    cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;
            input = Vector2.ClampMagnitude(input, 1f);

            IsRunning = keyboard.leftShiftKey.isPressed && input.y > 0f;
            float speed = IsRunning ? runSpeed : walkSpeed;
            Vector3 move = (transform.right * input.x + transform.forward * input.y) * speed;

            if (controller.isGrounded)
            {
                verticalVelocity = -2f;
                if (keyboard.spaceKey.wasPressedThisFrame)
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            verticalVelocity += gravity * Time.deltaTime;
            move.y = verticalVelocity;

            controller.Move(move * Time.deltaTime);

            if (keyboard.tKey.wasPressedThisFrame)
            {
                DistanceTravelled = 0f;
                TimerSeconds = 0f;
            }
            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;
            DistanceTravelled += delta.magnitude;
            TimerSeconds += Time.deltaTime;
            lastPosition = transform.position;
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            GroundSlope = Vector3.Angle(hit.normal, Vector3.up);
        }
    }
}
