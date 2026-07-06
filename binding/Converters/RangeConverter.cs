using Godot;
using System;

namespace cfGodotEngine.Binding;

/// <summary>
/// Linearly maps a value from one range to another.
/// </summary>
[GlobalClass]
public partial class RangeConverter : BindingConverter
{
    [Export]
    public float inMin = 0f;

    [Export]
    public float inMax = 1f;

    [Export]
    public float outMin = 0f;

    [Export]
    public float outMax = 1f;

    [Export]
    public bool clamp = true;

    public override Variant Convert(Variant input)
    {
        float value = input.AsSingle();
        float t = (value - inMin) / (inMax - inMin);
        if (clamp)
            t = Math.Clamp(t, 0f, 1f);
        return outMin + t * (outMax - outMin);
    }

    public override Variant ConvertBack(Variant output)
    {
        float value = output.AsSingle();
        float t = (value - outMin) / (outMax - outMin);
        return inMin + t * (inMax - inMin);
    }
}
