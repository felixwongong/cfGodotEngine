using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Converts a boolean to a boolean visibility value, with optional inversion.
/// </summary>
[GlobalClass]
public partial class BoolToVisibilityConverter : BindingConverter
{
    [Export]
    public bool invert = false;

    public override Variant Convert(Variant input)
    {
        bool value = input.AsBool();
        return invert ? !value : value;
    }

    public override Variant ConvertBack(Variant output)
    {
        bool value = output.AsBool();
        return invert ? !value : value;
    }
}
