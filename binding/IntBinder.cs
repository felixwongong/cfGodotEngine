using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[GlobalClass]
public partial class IntBinder : SinglePropertyBinder<int>
{
    [Signal] public delegate void OnValueChangedEventHandler(int value);
    protected override bool ValidateValue(object value)
    {
        return base.ValidateValue(value) && value is int;
    }

    protected override int ParseValue(object propertyValue)
    {
        return propertyValue is int intValue ? intValue : 0;
    }

    protected override void DispatchSignal(int value)
    {
        EmitSignalOnValueChanged(value);
    }
}