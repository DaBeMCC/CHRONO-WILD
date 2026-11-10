using UnityEngine;
using UnityEngine.InputSystem;
using ChronoWild.Core;
using ChronoWild.Health;

namespace ChronoWild.Prototype
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PrototypePlayer : MonoBehaviour
    {
        [Header("Movement")]
        [Min(0f)] public float walkSpeed = 5f;
        [Min(0f)] public float sprintSpeed = 8.5f;
        [Min(0f)] public float mouseSensitivity = 0.12f;
        [Range(0f, 1f)] public float stunnedSpeedMultiplier = 0.5f;
        [Min(0f)] public float stunDuration = 0.6f;

        [Header("Camera")]
        public Transform cameraPivot;
        [Min(0f)] public float bobFrequency = 10f;
        [Min(0f)] public float bobAmplitude = 0.045f;
        public HitFlash hitFlash;

        CharacterController controller;
        Vector3 cameraRestPosition;
        float pitch;
        float verticalVelocity;
        float bobPhase;
        float stunnedUntil;
        float shakeUntil;
        float shakeStart;

        public bool IsMoving { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsStunned => Time.time < stunnedUntil;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (cameraPivot != null)
                cameraRestPosition = cameraPivot.localPosition;
        }

        void OnEnable()
        {
            if (GameStateController.Instance == null) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || cameraPivot == null) return;
            if (GameStateController.Instance != null && !GameStateController.Instance.CanControl) { IsMoving = false; IsRunning = false; return; }

            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
                transform.Rotate(0f, look.x, 0f);
                pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
            }

            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed) input.y++;
            if (keyboard.sKey.isPressed) input.y--;
            if (keyboard.dKey.isPressed) input.x++;
            if (keyboard.aKey.isPressed) input.x--;
            input = Vector2.ClampMagnitude(input, 1f);

            bool sprinting = keyboard.leftShiftKey.isPressed && input.y > 0f;
            IsMoving = input.sqrMagnitude > 0.01f; IsRunning = IsMoving && sprinting;
            float speed = sprinting ? sprintSpeed : walkSpeed;
            if (IsStunned) speed *= stunnedSpeedMultiplier;
            Vector3 horizontal = (transform.right * input.x + transform.forward * input.y) * speed;

            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            verticalVelocity += -20f * Time.deltaTime;
            controller.Move((horizontal + Vector3.up * verticalVelocity) * Time.deltaTime);

            float targetBob = input.sqrMagnitude > 0.01f && controller.isGrounded ? 1f : 0f;
            if (targetBob > 0f) bobPhase += Time.deltaTime * bobFrequency * (sprinting ? 1.3f : 1f);
            float bob = Mathf.Sin(bobPhase) * bobAmplitude * targetBob;
            Vector3 targetPosition = cameraRestPosition + Vector3.up * bob;
            cameraPivot.localPosition = Vector3.Lerp(cameraPivot.localPosition, targetPosition, 12f * Time.deltaTime);

            float shake = 0f;
            if (Time.time < shakeUntil)
            {
                float remaining = (shakeUntil - Time.time) / (shakeUntil - shakeStart);
                shake = Mathf.Sin(Time.time * 65f) * 4f * remaining;
            }
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, shake);
        }

        public void ReceiveHit()
        {
            stunnedUntil = Time.time + stunDuration;
            var health = GetComponent<PlayerHealth>();
            if (health != null) health.TakeDamage(PlayerHealth.AttackDamage);
            else { shakeStart = Time.time; shakeUntil = Time.time + 0.4f; if (hitFlash != null) hitFlash.Flash(); }
        }
    }
}
