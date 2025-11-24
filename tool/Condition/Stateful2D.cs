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
    [Export] private Godot.Collections.Dictionary<string, Node2D> _stateNodes = new();

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
                if (kv.Value == null)
                    warnings.Add($"StatefulEnable2D: State node for key '{kv.Key}' is not set.");
            }
        }
        
        return base._GetConfigurationWarnings();
    }
#endif
    
    public override void _Ready()
    {
        if (Engine.IsEditorHint())
            return;

        foreach (var node in _stateNodes.Values)
        {
            node.Visible = false;
            node.SetProcessMode(ProcessModeEnum.Disabled);
        }
    }
    
    public void SetValue(Variant newValue)
    {
        var newKey = newValue.AsString();
        foreach (var (key, node) in _stateNodes)
        {
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
}