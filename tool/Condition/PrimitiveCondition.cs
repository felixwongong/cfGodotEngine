using System;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Tool;

[Tool]
[GlobalClass]
public partial class PrimitiveCondition: Condition
{
    private const double TOLERANCE = 0.0001d;
    
    [Export] private OpCode _op;
    [Export] private double _value;

    public override void SetValue(Variant v)
    {
        double value;
        if (v.IsPrimitive())
            value = v.AsDouble();
        else
        {
            GD.PrintErr(new ArgumentException($"PrimitiveCondition: Value must be a primitive type, received {v} ({v.VariantType})"));
            return;
        }
        
        bool fulfilled = _op switch
        {
            OpCode.Equal => Math.Abs(value - _value) < TOLERANCE,
            OpCode.NotEqual => Math.Abs(value - _value) > TOLERANCE,
            OpCode.Greater => value > _value,
            OpCode.GreaterOrEqual => value >= _value,
            OpCode.Less => value < _value,
            OpCode.LessOrEqual => value <= _value,
            _ => throw new ArgumentOutOfRangeException()
        };

        if (fulfilled)
            Fulfill();
    }
}