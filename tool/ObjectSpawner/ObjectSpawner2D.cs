using System.Collections.Generic;
using CatSweeper.Util;
using cfEngine.Logging;
using cfEngine.Pooling;
using Godot;
using Godot.Collections;

namespace cfGodotEngine.Tool;

[Tool]
[GlobalClass]
public partial class ObjectSpawner2D: Node2D
{
    private static System.Collections.Generic.Dictionary<PackedScene, ObjectPool<SimpleRecyclable2D>> _poolMap = new();
    
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
    
    [Export] public bool spawnAsChild = true;

    public override void _ValidateProperty(Dictionary property)
    {
        base._ValidateProperty(property);
        property.EnsureSceneType<SimpleRecyclable2D>(nameof(recyclable), ref _recyclable);
    }

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
            if (spawnAsChild)
                AddChild(instance);
            else 
                GetTree().Root.AddChild(instance);
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
        GetTree().Root.AddChild(poolRoot);
        
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
                GetTree().Root.AddChild(x);
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