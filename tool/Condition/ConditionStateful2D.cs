using cfEngine.Pooling;
using Godot;

namespace cfGodotEngine.Tool;

#if TOOLS
[Tool]
#endif
[GlobalClass]
public partial class ConditionStateful2D: Stateful2D
{
    [Export] private ConditionNode _condition;

#if TOOLS
    public override string[] _GetConfigurationWarnings()
    {
        using var _ = ListPool<string>.Default.Get(out var warnings);
        var baseWarnings = base._GetConfigurationWarnings(); 
        if(baseWarnings != null)
            warnings.AddRange(baseWarnings);
        
        if (_condition == null)
            warnings.Add("ConditionStateful2D: Condition is not set.");
        else
            _condition.GetConfigurationWarnings(warnings);
        
        return warnings.ToArray();
    }
#endif

    public override void _EnterTree()
    {
        base._EnterTree();
        if(Engine.IsEditorHint())
            return;
        _condition.OnConditionUpdated += OnConditionUpdated;
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if(Engine.IsEditorHint())
            return;
        _condition.OnConditionUpdated -= OnConditionUpdated;
    }

    private void OnConditionUpdated(bool fulfilled)
    {
        SetValue(fulfilled);
    }
}