using Godot;
using System;

namespace cfGodotEngine.Binding;

/// <summary>
/// Formats a value using a .NET format string. Defaults to "{0}".
/// </summary>
[GlobalClass]
public partial class FormatConverter : BindingConverter
{
    [Export]
    public string format = "{0}";

    public override Variant Convert(Variant input)
    {
        return string.Format(format, input.ToString());
    }
}
