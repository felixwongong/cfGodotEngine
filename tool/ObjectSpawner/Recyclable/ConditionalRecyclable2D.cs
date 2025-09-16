using System;
using System.Buffers;
using cfEngine.Pooling;
using cfEngine.Util;
using Godot;

namespace cfGodotEngine.Tool;

[Tool]
[GlobalClass]
public partial class ConditionalRecyclable2D: SimpleRecyclable2D
{
    [Export] private ConditionNode _condition;

#if TOOLS
    public override string[] _GetConfigurationWarnings()
    {
        if (_condition == null)
            return ["Requires a Condition resource."];

        using var handle = ListPool<string>.Default.Get(out var warnings);
        _condition.GetConfigurationWarnings(warnings);
        if (warnings.Count > 0)
            return warnings.ToArray();
        
        return [];
    }
#endif

    public override void _EnterTree()
    {
        base._EnterTree();
        _condition.OnFulfilled.AddListener(OnConditionFulfilled);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        _condition.OnFulfilled.RemoveListener(OnConditionFulfilled);
    }

    private void OnConditionFulfilled()
    {
        Recycle();
    }
}