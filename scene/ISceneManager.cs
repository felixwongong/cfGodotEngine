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
        /// <summary>
        /// Loads and immediately attaches a scene on the calling thread. The Godot implementation
        /// requires the Godot main thread: a wrong-thread caller is rejected synchronously with an
        /// <see cref="InvalidOperationException"/> before any cache or tree access. Single mode
        /// evicts the current scene first and assigns <c>SceneTree.CurrentScene</c>; Additive mode
        /// only attaches.
        /// </summary>
        public TScene LoadScene(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single);

        /// <summary>
        /// Begins asynchronous loading and returns a task that completes only after the scene has
        /// been instantiated and attached. The Godot implementation requires the Godot main thread
        /// on entry, so a wrong-thread caller is rejected synchronously with an
        /// <see cref="InvalidOperationException"/> (this method is not <c>async</c> and throws rather
        /// than returning a faulted task); the caller's current synchronization context is captured
        /// at entry so both cached and cold attachments run on the Godot main thread. A missing
        /// synchronization context is likewise rejected before any cache mutation rather than
        /// falling back to a worker. Attachment-before-success semantics are unchanged: a successful
        /// <c>Task</c> represents an already-attached scene.
        /// </summary>
        public Task<TScene> LoadSceneAsync(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single, IProgress<float> progress = null);

        /// <summary>
        /// Returns the tracked scene node for <paramref name="sceneName"/>, or <c>null</c> when it is
        /// not tracked/attached. Main-thread only; the Godot implementation is invoked on the Godot
        /// main thread.
        /// </summary>
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
        /// detached state must let their load settle before calling.
        /// </para>
        /// <para>
        /// Main-thread only: the Godot implementation rejects a wrong-thread caller synchronously
        /// with an <see cref="InvalidOperationException"/> before touching the scene tree.
        /// </para>
        /// </summary>
        public void UnloadCurrent();
    }
}