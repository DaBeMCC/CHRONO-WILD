using ChronoWild.Player;
using ChronoWild.Story;
using UnityEngine;

namespace ChronoWild.Core
{
    /// <summary>
    /// Connects the game states (playing, paused, dead) to the island's first-person controller:
    /// the controller only runs while playing and outside the intro, the pause menu owns the
    /// cursor, and the sensitivity slider drives the controller's mouse look.
    /// </summary>
    public sealed class GameplayBridge : MonoBehaviour
    {
        public GameStateController states;
        public DebugFirstPersonController controller;
        [Tooltip("HUD pieces hidden while the intro plays (health, heart).")]
        public GameObject[] hideDuringIntro = new GameObject[0];

        void OnEnable()
        {
            if (states) states.SensitivityChanged += OnSensitivity;
        }

        void OnDisable()
        {
            if (states) states.SensitivityChanged -= OnSensitivity;
        }

        void OnSensitivity(float value)
        {
            if (controller) controller.lookSensitivity = value;
        }

        void LateUpdate()
        {
            if (!states || !controller)
                return;
            controller.handleCursor = false;
            foreach (var go in hideDuringIntro)
                if (go && go.activeSelf == IntroCinematic.Playing) go.SetActive(!IntroCinematic.Playing);
            // The intro switches the controller off and on by itself; only interfere outside it.
            if (IntroCinematic.Playing)
                return;
            bool control = states.CanControl;
            if (controller.enabled != control)
                controller.enabled = control;
        }
    }
}
