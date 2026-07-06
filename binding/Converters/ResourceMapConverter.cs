using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Maps an input value to a <see cref="Resource"/> using a dictionary.
/// </summary>
[GlobalClass]
public partial class ResourceMapConverter : BindingConverter
{
    [Export]
    public Godot.Collections.Dictionary map = new();

    [Export]
    public Resource fallback;

    public override Variant Convert(Variant input)
    {
        if (map.TryGetValue(input, out var value))
            return (Resource)value;
        return fallback;
    }
}
