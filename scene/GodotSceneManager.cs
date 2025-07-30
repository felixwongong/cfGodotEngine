using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using cfEngine.Logging;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.SceneManagement;

public class GodotSceneManager: ISceneManager<PackedScene>
{
    private readonly ILogger logger;
    
    private Dictionary<string, TaskCompletionSource<PackedScene>> sceneLoadTasks = new();
    private HashSet<string> activeSceneSet = new();

    public GodotSceneManager(ILogger logger)
    {
        this.logger = logger;
    }
    
    public bool LoadScene(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single)
    {
        if (sceneLoadTasks.TryGetValue(sceneKey, out var t))
        {
            if (t.Task.IsCompletedSuccessfully)
            {
                HandleShowScene(t.Task.Result, mode);
                return true;
            }
            
            t.SetCanceled();
            sceneLoadTasks.Remove(sceneKey);
        }

        var scene = ResourceLoader.Load<PackedScene>(sceneKey);
        if (scene == null)
            return false;
        
        HandleShowScene(scene, mode);
        return true;
    }

    private void HandleShowScene(PackedScene newScene, LoadSceneMode mode)
    {
        var sceneTree = NodeUtil.GetSceneTree();

        switch (mode)
        {
            case LoadSceneMode.Single:
                sceneTree.ChangeSceneToPacked(newScene);
                break;
            case LoadSceneMode.Additive:
                var existing = sceneTree.Root.GetNodeOrNull(newScene.GetName());
                if (existing != null)
                    return;
                
                var sceneNode = newScene.Instantiate();
                sceneTree.Root.AddChild(sceneNode);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    public Task LoadSceneAsync(string sceneKey, LoadSceneMode mode = LoadSceneMode.Single, IProgress<float> progress = null)
    {
        throw new NotImplementedException();
    }

    public PackedScene GetActiveScene()
    {
        throw new NotImplementedException();
    }

    public PackedScene GetScene(string sceneName)
    {
        throw new NotImplementedException();
    }
    
    public void Dispose()
    {
    }
}