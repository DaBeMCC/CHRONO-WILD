using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ChronoWild.Prototype;

namespace ChronoWild.Core
{
    public enum GameState { Playing, Backpack, Paused, Dead }

    public sealed class GameStateController : MonoBehaviour
    {
        public static GameStateController Instance { get; private set; }
        public event Action<GameState> StateChanged;
        public GameState State { get; private set; } = GameState.Playing;
        public bool CanControl => State == GameState.Playing;
        [SerializeField] PrototypePlayer player;
        [Tooltip("P opens the backpack. Off where there is no inventory.")]
        public bool backpackEnabled = true;
        public event Action<float> SensitivityChanged;
        void Awake() { Instance = this; Time.timeScale = 1; if (!player) player = FindFirstObjectByType<PrototypePlayer>(); Apply(); }
        void OnDestroy() { if (Instance == this) Instance = null; Time.timeScale = 1; }
        void Update()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (State == GameState.Dead) { if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) MainMenu(); return; }
            if (backpackEnabled && k.pKey.wasPressedThisFrame && (State == GameState.Playing || State == GameState.Backpack)) SetState(State == GameState.Backpack ? GameState.Playing : GameState.Backpack);
            if (k.escapeKey.wasPressedThisFrame)
            {
                if (State == GameState.Backpack || State == GameState.Paused) SetState(GameState.Playing);
                else SetState(GameState.Paused);
            }
        }
        public void SetState(GameState next)
        {
            if (State == GameState.Dead || State == next) return;
            State = next; Apply(); StateChanged?.Invoke(State);
        }
        public void Die() { if (State == GameState.Dead) return; State = GameState.Dead; Apply(); StateChanged?.Invoke(State); }
        void Apply()
        {
            Time.timeScale = State == GameState.Paused || State == GameState.Dead ? 0 : 1;
            Cursor.lockState = State == GameState.Playing ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = State != GameState.Playing;
        }
        public void Resume() => SetState(GameState.Playing);
        // Without a main menu in the build, "back to menu" restarts the level.
        public void MainMenu() { Time.timeScale = 1; if (Application.CanStreamedLevelBeLoaded("MainMenu")) SceneManager.LoadScene("MainMenu"); else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().buildIndex : 0); }
        public void SetSensitivity(float value) { if (player) player.mouseSensitivity = value; SensitivityChanged?.Invoke(value); }
        public void SetVolume(float value) => AudioListener.volume = value;
    }
}
