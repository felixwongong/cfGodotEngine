using CatSweeper.Util;
using Godot;
using Godot.Collections;

namespace cfGodotEngine.Tool;

[Tool]
[GlobalClass]
public partial class ObjectSpawner2D: Node2D
{
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

    private static int id;
    public void Spawn(int spawnCount = 1)
    {
        for (int i = 0; i < spawnCount; i++)
        {
            if (recyclable == null)
                continue;

            var instance = recyclable.Instantiate<Node2D>();
            instance.Name = $"{recyclable.GetName()}_{id++}";
            if (spawnAsChild)
                AddChild(instance);
            else 
                GetTree().Root.AddChild(instance);
        }
    }
}