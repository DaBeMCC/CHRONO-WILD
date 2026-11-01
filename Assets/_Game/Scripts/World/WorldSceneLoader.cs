using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChronoWild.World
{
    /// <summary>
    /// Vive en la escena de gameplay y carga la escena de entorno (terreno, agua, luz) de forma
    /// aditiva si no está ya abierta. Permite que terreno y gameplay se editen por separado.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class WorldSceneLoader : MonoBehaviour
    {
        [SerializeField] string environmentScene = "Island_Terrain";
        [Tooltip("La escena de entorno aporta luz, niebla y skybox, así que se marca como activa.")]
        [SerializeField] bool makeEnvironmentActive = true;

        void Awake()
        {
            var scene = SceneManager.GetSceneByName(environmentScene);
            if (scene.isLoaded)
            {
                Activate(scene);
                return;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.LoadScene(environmentScene, LoadSceneMode.Additive);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != environmentScene)
                return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Activate(scene);
        }

        void Activate(Scene scene)
        {
            if (makeEnvironmentActive)
                SceneManager.SetActiveScene(scene);
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }
}
