using cfEngine;
using cfEngine.Pooling;
using Godot;
using Godot.Collections;

namespace cfGodotEngine.Tool;

#if TOOLS
[Tool]
#endif
[GlobalClass]
public partial class Stateful2D: Node2D
{
    [Export] private Dictionary<string, NodePath> _stateNodes = new();
    [Export] private int _version = 0;

    private readonly Dictionary<string, Node2D> _resolved = new();

#if TOOLS
    public override string[] _GetConfigurationWarnings()
    {
        using var _ = ListPool<string>.Default.Get(out var warnings);

        if (_stateNodes == null || _stateNodes.Count == 0)
            warnings.Add("StatefulEnable2D: No state nodes set.");
        else
        {
            foreach (var kv in _stateNodes)
            {
                if (kv.Value == null || kv.Value.IsEmpty)
                    warnings.Add($"StatefulEnable2D: State node for key '{kv.Key}' is not set.");
                else if (GetNodeOrNull<Node2D>(kv.Value) == null)
                    warnings.Add($"StatefulEnable2D: State node for key '{kv.Key}' does not resolve: '{kv.Value}'");
            }
        }
        
        return base._GetConfigurationWarnings();
    }

    public override void _ValidateProperty(Godot.Collections.Dictionary property)
    {
        if (property["name"].AsString() != "_stateNodes")
            return;

        Node ancestor = GetParent();
        while (ancestor != null)
        {
            var snap = ancestor.Get("_layoutSnapshot");
            if (snap.VariantType == Variant.Type.Dictionary && snap.AsGodotDictionary().Count > 0)
            {
                int usage = property["usage"].AsInt32();
                usage |= (int)PropertyUsageFlags.ReadOnly;
                property["usage"] = Variant.From(usage);
                return;
            }
            ancestor = ancestor.GetParent();
        }
    }
#endif

	public override void _EnterTree()
    {
        base._EnterTree();
        if (Engine.IsEditorHint())
            return;

        Resolve();
		if (_version == 0)
        {
            foreach (var node in _resolved.Values)
            {
                if (node == null) continue;
                node.Visible = false;
                node.SetProcessMode(ProcessModeEnum.Disabled);
            }
        }
    }

    public void SetStateMap(Dictionary<string, NodePath> stateNodes)
    {
        if (stateNodes == null)
        {
            Log.LogException(new System.ArgumentNullException(nameof(stateNodes)));
            return;
        }
        _stateNodes = stateNodes;
    }

    public void SetValue(Variant newValue)
    {
        if (Engine.IsEditorHint())
            return;
        
        Resolve();
        _version++;
        var newKey = newValue.AsString();
        foreach (var (key, node) in _resolved)
        {
            if (node == null) continue;
            if (key == newKey)
            {
                node.Visible = true;
                node.SetProcessMode(ProcessModeEnum.Inherit);
            }
            else
            {
                node.Visible = false;
                node.SetProcessMode(ProcessModeEnum.Disabled);
            }
        }
    }

    private void Resolve()
    {
        if (_resolved.Count > 0 || _stateNodes == null)
            return;
        foreach (var (key, path) in _stateNodes)
        {
            var node = GetNodeOrNull<Node2D>(path);
            if (node != null)
                _resolved[key] = node;
        }
    }
}