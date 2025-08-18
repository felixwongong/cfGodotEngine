using System;
using System.Threading.Tasks;
using cfEngine.Service;
using cfGodotEngine.SceneManagement;


namespace cfEngine.Core
{
    public static partial class GameExtension
    {
        public static Domain WithSceneManager<TScene>(this Domain domain, ISceneManager<TScene> sceneManager)
        {
            domain.Register(sceneManager, $"ISceneManager<{typeof(TScene).Name}");
            return domain;
        }

        public static ISceneManager<TScene> GetSceneManager<TScene>(this Domain domain) => domain.GetService<ISceneManager<TScene>>($"ISceneManager<{typeof(TScene).Name}");
    }
}


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