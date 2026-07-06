using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Concatenates a prefix and/or suffix around the input value's string representation.
/// </summary>
[GlobalClass]
public partial class StringConcatConverter : BindingConverter
{
    [Export]
    public string prefix = "";

    [Export]
    public string suffix = "";

    public override Variant Convert(Variant input)
    {
        return prefix + input.ToString() + suffix;
    }
}
