using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChronoWild.Core
{
    /// <summary>Main menu: "Jugar" opens the island, "Salir" closes the game (hidden in the browser, where it can't).</summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        public string gameScene = "Jungle_JurassicWetland";
        public GameObject quitButton;
        DisplaySettingsMenu menu;
        bool loading;
        ThreadPriority? previousLoadingPriority;

        void Start()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (quitButton != null && Application.platform == RuntimePlatform.WebGLPlayer)
                quitButton.SetActive(false);

            menu = gameObject.AddComponent<DisplaySettingsMenu>();
            menu.InitializeMain();
        }

        public void Play()
        {
            if (loading || !Application.CanStreamedLevelBeLoaded(gameScene))
                return;

            loading = true;
            StartCoroutine(LoadIsland());
        }

        /// <summary>Loads the island and restores the game saved the last time the player slept.</summary>
        public void Continue()
        {
            if (loading || !ChronoWild.Survival.SurvivalRun.HasSave)
                return;
            ChronoWild.Survival.SurvivalRun.LoadRequested = true;
            ChronoWild.Story.IntroCinematic.SkipNext = true;
            Play();
        }

        IEnumerator LoadIsland()
        {
            menu.ShowLoading();
            yield return null;

            // This is a dedicated loading screen, so the player can prioritize asset integration.
            if (!Application.isEditor)
            {
                previousLoadingPriority = Application.backgroundLoadingPriority;
                Application.backgroundLoadingPriority = ThreadPriority.High;
            }
            var request = SceneManager.LoadSceneAsync(gameScene);
            while (!request.isDone)
            {
                menu.SetLoadingProgress(Mathf.Clamp01(request.progress / .9f));
                yield return null;
            }
        }

        void OnDestroy()
        {
            if (previousLoadingPriority is ThreadPriority priority)
                Application.backgroundLoadingPriority = priority;
        }

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
