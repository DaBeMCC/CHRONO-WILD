using System.Text;
using ChronoWild.Player;
using ChronoWild.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;

namespace ChronoWild.Diagnostics
{
    /// <summary>
    /// Debug overlay: average/minimum FPS, memory, position, nearest POI and route timer.
    /// F1 hides it. Meant for measuring on the test machine, not for the final game.
    /// The text is rebuilt four times per second into a cached GUIContent: rebuilding it in every
    /// OnGUI call allocated ~31 KB per frame (boxing in AppendFormat + ToString + IMGUI temps).
    /// Hidden by default in release builds.
    /// </summary>
    public class PerformanceOverlay : MonoBehaviour
    {
        [SerializeField] DebugFirstPersonController player;
        [SerializeField] float sampleWindow = 1f;

        public bool HiddenByInterface { get; set; }

        bool visible = Debug.isDebugBuild;
        const float TextRefresh = .25f;
        float textAt = -1f;
        readonly GUIContent content = new GUIContent();
        Rect box = new Rect(10, 10, 430, 160);
        float accumulated;
        int frames;
        float worstFrame;
        float avgFps;
        float minFps;
        PointOfInterest[] pois;
        readonly StringBuilder text = new StringBuilder(512);
        GUIStyle style;
        float maxMouse;

        void Start()
        {
            pois = FindObjectsByType<PointOfInterest>();
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                visible = !visible;

            float dt = Time.unscaledDeltaTime;
            accumulated += dt;
            frames++;
            worstFrame = Mathf.Max(worstFrame, dt);
            if (accumulated >= sampleWindow)
            {
                avgFps = frames / accumulated;
                minFps = worstFrame > 0f ? 1f / worstFrame : 0f;
                accumulated = 0f;
                frames = 0;
                worstFrame = 0f;
            }
        }

        void OnGUI()
        {
            if (!visible || HiddenByInterface || Time.timeScale == 0f)
                return;
            if (style == null)
                style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = false };
            // Layout and repaint events both call OnGUI; only rebuild the text a few times per second.
            if (Event.current.type == EventType.Repaint && Time.unscaledTime >= textAt)
            {
                textAt = Time.unscaledTime + TextRefresh;
                RebuildText();
            }
            if (Event.current.type == EventType.Repaint)
                style.Draw(box, content, false, false, false, false);
        }

        void RebuildText()
        {
            text.Clear();
            text.AppendFormat("FPS {0:0} (min {1:0})  {2:0.0} ms\n", avgFps, minFps, avgFps > 0 ? 1000f / avgFps : 0f);
            text.AppendFormat("Memory: {0:0} MB allocated / {1:0} MB reserved\n",
                Profiler.GetTotalAllocatedMemoryLong() / 1048576f, Profiler.GetTotalReservedMemoryLong() / 1048576f);
            text.AppendFormat("Resolution: {0}x{1}  Quality: {2}\n", Screen.width, Screen.height, QualitySettings.names[QualitySettings.GetQualityLevel()]);

            var clock = DayNightCycle.Instance;
            if (clock != null)
                text.AppendFormat("Time: {0}  (Day {1})\n", clock.TimeText, clock.day);

            if (player != null)
            {
                Vector3 p = player.transform.position;
                text.AppendFormat("Pos: {0:0} {1:0.0} {2:0}  Slope: {3:0}°\n", p.x, p.y, p.z, player.GroundSlope);
                text.AppendFormat("Route: {0:0} m in {1:0.0} s ({2})  [T resets]\n",
                    player.DistanceTravelled, player.TimerSeconds, player.IsRunning ? "running" : "walking");
                maxMouse = Mathf.Max(maxMouse * 0.97f, player.LastMouseDelta.magnitude);
                text.AppendFormat("Mouse: {0:0.0}  Cursor: {1}  Focus: {2}\n", maxMouse, Cursor.lockState, Application.isFocused ? "yes" : "no");
                AppendNearestPoi(p);
            }

            content.text = text.ToString();
        }

        void AppendNearestPoi(Vector3 position)
        {
            if (pois == null || pois.Length == 0)
                return;
            PointOfInterest nearest = null;
            float best = float.MaxValue;
            foreach (var poi in pois)
            {
                if (poi == null) continue;
                float d = Vector3.Distance(position, poi.transform.position);
                if (d < best)
                {
                    best = d;
                    nearest = poi;
                }
            }
            if (nearest != null)
                text.AppendFormat("Nearest POI: {0} at {1:0} m\n", nearest.displayName, best);
        }
    }
}
