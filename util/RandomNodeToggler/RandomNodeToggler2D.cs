using cfGodotEngine.Binding;
using Godot;
using Godot.Collections;

namespace cfGodotEngine.Util;

[Tool]
[GlobalClass]
public partial class RandomNodeToggler2D: Node2D
{
    private static readonly RandomNumberGenerator rng = new();

    [Export] private Array<RandomNodeItem> _items = new();
    
    #if TOOLS
    [ExportToolButton(nameof(AddChildrenWithEvenWeight))]
    private Callable _addChildrenWithEvenWeightButton => Callable.From(AddChildrenWithEvenWeight);
    
    private void AddChildrenWithEvenWeight()
    {
        _items.Clear();
        foreach (var node in GetChildren())
        {
            if (node is Node2D)
            {
                var item = new RandomNodeItem
                {
                    Node = this.GetPathTo(node),
                    weight = 1
                };
                _items.Add(item);
            }
        }
        NotifyPropertyListChanged();
    }
    #endif
    
    [PropertyBinding] private Node2D _activeNode;
    private int _totalWeight;

    public override void _Ready()
    {
        base._Ready();

        if (Engine.IsEditorHint())
            return;
        
        int write = 0;
        for (var read = 0; read < _items.Count; read++)
        {
            var item = _items[read];
            var node = GetNodeOrNull<Node2D>(item.Node);
            if (node == null)
            {
                GD.PrintErr($"RandomNodeToggler: Node not found: {item.Node}");
                continue;
            }

            node.SetProcessMode(ProcessModeEnum.Disabled);
            node.Visible = false;
            
            _totalWeight += item.weight;
            write++;
        }
        
        if (write < _items.Count)
            _items.Resize(write);
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        
        if(Engine.IsEditorHint())
            return;
        
        ToggleNext();
    }

    public void ToggleNext()
    {
        if (_items.Count <= 0)
            return;
        
        ActiveNode?.SetProcessMode(ProcessModeEnum.Disabled);

        int acc = 0;
        var roll = rng.RandiRange(1, _totalWeight);
        foreach (var item in _items)
        {
            acc += item.weight;
            if (roll <= acc)
            {
                var newActive = GetNode<Node2D>(item.Node);
                newActive.SetProcessMode(ProcessModeEnum.Inherit);
                newActive.Visible = true;
                ActiveNode = newActive;
                break;
            }
        }
    }
}