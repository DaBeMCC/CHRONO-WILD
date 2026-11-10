using UnityEngine;
namespace ChronoWild.Health
{
    // The camera is below PrototypePlayer's bob/pitch pivot, so shake never competes with its motion.
    public sealed class CameraShake : MonoBehaviour
    {
        public PlayerHealth health;
        Vector3 rest;
        float until;
        void Awake() => rest = transform.localPosition;
        void OnEnable() { if (health) health.Damaged += Shake; }
        void OnDisable() { if (health) health.Damaged -= Shake; transform.localPosition = rest; }
        void Shake() => until = Time.unscaledTime + 0.35f;
        void LateUpdate()
        {
            float strength = Mathf.Clamp01((until - Time.unscaledTime) / 0.35f);
            transform.localPosition = rest + Random.insideUnitSphere * (0.045f * strength);
        }
    }
}
