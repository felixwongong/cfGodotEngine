using Godot;

namespace cfGodotEngine.Util;

[Tool]
[GlobalClass]
public partial class RandomNodeItem: Resource
{
    [Export()]
    public NodePath Node;
    [Export()]
    public int weight;
}