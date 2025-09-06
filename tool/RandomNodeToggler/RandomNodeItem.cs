using Godot;

namespace cfGodotEngine.Tool;

[Tool]
[GlobalClass]
public partial class RandomNodeItem: Resource
{
    [Export()]
    public NodePath Node;
    [Export()]
    public int weight;
}