using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChronoWild.Core
{
    /// <summary>Main menu: "Jugar" opens the island, "Salir" closes the game (hidden in the browser, where it can't).</summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        public string gameScene = "Jungle_JurassicWetland";
        public GameObject quitButton;

        void Start()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (quitButton != null && Application.platform == RuntimePlatform.WebGLPlayer)
                quitButton.SetActive(false);
        }

        public void Play() => SceneManager.LoadScene(gameScene);

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
