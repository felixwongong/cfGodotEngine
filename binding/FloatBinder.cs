using System.Globalization;
using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[GlobalClass]
public partial class FloatBinder : SinglePropertyBinder<float>
{
    [Signal] public delegate void OnValueChangedEventHandler(float value);
    [Signal] public delegate void OnValueTextChangedEventHandler(string text);
    protected override bool ValidateValue(object value)
    {
        return base.ValidateValue(value) && value is float;
    }

    protected override float ParseValue(object propertyValue)
    {
        return propertyValue is float floatValue ? floatValue : 0f;
    }

    protected override void DispatchSignal(float value)
    {
        EmitSignalOnValueChanged(value);
        if(HasConnections(SignalName.OnValueTextChanged))
            EmitSignalOnValueTextChanged(value.ToString(CultureInfo.InvariantCulture));
    }
}