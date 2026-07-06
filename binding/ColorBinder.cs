using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[GlobalClass]
public partial class ColorBinder : SinglePropertyBinder<Color>
{
    [Signal] public delegate void OnValueChangedEventHandler(Color value);

    protected override string GetSignalName() => SignalName.OnValueChanged;

    protected override bool ValidateValue(object value)
    {
        return value is Color;
    }

    protected override Color ParseValue(Variant propertyValue)
    {
        return propertyValue.AsColor();
    }
}
