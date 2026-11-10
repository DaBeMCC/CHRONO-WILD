using ChronoWild.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChronoWild.Story
{
    /// <summary>
    /// Runs the survival mission: the parts to recover, the journal entries found with them, the
    /// light beam that points at the next part and the countdown to the meteor impact.
    /// </summary>
    public class MissionDirector : MonoBehaviour
    {
        public static MissionDirector Instance { get; private set; }

        public Transform player;
        public MachinePart[] parts = new MachinePart[0];
        [Tooltip("Days left before the impact on the first day of the mission.")]
        public int impactDays = 15;
        [TextArea] public string finalEntry = "Tenemos todas las piezas. Con el núcleo en su sitio podemos reparar la máquina. Quizá volvamos a casa antes de que caiga el cielo.";
        public bool hudVisible = true;

        [Header("Voice")]
        [Tooltip("Day-one log read at the end of the intro.")]
        public AudioClip introVoice;
        public AudioClip finalVoice;
        [Range(0f, 1f)] public float voiceVolume = 1f;
        AudioSource voice;

        public int Recovered { get; private set; }
        public int DaysLeft
        {
            get
            {
                var clock = DayNightCycle.Instance;
                return Mathf.Max(0, impactDays - ((clock != null ? clock.day : 1) - 1));
            }
        }

        string message;
        float messageUntil;
        MachinePart nearby;
        GUIStyle hudStyle, messageStyle, promptStyle;

        void OnEnable() => Instance = this;

        void Start()
        {
            foreach (var part in parts)
                if (part != null) part.player = player;
            UpdateGuide();
        }

        /// <summary>Shows a journal line; with a voice clip it is read aloud and the text stays as its subtitle.</summary>
        public void ShowMessage(string text, float seconds = 9f, AudioClip clip = null)
        {
            if (clip != null)
            {
                if (voice == null)
                {
                    voice = gameObject.AddComponent<AudioSource>();
                    voice.playOnAwake = false;
                    voice.spatialBlend = 0f;
                }
                voice.Stop();
                voice.clip = clip;
                voice.volume = voiceVolume;
                voice.Play();
                seconds = Mathf.Max(seconds, clip.length + 1.5f);
            }
            message = text;
            messageUntil = Time.time + seconds;
        }

        void UpdateGuide()
        {
            // Only the nearest unrecovered part keeps its beam, so the island always says where to go next.
            MachinePart next = null;
            float best = float.MaxValue;
            foreach (var part in parts)
            {
                if (part == null || part.Collected) continue;
                float d = Vector3.Distance(part.transform.position, player.position);
                if (d < best) { best = d; next = part; }
            }
            foreach (var part in parts)
                if (part != null) part.SetGuided(part == next);
        }

        void Update()
        {
            nearby = null;
            foreach (var part in parts)
            {
                if (part == null || part.Collected) continue;
                if (Vector3.Distance(part.transform.position, player.position) <= part.pickupRadius)
                    nearby = part;
            }
            var keyboard = Keyboard.current;
            if (nearby != null && keyboard != null && keyboard.eKey.wasPressedThisFrame)
            {
                nearby.Collect();
                Recovered++;
                ShowMessage(nearby.journalEntry, 9f, nearby.voiceLine);
                if (Recovered == parts.Length)
                    Invoke(nameof(ShowFinal), Mathf.Max(9.5f, nearby.voiceLine != null ? nearby.voiceLine.length + 2f : 0f));
                UpdateGuide();
            }
            else if (Time.frameCount % 30 == 0)
            {
                UpdateGuide();
            }
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame)
                hudVisible = !hudVisible;
        }

        void ShowFinal() => ShowMessage(finalEntry, 12f, finalVoice);

        void OnGUI()
        {
            if (!hudVisible || IntroCinematic.Playing)
                return;
            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 20, fontStyle = FontStyle.Bold };
                messageStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 19, wordWrap = true };
                promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold };
            }
            int days = DaysLeft;
            string status = $"Piezas de la máquina {Recovered}/{parts.Length}      Impacto en {days} día{(days == 1 ? "" : "s")}";
            Shadowed(new Rect(0, 14, Screen.width, 30), status, hudStyle, days <= 3 ? new Color(1f, 0.45f, 0.35f) : new Color(1f, 0.92f, 0.78f));
            if (nearby != null)
                Shadowed(new Rect(0, Screen.height * 0.58f, Screen.width, 30), "[E]  Recuperar: " + nearby.displayName, promptStyle, Color.white);
            if (!string.IsNullOrEmpty(message) && Time.time < messageUntil)
            {
                float alpha = Mathf.Clamp01((messageUntil - Time.time) / 1.2f);
                float width = Mathf.Min(900f, Screen.width - 80f);
                Shadowed(new Rect((Screen.width - width) * 0.5f, Screen.height - 190, width, 150), message, messageStyle, new Color(1f, 0.95f, 0.85f, alpha));
            }
        }

        static void Shadowed(Rect rect, string text, GUIStyle style, Color color)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, color.a * 0.75f);
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), text, style);
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = old;
        }
    }
}
