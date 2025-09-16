using cfEngine.Pooling;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Tool;

#if TOOLS
[Tool]
#endif
[GlobalClass]
public partial class Conditional2D: Node2D
{
    [Export] private ConditionNode _condition;

    private Subscription _conditionUpdateSub;

#if TOOLS
    public override string[] _GetConfigurationWarnings()
    {
        if (_condition == null)
            return ["ConditionalEnable2D: Condition is not set."];

        using var handle = ListPool<string>.Default.Get(out var warnings);
        _condition.GetConfigurationWarnings(warnings);
        return warnings.ToArray();
    }
#endif

    public override void _Ready()
    {
        if(Engine.IsEditorHint())
            return;

        Visible = false;
        SetProcessMode(ProcessModeEnum.Disabled);
        _conditionUpdateSub = _condition.OnFulfilled.AddListener(OnConditionFulfilled);
    }

    private void OnConditionFulfilled()
    {
        Visible = true;
        SetProcessMode(ProcessModeEnum.Inherit);
    }
}