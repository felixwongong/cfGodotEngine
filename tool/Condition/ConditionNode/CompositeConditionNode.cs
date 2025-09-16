using System.Linq;
using cfEngine.Pooling;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Tool;

[Tool]
[GlobalClass]
public partial class CompositeConditionNode: ConditionNode
{
    public enum OpCode: byte
    {
        And,
        Or
    }
    
    [Export] private ConditionNode[] _conditions;
    [Export] private OpCode _opCode = OpCode.And;

    private Subscription _onConditionsFulfilledSub;
#if TOOLS
    public override string[] _GetConfigurationWarnings()
    {
        using var _ = ListPool<string>.Default.Get(out var warnings);
        if (_conditions == null || _conditions.Length == 0)
            warnings.Add("CompositeConditionNode: No conditions set.");

        foreach (var condition in _conditions)
        {
            if (condition == null)
            {
                warnings.Add("CompositeConditionNode: One of the conditions is not set.");
                continue;
            }
            condition.GetConfigurationWarnings(warnings);
        }

        return warnings.ToArray();
    }
#endif

    public override void _Ready()
    {
        base._Ready();
        if (Engine.IsEditorHint())
            return;
        if (_conditions == null || _conditions.Length == 0)
        {
            GD.PrintErr("CompositeConditionNode: No conditions set.");
            return;
        }

        var subscriptionGroup = new SubscriptionGroup();
        foreach (var condition in _conditions)
        {
            if (condition == null)
            {
                GD.PrintErr("CompositeConditionNode: One of the conditions is not set.");
                continue;
            }
            if (condition.isFulfilled)
                continue;
            
            subscriptionGroup.Add(condition.OnFulfilled.AddListener(OnConditionFulfilled));
        }

        _onConditionsFulfilledSub = subscriptionGroup;
    }

    private void OnConditionFulfilled()
    {
        bool fulfilled = false;
        switch (_opCode)
        {
            case OpCode.And:
                fulfilled = _conditions.All(IsFulfilled);
                break;
            case OpCode.Or:
                fulfilled = _conditions.Any(IsFulfilled);
                break;
        }
        
        if(fulfilled)
            Fulfill();
        else
            EmitSignalOnConditionUpdated(false);

        return;

        static bool IsFulfilled(ConditionNode condition)
        {
            return condition.isFulfilled;
        }
    }
}