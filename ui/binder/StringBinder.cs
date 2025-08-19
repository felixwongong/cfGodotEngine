using Godot;

namespace cfGodotEngine.UI;

[GlobalClass]
public partial class StringBinder: Binder<string>
{
    [Signal] public delegate void OnValueChangedEventHandler(string value);

    protected override void _PropertyChanged(string propertyValue)
    {
        EmitSignalOnValueChanged(propertyValue);
    }
}