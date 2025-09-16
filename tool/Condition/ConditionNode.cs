using System;
using System.Collections.Generic;
using System.Diagnostics;
using cfEngine.Rx;
using Godot;

namespace cfGodotEngine.Tool;

[Tool]
[GlobalClass]
public partial class ConditionNode : Node
{
    public bool isFulfilled { get; private set; }
    private Relay _OnFulfilled;
    public IRelay<Action> OnFulfilled => _OnFulfilled ??= new Relay(this);

    public void Fulfill()
    {
        if (isFulfilled)
        {
            GD.PushWarning($"Condition: Condition ({GetName()}) has already fulfilled.");
            return;
        }
        
        isFulfilled = true;
        _OnFulfilled?.Dispatch();
    }
    
    [Conditional("TOOLS")]
    public virtual void GetConfigurationWarnings(IList<string> warnings)
    {
    }
}

public enum OpCode: byte
{
    Equal,
    NotEqual,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual
}