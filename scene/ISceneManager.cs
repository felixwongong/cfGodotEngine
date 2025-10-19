using System;
using System.Threading.Tasks;
using cfEngine.Service;

namespace cfGodotEngine.SceneManagement
{
    public enum LoadSceneMode
    {
        Single,
        Additive
    }

    public interface ISceneManager<TScene> : IService
    {
        public TScene LoadScene(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single);
        public Task<TScene> LoadSceneAsync(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single, IProgress<float> progress = null);
        public TScene GetScene(string sceneName);
    }
}