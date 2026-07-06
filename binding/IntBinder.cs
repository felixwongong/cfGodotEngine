using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[GlobalClass]
public partial class IntBinder : SinglePropertyBinder<int>
{
    [Signal] public delegate void OnValueChangedEventHandler(int value);

    protected override string GetSignalName() => SignalName.OnValueChanged;

    protected override bool ValidateValue(object value)
    {
        return value is int;
    }

    protected override int ParseValue(Variant propertyValue)
    {
        return propertyValue.VariantType == Variant.Type.Int ? propertyValue.AsInt32() : 0;
    }
}
