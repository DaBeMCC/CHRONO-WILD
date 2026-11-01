using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChronoWild.World
{
    /// <summary>
    /// Lives in the gameplay scene and additively loads the environment scene (terrain, water, light)
    /// if it is not already open. Lets terrain and gameplay be edited separately.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class WorldSceneLoader : MonoBehaviour
    {
        [SerializeField] string environmentScene = "Island_Terrain";
        [Tooltip("The environment scene provides light, fog and skybox, so it is marked as active.")]
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
