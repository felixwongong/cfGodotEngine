using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[GlobalClass]
public partial class StringBinder : SinglePropertyBinder<string>
{
    [Signal] public delegate void OnValueChangedEventHandler(string value);

    protected override string GetSignalName() => SignalName.OnValueChanged;

    protected override bool ValidateValue(object value)
    {
        return value is string;
    }

    protected override string ParseValue(Variant propertyValue)
    {
        return propertyValue.AsString();
    }
}
