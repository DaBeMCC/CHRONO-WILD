using UnityEngine;

namespace ChronoWild.Prototype
{
    public sealed class HitFlash : MonoBehaviour
    {
        public UnityEngine.UI.Image overlay;
        [Min(0.01f)] public float duration = 0.55f;
        [Range(0f, 1f)] public float peakAlpha = 0.5f;

        float flashStart = float.NegativeInfinity;

        void Update()
        {
            if (overlay == null) return;
            float remaining = 1f - (Time.time - flashStart) / duration;
            Color color = Color.red;
            color.a = Mathf.Clamp01(remaining) * peakAlpha;
            overlay.color = color;
        }

        public void Flash() => flashStart = Time.time;
    }
}
