using Godot;

namespace cfGodotEngine.Tool;

[Tool]
[GlobalClass]
public partial class ConditionalEnable2D: Node2D
{
    [Export] private Condition _condition;

    public override void _Ready()
    {
        if(Engine.IsEditorHint())
            return;

        if (_condition == null)
        {
            Visible = false;
            SetProcessMode(ProcessModeEnum.Disabled);
            return;
        }
        
        var fulfilled = _condition.isFulfilled;
    }
}