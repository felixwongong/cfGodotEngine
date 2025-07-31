using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using cfEngine.Extension;
using cfEngine.Logging;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.SceneManagement;

public partial class GodotSceneManager: MonoInstance<GodotSceneManager>, ISceneManager<Node>
{
    struct LoadingProcess
    {
        public string sceneKey;
        public IProgress<float> progress;
    }
    
    private readonly ILogger logger;
    
    private Dictionary<string, TaskCompletionSource<PackedScene>> sceneLoadTasks = new();
    private HashSet<string> activeSceneSet = new();
    private List<LoadingProcess> loadingProcessQueue = new();

    public GodotSceneManager()
    {
    }

    private Godot.Collections.Array progressArray = new();
    public override void _Process(double delta)
    {
        base._Process(delta);

        int write = 0;
        for (var read = 0; read < loadingProcessQueue.Count; read++)
        {
            var process = loadingProcessQueue[read];
            var sceneKey = process.sceneKey;
            var progress = process.progress;

            if (!sceneLoadTasks.TryGetValue(sceneKey, out var taskSource) || taskSource.Task.IsCompleted)
                continue;

            var status = ResourceLoader.LoadThreadedGetStatus(sceneKey, progressArray);
            progress?.Report(progressArray[0].As<float>());
            switch (status)
            {
                case ResourceLoader.ThreadLoadStatus.Failed:
                case ResourceLoader.ThreadLoadStatus.InvalidResource:
                    taskSource.SetException(new Exception($"Failed to load scene: {sceneKey}, status: {status}"));
                    break;
                case ResourceLoader.ThreadLoadStatus.InProgress:
                    loadingProcessQueue[write++] = process;
                    continue;
                case ResourceLoader.ThreadLoadStatus.Loaded:
                    var resource = ResourceLoader.LoadThreadedGet(sceneKey);
                    if (resource is PackedScene scene)
                        taskSource.SetResult(scene);
                    else
                        taskSource.SetException(new Exception($"Loaded resource is not a PackedScene: {sceneKey}"));
                    break;
            }
        }
        if (write < loadingProcessQueue.Count)
        {
            loadingProcessQueue.RemoveRange(write, loadingProcessQueue.Count - write);
        }
    }

    public void LoadScene(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single)
    {
        if (sceneLoadTasks.TryGetValue(sceneKey, out var t))
        {
            if (t.Task.IsCompletedSuccessfully)
            {
                HandleShowScene(t.Task.Result, mode);
                return;
            }
            
            t.SetCanceled();
            sceneLoadTasks.Remove(sceneKey);
        }

        var scene = ResourceLoader.Load<PackedScene>(sceneKey);
        if (scene == null)
            return;
        
        HandleShowScene(scene, mode);
    }

    private void HandleShowScene(PackedScene newScene, LoadSceneMode mode)
    {
        switch (mode)
        {
            case LoadSceneMode.Single:
            {
                var sceneTree = GetSceneTree();
                var currentScene = sceneTree.GetCurrentScene();
                foreach (var sceneName in activeSceneSet)
                {
                    if(sceneName.Equals(currentScene.GetName())) 
                        continue;
                    if (!TryGetSceneNode(sceneName, out var sceneNode))
                    {
                        logger.LogException(new KeyNotFoundException($"Scene node not found: {sceneName}"));
                        continue;
                    }
                    
                    sceneTree.Root.RemoveChild(sceneNode);
                    sceneNode.QueueFree();
                }
                
                activeSceneSet.Clear();
                sceneTree.Root.RemoveChild(currentScene);
                currentScene.QueueFree();
                
                AddSceneToRoot(newScene);
                break;
            }
            case LoadSceneMode.Additive:
            {
                if (TryGetSceneNode(newScene.GetName(), out _))
                    return;
                AddSceneToRoot(newScene);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        void AddSceneToRoot(PackedScene newScene)
        {
            var sceneTree = GetSceneTree();
            var currentScene = sceneTree.GetCurrentScene();
            if (currentScene != null && currentScene.GetName().Equals(newScene.GetName()))
            {
                Log.LogWarning($"Attempting to add a scene that is already the current scene. sceneName: {newScene.GetName()}");
                return;
            }
            var sceneNode = newScene.Instantiate();
            GetSceneTree().Root.AddChild(sceneNode);
        }
    }

    public Task LoadSceneAsync(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single, IProgress<float> progress = null)
    {
        {
            if (sceneLoadTasks.TryGetValue(sceneKey, out var taskSource))
                return taskSource.Task;
        }

        {
            var m = mode;
            var taskSource = new TaskCompletionSource<PackedScene>();
            taskSource.Task.ContinueWithSynchronized(result =>
            {
                if (result.IsCompletedSuccessfully)
                    HandleShowScene(result.Result, m);
            });
            sceneLoadTasks[sceneKey] = taskSource;

            var error = ResourceLoader.LoadThreadedRequest(sceneKey);
            if (error == Error.Failed)
            {
                taskSource.SetException(new Exception($"Failed to load scene: {sceneKey}, err: {error}"));
                return taskSource.Task;
            }
            
            loadingProcessQueue.Add(new LoadingProcess()
            {
                progress = progress,
                sceneKey = sceneKey
            });
            return taskSource.Task;
        }
    }

    public Node GetScene(string sceneName)
    {
        if (!activeSceneSet.Contains(sceneName))
            return null;

        if (!TryGetSceneNode(sceneName, out var sceneNode))
            return null;

        return sceneNode;
    }
    
    private bool TryGetSceneNode(string sceneName, out Node sceneNode)
    {
        sceneNode = GetSceneTree().Root.GetNodeOrNull(sceneName);
        if (sceneNode == null)
            return false;

        return true;
    }
    
    private SceneTree GetSceneTree()
    {
        return NodeUtil.GetSceneTree();
    }
}