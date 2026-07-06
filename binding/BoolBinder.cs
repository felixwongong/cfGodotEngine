using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[GlobalClass]
public partial class BoolBinder : SinglePropertyBinder<bool>
{
    [Signal] public delegate void OnValueChangedEventHandler(bool value);

    protected override string GetSignalName() => SignalName.OnValueChanged;

    protected override bool ValidateValue(object value)
    {
        return value is bool;
    }

    protected override bool ParseValue(Variant propertyValue)
    {
        return propertyValue.VariantType == Variant.Type.Bool && propertyValue.AsBool();
    }
}
