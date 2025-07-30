using System;
using System.Threading.Tasks;
using cfEngine.Service;
using cfGodotEngine.SceneManagement;


namespace cfEngine.Core
{
    public static partial class GameExtension
    {
        public static Game WithSceneManager<TScene>(this Game game, ISceneManager<TScene> sceneManager)
        {
            game.Register(sceneManager, $"ISceneManager<{typeof(TScene).Name}");
            return game;
        }

        public static ISceneManager<TScene> GetSceneManager<TScene>(this Game game) => game.GetService<ISceneManager<TScene>>($"ISceneManager<{typeof(TScene).Name}");
    }
}


namespace cfGodotEngine.SceneManagement
{
    public enum LoadSceneMode
    {
        Single,
        Additive
    }

    public interface ISceneManager<out TScene> : IService
    {
        public bool LoadScene(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single);
        public Task LoadSceneAsync(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single, IProgress<float> progress = null);
        public TScene GetActiveScene();
        public TScene GetScene(string sceneName);
    }
}