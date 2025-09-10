using System.Collections.Generic;
using CatSweeper.Util;
using cfEngine.Logging;
using cfEngine.Pooling;
using Godot;

namespace cfGodotEngine.Tool;

/// <summary>
/// 2D version of ObjectSpawner that requires a PackedScene with root node inherited from <see cref="SimpleRecyclable2D"/>
/// </summary>
[Tool]
[GlobalClass]
public partial class ObjectSpawner2D: Node2D
{
    private static Dictionary<PackedScene, ObjectPool<SimpleRecyclable2D>> _poolMap = new();
    
    [Export]
    public PackedScene recyclable
    {
        get => _recyclable;
        set
        {
            if(value.IsSceneType<SimpleRecyclable2D>())
                _recyclable = value;
            else 
                GD.PrintErr("ObjectSpawner2D: recyclable must be a PackedScene of SimpleRecyclable2D or its subclass.");
        }
    }
    private PackedScene _recyclable;
    
    /// <summary>
    /// If yes, <see cref="recyclable"/> will be spawned as a child of this node, otherwise it will be added to the scene root.
    /// everytime the instantiated node is initialized by <see cref="SimpleRecyclable2D.Initialize()"/> method, the global position will be set the same as the spawner node.
    /// </summary>
    [Export] private bool _spawnAsChild = true;

#if TOOLS
    public override string[] _GetConfigurationWarnings()
    {
        if (_recyclable == null)
            return ["recyclable is not set."];

        return [];
    }
#endif
    
    public void Spawn(int spawnCount)
    {
        var pool = GetPool();
        if (pool == null)
        {
            Log.LogException(new KeyNotFoundException("ObjectSpawner2D: recyclable is not set or invalid."));
            return;
        }
        
        for (int i = 0; i < spawnCount; i++)
        {
            var instance = GetPool().Get();
            instance.GetParent()?.RemoveChild(instance);
            if (_spawnAsChild)
            {
                AddChild(instance);
                instance.SetPosition(Vector2.Zero);
            }
            else
            {
                GetTree().Root.AddChild(instance);
                instance.SetPosition(GetGlobalPosition());
            }
        }
    }
    
    private static int _id = 0;
    private ObjectPool<SimpleRecyclable2D> GetPool()
    {
        if (recyclable == null)
            return null;
        
        if (_poolMap.TryGetValue(recyclable, out var pool))
            return pool;

        var poolRoot = new Node2D();
        poolRoot.SetProcess(false);
        poolRoot.Visible = false;
        poolRoot.Name = $"{recyclable.ResourceName}_PoolRoot";
        var treeRoot = GetTree().Root;
        treeRoot.AddChild(poolRoot);
        
        pool = new ObjectPool<SimpleRecyclable2D>(
            () =>
            {
                var instance = recyclable.Instantiate<SimpleRecyclable2D>();
                instance.Name = $"{instance.GetName()}_{_id++}";
                instance.SetPool(_poolMap[recyclable]);
                poolRoot.AddChild(instance);
                return instance;
            }, 
            x =>
            {
                x.Visible = true;
                x.SetProcessMode(ProcessModeEnum.Inherit);
                x.GetParent()?.RemoveChild(x);
                treeRoot.AddChild(x);
                x.Initialize();
            }, 
            x =>
            {
                x.Visible = false;
                x.SetProcessMode(ProcessModeEnum.Disabled);
                x.GetParent()?.RemoveChild(x);
                poolRoot.AddChild(x);
            });
        
        _poolMap[recyclable] = pool;
        return pool;
    }
}