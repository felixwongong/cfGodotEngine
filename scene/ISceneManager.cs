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

        /// <summary>
        /// Synchronously detaches the currently attached Single-mode scene, releases it as the
        /// <c>SceneTree</c> current scene, clears tracked-scene bookkeeping, and queues the node for
        /// deferred destruction. Return means the scene is detached, not that Godot has finished
        /// destroying it. Safe no-op when there is no current scene; repeated calls never free the
        /// same scene twice. Persistent root children (e.g. UIRoot and registered services) are left
        /// intact.
        /// <para>
        /// This does not cancel, clear, or await outstanding <see cref="LoadSceneAsync"/> or preload
        /// work: an unsettled load may still attach a scene afterwards. Callers that require a stable
        /// detached state must let their load settle before calling. Main-thread only.
        /// </para>
        /// </summary>
        public void UnloadCurrent();
    }
}