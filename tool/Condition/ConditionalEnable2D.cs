using cfEngine.Pooling;
using Godot;

namespace cfGodotEngine.Tool;

[Tool]
[GlobalClass]
public partial class ConditionalEnable2D: Node2D
{
    [Export] private Condition _condition;

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
    }

    public void SetConditionValue(Variant value)
    {
        _condition.SetValue(value);
        if (_condition.isFulfilled)
        {
            Visible = true;
            SetProcessMode(ProcessModeEnum.Inherit);
        }
    }
}