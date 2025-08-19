using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using cfEngine.Logging;
using cfGodotEngine.Core;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.SceneManagement;

public partial class GodotSceneManager: MonoInstance<GodotSceneManager>, ISceneManager<Node>
{
    struct PreloadProcess
    {
        public string sceneKey;
        public IProgress<float> progress;
        
        public void Deconstruct(out string sceneKey, out IProgress<float> progress)
        {
            sceneKey = this.sceneKey;
            progress = this.progress;
        }
    }
    
    private readonly ILogger logger;
    
    private Dictionary<string, TaskCompletionSource<PackedScene>> preloadTasks = new();
    private List<PreloadProcess> preloadProcesses = new();
    
    private HashSet<string> activeSceneSet = new();

    public GodotSceneManager(): this(new GodotLogger())
    {
    }

    public GodotSceneManager(ILogger logger)
    {
        this.logger = logger;
    }

    private Godot.Collections.Array progressArray = new();
    public override void _Process(double delta)
    {
        base._Process(delta);

        int write = 0;
        for (var read = 0; read < preloadProcesses.Count; read++)
        {
            var process = preloadProcesses[read];
            var (sceneKey, progress) = process;

            if (!preloadTasks.TryGetValue(sceneKey, out var taskSource))
            {
                logger.LogError($"Preload task source not found for scene key: {sceneKey}");
                continue;
            }

            if (taskSource.Task.IsCompleted)
            {
                progress.Report(1f);
                continue;
            }

            var status = ResourceLoader.LoadThreadedGetStatus(sceneKey, progressArray);
            progress?.Report(progressArray[0].As<float>());
            switch (status)
            {
                case ResourceLoader.ThreadLoadStatus.Failed:
                case ResourceLoader.ThreadLoadStatus.InvalidResource:
                    taskSource.SetException(new Exception($"Failed to load scene: {sceneKey}, status: {status}"));
                    break;
                case ResourceLoader.ThreadLoadStatus.InProgress:
                    preloadProcesses[write++] = process;
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
        if (write < preloadProcesses.Count)
        {
            preloadProcesses.RemoveRange(write, preloadProcesses.Count - write);
        }
    }

    public Node LoadScene(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single)
    {
        if (preloadTasks.TryGetValue(sceneKey, out var t))
        {
            var task = t.Task;
            if (task.IsCompleted)
            {
                if (!task.IsFaulted)
                    return ShowScene(t.Task.Result, mode);
                
                //if preload task already failed, return anyway, dun start the scene loading process again
                logger.LogError($"Preload task failed, cannot load scene: {sceneKey}, err: {task.Exception?.Message}, stack trace: {task.Exception?.StackTrace}. If you want to retry loading the scene, please call LoadSceneAsync instead.");
                return null;
            }

            t.SetCanceled();
            preloadTasks.Remove(sceneKey);
            var key = sceneKey;
            preloadProcesses.RemoveAt(preloadProcesses.FindIndex(x => x.sceneKey == key));
            return null;
        }

        var source = new TaskCompletionSource<PackedScene>();
        var scene = ResourceLoader.Load<PackedScene>(sceneKey);
        if (scene == null)
        {
            var ex = new ArgumentException($"Failed to load scene: {sceneKey}", nameof(sceneKey));
            source.SetException(ex);
            logger.LogException(ex);
            preloadTasks.Add(sceneKey, source);
            return null;
        }
        else
        {
            source.SetResult(scene);
            preloadTasks.Add(sceneKey, source);
            return ShowScene(scene, mode);
        }
    }

    private Node ShowScene(PackedScene newScene, LoadSceneMode mode)
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
                        logger.LogException(new KeyNotFoundException($"Scene node not found while removing: {sceneName}"));
                        continue;
                    }
                    
                    sceneTree.Root.RemoveChild(sceneNode);
                    sceneNode.QueueFree();
                }
                
                activeSceneSet.Clear();
                sceneTree.Root.RemoveChild(currentScene);
                currentScene.QueueFree();
                
                return AddSceneToRoot(newScene);
                break;
            }
            case LoadSceneMode.Additive:
            {
                return AddSceneToRoot(newScene);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        Node AddSceneToRoot(PackedScene newScene)
        {
            var sceneNode = newScene.Instantiate();
            GetSceneTree().Root.AddChild(sceneNode);
            return sceneNode;
        }
    }

    public Task<Node> LoadSceneAsync(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single,
        IProgress<float> progress = null)
    {
        var m = mode;
        {
            if (preloadTasks.TryGetValue(sceneKey, out var taskSource))
                return taskSource.Task.ContinueWith(result =>
                {
                    if (result.IsCompletedSuccessfully)
                        return ShowScene(result.Result, m);

                    if (result.IsFaulted)
                        Log.LogException(result.Exception);
                    return null;
                });
        }

        {
            var taskSource = new TaskCompletionSource<PackedScene>();
            var instantiateTask = taskSource.Task.ContinueWith(result =>
            {
                if (result.IsCompletedSuccessfully)
                    return ShowScene(result.Result, m);
                if (result.IsFaulted)
                    Log.LogException(result.Exception);
                return null;
            });
        
            preloadTasks[sceneKey] = taskSource;

            var error = ResourceLoader.LoadThreadedRequest(sceneKey);
            if (error == Error.Failed)
            {
                var ex = new Exception($"Failed to load scene: {sceneKey}, err: {error}");
                taskSource.SetException(ex);
                return instantiateTask;
            }
            
            preloadProcesses.Add(new PreloadProcess()
            {
                progress = progress,
                sceneKey = sceneKey
            });

            return instantiateTask;
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