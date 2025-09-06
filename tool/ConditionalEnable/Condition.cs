using Godot;

namespace cfGodotEngine.Tool;

public abstract partial class Condition : Resource
{
    public bool isFulfilled { get; }
}