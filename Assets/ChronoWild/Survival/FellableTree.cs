using System.Collections;
using UnityEngine;

namespace ChronoWild.Survival
{
    [DisallowMultipleComponent]
    public sealed class FellableTree : MonoBehaviour
    {
        const int RequiredStrikes = 4;
        int strikes;
        public bool Falling { get; private set; }
        public int Remaining => RequiredStrikes - strikes;

        /// <summary>Removes the tree at once (a saved game where it was already felled).</summary>
        public void MarkFelled()
        {
            Falling = true;
            strikes = RequiredStrikes;
            gameObject.SetActive(false);
        }

        public bool Strike(Vector3 direction, System.Action<Vector3, Vector3, float> onFelled, int power = 1)
        {
            if (Falling)
                return false;
            strikes = Mathf.Min(RequiredStrikes, strikes + Mathf.Max(1, power));
            if (strikes < RequiredStrikes)
                return true;

            Falling = true;
            var capsule = GetComponent<CapsuleCollider>();
            float trunkRadius = capsule != null ? capsule.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) : .48f;
            foreach (var collider in GetComponentsInChildren<Collider>())
                collider.enabled = false;
            StartCoroutine(Fall(direction, Mathf.Clamp(trunkRadius, .28f, .68f), onFelled));
            return true;
        }

        IEnumerator Fall(Vector3 direction, float radius, System.Action<Vector3, Vector3, float> onFelled)
        {
            Vector3 ground = transform.position;
            Vector3 pivot = ground + Vector3.up * .43f;
            Vector3 fall = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            if (fall.sqrMagnitude < .01f)
                fall = transform.forward;
            Vector3 axis = Vector3.Cross(Vector3.up, fall);
            Quaternion start = transform.rotation;
            const float duration = 2.85f;
            for (float elapsed = 0; elapsed < duration; elapsed += Time.deltaTime)
            {
                float t = elapsed / duration;
                float angle;
                if (t < .16f)
                {
                    float tension = t / .16f;
                    angle = 4f * tension + Mathf.Sin(tension * Mathf.PI * 2f) * 1.25f;
                }
                else if (t < .88f)
                {
                    float fallProgress = Mathf.InverseLerp(.16f, .88f, t);
                    angle = Mathf.Lerp(4f, 87f, fallProgress * fallProgress);
                }
                else
                {
                    float settle = Mathf.InverseLerp(.88f, 1f, t);
                    angle = 87f - Mathf.Sin(settle * Mathf.PI) * (1f - settle) * 4.25f;
                }
                ApplyTilt(angle);
                yield return null;
            }
            ApplyTilt(87f);

            void ApplyTilt(float degrees)
            {
                var tilt = Quaternion.AngleAxis(degrees, axis);
                transform.SetPositionAndRotation(pivot + tilt * (ground - pivot), tilt * start);
            }

            onFelled?.Invoke(ground, fall, radius);
            gameObject.SetActive(false);
        }
    }
}
