using Godot;
using System;

namespace cfGodotEngine.Binding;

/// <summary>
/// Linearly interpolates between two colors based on a numeric input.
/// </summary>
[GlobalClass]
public partial class LerpColorConverter : BindingConverter
{
    [Export]
    public Color colorA = new(1f, 0f, 0f);

    [Export]
    public Color colorB = new(0f, 1f, 0f);

    [Export]
    public float fromMin = 0f;

    [Export]
    public float fromMax = 1f;

    public override Variant Convert(Variant input)
    {
        float t = (input.AsSingle() - fromMin) / (fromMax - fromMin);
        t = Math.Clamp(t, 0f, 1f);
        return colorA.Lerp(colorB, t);
    }
}
